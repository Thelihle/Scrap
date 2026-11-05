using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SCP_Control.Models
{
    public class ActivityLog
    {
        [Key]
        public int LogId { get; set; }
        public int? UserId { get; set; }
        public string? ClockNumber { get; set; }
        public string? ActionType { get; set; }
        public string? ActionDetails { get; set; }
        public string? Status { get; set; }
        public string? IPAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? SecurityStamp { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? DepartmentName { get; set; }  // Changed from Department
        public string? LineName { get; set; }

        [ForeignKey("UserId")]
        public virtual User? User { get; set; }
    }

    public class ErrorLog
    {
        [Key]
        public int ErrorId { get; set; }
        public int? UserId { get; set; }
        public string? ClockNumber { get; set; }
        public string? ErrorMessage { get; set; }
        public string? StackTrace { get; set; }
        public string? ErrorSource { get; set; }
        public string? IPAddress { get; set; }
        public string? UserAgent { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public virtual User? User { get; set; }
    }
}