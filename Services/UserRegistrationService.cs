using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SCP_Control.DbContext;
using SCP_Control.Models;
using SCP_Control.Models.Common;
using SCP_Control.Services.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.Data.SqlClient;
using System.Text;

namespace SCP_Control.Services
{
    public class UserRegistrationService : IUserRegistrationService
    {
        private readonly string _connectionString;
        private readonly IEmailService _emailService;
        private readonly ILogger<UserRegistrationService> _logger;
        private readonly ApplicationDbContext _context;

        public UserRegistrationService(
             ApplicationDbContext context,
            IConfiguration configuration,
            IEmailService emailService,
            ILogger<UserRegistrationService> logger)
            
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _emailService = emailService;
            _logger = logger;
            _context = context;
           
        }

        public async Task<UserDetails> GetUserDetails(string clockNumber)
        {
            try
            {
                _logger.LogInformation("Getting user details for clock number: {ClockNumber}", clockNumber);

                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.ClockNumber == clockNumber && u.IsActive == true);

                if (user == null)
                {
                    _logger.LogWarning("User not found or inactive for clock number: {ClockNumber}", clockNumber);
                    return null;
                }

                // Create UserDetails directly from User properties
                return new UserDetails
                {
                    ClockNumber = user.ClockNumber,
                    Department = user.Department ?? string.Empty,
                    Line = user.Line ?? string.Empty,
                    Shift = user.Shift ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user details for clock number: {ClockNumber}", clockNumber);
                throw;
            }
        }

        public async Task<bool> ProcessRegistrationRequest(int requestId, string approverType, string decision, string reason)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                const string updateQuery = @"
                    UPDATE scpRequest 
                    SET Status = @Status,
                        ProcessedBy = @ProcessedBy,
                        ProcessedDate = @ProcessedDate,
                        Comments = @Comments
                    WHERE RequestId = @RequestId";

                var result = await connection.ExecuteAsync(updateQuery, new
                {
                    RequestId = requestId,
                    Status = decision,
                    ProcessedBy = approverType,
                    ProcessedDate = DateTime.UtcNow,
                    Comments = reason
                });

