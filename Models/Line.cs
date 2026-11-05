namespace SCP_Control.Models
{
    public class Line
    {
        public int LineId { get; set; }
        public string LineName { get; set; } = string.Empty;
        public int DepartmentId { get; set; }
        public bool IsActive { get; set; }
        public int? ParentLineId { get; set; }
        public Line? Parent
        {
            get; set;
        }
        public DateTime CreatedAt { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public string? LineType { get; set; }

        // Navigation property
        public virtual Department Department { get; set; } = null!;
    }
}
