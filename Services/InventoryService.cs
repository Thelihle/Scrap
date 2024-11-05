using System.Data.SqlClient;
using Dapper;
using SCP_Control.Models;

namespace SCP_Control.Services
{
    public class InventoryService
    {
        private readonly string _scpConnectionString;
        private readonly string _sysproConnectionString;
        private readonly ILogger<InventoryService> _logger;

        public InventoryService(IConfiguration configuration, ILogger<InventoryService> logger)
        {
            _scpConnectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _sysproConnectionString = configuration.GetConnectionString("SysproConnection") ??
                throw new InvalidOperationException("Connection string 'SysproConnection' not found.");
            _logger = logger;
        }

        public async Task<bool> ValidatePartNumber(string partNumber)
        {
            try
            {
                using var connection = new SqlConnection(_sysproConnectionString);
                const string query = @"
                    SELECT COUNT(1) 
                    FROM InvMaster 
                    WHERE StockCode = @PartNumber";

                var count = await connection.ExecuteScalarAsync<int>(query, new { PartNumber = partNumber });
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating part number {PartNumber}", partNumber);
                throw;
            }
        }

        public async Task<IEnumerable<Department>> GetDepartments()
        {
            using var connection = new SqlConnection(_scpConnectionString);
            const string query = @"
                SELECT DepartmentId,
                       DepartmentName
                FROM [SCP_CONTROL].[dbo].[Departments]
                WHERE IsActive = 1
                ORDER BY DepartmentName";

            return await connection.QueryAsync<Department>(query);
        }

        public async Task<IEnumerable<Line>> GetLinesByDepartment(int departmentId)
        {
            using var connection = new SqlConnection(_scpConnectionString);
            const string query = @"
                SELECT LineId,
                       LineName,
                       DepartmentId
                FROM [SCP_CONTROL].[dbo].[Lines]
                WHERE DepartmentId = @DepartmentId
                AND IsActive = 1
                ORDER BY LineName";

            return await connection.QueryAsync<Line>(query, new { DepartmentId = departmentId });
        }
    }
}