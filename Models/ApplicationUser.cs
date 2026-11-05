// In Models/ApplicationUser.cs
using Microsoft.AspNetCore.Identity;

namespace SCP_Control.Models
{
    public class ApplicationUser : IdentityUser
    {
        
        public string Department { get; set; }
        public string Line { get; set; }
        public string Shift { get; set; }
        
    }
}