                return result > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing registration request {RequestId}", requestId);
                throw;
            }
        }

        public async Task<IEnumerable<UserRegistrationRequest>> GetPendingRegistrationRequests()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                const string query = @"
                    SELECT r.*, d.DepartmentName, l.LineName
                    FROM Request r
                    LEFT JOIN scpDepartment d ON r.DepartmentId = d.DepartmentId
                    LEFT JOIN scpLine l ON r.LineId = l.LineId
                    WHERE r.Status = 'Pending'
                    ORDER BY r.RequestDate DESC";

                return await connection.QueryAsync<UserRegistrationRequest>(query);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pending registration requests");
                throw;
            }
        }

        public async Task<UserRegistrationRequest?> GetRequestById(int requestId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                const string query = @"
                    SELECT r.*, d.DepartmentName, l.LineName
                    FROM scpRequest r
                    LEFT JOIN scpDepartment d ON r.DepartmentId = d.DepartmentId
                    LEFT JOIN scpLine l ON r.LineId = l.LineId
                    WHERE r.RequestId = @RequestId";

                return await connection.QueryFirstOrDefaultAsync<UserRegistrationRequest>(
                    query, new { RequestId = requestId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting registration request {RequestId}", requestId);
                throw;
            }
        }
        private async Task<bool> CheckAllApprovalsComplete(int requestId)
        {
            using var connection = new SqlConnection(_connectionString);
            const string query = @"
        SELECT 
            CASE 
                WHEN UserType IN ('Operator', 'TeamLeader') 
                    THEN (TeamLeaderApproval = 'Approved')
                WHEN UserType = 'GroupLeader'
                    THEN (ManagerApproval = 'Approved')
                WHEN UserType = 'Manager'
                    THEN (SeniorManagerApproval = 'Approved')
                ELSE 0
            END AS AllApprovalsComplete
        FROM scpRequest
        WHERE RequestId = @RequestId";

            return await connection.QueryFirstOrDefaultAsync<bool>(query, new { RequestId = requestId });
        }

        private static string GenerateSecurePassword()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 12)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private static string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);
        }

        public async Task<Result> ApproveUserAccess(string userId, string initialPassword, string approverId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    // Get the request and approver details
                    var request = await GetRequestById(int.Parse(userId));
                    var approver = await _context.Users
                        .FirstOrDefaultAsync(u => u.ClockNumber == approverId && u.IsActive == true);

                    if (request == null || approver == null)
                    {
                        return Result.CreateFailure("Invalid request or approver information.");
                    }

                    // Validate the approval hierarchy
                    bool isValidApproval = await ValidateApprovalHierarchy(request, approver);
                    if (!isValidApproval)
                    {
                        return Result.CreateFailure($"{approver.UserType} cannot approve {request.UserType} requests.");
                    }

                    // Update request status
                    const string updateRequestQuery = @"
                    UPDATE scpRequest
                    SET Status = 'Approved',
                        ProcessedBy = @ApproverId,
                        ProcessedDate = @ProcessedDate
                    WHERE RequestId = @RequestId AND Status = 'Pending'";

                    var updateResult = await connection.ExecuteAsync(updateRequestQuery, new
                    {
                        RequestId = userId,
                        ApproverId = approverId,
                        ProcessedDate = DateTime.UtcNow
                    }, transaction);

                    if (updateResult <= 0)
                        return Result.CreateFailure("Failed to update request status.");

                    // Create user account with the provided password
                    var hashedPassword = BCrypt.Net.BCrypt.HashPassword(initialPassword);

                    const string createUserQuery = @"
                    INSERT INTO scpUser (
                        ClockNumber, Email, Password, UserType, 
                        Department, Line, Shift,
                        IsActive, CreatedAt, CreatedBy, 
                        PasswordResetRequired, IsTemporary,
                        PhoneNumber
                    ) VALUES (
                        @ClockNumber, @Email, @Password, @UserType,
                        @Department, @Line, @Shift,
                        1, @CreatedAt, @CreatedBy,
                        1, 0,
                        @PhoneNumber
                    )";

                    var createUserResult = await connection.ExecuteAsync(createUserQuery, new
                    {
                        request.ClockNumber,
                        request.Email,
                        Password = hashedPassword,
                        request.UserType,
                        Department = request.Department?.DepartmentName,
                        Line = request.Line?.LineName,
                        request.Shift,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = approverId,
                   
                    }, transaction);

                    if (createUserResult <= 0)
                    {
                        await transaction.RollbackAsync();
                        return Result.CreateFailure("Failed to create user account.");
                    }

                    await transaction.CommitAsync();

                    // Send email notification if email exists
                    if (!string.IsNullOrEmpty(request.Email))
                    {
                        await _emailService.SendEmail(
                            request.Email,
                            "SCP Control System - Account Approved",
                            $"Your account has been approved. You can now log in with:\nUsername: {request.ClockNumber}\nPassword: {initialPassword}\n\nPlease change your password upon first login.");
                    }

                    return Result.CreateSuccess("User access approved successfully.");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Error in transaction while approving user access for {UserId}", userId);
                    return Result.CreateFailure($"Transaction error: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving user access for {UserId}", userId);
                return Result.CreateFailure("An error occurred while processing the approval.");
            }
        }

        public async Task<Result> ApproveRegistrationRequest(int requestId, string approverId, string comments)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var request = await _context.scpRequest
                    .Include(r => r.Department)
                    .Include(r => r.Line)
                    .FirstOrDefaultAsync(r => r.RequestId == requestId);

                if (request == null)
                    return Result.CreateFailure("Request not found");

                // Generate temporary password
                var tempPassword = GenerateTemporaryPassword();

                // Create new user with proper IdentityUser initialization
                var user = new User
                {
                    ClockNumber = request.ClockNumber ?? $"{request.Name}.{request.Surname}".ToLower(),
                    UserName = request.ClockNumber, // Set UserName same as ClockNumber
                    Email = request.Email,
                    UserType = request.UserType,
                    Department = request.Department?.DepartmentName,
                    Line = request.Line?.LineName,
                    Shift = request.Shift,               
                    IsActive = true,
                    PasswordResetRequired = true,
                    IsTemporary = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = approverId,
                    LastModifiedAt = DateTime.UtcNow,
                    SecurityStamp = Guid.NewGuid().ToString(),
                    NormalizedUserName = request.ClockNumber?.ToUpper(),
                    NormalizedEmail = request.Email?.ToUpper(),
                    EmailConfirmed = false,
                    PhoneNumberConfirmed = false,
                    TwoFactorEnabled = false,
                    LockoutEnabled = true,
                    AccessFailedCount = 0
                };

                // Hash password using IdentityUser's password hasher
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);

                // Add user to Users table
                _context.Users.Add(user);

                // Update request status
                request.Status = "Approved";
                request.ProcessedBy = approverId;
                request.ProcessedDate = DateTime.UtcNow;
              

                await _context.SaveChangesAsync();

                // Send email with credentials if email exists
                if (!string.IsNullOrEmpty(request.Email))
                {
                    await _emailService.SendEmail(
                        request.Email,
                        "Registration Request Approved",
                        $@"Your registration request has been approved.
                You can now login with:
                Username: {user.ClockNumber}
                Temporary Password: {tempPassword}
                
                You will be required to change your password on first login."
                    );
                }

                await transaction.CommitAsync();
                return Result.CreateSuccess("User account created successfully");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error approving registration request {RequestId}", requestId);
                return Result.CreateFailure("Error creating user account");
            }
        }


        private async Task<Result> ProcessApproval(UserRegistrationRequest request, User approver, string comments)
        {
            var now = DateTime.UtcNow;

            // Verify the approval hierarchy is valid
            bool isValidApproval = await ValidateApprovalHierarchy(request, approver);
            if (!isValidApproval)
            {
                return Result.CreateFailure($"{approver.UserType} cannot approve {request.UserType} requests");
            }

            // Update the request with approval information
            request.Status = "Approved";
            request.ProcessedBy = approver.ClockNumber;
            request.ProcessedDate = now;
           

            try
            {
                if (request.Status == "Approved")
                {
                    await CreateUserAccountInternal(request);
                }

                return Result.CreateSuccess();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing approval for request {RequestId}", request.RequestId);
                return Result.CreateFailure("Error processing approval");
            }
        }

        private async Task<bool> ValidateApprovalHierarchy(UserRegistrationRequest request, User approver)
        {
            // Basic role hierarchy validation
            bool isValidHierarchy = request.UserType?.ToLower() switch
            {
                "operator" when approver.UserType == "TeamLeader" => true, // TeamLeaders can approve any Operator
                "teamleader" when approver.UserType == "GroupLeader" => true, // GroupLeaders can approve any TeamLeader
                "groupleader" when approver.UserType == "Manager" => true, // Managers can approve any GroupLeader
                "manager" when approver.UserType == "SeniorManager" => true, // Senior Managers can approve any Manager
                "seniormanager" when approver.UserType == "Admin" => true, // Admins can approve any Senior Manager
                "admin" when approver.UserType == "Admin" => true, // Admins can approve other Admins
                _ => false
            };

            if (!isValidHierarchy)
            {
                return false;
            }

            // Verify approver is active
            return await _context.Users
                .AnyAsync(u => u.ClockNumber == approver.ClockNumber && u.IsActive == true);
        }

        private async Task<bool> ValidateTeamLeaderApproval(UserRegistrationRequest request, User teamLeader)
        {
            if (string.IsNullOrEmpty(teamLeader.Department) ||
                string.IsNullOrEmpty(teamLeader.Line) ||
                string.IsNullOrEmpty(teamLeader.Shift))
                return false;

            return await _context.Users.AnyAsync(u =>
                u.ClockNumber == teamLeader.ClockNumber &&
                u.Department == request.Department.DepartmentName &&
                u.Line == request.Line.LineName &&
                u.Shift == request.Shift &&
                u.IsActive == true);
        }
        private async Task<bool> ValidateGroupLeaderApproval(UserRegistrationRequest request, User groupLeader)
        {
            if (string.IsNullOrEmpty(groupLeader.Department))
                return false;

            return await _context.Users.AnyAsync(u =>
                u.ClockNumber == groupLeader.ClockNumber &&
                u.Department == request.Department.DepartmentName &&
                u.IsActive == true);
        }

        private async Task<bool> ValidateManagerApproval(UserRegistrationRequest request, User manager)
        {
            if (string.IsNullOrEmpty(manager.Department))
                return false;

            return await _context.Users.AnyAsync(u =>
                u.ClockNumber == manager.ClockNumber &&
                u.Department == request.Department.DepartmentName &&
                u.IsActive == true);
        }

        private async Task<bool> ValidateSeniorManagerApproval(UserRegistrationRequest request, User seniorManager)
        {
            if (string.IsNullOrEmpty(seniorManager.Department))
                return false;

            return await _context.Users.AnyAsync(u =>
                u.ClockNumber == seniorManager.ClockNumber &&
                u.Department == request.Department.DepartmentName &&
                u.IsActive == true);
        }

        private async Task<bool> ValidateAdminApproval(UserRegistrationRequest request, User admin)
        {
            var requestDept = await _context.scpDepartment
                .FirstOrDefaultAsync(d => d.DepartmentId == request.DepartmentId);

            if (requestDept == null || string.IsNullOrEmpty(admin.Department))
                return false;

            var adminDept = await _context.scpDepartment
                .FirstOrDefaultAsync(d => d.DepartmentName == admin.Department);

            if (adminDept == null)
                return false;

            return requestDept.Location == adminDept.Location;
        }

        private async Task CreateUserAccountInternal(UserRegistrationRequest request)
        {
            var password = GenerateSecurePassword();
            var departmentName = request.Department?.DepartmentName ?? string.Empty;
            var lineName = request.Line?.LineName ?? string.Empty;

            var user = new User
            {
                ClockNumber = request.ClockNumber ?? $"{request.Name}.{request.Surname}".ToLower(),
                Email = request.Email,
                // Convert password to Base64
                Password = Convert.ToBase64String(Encoding.UTF8.GetBytes(password)),
                UserType = request.UserType,
                Department = departmentName,
                Line = lineName,
                Shift = request.Shift,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                PasswordResetRequired = true,
                IsTemporary = false,
                LastPasswordChange = DateTime.UtcNow,
                LastModifiedAt = DateTime.UtcNow
            };

            // Add user directly to database
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrEmpty(request.Email))
            {
                await _emailService.SendEmail(
                    request.Email,
                    "Account Created",
                    $"Your account has been created.\n\n" +
                    $"Username: {user.ClockNumber}\n" +
                    $"Password: {password}\n\n" +
                    "Please change your password upon first login."
                );
            }
        }

        public async Task<Result> ResetUserPassword(string clockNumber, string password)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.ClockNumber == clockNumber && u.IsActive == true);

                if (user == null)
                {
                    _logger.LogWarning("User not found or inactive: {ClockNumber}", clockNumber);
                    return Result.CreateFailure("User not found or inactive.");
                }

                // Convert password to Base64
                user.Password = Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
                user.LastModifiedAt = DateTime.UtcNow;
                user.PasswordResetRequired = true;
                user.LastPasswordChange = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                // Send email notification if email exists
                if (!string.IsNullOrEmpty(user.Email))
                {
                    await _emailService.SendEmail(
                        user.Email,
                        "SCP Control - Password Reset",
                        "Your password has been reset. Please contact your Team Leader for your new password.\n\n" +
                        "You will be required to change your password upon first login."
                    );
                }

                return Result.CreateSuccess("Password reset successful.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting password for user {ClockNumber}", clockNumber);
                return Result.CreateFailure("An error occurred while resetting the password.");
            }
        }

        public async Task<RegistrationResult> CreateUserAccount(UserRegistrationRequest model)
        {
            try
            {
                if (model == null || string.IsNullOrEmpty(model.UserType))
                {
                    return new RegistrationResult
                    {
                        Success = false,
                        Message = "Invalid registration data"
                    };
                }

                // Generate temporary password
                var tempPassword = GenerateTemporaryPassword();

                // Create new user with proper IdentityUser initialization
                var user = new User
                {
                    ClockNumber = model.ClockNumber,
                    UserName = model.ClockNumber, // Important: set UserName same as ClockNumber
                    Email = model.Email,
                    UserType = model.UserType,
                    Department = model.Department?.DepartmentName,
                    Line = model.Line?.LineName,
                    Shift = model.Shift,
                    IsActive = true,
                    PasswordResetRequired = true,
                    IsTemporary = true,
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow,
                    SecurityStamp = Guid.NewGuid().ToString(), // Ensure SecurityStamp is set
                    NormalizedUserName = model.ClockNumber.ToUpper(), // Set normalized username
                    NormalizedEmail = model.Email?.ToUpper(), // Set normalized email if exists
                    EmailConfirmed = false,
                    PhoneNumberConfirmed = false,
                    TwoFactorEnabled = false,
                    LockoutEnabled = true,
                    AccessFailedCount = 0
                };

                // Hash password using IdentityUser's password hasher
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);

                // Store request
                model.RequestDate = DateTime.UtcNow;
                model.Status = "Pending";
                _context.scpRequest.Add(model);

                // Add user to Users table
                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                _logger.LogInformation("User registration request created for {UserType}", model.UserType);

                return new RegistrationResult
                {
                    Success = true,
                    TempPassword = tempPassword,
                    Message = "Registration request submitted successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user account");
                return new RegistrationResult
                {
                    Success = false,
                    Message = "An error occurred while creating the user account"
                };
            }
        }



        private bool IsManagementRole(string userType)
        {
            if (string.IsNullOrEmpty(userType)) return false;
            var r = userType.ToLower().Trim();
            return r == "teamleader" || r == "groupleader" || r == "manager" ||
                   r == "seniormanager" || r == "admin" ||
                   r == "engineering teamleader" || r == "engineering groupleader" ||
                   r == "engineering manager" || r == "engineering seniormanager" ||
                   r == "engineering admin";
        }

        private string DetermineUsername(UserRegistrationRequest model)
        {
            if (!string.IsNullOrEmpty(model.ClockNumber))
                return model.ClockNumber;

            return IsManagementRole(model.UserType)
                ? model.Email?.Split('@')[0] ?? $"{model.Name}.{model.Surname}".ToLower()
                : $"{model.Name}.{model.Surname}".ToLower();
        }

        private async Task NotifyApprovers(UserRegistrationRequest request)
        {
            var approvers = await _context.Users
                .Where(u => u.IsActive == true &&
                    (u.UserType == GetApproverRole(request.UserType)))
                .Select(u => u.Email)
                .Where(e => !string.IsNullOrEmpty(e))
                .ToListAsync();

            foreach (var approverEmail in approvers)
            {
                await _emailService.SendEmail(
                    approverEmail,
                    "New Registration Request Pending Approval",
                    $@"A new registration request requires your approval:

User Type: {request.UserType}
Name: {request.Name} {request.Surname}
Department: {request.Department?.DepartmentName}
Line: {request.Line?.LineName}

Please log in to the system to review this request."
                );
            }
        }

        private string GetApproverRole(string userType) => userType?.ToLower() switch
        {
            "operator" => "TeamLeader",
            "teamleader" => "GroupLeader",
            "groupleader" => "Manager",
            "manager" => "SeniorManager",
            "seniormanager" => "Admin",
            "engineer" => "Engineering TeamLeader",
            "engineering teamleader" => "Engineering GroupLeader",
            "engineering groupleader" => "Engineering Manager",
            "engineering manager" => "Engineering SeniorManager",
            "engineering seniormanager" => "Engineering Admin",
            _ => "Admin"
        };

        private static string GenerateTemporaryPassword()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%^&*";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

      
        public async Task<bool> ProcessScrapWeight(int scrapId, decimal physicalWeight)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                const string query = @"
                    UPDATE ScrapData 
                    SET PhysicalWeight = @PhysicalWeight,
                        Status = 'Completed',
                        ModifiedAt = @ModifiedAt
                    WHERE ScrapId = @ScrapId";

                var result = await connection.ExecuteAsync(query, new
                {
                    ScrapId = scrapId,
                    PhysicalWeight = physicalWeight,
                    ModifiedAt = DateTime.UtcNow
                });

                return result > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing scrap weight for {ScrapId}", scrapId);
                throw;
            }
        }
    }
}