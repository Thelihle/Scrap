using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SCP_Control.Models
{
    [Table("DeviceTokens")]
    [Index(nameof(Token), IsUnique = true)]
    public class DeviceToken
    {
        [Key]
        public int TokenId { get; set; }

        [Required]
        public int DeviceId { get; set; }

        [Required]
        [StringLength(100)]
        public string Token { get; set; } = "";

        public DateTime ExpiresAt { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation property
        [ForeignKey("DeviceId")]
        public virtual RegisteredDevice Device { get; set; } = null!;
    }
}