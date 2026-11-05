using SCP_Control.Models;
using SCP_Control.Models.Common;
using SCP_Control.ViewModels;

namespace SCP_Control.Services.Interfaces
{
    public interface IDepartmentLineService
    {
        Task<LocationUpdateViewModel> GetTeamLeaderLocation(string clockNumber);
        Task<IEnumerable<DepartmentViewModel>> GetActiveDepartments();
        Task<IEnumerable<LineViewModel>> GetLinesByDepartment(int departmentId);
        Task<bool> UpdateTeamLeaderLocation(string clockNumber, string department, string line, string shift);
        Task<IDictionary<string, IEnumerable<string>>> GetAllDepartmentsAndLines();
    }
}
