using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Data.Common;
using SCP_Control.Models;
using SCP_Control.ViewModels;
using SCP_Control.DbContext;
using SCP_Control.Services.Interfaces;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SCP_Control.Models.Common;

namespace SCP_Control.Services
{
    public class ScrapApprovalService : IScrapApprovalService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ScrapApprovalService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ScrapApprovalService(
            ApplicationDbContext context,
            ILogger<ScrapApprovalService> logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<IEnumerable<string>> GetAllDepartments()
        {
            return await _context.scpDepartment
                .Where(d => d.IsActive)
                .Select(d => d.DepartmentName)  // ❌ This causes duplicates
                .OrderBy(d => d)
                .ToListAsync();
        }

        public async Task<IEnumerable<DepartmentViewModel>> GetDepartmentsWithDetails()
        {
            return await _context.scpDepartment
                .Where(d => d.IsActive)
                .Select(d => new DepartmentViewModel
                {
                    DepartmentId = d.DepartmentId,
                    DepartmentName = d.DepartmentName,
                    Location = d.Location,
                    Plant = d.Plant
                })
                .OrderBy(d => d.Plant)
                .ThenBy(d => d.DepartmentName)
                .ToListAsync();
        }

        public async Task<IEnumerable<string>> GetAllLines()
        {
            return await _context.scpLine
                .Where(l => l.IsActive)
                .Select(l => l.LineName)
                .OrderBy(l => l)
                .ToListAsync();
        }

        public async Task<IEnumerable<string>> GetLinesByDepartment(string department)
        {
            return await _context.scpLine
                .Where(l => l.Department.DepartmentName == department && l.IsActive)
                .Select(l => l.LineName)
                .OrderBy(l => l)
                .ToListAsync();
        }

        public async Task<IEnumerable<string>> GetDepartmentsByUser(string? username)
        {
            try
            {
                if (string.IsNullOrEmpty(username))
                    return Enumerable.Empty<string>();

                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.ClockNumber == username && u.IsActive == true);

                if (user == null)
                    return Enumerable.Empty<string>();

                // For TeamLeader and GroupLeader, return only their assigned department
                if (user.UserType == "TeamLeader" || user.UserType == "GroupLeader")
                {
                    return new[] { user.Department ?? string.Empty };
                }

                // For Manager and above, return all active departments
                return await _context.scpDepartment
                    .Where(d => d.IsActive)
                    .Select(d => d.DepartmentName)
                    .OrderBy(d => d)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting departments for user {Username}", username);
                return Enumerable.Empty<string>();
            }
        }


        public async Task<FilteredScrapDataModel> GetFilteredScrapData(
           string department = "",
           string line = "",
           string shift = "",
           string timeFrame = "",
           DateTime? startDate = null,
           DateTime? endDate = null)
        {
            try
            {
                var query = _context.ScrapData.AsQueryable();

                // Apply filters
                if (!string.IsNullOrEmpty(department))
                    query = query.Where(s => s.Department == department);
                if (!string.IsNullOrEmpty(line))
                    query = query.Where(s => s.Line == line);
                if (!string.IsNullOrEmpty(shift))
                    query = query.Where(s => s.Shift == shift);

                var items = await query
                    .Select(s => new ScrapItemViewModel
                    {
                        ScrapId = s.ScrapId,
                        PartNumber = s.PartNumber,
                        Department = s.Department,
                        Line = s.Line,
                        Shift = s.Shift,
                        Quantity = s.Quantity,
                        ScrapReason = s.ScrapReason,
                        Status = s.Status,
                        ClockNumber = s.ClockNumber,
                        SubmissionDate = s.CreatedAt,
                        MaterialCost = 0
                    })
                    .ToListAsync();

                // Get unique part numbers
                var partNumbers = items.Select(i => i.PartNumber).Distinct().ToList();

                // Get material costs using raw SQL
                var materialCosts = new Dictionary<string, decimal>();
                var partNumberList = string.Join(",", partNumbers.Select(p => $"'{p}'"));

                using (var command = _context.Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = $@"
                    SELECT StockCode, MaterialCost 
                    FROM SysproCompanyB.dbo.InvMaster 
                    WHERE StockCode IN ({partNumberList})";

                    if (command.Connection.State != ConnectionState.Open)
                        await command.Connection.OpenAsync();

                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        materialCosts[reader.GetString(0)] = reader.GetDecimal(1);
                    }
                }

                // Calculate costs
                decimal totalCost = 0;
                foreach (var item in items)
                {
                    if (materialCosts.TryGetValue(item.PartNumber, out decimal cost))
                    {
                        item.MaterialCost = cost * item.Quantity;
                        totalCost += item.MaterialCost;
                    }
                }

                var groupedScrap = items
                    .GroupBy(i => i.PartNumber)
                    .Select(g => new ScrapGroupViewModel
                    {
                        PartNumber = g.Key,
                        Items = g.ToList()
                    })
                    .ToList();

                return new FilteredScrapDataModel
                {
                    GroupedScrap = groupedScrap,
                    Items = items,
                    TotalCount = items.Count,
                    TotalWeight = items.Sum(i => i.WeightKg),
                    TotalCost = totalCost,
                    PendingApprovalCount = items.Count(i => i.Status == "Pending")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting filtered scrap data");
                throw;
            }
        }

        public async Task<Dictionary<string, decimal>> GetMaterialCosts(List<string> partNumbers)
        {
            using var connection = _context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            var partNumberList = string.Join(",", partNumbers.Select(p => $"'{p}'"));
            using var command = connection.CreateCommand();
            command.CommandText = $@"
        SELECT StockCode, MaterialCost 
        FROM SysproCompanyB.dbo.InvMaster 
        WHERE StockCode IN ({partNumberList})";

            var materialCosts = new Dictionary<string, decimal>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                materialCosts[reader.GetString(0)] = reader.GetDecimal(1);
            }

