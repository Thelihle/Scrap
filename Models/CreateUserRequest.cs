using System.ComponentModel.DataAnnotations;

namespace SCP_Control.Models
{
    // Move these to separate files in your Models folder

    public class CreateUserRequest
    {
        [Required]
        public string ClockNumber { get; set; }

        [Required]
        public string Role { get; set; }

        [Required]
        public string CreatedBy { get; set; }
    }

    public class CreateUserResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public User? User { get; set; }
    }

    public class UpdatePasswordHashModel
    {
        [Required]
        public string ClockNumber { get; set; }

        [Required]
        public string NewPassword { get; set; }
    }

   
}