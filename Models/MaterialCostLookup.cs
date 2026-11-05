using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SCP_Control.Models
{
    // This matches your DbContext configuration as a view with HasNoKey()
    public class MaterialCostLookup
    {
        // No [Key] attribute since DbContext uses HasNoKey()
        public string StockCode { get; set; } = string.Empty;

        public string PartNumber { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal MaterialCost { get; set; }

        public string StockUom { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public DateTime? LastUpdated { get; set; }

        public string UpdatedBy { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        // Computed properties (not mapped to database)
        [NotMapped]
        public string FormattedMaterialCost => $"R {MaterialCost:N2}";

        [NotMapped]
        public string DisplayName => !string.IsNullOrEmpty(PartNumber) && !string.IsNullOrEmpty(Description)
            ? $"{PartNumber} - {Description}"
            : PartNumber;

        [NotMapped]
        public string LastUpdatedFormatted => LastUpdated?.ToString("yyyy-MM-dd HH:mm") ?? "N/A";
    }
}