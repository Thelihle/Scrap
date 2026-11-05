namespace SCP_Control.Models
{
    public class AccessGrantRequest
    {
        public string Username { get; set; }
        public string AccessLevel { get; set; }
        public string VerificationPassword { get; set; }
    }
}
