using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using SCP_Control.Models.Common;
using SCP_Control.DbContext;
using SCP_Control.Models;
using SCP_Control.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Dapper;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;
using SCP_Control.ViewModels;
using static SCP_Control.Controllers.ScrapApprovalController;

public class SecureAuthService : IAuthService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<SecureAuthService> _logger;
    private readonly IActivityLogService _activityLogService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const int PBKDF2_ITERATIONS = 600000;
    private const int SALT_SIZE = 32;
    private const int HASH_SIZE = 32;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IConfiguration _configuration;
    private const int MaxAttempts = 5;
    private const int LockoutMinutes = 15;
    public SecureAuthService(
       IConfiguration configuration,
        UserManager<User> userManager,
        ILogger<SecureAuthService> logger,
        IActivityLogService activityLogService,
        IHttpContextAccessor httpContextAccessor,
        IPasswordHasher<User> passwordHasher,
        ApplicationDbContext context)

    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));    
        _activityLogService = activityLogService ?? throw new ArgumentNullException(nameof(activityLogService));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    
    }

    public async Task<(bool success, string role)> AuthenticateUser(string clockNumber, string password)
    {
        var (success, _, user) = await ValidateLogin(clockNumber, password);
        return (success, user?.UserType ?? "Unknown");
    }

    public async Task<bool> IsRateLimited(string identifier)
    {
        try
        {
            var recentAttempts = await _context.LoginAttempts
                .Where(l => l.Identifier == identifier
                           && !l.IsSuccessful
                           && l.AttemptTime >= DateTime.UtcNow.AddMinutes(-15))
                .CountAsync();

            return recentAttempts >= 5;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking rate limit for user {Identifier}", identifier);
            return false;
        }
    }

    public async Task<(bool success, string message, User? user)> ValidateLogin(string identifier, string password)
    {
        try
        {
            // Get user with only necessary fields
            var user = await _context.Users
                .Where(u => u.ClockNumber == identifier && u.IsActive)
                .Select(u => new User
                {
                    Id = u.Id,
                    ClockNumber = u.ClockNumber,
                    Password = u.Password,
                    PasswordHash = u.PasswordHash,
                    UserType = u.UserType,
                    Department = u.Department,
                    Line = u.Line,
                    Shift = u.Shift,
                    IsActive = u.IsActive,
                    PasswordResetRequired = u.PasswordResetRequired
                })
                .FirstOrDefaultAsync();

            if (user == null)
            {
                _logger.LogWarning($"User not found or inactive: {identifier}");
                return (false, "Invalid credentials", null);
            }

            // Check for recent registration instead of using HashedPassword
            var regRequest = await _context.scpRequest
                .Where(r => r.ClockNumber == identifier &&
                           (r.Status == "Approved" || r.Status == "PasswordReset") &&
                           r.ProcessedDate >= DateTime.UtcNow.AddDays(-1))
                .OrderByDescending(r => r.ProcessedDate)
                .FirstOrDefaultAsync();

            bool isValidPassword = false;

            // Check if user has a Password field set directly (could be from approval)
            if (!string.IsNullOrEmpty(user.Password))
            {
                try
                {
                    isValidPassword = BCrypt.Net.BCrypt.Verify(password, user.Password);
                    if (isValidPassword)
                    {
                        _logger.LogInformation($"User {identifier} logged in with password from Users.Password field");
                        return (true, "Login successful", user);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error verifying password from Users.Password for user {Identifier}", identifier);
                }
            }

            // If direct password failed or doesn't exist, check PasswordHash
            if (!isValidPassword && !string.IsNullOrEmpty(user.PasswordHash))
            {
                try
                {
                    isValidPassword = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
                    if (isValidPassword)
                    {
                        _logger.LogInformation($"User {identifier} logged in with permanent password from PasswordHash");
                        return (true, "Login successful", user);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error verifying permanent password for user {Identifier}", identifier);
                }
            }

            if (!isValidPassword)
            {
                _logger.LogWarning($"Invalid password attempt for user: {identifier}");
                return (false, "Invalid credentials", null);
            }

            return (true, "Login successful", user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ValidateLogin for user {Identifier}", identifier);
            return (false, "An error occurred during login", null);
        }
    }

    public Task<string> GetRedirectUrl(User user)
    {
        if (user.PasswordResetRequired.GetValueOrDefault())
            return Task.FromResult("/Account/ChangePassword");

        return Task.FromResult(user.UserType?.ToLower() switch
        {
            "operator" => "/Operator/Index",
            "teamleader" or "groupleader" or "manager" or "seniormanager" or "admin"
                => "/ScrapApproval/Dashboard",
            _ => "/Account/Login"
        });
    }

    public Task<bool> IsManagementUser(string userType)
    {
        bool result = new[] { "teamleader", "groupleader", "manager", "seniormanager", "admin" }
            .Contains(userType.ToLower());
        return Task.FromResult(result);
    }

    public async Task<Result> CreateUserWithPassword(string clockNumber, string password)
    {
        try
        {
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

            var user = new User
            {
                ClockNumber = clockNumber,
                PasswordHash = hashedPassword,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Result.CreateSuccess("User created successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user");
            return Result.CreateFailure("Error creating user");
        }
    }

    public async Task<string> DebugLogin(string clockNumber, string password)
    {
        var sb = new StringBuilder();

        // 1. Check if user exists
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.ClockNumber == clockNumber && u.IsActive);

        sb.AppendLine($"User found: {user != null}");
        if (user != null)
        {
            sb.AppendLine($"PasswordHash exists: {!string.IsNullOrEmpty(user.PasswordHash)}");
            if (!string.IsNullOrEmpty(user.PasswordHash))
            {
                sb.AppendLine($"Hash format: {DetermineHashFormat(user.PasswordHash)}");
                sb.AppendLine($"Hash length: {user.PasswordHash.Length}");

                // Try BCrypt verification
                bool bcryptValid = false;
                try
                {
                    bcryptValid = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"BCrypt verify error: {ex.Message}");
                }
                sb.AppendLine($"BCrypt verification result: {bcryptValid}");
            }

            // Check Users.Password field
            sb.AppendLine($"Password field exists: {!string.IsNullOrEmpty(user.Password)}");
            if (!string.IsNullOrEmpty(user.Password))
            {
                bool passwordValid = false;
                try
                {
                    passwordValid = BCrypt.Net.BCrypt.Verify(password, user.Password);
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"Password field verify error: {ex.Message}");
                }
                sb.AppendLine($"Password field verification result: {passwordValid}");
            }
        }

        // 2. Check registration request (but don't check HashedPassword anymore)
        var regRequest = await _context.scpRequest
            .FirstOrDefaultAsync(r => r.ClockNumber == clockNumber &&
                                    r.Status == "Approved");

        sb.AppendLine($"Registration request found: {regRequest != null}");
        if (regRequest != null)
        {
            sb.AppendLine($"Registration status: {regRequest.Status}");
            sb.AppendLine($"Processed date: {regRequest.ProcessedDate}");
        }

        return sb.ToString();
    }

    public async Task<Result> ChangePassword(string clockNumber, string currentPassword, string newPassword)
    {
        try
        {
            if (string.IsNullOrEmpty(clockNumber))
            {
                _logger.LogWarning("Clock number is null or empty in ChangePassword");
                return Result.CreateFailure("Invalid user identifier");
            }

            _logger.LogInformation("Starting password change for user: {ClockNumber}", clockNumber);

            // First try to find user in Users table
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.ClockNumber == clockNumber && u.IsActive);

            if (user == null)
            {
                _logger.LogWarning("User not found: {ClockNumber}", clockNumber);
                return Result.CreateFailure("User not found");
            }

            // Skip verification for users with PasswordResetRequired
            if (!user.PasswordResetRequired.GetValueOrDefault())
            {
                // Only verify current password if it's not a required reset
                var isValid = await VerifyPassword(clockNumber, currentPassword);
                if (!isValid)
                {
                    _logger.LogWarning("Invalid current password for user: {ClockNumber}", clockNumber);
                    return Result.CreateFailure("Current password is incorrect");
                }
            }
            else
            {
                _logger.LogInformation("Password reset required for user {ClockNumber}, skipping current password verification", clockNumber);
            }

            // Hash the new password using BCrypt
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(newPassword);
            _logger.LogInformation("Password hashed successfully for user {ClockNumber}", clockNumber);

            // Using a manual update query for maximum control
            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();

            var updateQuery = @"
        UPDATE [SCP_CONTROL].[dbo].[Users]
        SET 
            PasswordHash = @PasswordHash,
            Password = NULL, 
            PasswordResetRequired = 0,
            LastPasswordChange = @LastPasswordChange,
            LastModifiedAt = @LastModifiedAt,
            LastModifiedBy = @LastModifiedBy,
            SecurityStamp = @SecurityStamp
        WHERE ClockNumber = @ClockNumber";

            int rowsAffected = await connection.ExecuteAsync(updateQuery, new
            {
                PasswordHash = hashedPassword,
                LastPasswordChange = DateTime.UtcNow,
                LastModifiedAt = DateTime.UtcNow,
                LastModifiedBy = clockNumber,
                SecurityStamp = Guid.NewGuid().ToString(),
                ClockNumber = clockNumber
            });

            if (rowsAffected == 0)
            {
                _logger.LogWarning("No rows affected when updating password for user {ClockNumber}", clockNumber);
                return Result.CreateFailure("Failed to update password");
            }

            _logger.LogInformation("Password updated successfully for user {ClockNumber}", clockNumber);

            // Write to activity log with safe values
            try
            {
                var activityQuery = @"
            INSERT INTO [SCP_CONTROL].[dbo].[ActivityLogs]
            (ClockNumber, ActionType, ActionDetails, Status, CreatedAt)
            VALUES (@ClockNumber, @ActionType, @ActionDetails, @Status, @CreatedAt)";

                await connection.ExecuteAsync(activityQuery, new
                {
                    ClockNumber = clockNumber,
                    ActionType = "Password Change",
                    ActionDetails = "Password changed successfully",
                    Status = "Success",
                    CreatedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                // Log but don't fail if activity logging fails
                _logger.LogError(ex, "Failed to log password change activity for {ClockNumber}", clockNumber);
            }

            return Result.CreateSuccess("Password changed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password for user {ClockNumber}", clockNumber);
            return Result.CreateFailure("An error occurred while changing the password");
        }
    }
    private void LogPasswordHashDetails(string? passwordHash)
    {
        if (string.IsNullOrEmpty(passwordHash))
        {
            _logger.LogInformation("Password hash is null or empty");
            return;
        }

        _logger.LogInformation($"Password hash length: {passwordHash.Length}");
        _logger.LogInformation($"Password hash format: {(passwordHash.StartsWith("$2a$") ? "BCrypt" :
                                                        passwordHash.StartsWith("AQA") ? "ASP.NET Identity" :
                                                        "Unknown")}");

        // Log first few characters to help identify the hash format
        _logger.LogInformation($"Password hash start: {passwordHash.Substring(0, Math.Min(10, passwordHash.Length))}...");
    }

    // Add this method to help with debugging
    public async Task<string> GetPasswordHashFormat(string clockNumber)
    {
        try
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.ClockNumber == clockNumber);
            if (user == null)
            {
                _logger.LogInformation($"No user found for clock number: {clockNumber}");
                return "No user found";
            }

            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                _logger.LogInformation($"No password hash found for user: {clockNumber}");

                // Check if user has Password field set
                if (!string.IsNullOrEmpty(user.Password))
                {
                    _logger.LogInformation($"Found password in Password field for user: {clockNumber}");
                    return "Password found in Password field";
                }

                // Check for recently approved registration
                var regRequest = await _context.scpRequest
                    .FirstOrDefaultAsync(r => r.ClockNumber == clockNumber &&
                                            r.Status == "Approved" &&
                                            r.ProcessedDate >= DateTime.UtcNow.AddDays(-1));

                if (regRequest != null)
                {
                    _logger.LogInformation($"Found recently approved registration for user: {clockNumber}");
                    return "Recently approved registration - Check User table for password";
                }

                return "No password hash found";
            }

            // Log password hash details
            _logger.LogInformation($"Password hash length: {user.PasswordHash.Length}");
            _logger.LogInformation($"Password hash format: {DetermineHashFormat(user.PasswordHash)}");

            return $"Password hash found: {DetermineHashFormat(user.PasswordHash)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking password hash format for {clockNumber}");
            return "Error checking password hash format";
        }
    }

    public async Task<string> TestPasswordHash(string clockNumber, string password)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.ClockNumber == clockNumber);
        if (user == null) return "User not found";

        if (string.IsNullOrEmpty(user.PasswordHash))
            return "No password hash found for user";

        try
        {
            bool verificationResult = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
            return $"Password verification result: {verificationResult}\n" +
                   $"Hash format: {(user.PasswordHash.StartsWith("$2a$") ? "BCrypt" : "Unknown")}\n" +
                   $"Hash length: {user.PasswordHash.Length}";
        }
        catch (Exception ex)
        {
            return $"Error verifying password: {ex.Message}";
        }
    }

    private string DetermineHashFormat(string hash)
    {
        if (string.IsNullOrEmpty(hash)) return "Empty";

        if (hash.StartsWith("$2a$")) return "BCrypt";
        if (hash.StartsWith("AQA")) return "ASP.NET Identity v3";
        if (hash.StartsWith("AF")) return "ASP.NET Identity v2";

        return "Unknown format";
    }

    private async Task UpdateFailedLoginAttempts(User user)
    {
        user.AccessFailedCount++;

        if (user.AccessFailedCount >= 5)
        {
            user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(30);
            user.LockoutEnabled = true;

            await _activityLogService.LogActivityAsync(
                user.ClockNumber,
                "Account Lockout",
                $"Account locked due to {user.AccessFailedCount} failed attempts",
                "Warning"
            );
        }

        await _context.SaveChangesAsync();
    }

    private async Task<User?> FindUserByClockNumber(string clockNumber)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u =>
                (u.ClockNumber == clockNumber || u.UserName == clockNumber) &&
                u.IsActive);
    }

    // Add rate limiting middleware
    public class LoginRateLimitingMiddleware
    {
        private static readonly Dictionary<string, (int Count, DateTime FirstAttempt)> _loginAttempts = new();
        private const int MaxAttempts = 5;
        private const int WindowMinutes = 15;

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            if (context.Request.Path.StartsWithSegments("/Account/Login") &&
                context.Request.Method == "POST")
            {
                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                if (IsRateLimited(ipAddress))
                {
                    context.Response.StatusCode = 429;
                    await context.Response.WriteAsJsonAsync(new { message = "Too many login attempts" });
                    return;
                }
            }

            await next(context);
        }

        private bool IsRateLimited(string ipAddress)
        {
            lock (_loginAttempts)
            {
                if (!_loginAttempts.ContainsKey(ipAddress))
                {
                    _loginAttempts[ipAddress] = (1, DateTime.UtcNow);
                    return false;
                }

                var (count, firstAttempt) = _loginAttempts[ipAddress];

                if (DateTime.UtcNow - firstAttempt > TimeSpan.FromMinutes(WindowMinutes))
                {
                    _loginAttempts[ipAddress] = (1, DateTime.UtcNow);
                    return false;
                }

                if (count >= MaxAttempts)
                    return true;

                _loginAttempts[ipAddress] = (count + 1, firstAttempt);
                return false;
            }
        }
    }

    private async Task LogActivity(string clockNumber, string actionType, string details, string status)
    {
        try
        {
            // Use parameterized queries with Dapper
            await _context.Database.GetDbConnection().ExecuteAsync(@"
            INSERT INTO ActivityLogs (ClockNumber, ActionType, ActionDetails, Status, CreatedAt)
            VALUES (@ClockNumber, @ActionType, @ActionDetails, @Status, GETUTCDATE())",
                new { ClockNumber = clockNumber, ActionType = actionType, ActionDetails = details, Status = status });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging activity for user {ClockNumber}", clockNumber);
        }
    }

    private async Task LogError(string clockNumber, string message, string stackTrace)
    {
        var error = new ErrorLog
        {
            ClockNumber = clockNumber,
            ErrorMessage = message,
            StackTrace = stackTrace,
            ErrorSource = "Authentication",
            IPAddress = _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString(),
            UserAgent = _httpContextAccessor?.HttpContext?.Request.Headers["User-Agent"].ToString(),
            CreatedAt = DateTime.UtcNow
        };

        _context.ErrorLogs.Add(error);
        await _context.SaveChangesAsync();
    }

    public async Task<Result<User>> ValidateCredentialsAsync(string identifier, string password, string loginType)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(loginType);

        var user = await _context.Users
            .FirstOrDefaultAsync(u => loginType == "operator" ?
                u.ClockNumber == identifier :
                u.Email == identifier);

        // Fix for the bool? comparison
        bool isActive = user?.IsActive ?? false;
        if (!isActive)
        {
            return Result<User>.CreateFailure("Invalid credentials");
        }

        // Fix for the Task<bool> conversion
        var isValidPassword = await VerifyPassword(identifier, password);
        if (!isValidPassword)
        {
            await _activityLogService.LogActivityAsync(
                identifier,
                "Login",
                "Failed login attempt",
                "Failed");
            return Result<User>.CreateFailure("Invalid credentials");
        }

        return Result<User>.CreateSuccess("Login successful", user);
    }

   
    public async Task<(bool success, string tempPassword)> GenerateTemporaryPasswordAsync(string identifier)
    {
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.ClockNumber == identifier);

            if (user == null)
            {
                return (false, string.Empty);
            }

            string tempPassword = GenerateSecurePassword();
            user.Password = BCrypt.Net.BCrypt.HashPassword(tempPassword);
            user.PasswordResetRequired = true;
            user.LastModifiedAt = DateTime.UtcNow;
            user.SecurityStamp = Guid.NewGuid().ToString();

            await _context.SaveChangesAsync();

            await _activityLogService.LogActivityAsync(
                identifier,
                "Password Reset",
                "Temporary password generated",
                "Success");

            return (true, tempPassword);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating temporary password for {Identifier}", identifier);
            return (false, string.Empty);
        }
    }
    // For the spelling warnings in Program.cs, you can add these suppressions at the top of the file:
