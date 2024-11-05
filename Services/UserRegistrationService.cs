using Dapper;
using SCP_Control.Models;
using SCP_Control.Services.Interfaces;
using System.Data.SqlClient;

namespace SCP_Control.Services
{
    public class UserRegistrationService
    {
        private readonly string _connectionString;
        private readonly IEmailService _emailService;
        private readonly ISMSService _smsService;
        private readonly ILogger<UserRegistrationService> _logger;

        public UserRegistrationService(
            IConfiguration configuration,
            IEmailService emailService,
            ISMSService smsService,
            ILogger<UserRegistrationService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _emailService = emailService;
            _smsService = smsService;
            _logger = logger;
        }

        public async Task<bool> ProcessRegistrationRequest(int requestId, string approverType, string decision, string reason)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);

                // First, get the request details
                var request = await GetRegistrationRequest(requestId);
                if (request == null)
                {
                    _logger.LogWarning("Registration request {RequestId} not found", requestId);
                    return false;
                }

                // Update the request status based on approver type
                var updateQuery = $@"
                    UPDATE UserRegistrationRequests 
                    SET {approverType}Approval = @Decision,
                        {approverType}ApprovalDate = @Now,
                        {approverType}ApprovalReason = @Reason,
                        Status = @Status
                    WHERE RequestId = @RequestId";

                var status = decision == "Approved" ? "Approved" : "Rejected";

                var result = await connection.ExecuteAsync(updateQuery, new
                {
                    RequestId = requestId,
                    Decision = decision,
                    Now = DateTime.UtcNow,
                    Reason = reason,
                    Status = status
                });

                if (result > 0 && decision == "Approved")
                {
                    // If approved and all required approvals are complete, create the user account
                    if (await CheckAllApprovalsComplete(requestId))
                    {
                        await CreateUserAccount(request);
                    }
                }

                return result > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing registration request {RequestId}", requestId);
                throw;
            }
        }

        public async Task<bool> CreateUserAccount(UserRegistrationRequest request)
        {
            try
            {
                var password = GenerateSecurePassword();  // Call the static method

                using var connection = new SqlConnection(_connectionString);
                const string insertQuery = @"
                    INSERT INTO Users (
                        ClockNumber, 
                        Password, 
                        UserType, 
                        Department, 
                        Line, 
                        Shift, 
                        Email, 
                        PhoneNumber, 
                        IsActive,
                        CreatedAt,
                        CreatedBy
                    )
                    VALUES (
                        @ClockNumber,
                        @Password,
                        @UserType,
                        @Department,
                        @Line,
                        @Shift,
                        @Email,
                        @PhoneNumber,
                        1,
                        @CreatedAt,
                        'System'
                    )";

                var result = await connection.ExecuteAsync(insertQuery, new
                {
                    request.ClockNumber,
                    Password = BCrypt.Net.BCrypt.HashPassword(password),
                    UserType = request.RequestedUserType,
                    request.Department,
                    request.Line,
                    request.Shift,
                    request.Email,
                    request.PhoneNumber,
                    CreatedAt = DateTime.UtcNow
                });

                if (result > 0)
                {
                    // Send credentials based on user type
                    if (request.RequestedUserType.ToLower() is "operator" or "teamleader")
                    {
                        await _smsService.SendSMS(
                            request.PhoneNumber ?? throw new InvalidOperationException("Phone number is required for Operator/TeamLeader"),
                            $"Your SCP Control credentials:\nClock Number: {request.ClockNumber}\nPassword: {password}");
                    }
                    else
                    {
                        await _emailService.SendEmail(
                            request.Email ?? throw new InvalidOperationException("Email is required for management users"),
                            "SCP Control Account Credentials",
                            $"Your account has been created.\nUsername: {request.Email}\nPassword: {password}");
                    }
                }

                return result > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user account for request {RequestId}", request.RequestId);
                throw;
            }
        }

        public async Task<IEnumerable<UserRegistrationRequest>> GetPendingRegistrationRequests()
        {
            using var connection = new SqlConnection(_connectionString);
            const string query = @"
                SELECT * FROM UserRegistrationRequests 
                WHERE Status = 'Pending'
                ORDER BY RequestDate DESC";

            return await connection.QueryAsync<UserRegistrationRequest>(query);
        }

        public async Task<UserRegistrationRequest?> GetRegistrationRequest(int requestId)
        {
            using var connection = new SqlConnection(_connectionString);
            const string query = "SELECT * FROM UserRegistrationRequests WHERE RequestId = @RequestId";
            return await connection.QueryFirstOrDefaultAsync<UserRegistrationRequest>(query, new { RequestId = requestId });
        }

        private async Task<bool> CheckAllApprovalsComplete(int requestId)
        {
            using var connection = new SqlConnection(_connectionString);
            const string query = @"
                SELECT 
                    CASE 
                        WHEN RequestedUserType IN ('Operator', 'TeamLeader') 
                            THEN (TeamLeaderApproval = 'Approved')
                        WHEN RequestedUserType = 'GroupLeader'
                            THEN (ManagerApproval = 'Approved')
                        WHEN RequestedUserType = 'Manager'
                            THEN (SeniorManagerApproval = 'Approved')
                        ELSE 0
                    END AS AllApprovalsComplete
                FROM UserRegistrationRequests
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

        public async Task<bool> ProcessScrapWeight(int scrapId, decimal physicalWeight)
        {
            using var connection = new SqlConnection(_connectionString);
            const string query = @"
                UPDATE ScrapData 
                SET PhysicalWeight = @PhysicalWeight,
                    Status = 'Completed'
                WHERE ScrapId = @ScrapId";

            var result = await connection.ExecuteAsync(query, new { ScrapId = scrapId, PhysicalWeight = physicalWeight });
            return result > 0;
        }
    }
}