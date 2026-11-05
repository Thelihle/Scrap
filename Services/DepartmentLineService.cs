using System.Data.SqlClient;
using Dapper;
using SCP_Control.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SCP_Control.ViewModels;
namespace SCP_Control.Services
{
    public class DepartmentLineService : IDepartmentLineService
    {
        private readonly string _connectionString;
        private readonly ILogger<DepartmentLineService> _logger;

        public DepartmentLineService(IConfiguration configuration, ILogger<DepartmentLineService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
        }

        public async Task<LocationUpdateViewModel> GetTeamLeaderLocation(string clockNumber)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                const string query = @"
                    SELECT Department, Line, Shift
                    FROM Users
                    WHERE ClockNumber = @ClockNumber
                    AND UserType = 'TeamLeader'
                    AND IsActive = 1";

                var location = await connection.QueryFirstOrDefaultAsync<LocationUpdateViewModel>(
                    query,
                    new { ClockNumber = clockNumber }
                );

                return location ?? new LocationUpdateViewModel();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting TeamLeader location for {ClockNumber}", clockNumber);
                throw;
            }
        }

        public async Task<IEnumerable<DepartmentViewModel>> GetActiveDepartments()
        {
            using var connection = new SqlConnection(_connectionString);
            const string query = @"
                SELECT 
                    DepartmentId,
                    DepartmentName
                FROM [dbo].Departments
                WHERE IsActive = 1
                ORDER BY DepartmentName";

            try
            {
                var results = await connection.QueryAsync<DepartmentViewModel>(query);
                return results.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving departments");
                throw;
            }
        }

        public async Task<IEnumerable<LineViewModel>> GetLinesByDepartment(int departmentId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                const string query = @"
                    SELECT DISTINCT Line as LineName
                    FROM Users
                    WHERE Department = (
                        SELECT Department 
                        FROM Users 
                        WHERE Department IS NOT NULL 
                        AND IsActive = 1 
                        OFFSET @DepartmentId ROWS 
                        FETCH NEXT 1 ROW ONLY
                    )
                    AND Line IS NOT NULL
                    AND IsActive = 1
                    ORDER BY Line";

                var lines = await connection.QueryAsync<LineViewModel>(query, new { DepartmentId = departmentId });
                return lines;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting lines for department {DepartmentId}", departmentId);
                throw;
            }
        }

        public async Task<bool> UpdateTeamLeaderLocation(string clockNumber, string department, string line, string shift)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                const string query = @"
                    UPDATE Users
                    SET Department = @Department,
                        Line = @Line,
                        Shift = @Shift,
                        LastModifiedAt = @ModifiedAt,
                        LastModifiedBy = @ModifiedBy
                    WHERE ClockNumber = @ClockNumber
                    AND UserType = 'TeamLeader'
                    AND IsActive = 1";

                var result = await connection.ExecuteAsync(query, new
                {
                    ClockNumber = clockNumber,
                    Department = department,
                    Line = line,
                    Shift = shift,
                    ModifiedAt = DateTime.UtcNow,
                    ModifiedBy = clockNumber
                });

                return result > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating TeamLeader location for {ClockNumber}", clockNumber);
                throw;
            }
        }

        public async Task<IDictionary<string, IEnumerable<string>>> GetAllDepartmentsAndLines()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                const string query = @"
                    SELECT DISTINCT Department, Line
                    FROM Users
                    WHERE Department IS NOT NULL 
                    AND Line IS NOT NULL
                    AND IsActive = 1
                    ORDER BY Department, Line";

                var results = await connection.QueryAsync<(string Department, string Line)>(query);

                return results
                    .GroupBy(x => x.Department)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(x => x.Line).Distinct()
                    );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all departments and lines");
                throw;
            }
        }
    }
}