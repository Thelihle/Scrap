namespace SCP_Control.Models
{
    public class ScrapDataResponse
    {
        public int ScrapId { get; set; }
        public string? PartNumber { get; set; }
        public int Quantity { get; set; }
        public decimal WeightKg { get; set; }
        public string? Status { get; set; }
        public decimal MaterialCost { get; set; }
        public decimal ItemCost { get; set; }
        public string? Department { get; set; }
        public string? Line { get; set; }
        public string? Shift { get; set; }
        public DateTime? SubmissionDate { get; set; }
        public string? ApprovedBy { get; set; }
        public string? RejectedBy { get; set; }
        public string? FirstApprovalStatus { get; set; }
    }
}
