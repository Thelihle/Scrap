using System.ComponentModel.DataAnnotations;

namespace SCP_Control.Models
{
    public class ScannerRoleSelectionViewModel
    {
        public string ScannerDeviceName { get; set; }
        public string ScannerLocation { get; set; }
        public int ScannerDeviceId { get; set; }

        [Required(ErrorMessage = "Clock Number is required")]
        [StringLength(20, ErrorMessage = "Clock Number cannot exceed 20 characters")]
        public string ClockNumber { get; set; }

        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        public string SelectedRole { get; set; }
        public string ReturnUrl { get; set; }

        // Available roles for this user (populated from database)
        public List<string> AvailableRoles { get; set; } = new List<string>();

        // Quick access options
        public bool ShowQuickOperatorAccess { get; set; } = true;
    }

    public class ScannerAuthenticationResult
    {
        public bool IsSuccess { get; set; }
        public string UserRole { get; set; }
        public string UserName { get; set; }
        public string ClockNumber { get; set; }
        public string RedirectPath { get; set; }
        public string ErrorMessage { get; set; }
        public List<string> AvailableRoles { get; set; } = new List<string>();
    }
}