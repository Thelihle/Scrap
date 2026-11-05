using System.ComponentModel.DataAnnotations;

namespace SCP_Control.ViewModels
{
    public class ScrapApprovalViewModel
    {
        public int ScrapId { get; set; }
        public bool IsApproved { get; set; }
        public string? Reason { get; set; }
    } 
    


    public class ApprovalHistoryModel
    {
        public int ScrapId { get; set; }
        public string ApproverClockNumber { get; set; } = string.Empty;
        public DateTime ApprovalDate { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }
    public class UserAccessManagementModel
    {
        public string UserClockNumber { get; set; } = string.Empty;
        public string CurrentAccessLevel { get; set; } = string.Empty;
        public List<string> AvailableAccessLevels { get; set; } = new List<string>();
        public bool IsActive { get; set; }
        public DateTime? LastAccessGranted { get; set; }
        public string? GrantedBy { get; set; }
    }
    public class UserAccessRequest
    {
        [Required]
        public string ClockNumber { get; set; } = string.Empty;

        [Required]
        public string AccessLevel { get; set; } = string.Empty;
    }
    public class LocationManagementModel
    {
        public string CurrentDepartment { get; set; } = string.Empty;
        public string CurrentLine { get; set; } = string.Empty;
        public string CurrentShift { get; set; } = string.Empty;

        // Changed from IEnumerable<string> to IEnumerable<DepartmentViewModel>
        public IEnumerable<DepartmentViewModel> Departments { get; set; } = new List<DepartmentViewModel>();

        // Changed from IEnumerable<string> to IEnumerable<LineViewModel>
        public IEnumerable<LineViewModel> Lines { get; set; } = new List<LineViewModel>();

        // Changed from IEnumerable<string> to List<string> to match controller usage
        public IEnumerable<string> Shifts { get; set; } = new List<string>();
    }
    public class ScrapStatusUpdateRequest
    {
        public int ScrapId { get; set; }
        public bool IsApproved { get; set; }

        [Required]
        public string Reason { get; set; } = string.Empty;

        [Required]
        public string ApproverPassword { get; set; } = string.Empty;
    }
    public class FilteredScrapDataModel
    {
        public List<ScrapGroupViewModel> GroupedScrap { get; set; } = new();
        public int TotalCount { get; set; }
        public decimal TotalWeight { get; set; }
        public decimal TotalCost { get; set; } // Add this property
        public int PendingApprovalCount { get; set; }
        public List<ScrapItemViewModel> Items { get; set; } = new();
    }
    public class ScrapGroupViewModel
    {
        public string PartNumber { get; set; }
        public List<ScrapItemViewModel> Items { get; set; } = new();
    }
    public class ScrapItemViewModel
    {
        public int ScrapId { get; set; }
        public string PartNumber { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public string Shift { get; set; } = string.Empty;
        public string SectionName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int TerminalQuantity { get; set; }
        public decimal Length { get; set; }
        public string ScrapReason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string ClockNumber { get; set; } = string.Empty;
        public DateTime SubmissionDate { get; set; }
        public decimal WeightKg { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovalDate { get; set; }
        public string? ApprovalReason { get; set; }
        public string StockCode { get; set; } = string.Empty;
        public decimal MaterialCost { get; set; }
    }

    public class ScrapUpdateRequest
    {
        public int ScrapId { get; set; }
        public bool IsApproved { get; set; }
        public string VerificationPassword { get; set; }
        public string FirstApprovalReason { get; set; }
        public string FirstApprovalStatus { get; set; }
    }
    public class BulkUpdateRequest
    {
        public List<int> ScrapIds { get; set; } = new();
        public bool IsApproved { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string VerificationPassword { get; set; } = string.Empty;
        public string FirstApprovalReason { get; set; }
        public string FirstApprovalStatus { get; set; }
    }
    public class BulkScrapUpdateRequest
    {
        public List<int> ScrapIds { get; set; } = new List<int>();
        public bool IsApproved { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
    public class ScrapSummaryModel
    {
        public int ScrapId { get; set; }
        public string ClockNumber { get; set; }
        public int TerminalQuantity { get; set; }
        public string ScrapReason {  get; set; }
        public decimal Length { get; set; }
        public string PartNumber { get; set; }
        public string Department { get; set; }
        public string Line { get; set; }
        public string SubmittedClockNumber { get; set; }
        public string Shift { get; set; }
        public int Quantity { get; set; }
        public decimal WeightKg { get; set; }
        public string Status { get; set; }
        public DateTime SubmissionDate { get; set; }
        public decimal Cost { get; set; }
    }

    
    public class LocationUpdateRequest
    {
        [Required]
        public string Department { get; set; } = string.Empty;

        [Required]
        public string Line { get; set; } = string.Empty;

        [Required]
        public string Shift { get; set; } = string.Empty;
        [Required]
        public string VerificationPassword { get; set; } = string.Empty;
    }
}
