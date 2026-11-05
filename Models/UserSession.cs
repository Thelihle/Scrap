using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SCP_Control.Models
{
    [Table("scpUserSession")]
    public class UserSession
    {
        [Key]
        public int SessionId { get; set; }   // DB IDENTITY PK

        [Required]
        [MaxLength(100)]
        public string ClockNumber { get; set; } = null!;

        [Required]
        [MaxLength(36)]
        public string SecurityStamp { get; set; } = null!; // GUID as string

        [Required]
        public DateTime CreatedAt { get; set; }

        [Required]
        public DateTime LastActivity { get; set; }

        public DateTime? EndedAt { get; set; }

        [MaxLength(50)]
        public string? IpAddress { get; set; }

        [MaxLength(500)]
        public string? UserAgent { get; set; }

        [Required]
        public bool IsActive { get; set; } = true;
    }
}
