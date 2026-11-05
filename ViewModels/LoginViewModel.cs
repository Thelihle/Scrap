using System.ComponentModel.DataAnnotations;

namespace SCP_Control.ViewModels
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Clock Number is required")]
        public string ClockNumber { get; set; }
        [Required(ErrorMessage = "Password is required")]
        public string Password { get; set; }
        public bool ForceLogin { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
        public string LoginType { get; set; } = "operator";

        // Make ReturnUrl optional
        public string? ReturnUrl { get; set; }
        [Display(Name = "Remember me?")]
        public bool RememberMe { get; set; }
    }
}