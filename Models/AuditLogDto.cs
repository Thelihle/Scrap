namespace SCP_Control.Models
{
    public class AuditLogDto
    {
        public string Action { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string? Details { get; set; }
        public string ClockNumber { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
