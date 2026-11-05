// ViewModels/UpdateLocationViewModel.cs
namespace SCP_Control.ViewModels
{
    public class UpdateLocationViewModel
    {
        public string? Department { get; set; }
        public string? Line { get; set; }
        public string? Shift { get; set; }
        public ICollection<DepartmentViewModel> DepartmentList { get; set; } = new List<DepartmentViewModel>();
        public ICollection<LineViewModel> LineList { get; set; } = new List<LineViewModel>();
    }

    public class DepartmentViewModel
    {
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;  // ✅ Add Location
        public string Plant { get; set; } = string.Empty;
        public string? Category { get; set; }   // ✅ add this
        // ✅ Update to show unique display names
        public string Value => Location;  // Use Location as the value
        public string Text => DepartmentName == "ENGINEERING"
            ? $"ENGINEERING - {Plant}"  // Show "ENGINEERING - Plant A"
            : Location;                  // Show Location for other departments
    }


    public class LineViewModel
    {
        public int LineId { get; set; }
        public string LineName { get; set; } = string.Empty;
        public string Value => LineId.ToString();
        public string Text => LineName;
    }

}