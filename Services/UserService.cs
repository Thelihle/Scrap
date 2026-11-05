using Microsoft.EntityFrameworkCore;
using SCP_Control.DbContext;
using SCP_Control.Models;
using SCP_Control.Services.Interfaces;
using SCP_Control.Models.Common;

namespace SCP_Control.Services
{
    public class UserService : IUserService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<UserService> _logger;
        private readonly IAuthService _authService;  // Re-add the field

        public UserService(
            ApplicationDbContext context,
            ILogger<UserService> logger,
            IAuthService authService)  // Add the dependency
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));  // Initialize the field
        }

        public async Task<bool> ValidatePassword(string? clockNumber, string password)
        {
            try
            {
                if (string.IsNullOrEmpty(clockNumber))
                {
                    _logger.LogWarning("Empty clock number provided for validation");
                    return false;
                }

                // Now _authService will be available
                return await _authService.VerifyPassword(clockNumber, password);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating password for user {ClockNumber}", clockNumber);
                return false;
            }
        }

        public async Task<Result<User>> CreateUser(CreateUserRequest request)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(request.ClockNumber))
                    return Result<User>.CreateFailure("Clock number is required");

                // Check if user already exists
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.ClockNumber == request.ClockNumber);

                if (existingUser != null)
                    return Result<User>.CreateFailure("User already exists");

                // Generate a temporary password
                var (success, tempPassword) = await _authService.GenerateTemporaryPasswordAsync(request.ClockNumber);
                if (!success)
                    return Result<User>.CreateFailure("Failed to generate temporary password");

                // Create new user
                var newUser = new User
                {
                    ClockNumber = request.ClockNumber,
                    UserName = request.ClockNumber, // Use clock number as username
                    UserType = request.Role,
                    IsActive = true,
                    EmailConfirmed = true, // For operators/team leaders
                    PasswordResetRequired = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = request.CreatedBy,
                    LastModifiedAt = DateTime.UtcNow,
                    LastModifiedBy = request.CreatedBy
                };

                _context.Users.Add(newUser);
                await _context.SaveChangesAsync();

                return Result<User>.CreateSuccess("User created successfully", newUser);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user with clock number {ClockNumber}", request.ClockNumber);
                return Result<User>.CreateFailure("Error creating user");
            }
        }

        public async Task<UserLocationModel> GetUserLocation(string? clockNumber)
        {
            if (string.IsNullOrEmpty(clockNumber))
                return new UserLocationModel();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.ClockNumber == clockNumber && u.IsActive == true);

            return new UserLocationModel
            {
                Department = user?.Department ?? string.Empty,
                Line = user?.Line ?? string.Empty,
                Shift = user?.Shift ?? string.Empty
            };
        }
        public async Task<Result> GrantApprovalAccess(string username, string accessLevel, string granterUsername)
        {
            try
            {
                // Now _authService will be available
                var result = await _authService.GrantUserAccess(username, accessLevel, granterUsername);

                // Use static factory methods instead of direct property assignment
                return result.Success
                    ? Result.CreateSuccess(result.Message)
                    : Result.CreateFailure(result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error granting approval access for user {Username}", username);
                return Result.CreateFailure($"Error granting access: {ex.Message}");
            }
        }

        public async Task<Result> UpdateUserLocation(
            string? clockNumber,
            string department,
            string line,
            string shift)
        {
            if (string.IsNullOrEmpty(clockNumber))
                return Result.CreateFailure("Invalid user");

            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.ClockNumber == clockNumber && u.IsActive == true);

                if (user == null)
                    return Result.CreateFailure("User not found");

                user.Department = department;
                user.Line = line;
                user.Shift = shift;
                user.LastModifiedAt = DateTime.UtcNow;
                user.LastModifiedBy = clockNumber;

                await _context.SaveChangesAsync();
                return Result.CreateSuccess("Location updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user location");
                return Result.CreateFailure("Error updating location");
            }
        }


        public async Task<bool> IsUserAuthorized(string clockNumber, string[] roles)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.ClockNumber == clockNumber && u.IsActive == true);
            return user != null && roles.Contains(user.UserType);
        }
    }
}