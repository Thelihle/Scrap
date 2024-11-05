namespace SCP_Control.Models;

public class User
{
    public int UserId { get; set; }
    public string ClockNumber { get; set; }
    public string Password { get; set; }
    public string UserType { get; set; } // Operator, TeamLeader, GroupLeader, Manager, SeniorManager, Admin
    public bool IsTemporary { get; set; }
    public DateTime? LastPasswordChange { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string LastModifiedBy { get; set; }
    public string Department { get; set; }
    public string Line { get; set; }
    public string Shift { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
}