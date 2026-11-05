namespace SCP_Control.Models
{
    public class ScrapReason
    {
        public int ErrorId { get; set; }
        public int ScrapSectionId { get; set; }
        public string ScrapReasonName { get; set; } = string.Empty;
        public string? Part_Position { get; set; }
        public string? Defect_Item { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public string? ModifiedBy { get; set; }
        public string Description { get; set; } = string.Empty;

        // Navigation property
        public virtual ICollection<ScrapData> ScrapData { get; set; } = new List<ScrapData>();
    }
}
