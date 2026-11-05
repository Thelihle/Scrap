using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SCP_Control.Models
{
    // User.cs
    public class User : IdentityUser<int>
    {
        [Key]
        public override int Id { get; set; }

        // These two are the crash culprits — must be nullable
        public string? ClockNumber { get; set; }
        public string? UserType { get; set; }

        public string? Department { get; set; }
        public string? Line { get; set; }

        [Column("Password")]
        public string? Password { get; set; }
        public string? MachineNumber { get; set; }
        public string? Shift { get; set; }
        public bool IsActive { get; set; }
        public bool? IsTemporary { get; set; }
        public bool? PasswordResetRequired { get; set; }
        public DateTime? LastPasswordChange { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? LastModifiedAt { get; set; }
        public string? LastModifiedBy { get; set; }
        public string? TempPassword { get; set; }
        public override string? PasswordHash { get; set; }

        [NotMapped] public override string? NormalizedUserName { get; set; }
        [NotMapped] public override string? NormalizedEmail { get; set; }
        [NotMapped] public override bool EmailConfirmed { get; set; }
        [NotMapped] public override string? SecurityStamp { get; set; }   // also add ? here
        [NotMapped] public override string? ConcurrencyStamp { get; set; } // and here
        [NotMapped] public override string? PhoneNumber { get; set; }
        [NotMapped] public override bool PhoneNumberConfirmed { get; set; }
        [NotMapped] public override bool TwoFactorEnabled { get; set; }
        [NotMapped] public override DateTimeOffset? LockoutEnd { get; set; }
        public override bool LockoutEnabled { get; set; }
        public override int AccessFailedCount { get; set; }

        public User()
        {
            IsActive = true;
            CreatedAt = DateTime.UtcNow;
            SecurityStamp = Guid.NewGuid().ToString();
            ConcurrencyStamp = Guid.NewGuid().ToString();
            LockoutEnabled = true;
        }
    }
}