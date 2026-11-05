namespace SCP_Control.ViewModels
{
    public class ScrapHistoryViewModel
    {
        public int ScrapId { get; set; }
        public string PartNumber { get; set; } = string.Empty;
        public int ScrapSectionId { get; set; }
        public string Section { get; set; } = string.Empty;
        public string ScrapReason { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string Position { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal MaterialCost { get; set; }
        public DateTime SubmissionDate { get; set; }
        public string FormattedSubmissionDate { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public string Shift { get; set; } = string.Empty;
        public string ClockNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        public string FirstApprovalStatus { get; set; } = string.Empty;

        // THESE THREE PROPERTIES MUST BE HERE:
        public string StockUom { get; set; } = string.Empty;
        public string ApprovalStatusColor { get; set; } = "#ffff00";
        public string CurrentApprovalLevel { get; set; } = "First";

        public string StatusColor => Status.ToLower() switch
        {
            "pending" => "#ffff00",
            "approved" => "#00ff00",
            "rejected" => "#ff4444",
            _ => "#00fff2"
        };
    }
}