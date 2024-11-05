namespace SCP_Control.Models
{
    public class ScrapData
    {
        public int ScrapId { get; set; }
        public string ClockNumber { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string ScrapReason { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public decimal WeightKg { get; set; }
        public decimal Length { get; set; }
        public string Status { get; set; } = "Submitted";
        public DateTime SubmissionDate { get; set; }
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public string Shift { get; set; } = string.Empty;
        public string? TeamLeaderApproval { get; set; }
        public DateTime? TeamLeaderApprovalDate { get; set; }
        public string? TeamLeaderApprovalReason { get; set; }
    }
}