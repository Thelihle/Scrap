namespace SCP_Control.Models
{
    public class RegistrationResult
    {
        public bool Success { get; set; }
        public string? TempPassword { get; set; }
        public string? Message { get; set; }
    }
}
