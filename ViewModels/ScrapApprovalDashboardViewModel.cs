using static SCP_Control.Controllers.ScrapApprovalController;

namespace SCP_Control.ViewModels
{
    public class ScrapApprovalDashboardViewModel
    {
        // Change this property to match your actual data type
        public IEnumerable<ScrapDataViewModel> ScrapItems { get; set; } = new List<ScrapDataViewModel>();
        public string SelectedPartNumber { get; set; } = string.Empty;
        public string CurrentDepartment { get; set; } = string.Empty;
        public string CurrentLine { get; set; } = string.Empty;
        public string CurrentShift { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public decimal TotalCost { get; set; }
        public int PendingApprovalCount { get; set; }
        public decimal TotalWeight { get; set; }
        public List<WeightHistoryViewModel> WeightHistory { get; set; }
    }

    public class ScrapApprovalItem
    {
        public int ScrapId { get; set; }
        public string PartNumber { get; set; } = "";
        public string ClockNumber { get; set; } = "";
        public string ScrapReason { get; set; } = "";
        public int Quantity { get; set; }
        public decimal MaterialCost { get; set; }
        public decimal TotalValue => Quantity * MaterialCost;
        public string Department { get; set; } = "";
        public string Line { get; set; } = "";
        public string Shift { get; set; } = "";
        public DateTime SubmissionDate { get; set; }
        public string FormattedSubmissionDate => SubmissionDate.ToString("yyyy-MM-dd HH:mm:ss");
        public string Status { get; set; } = "";
        public string FirstApprovalStatus { get; set; } = "";
        public int CurrentApprovalLevel { get; set; }
        public string SectionName { get; set; } = "";
        public string Position { get; set; } = "";
        public string StockUom { get; set; } = "";
    }

    public class DepartmentSummary
    {
        public string Department { get; set; } = "";
        public int PendingCount { get; set; }
        public decimal TotalValue { get; set; }
    }
}
