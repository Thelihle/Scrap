using SCP_Control.Models;
using SCP_Control.Models.Common; // Add this for Result type

namespace SCP_Control.Services.Interfaces
{
    public interface IUserService
    {
        Task<bool> ValidatePassword(string? clockNumber, string password);
        Task<UserLocationModel> GetUserLocation(string? clockNumber);
        Task<Result> UpdateUserLocation(string? clockNumber, string department, string line, string shift);
        Task<bool> IsUserAuthorized(string clockNumber, string[] roles);
        Task<Result<User>> CreateUser(CreateUserRequest request);
        Task<Result> GrantApprovalAccess(string username, string accessLevel, string granterUsername);
    }
}
