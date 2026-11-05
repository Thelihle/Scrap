using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace SCP_Control.Models
{
    public class AuditLog
    {
        [Key]
        public int Id { get; set; }
        public string Action { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public int? UserId { get; set; }
        public string? Details { get; set; }
        public string ClockNumber { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }

        [ForeignKey("UserId")]
        public virtual User? User { get; set; }
    }
}
