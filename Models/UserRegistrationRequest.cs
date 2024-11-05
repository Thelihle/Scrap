using System.ComponentModel.DataAnnotations;

namespace SCP_Control.Models
{
    public class UserRegistrationRequest
    {
        public int RequestId { get; set; }
        public string RequestedUserType { get; set; } = string.Empty;
        public string ClockNumber { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public string Shift { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime RequestDate { get; set; }
        public string? TeamLeaderApproval { get; set; }
        public string? GroupLeaderApproval { get; set; }
        public string? ManagerApproval { get; set; }
        public string? SeniorManagerApproval { get; set; }
    }
}
