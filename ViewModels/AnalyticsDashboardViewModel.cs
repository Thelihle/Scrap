using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SCP_Control.ViewModels
{
    public class AnalyticsDashboardViewModel
    {
        // Current user context
        public string CurrentDepartment { get; set; } = string.Empty;
        public string CurrentLine { get; set; } = string.Empty;
        public string CurrentShift { get; set; } = string.Empty;

        // Location selection properties
        public int SelectedDepartmentId { get; set; }
        public int SelectedLineId { get; set; }
        public int SelectedShiftId { get; set; }
        public List<DepartmentViewModel> Departments { get; set; } = new List<DepartmentViewModel>();
        public List<ProductionLineViewModel> Lines { get; set; } = new List<ProductionLineViewModel>();
        public List<ShiftViewModel> Shifts { get; set; } = new List<ShiftViewModel>();

        // Filter options
        public string? FilterDepartment { get; set; }
        public string? FilterLine { get; set; }
        public string? FilterShift { get; set; }

        // Summary metrics
        public int TotalScrapEntries { get; set; }
        public int TotalScrapQuantity { get; set; }
        public int ApprovedScrapCount { get; set; }
        public int RejectedScrapCount { get; set; }
        public int PendingScrapCount { get; set; }
        public decimal TotalScrapCost { get; set; }

        // Detailed data
        public List<TopScrapItemViewModel> TopScrapItems { get; set; } = new List<TopScrapItemViewModel>();
        public List<ScrapBySectionViewModel> ScrapBySection { get; set; } = new List<ScrapBySectionViewModel>();
        public List<ScrapTrendViewModel> DailyTrends { get; set; } = new List<ScrapTrendViewModel>();
        public List<ActivitySummaryViewModel> ActivitySummary { get; set; } = new List<ActivitySummaryViewModel>();
        public DateRangeViewModel DateRange { get; set; } = new DateRangeViewModel();

        // NEW: Additional trend data
        public IEnumerable<dynamic> ApprovalFlowData { get; set; } = new List<dynamic>();
        public IEnumerable<dynamic> ScrapByReason { get; set; } = new List<dynamic>();
        public IEnumerable<dynamic> TopOperators { get; set; } = new List<dynamic>();
        public IEnumerable<dynamic> ScrapByDepartment { get; set; } = new List<dynamic>();
        public IEnumerable<dynamic> ScrapByShift { get; set; } = new List<dynamic>();
        public IEnumerable<dynamic> ScrapByLine { get; set; } = new List<dynamic>();

        // Weight statistics - FIXED: Removed duplicate AverageWeight
        public int TotalWeighings { get; set; }
        public decimal TotalWeight { get; set; }
        public decimal AverageWeight { get; set; }  // Only one definition now
        public decimal MaxWeight { get; set; }

        // Weight trend data
        public IEnumerable<dynamic> WeightByDepartment { get; set; } = new List<dynamic>();
        public IEnumerable<dynamic> WeightByShift { get; set; } = new List<dynamic>();
        public IEnumerable<dynamic> DailyWeightTrend { get; set; } = new List<dynamic>();
    }

    public class DateRangeViewModel
    {
        [Required]
        [Display(Name = "Start Date")]
        public DateTime StartDate { get; set; }

        [Required]
        [Display(Name = "End Date")]
        public DateTime EndDate { get; set; }
    }

    public class AnalyticsFilterViewModel
    {
        [Required]
        public DateTime StartDate { get; set; }

        [Required]
        public DateTime EndDate { get; set; }
        public string? Department { get; set; }
        public string? Line { get; set; }
        public string? Shift { get; set; }
    }

    public class TopScrapItemViewModel
    {
        public string PartNumber { get; set; } = string.Empty;
        public int TotalQuantity { get; set; }
        public string? Description { get; set; }
        public int OccurrenceCount { get; set; }
        public DateTime LastOccurrence { get; set; }
    }

    public class ProductionLineViewModel
    {
        public int LineId { get; set; }
        public string LineName { get; set; } = string.Empty;
        public int DepartmentId { get; set; }
    }

    public class ShiftViewModel
    {
        public int ShiftId { get; set; }
        public string ShiftName { get; set; } = string.Empty;
    }

    public class ScrapBySectionViewModel
    {
        public string ScrapSectionName { get; set; } = string.Empty;
        public int EntryCount { get; set; }
        public int TotalQuantity { get; set; }
    }

    public class ScrapByReasonViewModel
    {
        public string ReasonName { get; set; } = string.Empty;
        public int EntryCount { get; set; }
        public int TotalQuantity { get; set; }
    }

    public class ScrapTrendViewModel
    {
        public DateTime Date { get; set; }
        public int EntryCount { get; set; }
        public int TotalQuantity { get; set; }
        public decimal AverageWeight { get; set; }  // Changed to decimal for consistency
    }

    public class ActivitySummaryViewModel
    {
        public string ActivityType { get; set; } = string.Empty;
        public int ActivityCount { get; set; }
    }

    public class UserActivityViewModel
    {
        public string Username { get; set; } = string.Empty;
        public int ActivityCount { get; set; }
        public DateTime LastActivity { get; set; }
    }

    public class DetailedReportViewModel
    {
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public string Shift { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public int EntryCount { get; set; }
        public int TotalQuantity { get; set; }
        public decimal AverageWeight { get; set; }  // Changed to decimal for consistency
    }
}