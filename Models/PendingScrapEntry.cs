using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SCP_Control.Models
{
    public class PendingScrapEntry
    {
        public int EntryId { get; set; }
        public int ScrapId { get; set; }
        public string ClockNumber { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int TerminalQuantity { get; set; }
        public decimal WeightKg { get; set; }
        public decimal Length { get; set; }
        public string ScrapReason { get; set; } = string.Empty;
        public string SectionName { get; set; } = string.Empty;
        public string? Position { get; set; }
        public string ReasonName { get; set; } = string.Empty;
        public DateTime? SubmissionDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public string Shift { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        // Navigation properties 
        public virtual ScrapSection? Section { get; set; }
        public virtual ScrapReason? Reason { get; set; }
    }
}