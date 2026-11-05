using Microsoft.AspNetCore.Mvc.Rendering;
using SCP_Control.Models;
using SCP_Control.ViewModels;

namespace SCP_Control.Services.Interfaces
{
    public interface IOperatorService
    {
        Task<ScrapSection?> GetScrapSectionById(int sectionId);
        Task<List<SelectListItem>> GetScrapReasons();
        Task<ScrapReason?> GetScrapReasonDetails(int reasonId);
        Task<(bool Success, string Message)> UpdateScrapEntry(UpdateScrapEntryRequest request);
        Task<(bool Success, string Message)> DeleteScrapEntry(int entryId);
        Task<List<PendingScrapEntry>> GetPendingScrapEntries(string clockNumber);
        Task<OperatorDetailsViewModel?> GetOperatorDetails(string clockNumber);
        Task<List<Models.Department>> GetActiveDepartments();
        Task<List<Line>> GetLinesByDepartment(string departmentName);
        Task<bool> UpdateOperatorDetails(string clockNumber, UpdateOperatorDetailsViewModel model);
        Task<List<ScrapError>> GetScrapErrorsBySection(int sectionId);
        Task<(bool IsValid, string Message)> ValidateScrapEntry(ScrapEntryValidationRequest request);
        Task<List<SelectListItem>> GetActiveScrapSections();
        Task<List<SelectListItem>> GetScrapReasonsBySection(int sectionId);
        Task<ScrapEntry?> GetExistingScrapEntry(string partNumber);
        Task<List<string>> GetPartPositions(int sectionId, int reasonId);
        Task<bool> SaveScrapEntry(ScrapEntry entry);
        Task<bool> SectionRequiresPosition(int sectionId);
        Task<bool> SubmitScrapEntry(ScrapEntry entry);
    }
}