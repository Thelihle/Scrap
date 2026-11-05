using System;
using System.ComponentModel.DataAnnotations;

namespace SCP_Control.Models
{
    public class LoginAttemptLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ClockNumber { get; set; }

        [Required]
        public DateTime AttemptDate { get; set; }

        [Required]
        [StringLength(20)]
        public string Status { get; set; }

        [StringLength(255)]
        public string? Reason { get; set; }

        [StringLength(45)]  // IPv6 addresses can be up to 45 characters
        public string? IpAddress { get; set; }
    }
}
