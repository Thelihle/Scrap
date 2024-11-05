namespace SCP_Control.Models
{
    public class Line
    {
        public int LineId { get; set; }
        public string LineName { get; set; } = string.Empty;
        public int DepartmentId { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ModifiedAt { get; set; }
    }
}
