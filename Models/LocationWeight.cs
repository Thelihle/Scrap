namespace SCP_Control.Models
{
    public class LocationWeight
    {
        public int Id { get; set; }
        public string Department { get; set; }
        public string Line { get; set; }
        public string Shift { get; set; }
        public decimal Weight { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; }
        public bool IsActive { get; set; }
    }
}