            return materialCosts;
        }

        private bool IsManagementRole(string? userType)
        {
            if (string.IsNullOrEmpty(userType)) return false;

            return userType.ToLower() switch
            {
                "groupleader" => true,
                "manager" => true,
                "seniormanager" => true,
                "admin" => true,
                _ => false
            };
        }

        public async Task<Result> UpdateScrapStatus(
            int scrapId,
            bool isApproved,
            string reason,
            string approverClockNumber)
        {
            try
            {
                var scrap = await _context.ScrapData.FindAsync(scrapId);
                if (scrap == null)
                    return Result.CreateFailure("Scrap record not found");

                scrap.Status = isApproved ? "Approved" : "Rejected";
                scrap.ApprovedBy = approverClockNumber;
                scrap.ApprovalDate = DateTime.UtcNow;
                scrap.ApprovalReason = reason;
                scrap.ModifiedAt = DateTime.UtcNow;
                scrap.ModifiedBy = approverClockNumber;

                await _context.SaveChangesAsync();
                return Result.CreateSuccess("Status updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating scrap status");
                return Result.CreateFailure("Error updating status");
            }
        }

        public async Task<Result> BulkUpdateScrapStatus(
    List<int> scrapIds,
    bool isApproved,
    string reason,
    string approverClockNumber)
        {
            try
            {
                var scraps = await _context.ScrapData
                    .Where(s => scrapIds.Contains(s.ScrapId))
                    .ToListAsync();

                if (!scraps.Any())
                    return Result.CreateFailure("No scrap records found");

                foreach (var scrap in scraps)
                {
                    scrap.Status = isApproved ? "Approved" : "Rejected";
                    scrap.ApprovedBy = approverClockNumber;
                    scrap.ApprovalDate = DateTime.UtcNow;
                    scrap.ApprovalReason = reason;
                    scrap.ModifiedAt = DateTime.UtcNow;
                    scrap.ModifiedBy = approverClockNumber;
                }

                await _context.SaveChangesAsync();
                return Result.CreateSuccess($"Successfully {(isApproved ? "approved" : "rejected")} {scraps.Count} items");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing bulk status update");
                return Result.CreateFailure("Error updating statuses");
            }
        }

        public async Task<IEnumerable<ViewModels.ScrapSummaryModel>> GetScrapDataByLocation(ViewModels.LocationDetails location)
        {
            return await _context.ScrapData
                .Where(s =>
                    (string.IsNullOrEmpty(location.Department) || s.Department == location.Department) &&
                    (string.IsNullOrEmpty(location.Line) || s.Line == location.Line) &&
                    (string.IsNullOrEmpty(location.Shift) || s.Shift == location.Shift))
                .Select(s => new ViewModels.ScrapSummaryModel
                {
                    ScrapId = s.ScrapId,
                    PartNumber = s.PartNumber,
                    ClockNumber = s.ClockNumber,
                    Department = s.Department,
                    Line = s.Line,
                    Shift = s.Shift,
                    Quantity = s.Quantity,
                    ScrapReason = s.ScrapReason ?? string.Empty,
                    Status = s.Status,
                    SubmissionDate = s.CreatedAt
                })
                .ToListAsync();
        }

       

        public async Task<IEnumerable<ScrapSummaryModel>> GetScrapDataForApproval(
            string department,
            string line,
            string shift)
        {
            return await _context.ScrapData
                .Where(s =>
                    s.Status == "Pending" &&
                    (string.IsNullOrEmpty(department) || s.Department == department) &&
                    (string.IsNullOrEmpty(line) || s.Line == line) &&
                    (string.IsNullOrEmpty(shift) || s.Shift == shift))
                .Select(s => new ScrapSummaryModel
                {
                    ScrapId = s.ScrapId,
                    PartNumber = s.PartNumber,
                    SubmittedClockNumber = s.ClockNumber,
                    Department = s.Department,
                    Line = s.Line,
                    Shift = s.Shift,
                    Quantity = s.Quantity,
                    Status = s.Status,
                    SubmissionDate = s.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<IEnumerable<PendingApprovalModel>> GetPendingApprovals(string department)
        {
            return await _context.ScrapData
                .Where(s => s.Status == "Pending" && s.Department == department)
                .Select(s => new PendingApprovalModel
                {
                    ScrapId = s.ScrapId,
                    PartNumber = s.PartNumber,
                    Department = s.Department,
                    Line = s.Line,
                    Shift = s.Shift,
                    SubmissionDate = s.CreatedAt,
                    Quantity = s.Quantity                   
                })
                .ToListAsync();
        }

        public async Task<Result> ProcessFirstApproval(
            int scrapId,
            bool isApproved,
            string reason,
            string approverClockNumber)
        {
            try
            {
                var scrap = await _context.ScrapData
                    .FirstOrDefaultAsync(s => s.ScrapId == scrapId && s.Status == "Pending");

                if (scrap == null)
                    return Result.CreateFailure("Pending scrap record not found");

                scrap.FirstApprovalStatus = isApproved ? "Approved" : "Rejected";
                scrap.FirstApprovalBy = approverClockNumber;
                scrap.FirstApprovalDate = DateTime.UtcNow;
                scrap.FirstApprovalReason = reason;
                scrap.ModifiedAt = DateTime.UtcNow;
                scrap.ModifiedBy = approverClockNumber;

                await _context.SaveChangesAsync();
                return Result.CreateSuccess("First approval processed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing first approval");
                return Result.CreateFailure("Error processing first approval");
            }

            
        }
    }
}