using SCP_Control.Models;

namespace SCP_Control.ViewModels
{
    public class OperatorSetupViewModel
    {
        public List<Department> Departments { get; set; } = new();
        public List<string> Shifts { get; set; } = new();
        public string? CurrentDepartment { get; set; }
        public string? CurrentLine { get; set; }
        public string? CurrentShift { get; set; }
        public string? CurrentMachine { get; set; }
    }
}
