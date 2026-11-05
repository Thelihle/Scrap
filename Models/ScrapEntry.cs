using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SCP_Control.Models
{
    [Table("ScrapData")] // Matches your actual table name
    public class ScrapEntry
    {
        [Key]
        public int ScrapId { get; set; } // Primary key matches database

        // Required fields from frontend
        [Required]
        public string PartNumber { get; set; } = string.Empty;

        [Required]
        public int? ScrapSectionId { get; set; }

        [Required]
        public int? ReasonId { get; set; }

        [Required]
        public string ScrapReason { get; set; } = string.Empty;

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than 0")]
        public int Quantity { get; set; }

        // Server-set fields (no [Required] validation)
        public string ClockNumber { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public string ModifiedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        public DateTime SubmissionDate { get; set; }

        // Standard fields from database
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public string Shift { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string StockUom { get; set; } = string.Empty;
        public decimal MaterialCost { get; set; }

        // Approval fields from database
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovalDate { get; set; }
        public string? ApprovalReason { get; set; }
        public string? RejectedBy { get; set; }
        public string? RejectedReason { get; set; }

        // Team Leader Approval
        public string? TeamLeaderApprovalStatus { get; set; }
        public string? TeamLeaderApprovalBy { get; set; }
        public DateTime? TeamLeaderApprovalDate { get; set; }

        // Group Leader Approval
        public string? GroupLeaderApprovalStatus { get; set; }
        public string? GroupLeaderApprovalBy { get; set; }
        public DateTime? GroupLeaderApprovalDate { get; set; }

        // Manager Approval
        public string? ManagerApprovalStatus { get; set; }
        public string? ManagerApprovalBy { get; set; }
        public DateTime? ManagerApprovalDate { get; set; }

        // Senior Manager Approval
        public string? SeniorManagerApprovalStatus { get; set; }
        public string? SeniorManagerApprovalBy { get; set; }
        public DateTime? SeniorManagerApprovalDate { get; set; }

        // First Approval
        public string? FirstApprovalStatus { get; set; }
        public string? FirstApprovalBy { get; set; }
        public DateTime? FirstApprovalDate { get; set; }
        public string? FirstApprovalReason { get; set; }

        // Current approval level tracking
        public string? CurrentApprovalLevel { get; set; }

        // Weighing fields
        public bool? IsWeighed { get; set; }
        public DateTime? LastWeighedAt { get; set; }
        public string? LastWeighedBy { get; set; }

        // Navigation property
        public virtual ScrapSection? ScrapSection { get; set; }

        // Validation method
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(PartNumber) &&
                   ScrapSectionId.HasValue &&
                   ReasonId.HasValue &&
                   !string.IsNullOrEmpty(ScrapReason) &&
                   !string.IsNullOrEmpty(Department) &&
                   !string.IsNullOrEmpty(Line) &&
                   !string.IsNullOrEmpty(Shift) &&
                   Quantity > 0;
        }
    }
}