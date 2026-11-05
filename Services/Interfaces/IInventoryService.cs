using SCP_Control.Models;

namespace SCP_Control.Services.Interfaces
{
    public interface IInventoryService
    {
        
       
            Task<bool> ValidatePartNumber(string partNumber);
            Task<IEnumerable<Department>> GetDepartments();
            Task<IEnumerable<Line>> GetLinesByDepartment(int departmentId);
        // Updated method
        Task<(bool IsValid, string StockUom, decimal MaterialCost)> ValidatePartNumberWithUom(string partNumber);
    }
}
