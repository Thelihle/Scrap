using Microsoft.AspNetCore.Http.HttpResults;
namespace SCP_Control.ViewModels
{
    public class ScrapDataViewModel
    {
        // Basic Properties
        public int Id { get; set; }
        public int ScrapId { get; set; }
        public int? SubmittedByUserId { get; set; }
        public string PartNumber { get; set; } = string.Empty;
        public string ScrapType { get; set; } = string.Empty;
        // Location Info
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public string Shift { get; set; } = string.Empty;

        // Quantities and Measurements
        public int Quantity { get; set; }
        public decimal? WeightKg { get; set; }
        public decimal? TotalCost { get; set; }
        public decimal? MaterialCost { get; set; }
        public string StockUom { get; set; } = string.Empty; // Added missing property
        public string CreatedAtFormatted { get; set; }
      
        // Status and Tracking
        public string Status { get; set; } = string.Empty;
        public string? Position { get; set; }
        public string SectionName { get; set; } = string.Empty;
        public string ErrorName { get; set; } = string.Empty;
        public string? Comments { get; set; }

        // Dates
        public DateTime CreatedAt { get; set; }
        public DateTime EntryDate { get; set; }
        public DateTime? SubmissionDate { get; set; }
        public DateTime RawCreatedAt { get; set; }

        // User Information
        public string SubmittedBy { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;

        // Approval Information
        public string? FirstApprovalBy { get; set; }
        public DateTime? FirstApprovalDate { get; set; }
        public string? FirstApprovalStatus { get; set; }
        public string? FirstApprovalReason { get; set; }
        public string? ApprovedBy { get; set; }
        public string? ApprovalDate { get; set; }
        public int CanApprove { get; set; }

        // Computed Properties
        public decimal TotalItemCost => (MaterialCost ?? 0m) * Quantity;

        // Formatted Display Properties
        public string FormattedCreatedAt => CreatedAt.ToString("yyyy-MM-dd HH:mm");
        public string FormattedDate => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
        public string FormattedWeight => $"{WeightKg?.ToString("N2") ?? "0.00"} kg";

        // Cost Formatting Methods
        public string GetFormattedMaterialCost() => $"R {MaterialCost?.ToString("N2") ?? "0.00"}";
        public string GetFormattedTotalCost() => $"R {TotalItemCost.ToString("N2")}";

        // Rejection reason is already in the existing model
        public string RejectedReason { get; set; } = string.Empty;

        // New multi-level approval properties
        public int? CurrentApprovalLevel { get; set; }

        // TeamLeader approval properties
        public string TeamLeaderApprovalStatus { get; set; } = string.Empty;
        public string TeamLeaderApprovalBy { get; set; } = string.Empty;
        public DateTime? TeamLeaderApprovalDate { get; set; }

        // GroupLeader approval properties
        public string GroupLeaderApprovalStatus { get; set; } = string.Empty;
        public string GroupLeaderApprovalBy { get; set; } = string.Empty;
        public DateTime? GroupLeaderApprovalDate { get; set; }

        // Manager approval properties
        public string ManagerApprovalStatus { get; set; } = string.Empty;
        public string ManagerApprovalBy { get; set; } = string.Empty;
        public DateTime? ManagerApprovalDate { get; set; }

        // SeniorManager approval properties
        public string SeniorManagerApprovalStatus { get; set; } = string.Empty;
        public string SeniorManagerApprovalBy { get; set; } = string.Empty;
        public DateTime? SeniorManagerApprovalDate { get; set; }


        // ✅ ADD THESE 5 PROPERTIES
        public string SubmitterClockNumber { get; set; } = "N/A";
        public string TeamLeaderClockNumber { get; set; } = "N/A";
        public string GroupLeaderClockNumber { get; set; } = "N/A";
        public string ManagerClockNumber { get; set; } = "N/A";
        public decimal? RecordedWeight { get; set; }


    }

    // Added missing WeightHistoryViewModel
    public class WeightHistoryViewModel
    {
        public int Id { get; set; }
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public string Shift { get; set; } = string.Empty;
        public decimal Weight { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime? ModifiedAt { get; set; }
        public string? ModifiedBy { get; set; }

        public string FormattedCreatedAt => CreatedAt.ToString("yyyy-MM-dd HH:mm");
        public string FormattedWeight => $"{Weight:N2} kg";
        public string LocationLabel => $"{Department}/{Line}/{Shift}";

    }

    public class UpdateScrapLocationRequest
    {
        public List<int> ScrapIds { get; set; } = new();
        public string Department { get; set; } = "";
        public string Line { get; set; } = "";
        public string Shift { get; set; } = "";
    }
}