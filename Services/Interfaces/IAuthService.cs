using SCP_Control.Models;
using SCP_Control.Models.Common;

namespace SCP_Control.Services.Interfaces
{
    public interface IAuthService
    {
        Task<(bool success, string role)> AuthenticateUser(string clockNumber, string password);
        Task<string> DebugVerifyPassword(string clockNumber, string providedPassword);
        Task<Result> ChangePassword(string clockNumber, string currentPassword, string newPassword);
        Task<Result<User>> ValidateCredentialsAsync(string identifier, string password, string loginType);
        Task<(bool success, string tempPassword)> GenerateTemporaryPasswordAsync(string identifier);
        Task<Result> ChangePasswordAsync(string identifier, string currentPassword, string newPassword);
        Task<Result> ResetPasswordAsync(string identifier);
        Task<bool> ResetAccountLockout(string identifier);
        Task<string> GetPasswordHashFormat(string clockNumber);
        Task<bool> IsRateLimited(string identifier);
        Task<bool> ValidateUser(string clockNumber, string password);
        Task<string> DebugLogin(string clockNumber, string password);
        Task<OperationResult> GrantUserAccess(string userClockNumber, string accessLevel, string granterClockNumber);
      
        Task MigrateUserPasswords();
        Task<(bool success, string message, User? user)> ValidateLogin(string clockNumber, string password);
        Task<bool> VerifyPassword(string clockNumber, string password);
        Task<(bool success, string message, string? tempPassword)> GrantAccess(
            string clockNumber,
            string email,
            string department,
            string line,
            string shift,
            string userType,
            string approverUserName);
        Task<string> GetRedirectUrl(User user);
        Task<bool> IsManagementUser(string userType);
        
        Task<Result> ResetUserPassword(string identifier, string newPassword);
    }
}