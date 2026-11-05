using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SCP_Control.Models
{
    public class UserRegistrationRequest : IValidatableObject
    {
        [Key]
        public int RequestId { get; set; }

        [Required(ErrorMessage = "Clock Number is required")]
        [StringLength(50)]
        [RegularExpression(@"^[0-9]{1,6}$", ErrorMessage = "Clock Number must contain only numbers (1-6 digits)")]
        public string? ClockNumber { get; set; }

        [Required(ErrorMessage = "Name is required")]
        [StringLength(100)]
        [RegularExpression(@"^[a-zA-Z\s]+$", ErrorMessage = "Name must contain only letters and spaces")]
        public string? Name { get; set; }

        // Nullable - surname optional for Admin/SeniorManager roles
        [StringLength(100)]
        [RegularExpression(@"^[a-zA-Z\s]*$", ErrorMessage = "Surname must contain only letters")]
        public string? Surname { get; set; }

        // No [Required] - email requirement handled per-role in Validate()
        [EmailAddress]
        [StringLength(50)]
        public string? Email { get; set; }

        [Required(ErrorMessage = "User Type is required")]
        [StringLength(50)]
        public string? UserType { get; set; }

        [Required(ErrorMessage = "Department is required")]
        public int DepartmentId { get; set; }

        public int? LineId { get; set; }

        [Required(ErrorMessage = "Shift is required")]
        [StringLength(10)]
        public string Shift { get; set; }
        [NotMapped]
        public string? ZoneName { get; set; }

        [NotMapped]
        public string? LineName { get; set; }

        [NotMapped]
        public string? MachineNumber { get; set; }

        [StringLength(50)]
        public string Status { get; set; } = "Pending";

        public DateTime RequestDate { get; set; } = DateTime.UtcNow;

        [StringLength(50)]
        public string? ProcessedBy { get; set; }

        public DateTime? ProcessedDate { get; set; }

        public virtual Department? Department { get; set; }
        public virtual Line? Line { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var noEmailRoles = new[]
            {
            "operator", "teamleader", "engineer", "engineering teamleader"
        };

            var lineOptionalRoles = new[]
            {
            "teamleader", "groupleader", "manager", "seniormanager", "admin",
            "engineering teamleader", "engineering groupleader",
            "engineering manager", "engineering seniormanager", "engineering admin"
        };

            var userTypeLower = UserType?.ToLower().Trim() ?? "";

            if (DepartmentId == 0)
                yield return new ValidationResult("Department is required", new[] { nameof(DepartmentId) });

            if (string.IsNullOrEmpty(Shift))
                yield return new ValidationResult("Shift is required", new[] { nameof(Shift) });

            if (!lineOptionalRoles.Contains(userTypeLower) && (LineId == null || LineId == 0))
                yield return new ValidationResult("Production line is required for this user type", new[] { nameof(LineId) });

            if (!noEmailRoles.Contains(userTypeLower) && string.IsNullOrEmpty(Email))
                yield return new ValidationResult("Email is required for this role", new[] { nameof(Email) });

            if (!string.IsNullOrEmpty(Email))
            {
                var emailPattern = @"^[a-zA-Z]+(\.[a-zA-Z]+)?@hesto\.co\.za$";
                if (!System.Text.RegularExpressions.Regex.IsMatch(Email, emailPattern))
                    yield return new ValidationResult(
                        "Email must be in format: firstname.lastname@hesto.co.za",
                        new[] { nameof(Email) });
            }
        }
    }
}