#pragma warning disable SPELL // Ignore spelling for .cshtml files
    public async Task<bool> ValidateUser(string clockNumber, string password)
    {
        var (success, _, _) = await ValidateLogin(clockNumber, password);
        return success;
    }
    public async Task<OperationResult> GrantUserAccess(string userClockNumber, string accessLevel, string granterClockNumber)
    {
        try
        {
            _logger.LogInformation("Attempting to grant {AccessLevel} access to {UserClockNumber} by {GranterClockNumber}",
                accessLevel, userClockNumber, granterClockNumber);

            // Validate granter's permissions
            var granter = await _context.Users
                .FirstOrDefaultAsync(u => u.ClockNumber == granterClockNumber && u.IsActive == true);

            if (granter == null)
            {
                _logger.LogWarning("Granter not found: {GranterClockNumber}", granterClockNumber);
                return OperationResult.Failed("Unauthorized access");
            }

            // Verify granter has sufficient privileges
            if (!IsAuthorizedToGrantAccess(granter.UserType, accessLevel))
            {
                _logger.LogWarning("Insufficient privileges for {GranterClockNumber} to grant {AccessLevel}",
                    granterClockNumber, accessLevel);

                await _activityLogService.LogActivityAsync(
                    granterClockNumber,
                    "Access Grant",
                    $"Attempted to grant {accessLevel} access to {userClockNumber} - Insufficient privileges",
                    "Failed"
                );

                return OperationResult.Failed("Insufficient privileges to grant this access level");
            }

            // Find target user
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.ClockNumber == userClockNumber);

            if (user == null)
            {
                _logger.LogWarning("Target user not found: {UserClockNumber}", userClockNumber);
                return OperationResult.Failed("User not found");
            }

            // Update user access
            user.UserType = accessLevel;
            user.LastModifiedAt = DateTime.UtcNow;
            user.LastModifiedBy = granterClockNumber;

            await _context.SaveChangesAsync();

            // Log the access grant
            await _activityLogService.LogActivityAsync(
                granterClockNumber,
                "Access Grant",
                $"Granted {accessLevel} access to {userClockNumber}",
                "Success"
            );

            _logger.LogInformation("Successfully granted {AccessLevel} access to {UserClockNumber}",
                accessLevel, userClockNumber);

            return OperationResult.Succeeded(
                $"Successfully granted {accessLevel} access to {userClockNumber}",
                new { UserClockNumber = userClockNumber, NewAccessLevel = accessLevel }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error granting access to {UserClockNumber}", userClockNumber);

            await LogError(granterClockNumber, "Error granting user access", ex.StackTrace ?? "No stack trace");

            return OperationResult.Failed("An error occurred while granting access");
        }
    }

    public async Task<(bool success, string message, string? tempPassword)> GrantAccess(string clockNumber, string email, string department, string line, string shift, string userType, string approverUserName)
    {
        try
        {
            // Generate temporary password
            string tempPassword = GenerateSecurePassword();

            var user = new User
            {
                ClockNumber = clockNumber,
                UserName = clockNumber,
                Email = email,
                Department = department,
                Line = line,
                Shift = shift,
                UserType = userType,
                TempPassword = tempPassword,
                Password = tempPassword,
                PasswordHash = null,
                IsActive = true,
                PasswordResetRequired = true,
                CreatedBy = approverUserName,
                CreatedAt = DateTime.UtcNow,
                EmailConfirmed = true
            };

            _context.Users.Add(user);

            _logger.LogInformation($"Creating user {clockNumber} with temporary password");
            _logger.LogDebug($"TempPassword field set: {!string.IsNullOrEmpty(user.TempPassword)}");
            _logger.LogDebug($"Password field set: {!string.IsNullOrEmpty(user.Password)}");

            await _context.SaveChangesAsync();

            // Log the activity
            await _activityLogService.LogActivityAsync(
                approverUserName,
                "User Creation",
                $"Created new user account for {clockNumber}",
                "Success"
            );

            return (true, "User created successfully", tempPassword);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error granting access for user {ClockNumber}", clockNumber);
            return (false, "Error creating user", null);
        }
    }

    private string GenerateSecurePassword()
    {
        const string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!@#$%^&*";
        var random = new Random();
        var chars = new char[12];

        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = validChars[random.Next(validChars.Length)];
        }

        return new string(chars);
    }

    private bool IsAuthorizedToGrantAccess(string? granterUserType, string accessLevelToGrant)
    {
        var accessLevels = new Dictionary<string, int>
    {
        { "Operator", 1 },
        { "TeamLeader", 2 },
        { "GroupLeader", 3 },
        { "Manager", 4 },
        { "SeniorManager", 5 },
        { "Admin", 6 }
    };

        return accessLevels.TryGetValue(granterUserType ?? "", out int granterLevel) &&
               accessLevels.TryGetValue(accessLevelToGrant, out int grantLevel) &&
               granterLevel > grantLevel;
    }

    public async Task<Result> ChangePasswordAsync(string identifier, string currentPassword, string newPassword)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.ClockNumber == identifier);
        if (user == null) return Result.CreateFailure("User not found");

        var (isValid, _, _) = await ValidateLogin(identifier, currentPassword);
        if (!isValid) return Result.CreateFailure("Current password is incorrect");

        var passwordHasher = new PasswordHasher<User>();
        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        user.PasswordResetRequired = false;
        user.LastPasswordChange = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Result.CreateSuccess("Password changed successfully");
    }

    public async Task<Result> ResetPasswordAsync(string identifier)
    {
        try
        {
            ArgumentException.ThrowIfNullOrEmpty(identifier);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.ClockNumber == identifier || u.Email == identifier);

            if (user == null)
            {
                return Result.CreateFailure("User not found");
            }

            // Use proper password hashing
            var tempPassword = GenerateSecurePassword();
            user.PasswordHash = _passwordHasher.HashPassword(user, tempPassword);
            user.Password = null; // Don't store raw passwords
            user.PasswordResetRequired = true;
            user.LastModifiedAt = DateTime.UtcNow;

            await LogActivity(identifier, "Password Reset", "Password reset initiated", "Success");
            await _context.SaveChangesAsync();

            return Result.CreateSuccess($"Password reset successful. New temporary password: {tempPassword}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting password for {Identifier}", identifier);
            await LogError(identifier, "Error during password reset", ex.StackTrace ?? "No stack trace");
            return Result.CreateFailure("An error occurred while resetting the password");
        }
    }

    private static string HashPassword(User user, string password)
    {
        var passwordHasher = new PasswordHasher<User>();
        return passwordHasher.HashPassword(user, password);
    }
    //Single&Bulk
    public async Task<bool> VerifyPassword(string clockNumber, string providedPassword)
    {
        try
        {
            // Get the user record
            var user = await _context.Users
                .Where(u => u.ClockNumber == clockNumber && u.IsActive)
                .Select(u => new { u.PasswordHash, u.Password })
                .FirstOrDefaultAsync();

            if (user == null)
            {
                _logger.LogWarning($"User not found or inactive for clock number: {clockNumber}");
                return false;
            }

            // First check the PasswordHash field
            if (!string.IsNullOrEmpty(user.PasswordHash))
            {
                try
                {
                    return BCrypt.Net.BCrypt.Verify(providedPassword, user.PasswordHash);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error verifying BCrypt password for user {clockNumber}");
                }
            }

            // Then check the Password field if PasswordHash is null or verification failed
            if (!string.IsNullOrEmpty(user.Password))
            {
                try
                {
                    return BCrypt.Net.BCrypt.Verify(providedPassword, user.Password);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error verifying Password field for user {clockNumber}");
                }
            }

            _logger.LogWarning($"No valid password hash found for user {clockNumber}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error in VerifyPassword for user {clockNumber}");
            return false;
        }
    }

    public async Task<string> DebugVerifyPassword(string clockNumber, string providedPassword)
    {
        var sb = new StringBuilder();
        try
        {
            var user = await _context.Users
                .Where(u => u.ClockNumber == clockNumber && u.IsActive)
                .Select(u => new { u.PasswordHash, u.Password })
                .FirstOrDefaultAsync();

            if (user == null)
            {
                return "User not found or inactive";
            }

            sb.AppendLine($"Found user: {clockNumber}");

            if (!string.IsNullOrEmpty(user.Password))
            {
                sb.AppendLine("Plain password exists");
                sb.AppendLine($"Plain password match: {user.Password == providedPassword}");
            }
            else
            {
                sb.AppendLine("No plain password found");
            }

            if (!string.IsNullOrEmpty(user.PasswordHash))
            {
                sb.AppendLine("Password hash exists");
                sb.AppendLine($"Hash format: {(user.PasswordHash.StartsWith("$2") ? "BCrypt" : "Unknown")}");
                try
                {
                    bool bcryptMatch = BCrypt.Net.BCrypt.Verify(providedPassword, user.PasswordHash);
                    sb.AppendLine($"BCrypt verify result: {bcryptMatch}");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"BCrypt verify error: {ex.Message}");
                }
            }
            else
            {
                sb.AppendLine("No password hash found");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error in debug verification: {ex.Message}";
        }
    }  

    private async Task HandleFailedLoginAttempt(User user)
    {
        try
        {
            user.AccessFailedCount++;
            _logger.LogWarning($"Failed login attempt for user {user.ClockNumber}. Count: {user.AccessFailedCount}");

            if (user.AccessFailedCount >= 5)
            {
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(15);
                _logger.LogWarning($"Account locked for user {user.ClockNumber}");
            }

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling failed login attempt");
        }
    }

    public async Task<bool> SetPermanentPassword(string clockNumber, string newPassword)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.ClockNumber == clockNumber);

            if (user == null) return false;

            // Hash new permanent password
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            user.Password = null; // Clear any legacy password
            user.PasswordResetRequired = false;
            user.LastPasswordChange = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static byte[] GetPbkdf2Hash(string password, byte[] salt, int iterations = PBKDF2_ITERATIONS)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(HASH_SIZE);
    }

    private async Task<bool> IsAccountLocked(string identifier)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-15);

            // Use a more efficient query with timeout
            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = @"
            SELECT COUNT(*)
            FROM LoginAttempts
            WHERE Identifier = @identifier
            AND AttemptTime > @cutoffTime
            AND IsSuccessful = 0";

            command.CommandTimeout = 30; // Set timeout to 30 seconds

            var identifierParam = command.CreateParameter();
            identifierParam.ParameterName = "@identifier";
            identifierParam.Value = identifier;
            command.Parameters.Add(identifierParam);

            var cutoffParam = command.CreateParameter();
            cutoffParam.ParameterName = "@cutoffTime";
            cutoffParam.Value = cutoffTime;
            command.Parameters.Add(cutoffParam);

            await _context.Database.OpenConnectionAsync();
            var result = await command.ExecuteScalarAsync();

            var failedAttempts = Convert.ToInt32(result);
            return failedAttempts >= 5;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking account lock status for {Identifier}", identifier);
            return false; // Fail open to prevent lockout issues
        }
        finally
        {
            if (_context.Database.GetDbConnection().State == System.Data.ConnectionState.Open)
            {
                await _context.Database.CloseConnectionAsync();
            }
        }
    }
    public async Task<bool> ResetAccountLockout(string identifier)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.ClockNumber == identifier);
        if (user == null) return false;

        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.LockoutEnabled = false;

        await _context.SaveChangesAsync();
        return true;
    }

    private async Task CleanupOldLoginAttempts(string identifier)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddDays(-1); // Remove attempts older than 1 day
            var oldAttempts = await _context.LoginAttempts
                .Where(la => la.Identifier == identifier && la.AttemptTime < cutoffTime)
                .ToListAsync();

            if (oldAttempts.Any())
            {
                _context.LoginAttempts.RemoveRange(oldAttempts);
                await _context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up old login attempts for {Identifier}", identifier);
        }
    }
    private async Task LogFailedAttempt(string identifier)
    {
        try
        {
            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = @"
            INSERT INTO LoginAttempts (Identifier, AttemptTime, IsSuccessful, IpAddress)
            VALUES (@identifier, @attemptTime, 0, @ipAddress)";

            command.CommandTimeout = 30;

            var identifierParam = command.CreateParameter();
            identifierParam.ParameterName = "@identifier";
            identifierParam.Value = identifier;
            command.Parameters.Add(identifierParam);

            var timeParam = command.CreateParameter();
            timeParam.ParameterName = "@attemptTime";
            timeParam.Value = DateTime.UtcNow;
            command.Parameters.Add(timeParam);

            var ipParam = command.CreateParameter();
            ipParam.ParameterName = "@ipAddress";
            ipParam.Value = _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
            command.Parameters.Add(ipParam);

            await _context.Database.OpenConnectionAsync();
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging failed attempt for {Identifier}", identifier);
        }
        finally
        {
            if (_context.Database.GetDbConnection().State == System.Data.ConnectionState.Open)
            {
                await _context.Database.CloseConnectionAsync();
            }
        }
    }


    private async Task ResetFailedAttempts(User user)
    {
        try
        {
            if (user.AccessFailedCount > 0)
            {
                user.AccessFailedCount = 0;
                user.LockoutEnd = null;
                await _context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting failed attempts");
        }
    }

    public async Task<Result> ResetUserPassword(string identifier, string newPassword)
    {
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.ClockNumber == identifier || u.Email == identifier);

            if (user == null)
            {
                _logger.LogWarning("User not found during password reset: {Identifier}", identifier);
                return Result.CreateFailure("User not found");
            }

            // Hash the new password
            var passwordHasher = new PasswordHasher<User>();
            user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
            user.Password = null; // Clear any legacy password
            user.PasswordResetRequired = true;
            user.LastPasswordChange = DateTime.UtcNow;
            user.SecurityStamp = Guid.NewGuid().ToString();
            user.LastModifiedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _activityLogService.LogActivityAsync(
                identifier,
                "Password Reset",
                "Password reset by administrator",
                "Success");

            _logger.LogInformation("Password successfully reset for user {Identifier}", identifier);
            return Result.CreateSuccess("Password reset successful");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting password for user {Identifier}", identifier);
            return Result.CreateFailure("An error occurred while resetting the password");
        }
    }

    public async Task MigrateUserPasswords()
    {
        try
        {
            _logger.LogInformation("Starting user password migration");
            using var connection = _context.Database.GetDbConnection();

            // Simple DTO matching only the fields we know exist
            var originalUsers = (await connection.QueryAsync<(string ClockNumber, string Password)>(@"
            SELECT ClockNumber, Password 
            FROM dbo.Users 
            WHERE IsActive = 1")).ToList();

            foreach (var (clockNumber, password) in originalUsers)
            {
                try
                {
                    var identityUser = await _context.Users
                        .FirstOrDefaultAsync(u => u.ClockNumber == clockNumber);

                    if (identityUser != null)
                    {
                        identityUser.Password = password;
                        identityUser.SecurityStamp = Guid.NewGuid().ToString();
                        _logger.LogInformation($"Updated password for {clockNumber}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing user {clockNumber}");
                }
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Password migration completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Password migration failed");
            throw;
        }
    }

    private class OriginalUserDto
    {
        public string ClockNumber { get; set; }
        public string Password { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public string Department { get; set; }
        public string UserType { get; set; }
        public bool IsActive { get; set; }
    }

    // Add these methods to SecureAuthService.cs for debugging password issues

    public async Task<string> DebugUserPasswordFlow(string clockNumber)
    {
        var sb = new StringBuilder();

        try
        {
            // Check user record
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.ClockNumber == clockNumber);

            sb.AppendLine($"User exists: {user != null}");

            if (user != null)
            {
                sb.AppendLine($"User is active: {user.IsActive}");
                sb.AppendLine($"PasswordResetRequired: {user.PasswordResetRequired}");
                sb.AppendLine($"Has PasswordHash: {!string.IsNullOrEmpty(user.PasswordHash)}");

                if (!string.IsNullOrEmpty(user.PasswordHash))
                {
                    sb.AppendLine($"PasswordHash type: {DetermineHashFormat(user.PasswordHash)}");
                    sb.AppendLine($"PasswordHash length: {user.PasswordHash.Length}");
                }

                sb.AppendLine($"Has old Password field: {!string.IsNullOrEmpty(user.Password)}");
                sb.AppendLine($"Has TempPassword field: {!string.IsNullOrEmpty(user.TempPassword)}");
                sb.AppendLine($"Last password change: {user.LastPasswordChange}");
            }

            // Check registration request
            var regRequest = await _context.scpRequest
                .FirstOrDefaultAsync(r => r.ClockNumber == clockNumber);

            sb.AppendLine($"Registration request exists: {regRequest != null}");

            if (regRequest != null)
            {
                sb.AppendLine($"Registration status: {regRequest.Status}");
                sb.AppendLine($"ProcessedDate: {regRequest.ProcessedDate}");
            }

            // Check database connectivity
            try
            {
                using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await connection.OpenAsync();
                sb.AppendLine("Database connection successful");

                // Test a simple query
                var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM [SCP_CONTROL].[dbo].[Users]");
                sb.AppendLine($"Total user count: {count}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Database connection error: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Error during debug: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
        }

        return sb.ToString();
    }

    public async Task<string> TestPasswordChangeQuery(string clockNumber, string newPassword)
    {
        var sb = new StringBuilder();

        try
        {
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(newPassword);
            sb.AppendLine("Password hashed successfully");

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();
            sb.AppendLine("Database connection opened");

            var updateQuery = @"
            UPDATE [SCP_CONTROL].[dbo].[Users]
            SET 
                PasswordHash = @PasswordHash,
                PasswordResetRequired = 0,
                LastPasswordChange = @LastPasswordChange,
                LastModifiedAt = @LastModifiedAt,
                LastModifiedBy = @LastModifiedBy,
                SecurityStamp = @SecurityStamp
            WHERE ClockNumber = @ClockNumber";

            int rowsAffected = await connection.ExecuteAsync(updateQuery, new
            {
                PasswordHash = hashedPassword,
                LastPasswordChange = DateTime.UtcNow,
                LastModifiedAt = DateTime.UtcNow,
                LastModifiedBy = clockNumber,
                SecurityStamp = Guid.NewGuid().ToString(),
                ClockNumber = clockNumber
            });

            sb.AppendLine($"Update query executed. Rows affected: {rowsAffected}");

            // Test activity logging
            try
            {
                var activityQuery = @"
                INSERT INTO [SCP_CONTROL].[dbo].[ActivityLogs]
                (ClockNumber, ActionType, ActionDetails, Status, CreatedAt)
                VALUES (@ClockNumber, @ActionType, @ActionDetails, @Status, @CreatedAt)";

                int logResult = await connection.ExecuteAsync(activityQuery, new
                {
                    ClockNumber = clockNumber,
                    ActionType = "Password Change Test",
                    ActionDetails = "Test execution of password change query",
                    Status = "Test",
                    CreatedAt = DateTime.UtcNow
                });

                sb.AppendLine($"Activity log insert executed. Rows affected: {logResult}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Activity logging error: {ex.Message}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Error during test: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
            return sb.ToString();
        }
    }

}