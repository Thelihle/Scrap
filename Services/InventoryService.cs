using System.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.Configuration;
using SCP_Control.Models;
using SCP_Control.Services.Interfaces;

namespace SCP_Control.Services
{
    public class InventoryService : IInventoryService
    {
        private readonly string _scpConnectionString;
        private readonly string _sysproConnectionString;
        private readonly ILogger<InventoryService> _logger;
        private readonly string _connectionString;

        public InventoryService(
            IConfiguration configuration,
            ILogger<InventoryService> logger)
        {
            _scpConnectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _sysproConnectionString = configuration.GetConnectionString("SysproConnection") ??
                throw new InvalidOperationException("Connection string 'SysproConnection' not found.");
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        public async Task<bool> ValidatePartNumber(string partNumber)
        {
            try
            {
                // Add logging to track what's being validated
                _logger.LogInformation("Validating part number: {PartNumber}", partNumber);

                using var connection = new SqlConnection(_sysproConnectionString);
                await connection.OpenAsync();

                const string query = @"
                SELECT COUNT(1) 
                FROM InvMaster 
                WHERE StockCode = @PartNumber 
                AND StockUom <> 'DELETE'";

                var exists = await connection.ExecuteScalarAsync<int>(query, new { PartNumber = partNumber?.Trim() });

                _logger.LogInformation("Part number {PartNumber} validation result: {Exists}",
                    partNumber, exists > 0);

                return exists > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating part number {PartNumber}", partNumber);
                return false;
            }
        }

        public async Task<(bool IsValid, string StockUom, decimal MaterialCost)> ValidatePartNumberWithUom(string partNumber)
        {
            try
            {
                // Ensure we're using a normalized, trimmed part number for logging consistency
                var normalizedPartNumber = partNumber?.Trim() ?? string.Empty;
                _logger.LogInformation("Validating part number with UOM: {PartNumber}", normalizedPartNumber);

                using var connection = new SqlConnection(_sysproConnectionString);
                await connection.OpenAsync();

                // Make sure we're querying the exact table shown in your SQL screenshot
                string query = @"
            SELECT StockCode, StockUom, MaterialCost
            FROM SysproCompanyB.dbo.InvMaster
            WHERE StockCode = @PartNumber AND StockUom <> 'DELETE'";

                // Create a proper class to avoid dynamic type issues
                var result = await connection.QueryFirstOrDefaultAsync<PartInfo>(query, new { PartNumber = normalizedPartNumber });

                // Log the raw result FIRST, before any manipulation
                _logger.LogInformation("Raw query result for {PartNumber}: {@Result}", normalizedPartNumber, result);

                if (result == null)
                {
                    _logger.LogWarning("Part number {PartNumber} not found in database", normalizedPartNumber);
                    return (false, "", 0);
                }

                // Ensure material cost is properly handled, even if it's 0
                string stockUom = result.StockUom;
                decimal materialCost = result.MaterialCost;

                _logger.LogInformation("Part validation successful - Part={PartNumber}, UOM={StockUom}, Cost={Cost:0.00000}",
                    normalizedPartNumber, stockUom, materialCost);

                return (true, stockUom, materialCost);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating part number with UOM {PartNumber}", partNumber);
                return (false, "", 0);
            }
        }

        // Add this class at the bottom of your file
        public class PartInfo
        {
            public string StockCode { get; set; }
            public string StockUom { get; set; }
            public decimal MaterialCost { get; set; }
        }

        public async Task<IEnumerable<Department>> GetDepartments()
        {
            using var connection = new SqlConnection(_scpConnectionString);
            const string query = @"
                SELECT DepartmentId,
                       DepartmentName
                FROM [IntegrationCompanyB].[dbo].[scpDepartment]
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
                FROM [IntegrationCompanyB].[dbo].[scpLine]
                WHERE DepartmentId = @DepartmentId
                AND IsActive = 1
                ORDER BY LineName";
            return await connection.QueryAsync<Line>(query, new { DepartmentId = departmentId });
        }
    }
}