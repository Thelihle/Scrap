// Models/RegisteredDevice.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SCP_Control.Models
{
    [Table("RegisteredDevices")]
    [Index(nameof(MacAddress), IsUnique = true)]
    [Index(nameof(DeviceFingerprint), IsUnique = true)]
    public class RegisteredDevice
    {
        [Key]
        public int DeviceId { get; set; }

        [Required]
        [StringLength(100)]
        public string DeviceName { get; set; } = "";

        [Required]
        [StringLength(50)]
        public string DeviceModel { get; set; } = "";

        [StringLength(100)]
        public string? SerialNumber { get; set; }

        [Required]
        [StringLength(17)]
        public string MacAddress { get; set; } = "";

        [StringLength(45)]
        public string? IPAddress { get; set; }

        [Required]
        [StringLength(500)]
        public string DeviceFingerprint { get; set; } = "";

        [StringLength(20)]
        public string? AndroidVersion { get; set; }

        [StringLength(500)]
        public string? BuildFingerprint { get; set; }

        [StringLength(500)]
        public string? UserAgent { get; set; }

        [StringLength(50)]
        public string? Department { get; set; }

        [StringLength(100)]
        public string? AssignedUser { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime LastSeen { get; set; } = DateTime.Now;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // Navigation properties
        public virtual ICollection<DeviceSession> DeviceSessions { get; set; } = new List<DeviceSession>();
        public virtual ICollection<DeviceToken> DeviceTokens { get; set; } = new List<DeviceToken>();
    }
}