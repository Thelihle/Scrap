using SCP_Control.Models;

namespace SCP_Control.ViewModels
{
    public class AdminWeightDashboardViewModel
    {
        public User CurrentUser { get; set; }
        public DateTime AccessTime { get; set; }
        public List<LocationWeightSummary> LocationSummaries { get; set; } = new List<LocationWeightSummary>();
        public List<WeightRecord> RecentWeightRecords { get; set; } = new List<WeightRecord>();
        public WeightStatistics OverallStatistics { get; set; } = new WeightStatistics();
    }

    public class LocationWeightSummary
    {
        public string Department { get; set; } = "";
        public string Line { get; set; } = "";
        public string Shift { get; set; } = "";
        public int ApprovedCount { get; set; }
        public decimal TotalCost { get; set; }
        public decimal TotalWeight { get; set; }
        public DateTime? LastWeightDate { get; set; }
        public int WeightRecordCount { get; set; }
    }

    public class WeightRecord
    {
        public int WeightId { get; set; }
        public string Department { get; set; } = "";
        public string Line { get; set; } = "";
        public string Shift { get; set; } = "";
        public decimal Weight { get; set; }
        public string Description { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = "";
        public bool IsBulkWeighing { get; set; }
        public string WeightType => IsBulkWeighing ? "Bulk" : "Single";
    }

    public class WeightStatistics
    {
        public int TotalRecords { get; set; }
        public decimal TotalWeight { get; set; }
        public decimal AverageWeight { get; set; }
        public int LocationCount { get; set; }
        public DateTime? LastWeightDate { get; set; }
        public int BulkRecords { get; set; }
        public int SingleRecords { get; set; }
    }
}
