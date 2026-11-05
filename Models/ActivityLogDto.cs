namespace SCP_Control.Models
{
    public class ActivityLogDto
    {
        public int Id { get; set; }
        public string Action { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public int UserId { get; set; } 
        public string ActionDetails { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string IPAddress { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string LineName { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
