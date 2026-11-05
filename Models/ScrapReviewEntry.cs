using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SCP_Control.Models
{
    [Table("scpScrapReviewEntries")]
    public class ScrapReviewEntry
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(450)]
        public string SecurityStamp { get; set; } = string.Empty;

        public int ScrapSectionId { get; set; }

        [StringLength(100)]
        public string? SectionName { get; set; }

        public int ReasonId { get; set; }

        [StringLength(200)]
        public string? ScrapReason { get; set; }

        [StringLength(100)]
        public string? Position { get; set; }

        public int Quantity { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

        public int OrderIndex { get; set; }

        // Navigation property
        [ForeignKey("SecurityStamp")]
        public virtual ScrapReviewSession? Session { get; set; }
    }
}