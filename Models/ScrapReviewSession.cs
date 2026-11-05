using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SCP_Control.Models
{
    [Table("scpScrapReviewSessions")]
    public class ScrapReviewSession
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(450)]
        public string SecurityStamp { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string ClockNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string PartNumber { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Department { get; set; }

        [StringLength(100)]
        public string? Line { get; set; }

        [StringLength(50)]
        public string? Shift { get; set; }

        [StringLength(20)]
        public string? StockUom { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? MaterialCost { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastActivity { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;

        // Navigation property
        public virtual ICollection<ScrapReviewEntry> Entries { get; set; } = new List<ScrapReviewEntry>();
    }
}