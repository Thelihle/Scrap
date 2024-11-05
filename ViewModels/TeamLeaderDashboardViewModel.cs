namespace SCP_Control.ViewModels
{
    public class TeamLeaderDashboardViewModel
    {
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public string Shift { get; set; } = string.Empty;
        public List<ScrapDataViewModel> ScrapItems { get; set; } = new();
        public List<UserRegistrationViewModel> PendingRegistrations { get; set; } = new();
        public List<DepartmentViewModel> Departments { get; set; } = new();
    }
}
