namespace SCP_Control.Models
{
    public class LoginAttempt
    {
        public int Id { get; set; }
        public required string Identifier { get; set; }
        public DateTime AttemptTime { get; set; }
        public bool IsSuccessful { get; set; }
        public string? IpAddress { get; set; }
    }
}
