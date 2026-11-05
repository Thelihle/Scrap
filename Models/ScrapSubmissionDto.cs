using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SCP_Control.Models
{
    // ============================================================================
    // DTO: Data Transfer Object for Frontend Scrap Submissions
    // ============================================================================

    public class ScrapSubmissionDto
    {
        // ONLY fields that the frontend should send

        [Required(ErrorMessage = "PartNumber is required")]
        [StringLength(100, ErrorMessage = "PartNumber cannot exceed 100 characters")]
        [JsonPropertyName("partNumber")]
        public string PartNumber { get; set; } = "";

        [Required(ErrorMessage = "ScrapReason is required")]
        [StringLength(200, ErrorMessage = "ScrapReason cannot exceed 200 characters")]
        [JsonPropertyName("scrapReason")]
        public string ScrapReason { get; set; } = "";

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than 0")]
        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "ScrapSectionId must be greater than 0")]
        [JsonPropertyName("scrapSectionId")]
        public int ScrapSectionId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "ReasonId must be greater than 0")]
        [JsonPropertyName("reasonId")]
        public int ReasonId { get; set; }

        [StringLength(100, ErrorMessage = "Position cannot exceed 100 characters")]
        [JsonPropertyName("position")]
        public string Position { get; set; } = "";

        [Range(0, double.MaxValue, ErrorMessage = "MaterialCost cannot be negative")]
        [JsonPropertyName("materialCost")]
        public decimal MaterialCost { get; set; } = 0;

        // ============================================================================
        // VALIDATION METHOD
        // ============================================================================

        public bool IsValid()
        {
            // Basic validation
            if (string.IsNullOrWhiteSpace(PartNumber) ||
                string.IsNullOrWhiteSpace(ScrapReason) ||
                Quantity <= 0 ||
                ScrapSectionId <= 0 ||
                ReasonId <= 0)
            {
                return false;
            }

            // CombiMeter validation
            if (ScrapSectionId == 2 && string.IsNullOrWhiteSpace(Position))
            {
                return false;
            }

            return true;
        }

        // ============================================================================
        // MAPPING METHOD: Convert DTO to Full ScrapData Model
        // ============================================================================

        public ScrapData ToScrapData(string clockNumber, string department, string line, string shift, string stockUom = "")
        {
            var now = DateTime.Now;

            return new ScrapData
            {
                // Map from DTO
                PartNumber = this.PartNumber?.Trim() ?? "",
                ScrapReason = this.ScrapReason?.Trim() ?? "",
                Quantity = this.Quantity,
                ScrapSectionId = this.ScrapSectionId,
                ReasonId = this.ReasonId,
                Position = this.Position?.Trim() ?? "",
                MaterialCost = this.MaterialCost,

                // Set by server
                ClockNumber = clockNumber,
                CreatedBy = clockNumber,
                ModifiedBy = clockNumber,
                Department = department,
                Line = line,
                Shift = shift,
                StockUom = stockUom,

                // Default values
                SubmissionDate = now,
                CreatedAt = now,
                ModifiedAt = now,
                Status = "Pending",

                // FIXED: Use int instead of string
                CurrentApprovalLevel = 1, // 1 = TeamLeader

                // Initialize approval fields as empty strings (or int if needed)
                ApprovedBy = "",
                ApprovalReason = "",
                RejectedBy = "",
                RejectedReason = "",
                TeamLeaderApprovalStatus = "",
                TeamLeaderApprovalBy = "",
                GroupLeaderApprovalStatus = "",
                GroupLeaderApprovalBy = "",
                ManagerApprovalStatus = "",
                ManagerApprovalBy = "",
                SeniorManagerApprovalStatus = "",
                SeniorManagerApprovalBy = "",
                FirstApprovalStatus = "",
                FirstApprovalBy = "",
                FirstApprovalReason = "",
                IsWeighed = false,
                LastWeighedBy = ""
            };
        }

        // ============================================================================
        // STEP 3: ADD CONSTANTS FOR CLARITY
        // ============================================================================

        public static class ApprovalLevels
        {
            public const int TeamLeader = 1;
            public const int GroupLeader = 2;
            public const int Manager = 3;
            public const int SeniorManager = 4;
            public const int Completed = 5;
        }
    }
}