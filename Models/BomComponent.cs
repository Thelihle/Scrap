using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SCP_Control.Models
{
    [Table("scpBomComponents")]
    public class BomComponent
    {
        [Key]
        public int BomComponentId { get; set; }

        [Required]
        public int ScrapId { get; set; }
        public decimal ScrapQty { get; set; }
        public string? SequenceNum { get; set; }
        [Required]
        [MaxLength(50)]
        public string ParentPart { get; set; }

        [Required]
        [MaxLength(50)]
        public string Component { get; set; }

        [MaxLength(255)]
        public string Description { get; set; }

        [MaxLength(500)]
        public string LongDesc { get; set; }

        [Column(TypeName = "decimal(18,5)")]
        public decimal? QtyPer { get; set; }

        [MaxLength(10)]
        public string Uom { get; set; }

        [MaxLength(50)]
        public string ProductClass { get; set; }

        [MaxLength(10)]
        public string PartCategory { get; set; }

        [MaxLength(100)]
        public string UserField1 { get; set; }

        [MaxLength(100)]
        public string UserField2 { get; set; }

        [Required]
        public DateTime ProcessedAt { get; set; }

        [Required]
        [MaxLength(50)]
        public string ProcessedBy { get; set; }

        // Navigation property
        [ForeignKey("ScrapId")]
        public virtual ScrapData ScrapData { get; set; }
    }
}