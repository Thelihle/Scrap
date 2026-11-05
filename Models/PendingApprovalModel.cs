namespace SCP_Control.Models
{
    public class PendingApprovalModel
    {
        public int ScrapId { get; set; }
        public string PartNumber { get; set; } = string.Empty;
        public string SubmitterClockNumber { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public string Shift { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public DateTime SubmissionDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal WeightKg { get; set; }
    }
}
