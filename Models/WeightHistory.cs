using System.ComponentModel.DataAnnotations;

namespace SCP_Control.Models
{
    public class WeightHistory
    {
        [Key]
        public int WeightId { get; set; }

        [Required]
        [StringLength(100)]
        public string Department { get; set; } = "";

        [Required]
        [StringLength(100)]
        public string Line { get; set; } = "";

        [Required]
        [StringLength(50)]
        public string Shift { get; set; } = "";

        [Required]
        [Range(0.001, 999999.999)]
        public decimal Weight { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        [StringLength(50)]
        public string CreatedBy { get; set; } = "";

        [Required]
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

        [Required]
        [StringLength(50)]
        public string ModifiedBy { get; set; } = "";

        [StringLength(500)]
        public string? Description { get; set; }

        [StringLength(100)]
        public string? SecurityStamp { get; set; }

        public bool IsBulkWeighing { get; set; } = false;
    }
}
