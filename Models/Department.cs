using System.ComponentModel.DataAnnotations;

namespace SCP_Control.Models;

public class Department
{
    public int DepartmentId { get; set; }

    [Required]
    [StringLength(200)]  // Based on the nvarchar(200) in your DB
    public string DepartmentName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public string? Category { get; set; }
    // New SYSPRO fields
    public string Plant { get; set; }
    public string WH { get; set; }  // Warehouse
    public string PostType { get; set; }
    public string PostingGlCode { get; set; }
    public string NominatingPostType { get; set; }
    public string NominatingPostingGlCode { get; set; }


    [Required]
    public string Location { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? ModifiedAt { get; set; }

    // Navigation property
    public ICollection<Line> Lines { get; set; } = new List<Line>();
}