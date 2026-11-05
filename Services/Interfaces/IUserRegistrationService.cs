using SCP_Control.Models;
using SCP_Control.Models.Common;

namespace SCP_Control.Services.Interfaces
{
    public interface IUserRegistrationService
    {
        Task<bool> ProcessScrapWeight(int scrapId, decimal physicalWeight);
        Task<Result> ApproveUserAccess(string userId, string initialPassword, string approverId);
        Task<bool> ProcessRegistrationRequest(int requestId, string approverType, string decision, string reason);
        Task<RegistrationResult> CreateUserAccount(UserRegistrationRequest request);
        Task<IEnumerable<UserRegistrationRequest>> GetPendingRegistrationRequests();
        Task<UserRegistrationRequest?> GetRequestById(int requestId);
        Task<UserDetails> GetUserDetails(string clockNumber);
        Task<Result> ResetUserPassword(string clockNumber, string hashedPassword);

    }

    public class UserDetails
    {
        public string ClockNumber { get; set; }
        public string Department { get; set; }
        public string Line { get; set; }
        public string Shift { get; set; }
    }
}
