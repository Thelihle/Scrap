namespace SCP_Control.Services.Interfaces
{
    public interface IAuditService
    {
        Task LogActivityAsync(string clockNumber, string actionType, string actionDetails, string status);
        Task LogErrorAsync(string clockNumber, Exception ex);
    }
}
