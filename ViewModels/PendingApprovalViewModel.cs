using SCP_Control.Models;

namespace SCP_Control.ViewModels
{
    public class PendingApprovalViewModel
    {
        public int ScrapId { get; set; }
        public string ClockNumber { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int TerminalQuantity { get; set; }
        public decimal WeightKg { get; set; }
        public decimal Length { get; set; }
        public string Section { get; set; } = string.Empty;
        public string ScrapReason { get; set; } = string.Empty;
        public string SectionName { get; set; } = string.Empty;
        public string ReasonName { get; set; } = string.Empty;
        public DateTime? SubmissionDate { get; set; }
        public virtual ScrapSection? ScrapSection { get; set; }
        public string Status {  get; set; } = string.Empty;
        public string FormattedWeight => WeightKg.ToString("F2");
        public string FormattedLength => Length.ToString("F2");
        public string FormattedDate => SubmissionDate?.ToString("yyyy-MM-dd HH:mm") ?? "-";
    }
}