using System.ComponentModel.DataAnnotations;

namespace SCP_Control.ViewModels
{
    public class LocationUpdateViewModel
    {
        [Required]
        public string Department { get; set; } = string.Empty;

        [Required]
        public string Line { get; set; } = string.Empty;

        [Required]
        public string Shift { get; set; } = string.Empty;

        public string UpdatedBy { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
