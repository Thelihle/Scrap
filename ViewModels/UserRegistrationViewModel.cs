using SCP_Control.Models;

namespace SCP_Control.ViewModels
{
    public class UserRegistrationViewModel
    {
        public int RequestId { get; set; }
        public string ClockNumber { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
    }
}
