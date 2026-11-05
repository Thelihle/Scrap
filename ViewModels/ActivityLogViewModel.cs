namespace SCP_Control.ViewModels
{
    public class ActivityLogViewModel
    {
        public int LogId { get; set; }
        public string ClockNumber { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string ActionDetails { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? AdditionalInfo { get; set; }
        public string IPAddress { get; set; } = string.Empty;
    }
}
