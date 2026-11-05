namespace SCP_Control.Models
{
    public class ScrapError
    {
        public int ErrorId { get; set; }
        public string ErrorName { get; set; } = string.Empty;
        public int ScrapSectionId { get; set; }
        public bool RequiresPosition { get; set; }
        public string? DefectItem { get; set; }
        public string? PartDescription { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ModifiedAt { get; set; }

        // Navigation property
        public virtual ScrapSection Section { get; set; } = null!;
    }
}
