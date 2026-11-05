using SCP_Control.ViewModels;
using SCP_Control.Models.Common;
using SCP_Control.Models;

namespace SCP_Control.Services.Interfaces
{
    public interface IScrapApprovalService
    {
        Task<IEnumerable<string>> GetAllDepartments();
        Task<IEnumerable<string>> GetAllLines();
        Task<IEnumerable<string>> GetLinesByDepartment(string department);
        Task<IEnumerable<string>> GetDepartmentsByUser(string? username);
        Task<IEnumerable<DepartmentViewModel>> GetDepartmentsWithDetails();
        Task<FilteredScrapDataModel> GetFilteredScrapData(
            string department = "",
            string line = "",
            string shift = "",
            string timeFrame = "",
            DateTime? startDate = null,
            DateTime? endDate = null);
        Task<IEnumerable<ScrapSummaryModel>> GetScrapDataByLocation(LocationDetails location);
        Task<IEnumerable<ScrapSummaryModel>> GetScrapDataForApproval(
            string department,
            string line,
            string shift);
        Task<IEnumerable<PendingApprovalModel>> GetPendingApprovals(string department);
        Task<Result> UpdateScrapStatus(int scrapId, bool isApproved, string reason, string approverClockNumber);
        Task<Result> BulkUpdateScrapStatus(
            List<int> scrapIds,
            bool isApproved,
            string reason,
            string approverClockNumber);
        Task<Result> ProcessFirstApproval(
            int scrapId,
            bool isApproved,
            string reason,
            string approverClockNumber);
    }
}