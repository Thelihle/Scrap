namespace SCP_Control.ViewModels
{
    public class ScrapDataViewModel
    {
        public int ScrapId { get; set; }
        public string PartNumber { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string ScrapReason { get; set; } = string.Empty;
        public decimal WeightKg { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime SubmissionDate { get; set; }
        public string SubmittedBy { get; set; } = string.Empty;
    }
}
