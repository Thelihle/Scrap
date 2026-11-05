
namespace SCP_Control.Services.Interfaces
{
    public interface IBatchIdService
    {
        Task<int> GetNextBatchIdAsync(string clockNumber);
    }
}