using System.ComponentModel.DataAnnotations;

namespace SCP_Control.ViewModels
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Clock Number is required")]
        [Display(Name = "Clock Number")]
        public string ClockNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }
        public string LoginType { get; set; } = "operator";
    }
}