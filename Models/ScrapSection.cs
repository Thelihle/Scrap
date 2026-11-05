namespace SCP_Control.Models
{
    public class ScrapSection
    {
        public int ScrapSectionId { get; set; }
        //public int ErrorId { get; set; }
        public string ScrapSectionName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public bool RequiresPosition { get; set; }
        

        // Navigation property for errors
        public virtual ICollection<ScrapError> Errors { get; set; } = new List<ScrapError>();
    }
}