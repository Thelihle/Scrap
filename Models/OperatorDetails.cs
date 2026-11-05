namespace SCP_Control.Models
{
    public class OperatorDetails
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public int DepartmentId { get; set; }
        public int LineId { get; set; }
        public string Shift { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
