using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SCP_Control.Models
{
    public class ScrapData
    {
        // ============================================================================
        // PRIMARY SUBMISSION DATA
        // ============================================================================

        [StringLength(20)]
        public string? ProcessedData { get; set; }
        public int ScrapId { get; set; }

        public decimal? Weight { get; set; }

        [StringLength(20)]
        public string? ScrapType { get; set; }

        [StringLength(1)]
        public string? PartCategory { get; set; }

        [StringLength(50)]
        public string? Plant { get; set; }

        // ✅ NEW: Engineering plant (Plant A, Plant B, Plant C, Fielli)
        [StringLength(50)]
        public string? EngineeringPlant { get; set; }

        public int? BatchScrapId { get; set; }

        [StringLength(50)]
        public string? MachineType { get; set; }

        [StringLength(10)]
        public string? MachineNumber { get; set; }

        [StringLength(1)]
        public string? Company { get; set; } = "B";

        public string? ClockNumber { get; set; }

        public string? PartNumber { get; set; }

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than 0")]
        public int Quantity { get; set; }

        public string? ScrapReason { get; set; }

        [Required]
        public DateTime SubmissionDate { get; set; } = DateTime.Now;

        public string? Status { get; set; }

        public string? Department { get; set; }

        public string? Line { get; set; }

        public string? Shift { get; set; }

        [Required]
        public int? ScrapSectionId { get; set; }

        public int? ReasonId { get; set; }

        public string? Position { get; set; }

        [Column(TypeName = "decimal(10,3)")]
        public decimal? Length { get; set; }

        public string? StockUom { get; set; }

        [Column(TypeName = "decimal(18,5)")]
        public decimal? MaterialCost { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string? CreatedBy { get; set; }

        [Required]
        public DateTime ModifiedAt { get; set; } = DateTime.Now;

        public string? ModifiedBy { get; set; }

        // ============================================================================
        // BASIC APPROVAL FIELDS
        // ============================================================================

        public string? ApprovedBy { get; set; }
        public DateTime? ApprovalDate { get; set; }
        public string? ApprovalReason { get; set; }
        public string? RejectedBy { get; set; }
        public string? RejectedReason { get; set; }

        // ============================================================================
        // TEAM LEADER APPROVAL
        // ============================================================================

        [StringLength(20)]
        public string? TeamLeaderApprovalStatus { get; set; }

        [StringLength(50)]
        public string? TeamLeaderApprovalBy { get; set; }

        public DateTime? TeamLeaderApprovalDate { get; set; }

        // ============================================================================
        // GROUP LEADER APPROVAL
        // ============================================================================

        [StringLength(20)]
        public string? GroupLeaderApprovalStatus { get; set; }

        [StringLength(50)]
        public string? GroupLeaderApprovalBy { get; set; }

        public DateTime? GroupLeaderApprovalDate { get; set; }

        // ============================================================================
        // MANAGER APPROVAL
        // ============================================================================

        [StringLength(20)]
        public string? ManagerApprovalStatus { get; set; }

        [StringLength(50)]
        public string? ManagerApprovalBy { get; set; }

        public DateTime? ManagerApprovalDate { get; set; }

        // ============================================================================
        // SENIOR MANAGER APPROVAL
        // ============================================================================

        [StringLength(20)]
        public string? SeniorManagerApprovalStatus { get; set; }

        [StringLength(50)]
        public string? SeniorManagerApprovalBy { get; set; }

        public DateTime? SeniorManagerApprovalDate { get; set; }

        // ============================================================================
        // APPROVAL WORKFLOW CONTROL
        // ============================================================================

        public int? CurrentApprovalLevel { get; set; }

        [StringLength(20)]
        public string? FirstApprovalStatus { get; set; }

        [StringLength(50)]
        public string? FirstApprovalBy { get; set; }

        public DateTime? FirstApprovalDate { get; set; }

        [StringLength(500)]
        public string? FirstApprovalReason { get; set; }

        // ============================================================================
        // WEIGHING FIELDS
        // ============================================================================

        public bool? IsWeighed { get; set; }
        public DateTime? LastWeighedAt { get; set; }

        [StringLength(50)]
        public string? LastWeighedBy { get; set; }

        // ============================================================================
        // VALIDATION METHOD
        // ============================================================================

        public bool IsValid()
        {
            if (string.IsNullOrWhiteSpace(ClockNumber) ||
                string.IsNullOrWhiteSpace(PartNumber) ||
                string.IsNullOrWhiteSpace(ScrapReason) ||
                Quantity <= 0)
            {
                return false;
            }

            // CombiMeter requires position
            if (ScrapSectionId == 2 && string.IsNullOrWhiteSpace(Position))
                return false;

            // Engineering requires a plant selection
            if (ScrapSectionId == 10 && string.IsNullOrWhiteSpace(EngineeringPlant))
                return false;

            return true;
        }

        // ============================================================================
        // DISPLAY HELPERS
        // ============================================================================

        [NotMapped]
        public string FormattedSubmissionDate => SubmissionDate.ToString("yyyy-MM-dd HH:mm");

        [NotMapped]
        public string StatusDisplay => Status switch
        {
            "Pending" => "⏳ Pending",
            "Approved" => "✅ Approved",
            "Rejected" => "❌ Rejected",
            _ => Status ?? ""
        };

        [NotMapped]
        public string CurrentApprovalLevelDisplay => CurrentApprovalLevel switch
        {
            1 => "👥 Team Leader Review",
            2 => "👤 Group Leader Review",
            3 => "👔 Manager Review",
            4 => "🎯 Senior Manager Review",
            5 => "✅ Approval Complete",
            _ => "⏳ Pending Review"
        };

        [NotMapped]
        public string LocationDisplay => $"{Department} - {Line} (Shift {Shift})";

        [NotMapped]
        public string ApprovalSummary
        {
            get
            {
                if (!string.IsNullOrEmpty(ApprovedBy))
                    return $"✅ Approved by {ApprovedBy}";
                if (!string.IsNullOrEmpty(RejectedBy))
                    return $"❌ Rejected by {RejectedBy}";
                return $"⏳ Pending {CurrentApprovalLevel} Review";
            }
        }

        [Column(TypeName = "decimal(10,3)")]
        public decimal? ActualWeight { get; set; }

        [NotMapped]
        public DateTime? RejectedDate { get; set; }

        [MaxLength(50)]
        public string? WeightRecordedBy { get; set; }

        public DateTime? WeightRecordedDate { get; set; }

        public bool? ScaleAvailable { get; set; }

        [MaxLength(255)]
        public string? WeightNotRecordedReason { get; set; }
    }
}