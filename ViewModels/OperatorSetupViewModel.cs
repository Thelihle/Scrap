using SCP_Control.Models;

namespace SCP_Control.ViewModels
{
    public class OperatorSetupViewModel
    {
        public List<Department> Departments { get; set; } = new();
        public List<Line> Lines { get; set; } = new();
        public List<string> Shifts { get; set; } = new() { "A", "B", "Straight" };
    }
}
