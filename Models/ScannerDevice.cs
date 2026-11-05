using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SCP_Control.Models
{
    [Table("scpScannerDevices")]
    public class ScannerDevice
    {
        public int Id { get; set; }

        [Required]
        [StringLength(45)] // IPv4 or IPv6
        public string IPAddress { get; set; }

        [Required]
        [StringLength(100)]
        public string DeviceName { get; set; }

        [StringLength(200)]
        public string Description { get; set; }

        [Required]
        [StringLength(20)]
        public string DefaultRole { get; set; } = "Operator"; // Used for quick access only

        public bool AllowQuickAccess { get; set; } = true; // Whether to show quick access button

        public bool AllowAnyRole { get; set; } = true; // Whether any authenticated user can use this scanner

        public bool IsActive { get; set; } = true;

        [StringLength(50)]
        public string Location { get; set; } // "Production Line A", "Quality Control", etc.

        [StringLength(50)]
        public string Department { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        [Required]
        [StringLength(50)]
        public string CreatedBy { get; set; }

        public DateTime? LastAccessDate { get; set; }

        public int AccessCount { get; set; } = 0;

        [StringLength(50)]
        public string LastAccessUser { get; set; } // Track last user who accessed

        // Navigation properties for audit trail
        public virtual ICollection<ScannerAccessLog> AccessLogs { get; set; } = new List<ScannerAccessLog>();
    }

    [Table("scpScannerAccessLogs")]
    public class ScannerAccessLog
    {
        public int Id { get; set; }
        public int ScannerDeviceId { get; set; }

        [StringLength(200)]
        public string AccessPath { get; set; }

        public DateTime AccessDateTime { get; set; } = DateTime.UtcNow;

        [StringLength(500)]
        public string UserAgent { get; set; }

        [StringLength(50)]
        public string UserClockNumber { get; set; } // Track which user accessed

        [StringLength(50)]
        public string UserRole { get; set; } // Track what role they used

        [StringLength(50)]
        public string AccessType { get; set; } // 'QuickAccess', 'FullAuth', 'Logout', etc.

        public bool WasSuccessful { get; set; } = true;

        [StringLength(1000)]
        public string Notes { get; set; }

        public virtual ScannerDevice ScannerDevice { get; set; }
    }

    // Enhanced view model for scanner management
    public class ScannerDeviceViewModel
    {
        public int Id { get; set; }
        public string IPAddress { get; set; }
        public string DeviceName { get; set; }
        public string Description { get; set; }
        public string DefaultRole { get; set; }
        public bool AllowQuickAccess { get; set; }
        public bool AllowAnyRole { get; set; }
        public bool IsActive { get; set; }
        public string Location { get; set; }
        public string Department { get; set; }
        public DateTime CreatedDate { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? LastAccessDate { get; set; }
        public int AccessCount { get; set; }
        public string LastAccessUser { get; set; }

        // Statistics
        public int UniqueUserCount { get; set; }
        public int QuickAccessCount { get; set; }
        public int FullAuthCount { get; set; }
        public int FailedAttempts { get; set; }
        public DateTime? MostRecentAccess { get; set; }
    }
}