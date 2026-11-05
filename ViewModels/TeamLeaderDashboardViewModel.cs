namespace SCP_Control.ViewModels
{
    public class TeamLeaderDashboardViewModel
    {
        public IEnumerable<ScrapSummaryModel> PendingApprovals { get; set; } =
            new List<ScrapSummaryModel>();

        public LocationFilterModel LocationFilter { get; set; } = new();

        public DateFilterModel DateFilter { get; set; } = new();

        public ScrapMetricsModel Metrics { get; set; } = new();

        public ICollection<UserAccessModel> PendingAccessRequests { get; set; } =
            new List<UserAccessModel>();
    }
    public class LocationFilterModel
    {
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public string Shift { get; set; } = string.Empty;
        public IEnumerable<string> AvailableDepartments { get; set; } = new List<string>();
        public IEnumerable<string> AvailableLines { get; set; } = new List<string>();
        public IEnumerable<string> AvailableShifts { get; set; } = new List<string>();
    }

    public class DateFilterModel
    {
        public string TimeFrame { get; set; } = "Day";
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    public class ScrapMetricsModel
    {
        public int TotalSubmissions { get; set; }
        public int PendingApprovals { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }
        public decimal TotalWeight { get; set; }
        public Dictionary<string, int> ScrapByDepartment { get; set; } =
            new Dictionary<string, int>();
    }
    public class UserAccessModel
    {
        public string ClockNumber { get; set; } = string.Empty;
        public string RequestedRole { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public DateTime RequestDate { get; set; }
        public string Status { get; set; } = string.Empty;
    }
    public class ScrapApprovalUpdateModel
    {
        public int ScrapId { get; set; }
        public string ApproverClockNumber { get; set; } = string.Empty;
        public string ApproverPassword { get; set; } = string.Empty;
        public bool IsApproved { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime ApprovalDate { get; set; } = DateTime.Now;
    }

    public class BulkApprovalModel
    {
        public List<int> ScrapIds { get; set; } = new List<int>();
        public string ApproverClockNumber { get; set; } = string.Empty;
        public string ApproverPassword { get; set; } = string.Empty;
        public bool IsApproved { get; set; }
        public string? RejectionReason { get; set; }
    }
}
