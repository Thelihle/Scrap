using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SCP_Control.Models
{
    [Table("DeviceSessions")]
    public class DeviceSession
    {
        [Key]
        public int SecurityStamp { get; set; }

        [Required]
        public int DeviceId { get; set; }

        [Required]
        [StringLength(50)]
        public string UserClockNumber { get; set; } = "";

        public DateTime LoginTime { get; set; } = DateTime.Now;

        public DateTime? LogoutTime { get; set; }

        [StringLength(45)]
        public string? IPAddress { get; set; }

        [StringLength(500)]
        public string? UserAgent { get; set; }

        [StringLength(20)]
        public string SessionStatus { get; set; } = "Active";

        public DateTime LastActivity { get; set; } = DateTime.Now;

        // Navigation property
        [ForeignKey("DeviceId")]
        public virtual RegisteredDevice Device { get; set; } = null!;
    }
}