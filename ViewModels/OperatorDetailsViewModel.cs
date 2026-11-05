using SCP_Control.Models;

namespace SCP_Control.ViewModels
{
    public class OperatorDetailsViewModel
    {
        public string? ClockNumber { get; set; }
        public string? FullName { get; set; }
        public string? Department { get; set; }
        public string? Line { get; set; }
        public string? Shift { get; set; }
        public string? UserType { get; set; }
        public DateTime? LastLogin { get; set; }
        public bool IsActive { get; set; }
        public string? MachineNumber { get; set; }
        public IEnumerable<UserRegistrationRequest> PendingUserRequests { get; set; } =
            Enumerable.Empty<UserRegistrationRequest>();
    }
}
