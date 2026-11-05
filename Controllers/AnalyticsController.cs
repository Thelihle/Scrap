using ClosedXML.Excel;
using Dapper;
using DocumentFormat.OpenXml.InkML;
using DocumentFormat.OpenXml.Math;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SCP_Control.DbContext;
using SCP_Control.Models;
using SCP_Control.ViewModels;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;

namespace SCP_Control.Controllers
{
    [Authorize(Policy = "ManagementOnly")]
    public class AnalyticsController : Controller
    {
        private readonly ILogger<AnalyticsController> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly ApplicationDbContext _context;

        public AnalyticsController(
            ILogger<AnalyticsController> logger,
            ApplicationDbContext context,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                // Get available departments
                var departments = new List<string>
        {
            "All Departments",
            "FORD", "HILUX", "HIACE", "CSUV", "CSUV SCRAP", "FIELLI", "PLANT C",
            "PRE-ASSEMBLY FORD", "PRE-ASSEMBLY ISUZU", "PRE-ASSEMBLY TSAM", "PRE-ASSEMBLY CSUV",
            "ENGINEERING PLANT A", "ENGINEERING PLANT B", "ENGINEERING PLANT C", "ENGINEERING FIELLI",
            "SQA", "STORES", "ENGINEERING", "PROCESS", "KANBAN", "PROJECT", "QUALITY",
            "QUANTUM REWORK", "FINAL ASSY-CSUV REWORK", "FINAL ASSY-ISUZU REWORK",
            "PRE ASSY-CSUV REWORK", "PRE ASSY-ISUZU REWORK", "HILUX REWORK",
            "PROJECT 581D REWORK", "PRE ASSY-TSAM REWORK", "FINAL ASSY-FORD REWORK",
            "PRE ASSY-FORD REWORK", "FIELLI REWORK"
        };

                ViewBag.Departments = departments;

                // ── NEW: is the current user an admin? ──────────────────────
                var clockNumber = User?.Identity?.Name;
                var currentUser = await _context.User.FirstOrDefaultAsync(
                    u => (u.ClockNumber == clockNumber || u.UserName == clockNumber) && u.IsActive);
                ViewBag.IsAdmin = currentUser?.UserType?.ToLower() == "admin";
                // ─────────────────────────────────────────────────────────────

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading Analytics Hub");
                ViewBag.IsAdmin = false;   // NEW: safe default on error
                return View();
            }
        }

        private async Task PopulateSummaryData(AnalyticsDashboardViewModel model)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Get scrap summary
            const string scrapSummaryQuery = @"
SELECT 
    COUNT(*) as TotalEntries,
    SUM(Quantity) as TotalQuantity,
    SUM(CASE WHEN Status = 'Approved' THEN 1 ELSE 0 END) as ApprovedCount,
    SUM(CASE WHEN Status = 'Rejected' THEN 1 ELSE 0 END) as RejectedCount,
    SUM(CASE WHEN Status = 'Pending' OR Status IS NULL THEN 1 ELSE 0 END) as PendingCount
FROM [IntegrationCompanyB].[dbo].[scpScrapData]
WHERE CreatedAt BETWEEN @StartDate AND @EndDate
AND (@Department IS NULL OR Department = @Department)
AND (@Line IS NULL OR Line = @Line)
AND (@Shift IS NULL OR Shift = @Shift)";

            var scrapSummary = await connection.QueryFirstOrDefaultAsync(scrapSummaryQuery, new
            {
                StartDate = model.DateRange.StartDate,
                EndDate = model.DateRange.EndDate.AddDays(1),
                Department = model.FilterDepartment ?? model.CurrentDepartment,
                Line = model.FilterLine ?? model.CurrentLine,
                Shift = model.FilterShift ?? model.CurrentShift
            });

            model.TotalScrapEntries = scrapSummary?.TotalEntries ?? 0;
            model.TotalScrapQuantity = scrapSummary?.TotalQuantity ?? 0;
            model.ApprovedScrapCount = scrapSummary?.ApprovedCount ?? 0;
            model.RejectedScrapCount = scrapSummary?.RejectedCount ?? 0;
            model.PendingScrapCount = scrapSummary?.PendingCount ?? 0;

            // ✅ NEW — AverageWeight now correctly sourced from scpWeightHistory
            const string avgWeightQuery = @"
SELECT ISNULL(AVG(Weight), 0) AS AverageWeight
FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
WHERE NOT (Line = 'ALL LINES' AND Shift = 'ALL')
  AND NOT (Shift = 'MONTHLY')
  AND CreatedAt BETWEEN @StartDate AND @EndDate
  AND (@Department IS NULL OR Department = @Department)
  AND (@Line IS NULL OR Line = @Line)
  AND (@Shift IS NULL OR Shift = @Shift)";

            var avgWeightResult = await connection.QueryFirstOrDefaultAsync(avgWeightQuery, new
            {
                StartDate = model.DateRange.StartDate,
                EndDate = model.DateRange.EndDate.AddDays(1),
                Department = model.FilterDepartment ?? model.CurrentDepartment,
                Line = model.FilterLine ?? model.CurrentLine,
                Shift = model.FilterShift ?? model.CurrentShift
            });

            model.AverageWeight = avgWeightResult?.AverageWeight ?? 0;

            // Get top 5 most scrapped parts
            const string topPartsQuery = @"
            SELECT TOP 5
                PartNumber,
                SUM(Quantity) as TotalQuantity,
                COUNT(*) as OccurrenceCount,
                MAX(CreatedAt) as LastOccurrence
            FROM [IntegrationCompanyB].[dbo].[scpScrapData]
            WHERE CreatedAt BETWEEN @StartDate AND @EndDate
            AND (@Department IS NULL OR Department = @Department)
            AND (@Line IS NULL OR Line = @Line)
            AND (@Shift IS NULL OR Shift = @Shift)
            GROUP BY PartNumber
            ORDER BY TotalQuantity DESC";

            var topParts = await connection.QueryAsync<TopScrapItemViewModel>(topPartsQuery, new
            {
                StartDate = model.DateRange.StartDate,
                EndDate = model.DateRange.EndDate.AddDays(1),
                Department = model.FilterDepartment ?? model.CurrentDepartment,
                Line = model.FilterLine ?? model.CurrentLine,
                Shift = model.FilterShift ?? model.CurrentShift
            });

            model.TopScrapItems = topParts.ToList();

            // Get scrap by section
            const string scrapBySectionQuery = @"
            SELECT 
                ss.ScrapSectionName,
                COUNT(s.ScrapId) as EntryCount,
                SUM(s.Quantity) as TotalQuantity
            FROM [IntegrationCompanyB].[dbo].[scpScrapData] s
            INNER JOIN [IntegrationCompanyB].[dbo].[ScrapSection] ss ON s.ScrapSectionId = ss.ScrapSectionId
            WHERE s.CreatedAt BETWEEN @StartDate AND @EndDate
            AND (@Department IS NULL OR s.Department = @Department)
            AND (@Line IS NULL OR s.Line = @Line)
            AND (@Shift IS NULL OR s.Shift = @Shift)
            GROUP BY ss.ScrapSectionName
            ORDER BY TotalQuantity DESC";

            var scrapBySection = await connection.QueryAsync<ScrapBySectionViewModel>(scrapBySectionQuery, new
            {
                StartDate = model.DateRange.StartDate,
                EndDate = model.DateRange.EndDate.AddDays(1),
                Department = model.FilterDepartment ?? model.CurrentDepartment,
                Line = model.FilterLine ?? model.CurrentLine,
                Shift = model.FilterShift ?? model.CurrentShift
            });

            model.ScrapBySection = scrapBySection.ToList();

            // Get daily scrap trends
            const string dailyTrendQuery = @"
SELECT 
    CAST(CreatedAt AS DATE) as Date,
    COUNT(*) as EntryCount,
    SUM(Quantity) as TotalQuantity
FROM [IntegrationCompanyB].[dbo].[scpScrapData]
WHERE CreatedAt BETWEEN @StartDate AND @EndDate
AND (@Department IS NULL OR Department = @Department)
AND (@Line IS NULL OR Line = @Line)
AND (@Shift IS NULL OR Shift = @Shift)
GROUP BY CAST(CreatedAt AS DATE)
ORDER BY CAST(CreatedAt AS DATE)";

            var dailyTrends = (await connection.QueryAsync<ScrapTrendViewModel>(dailyTrendQuery, new
            {
                StartDate = model.DateRange.StartDate,
                EndDate = model.DateRange.EndDate.AddDays(1),
                Department = model.FilterDepartment ?? model.CurrentDepartment,
                Line = model.FilterLine ?? model.CurrentLine,
                Shift = model.FilterShift ?? model.CurrentShift
            })).ToList();

            // ✅ NEW — daily average weight from the correct table, merged in by date
            const string dailyWeightQuery = @"
SELECT CAST(CreatedAt AS DATE) as Date, ISNULL(AVG(Weight), 0) AS AverageWeight
FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
WHERE NOT (Line = 'ALL LINES' AND Shift = 'ALL')
  AND NOT (Shift = 'MONTHLY')
  AND CreatedAt BETWEEN @StartDate AND @EndDate
  AND (@Department IS NULL OR Department = @Department)
  AND (@Line IS NULL OR Line = @Line)
  AND (@Shift IS NULL OR Shift = @Shift)
GROUP BY CAST(CreatedAt AS DATE)";

            var dailyWeights = (await connection.QueryAsync(dailyWeightQuery, new
            {
                StartDate = model.DateRange.StartDate,
                EndDate = model.DateRange.EndDate.AddDays(1),
                Department = model.FilterDepartment ?? model.CurrentDepartment,
                Line = model.FilterLine ?? model.CurrentLine,
                Shift = model.FilterShift ?? model.CurrentShift
            })).ToDictionary(r => (DateTime)r.Date, r => (decimal)r.AverageWeight);   // ✅ decimal, not double

            foreach (var day in dailyTrends)
            {
                day.AverageWeight = dailyWeights.TryGetValue(day.Date, out var w) ? w : 0m;   // ✅ 0m, not 0
            }
            model.DailyTrends = dailyTrends;

            // Get activity log summary
            const string activityLogQuery = @"
            SELECT 
                ActivityType,
                COUNT(*) as ActivityCount
            FROM [IntegrationCompanyB].[dbo].[scpUserSession]
            WHERE Timestamp BETWEEN @StartDate AND @EndDate
            GROUP BY ActivityType
            ORDER BY ActivityCount DESC";

            var activitySummary = await connection.QueryAsync<ActivitySummaryViewModel>(activityLogQuery, new
            {
                StartDate = model.DateRange.StartDate,
                EndDate = model.DateRange.EndDate.AddDays(1)
            });

            model.ActivitySummary = activitySummary.ToList();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FilterData(AnalyticsFilterViewModel filter)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { success = false, message = "Invalid filter parameters" });
                }

                var model = new AnalyticsDashboardViewModel
                {
                    DateRange = new DateRangeViewModel
                    {
                        StartDate = filter.StartDate,
                        EndDate = filter.EndDate
                    },
                    FilterDepartment = filter.Department,
                    FilterLine = filter.Line,
                    FilterShift = filter.Shift
                };

                await PopulateSummaryData(model);

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        summary = new
                        {
                            totalEntries = model.TotalScrapEntries,
                            totalQuantity = model.TotalScrapQuantity,
                            approvedCount = model.ApprovedScrapCount,
                            rejectedCount = model.RejectedScrapCount,
                            pendingCount = model.PendingScrapCount,
                            averageWeight = model.AverageWeight
                        },
                        topParts = model.TopScrapItems,
                        scrapBySection = model.ScrapBySection,
                        dailyTrends = model.DailyTrends,
                        activitySummary = model.ActivitySummary
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error filtering analytics data");
                return Json(new { success = false, message = "Error filtering data" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetScrapByReason(DateTime? startDate, DateTime? endDate, string department = null, string line = null, string shift = null)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                var query = @"
                SELECT 
                    ScrapReason as ReasonName,
                    COUNT(*) as EntryCount,
                    SUM(Quantity) as TotalQuantity
                FROM [IntegrationCompanyB].[dbo].[scpScrapData]
                WHERE CreatedAt BETWEEN @StartDate AND @EndDate
                AND (@Department IS NULL OR Department = @Department)
                AND (@Line IS NULL OR Line = @Line)
                AND (@Shift IS NULL OR Shift = @Shift)
                GROUP BY ScrapReason
                ORDER BY TotalQuantity DESC";

                var scrapByReason = await connection.QueryAsync<ScrapByReasonViewModel>(query, new
                {
                    StartDate = startDate ?? DateTime.Today.AddDays(-30),
                    EndDate = (endDate ?? DateTime.Today).AddDays(1),
                    Department = department,
                    Line = line,
                    Shift = shift
                });

                return Json(new { success = true, data = scrapByReason });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scrap by reason");
                return Json(new { success = false, message = "Error loading data" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUserActivity(DateTime? startDate, DateTime? endDate)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                var query = @"
                SELECT 
                    Username,
                    COUNT(*) as ActivityCount,
                    MAX(Timestamp) as LastActivity
                FROM [IntegrationCompanyB].[dbo].[scpUserSession]
                WHERE Timestamp BETWEEN @StartDate AND @EndDate
                GROUP BY Username
                ORDER BY ActivityCount DESC";

                var userActivity = await connection.QueryAsync<UserActivityViewModel>(query, new
                {
                    StartDate = startDate ?? DateTime.Today.AddDays(-7),
                    EndDate = (endDate ?? DateTime.Today).AddDays(1)
                });

                return Json(new { success = true, data = userActivity });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user activity");
                return Json(new { success = false, message = "Error loading activity data" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetChartData(DateTime? startDate, DateTime? endDate, string department = null, string line = null, string shift = null)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);

                // Get daily scrap quantity
                const string dailyScrapQuery = @"
                SELECT 
                    CAST(CreatedAt AS DATE) as Date,
                    SUM(Quantity) as TotalQuantity
                FROM [IntegrationCompanyB].[dbo].[scpScrapData]
                WHERE CreatedAt BETWEEN @StartDate AND @EndDate
                AND (@Department IS NULL OR Department = @Department)
                AND (@Line IS NULL OR Line = @Line)
                AND (@Shift IS NULL OR Shift = @Shift)
                GROUP BY CAST(CreatedAt AS DATE)
                ORDER BY CAST(CreatedAt AS DATE)";

                var dailyScrap = await connection.QueryAsync(dailyScrapQuery, new
                {
                    StartDate = startDate ?? DateTime.Today.AddDays(-30),
                    EndDate = (endDate ?? DateTime.Today).AddDays(1),
                    Department = department,
                    Line = line,
                    Shift = shift
                });

                // Get scrap by section
                const string scrapBySectionQuery = @"
                SELECT 
                    ss.ScrapSectionName,
                    SUM(s.Quantity) as TotalQuantity
                FROM [IntegrationCompanyB].[dbo].[scpScrapData] s
                INNER JOIN [IntegrationCompanyB].[dbo].[scpScrapSection] ss ON s.ScrapSectionId = ss.ScrapSectionId
                WHERE s.CreatedAt BETWEEN @StartDate AND @EndDate
                AND (@Department IS NULL OR s.Department = @Department)
                AND (@Line IS NULL OR s.Line = @Line)
                AND (@Shift IS NULL OR s.Shift = @Shift)
                GROUP BY ss.ScrapSectionName
                ORDER BY TotalQuantity DESC";

                var scrapBySection = await connection.QueryAsync(scrapBySectionQuery, new
                {
                    StartDate = startDate ?? DateTime.Today.AddDays(-30),
                    EndDate = (endDate ?? DateTime.Today).AddDays(1),
                    Department = department,
                    Line = line,
                    Shift = shift
                });

                // Get scrap status distribution
                const string statusQuery = @"
                SELECT 
                    COALESCE(Status, 'Pending') as Status,
                    COUNT(*) as Count
                FROM [IntegrationCompanyB].[dbo].[scpScrapData]
                WHERE CreatedAt BETWEEN @StartDate AND @EndDate
                AND (@Department IS NULL OR Department = @Department)
                AND (@Line IS NULL OR Line = @Line)
                AND (@Shift IS NULL OR Shift = @Shift)
                GROUP BY Status
                ORDER BY Count DESC";

                var statusDistribution = await connection.QueryAsync(statusQuery, new
                {
                    StartDate = startDate ?? DateTime.Today.AddDays(-30),
                    EndDate = (endDate ?? DateTime.Today).AddDays(1),
                    Department = department,
                    Line = line,
                    Shift = shift
                });

                // Return JSON with all chart data
                return Json(new
                {
                    success = true,
                    dailyScrap = dailyScrap.Select(d => new
                    {
                        date = ((DateTime)d.Date).ToString("yyyy-MM-dd"),
                        quantity = d.TotalQuantity
                    }),
                    scrapBySection = scrapBySection.Select(s => new
                    {
                        section = s.ScrapSectionName,
                        quantity = s.TotalQuantity
                    }),
                    statusDistribution = statusDistribution.Select(s => new
                    {
                        status = s.Status,
                        count = s.Count
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting chart data");
                return Json(new { success = false, message = "Error loading chart data" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDetailedReport(DateTime? startDate, DateTime? endDate, string department = null, string line = null, string shift = null)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);

                // Get detailed report data
                var query = @"
                SELECT 
                    s.Department,
                    s.Line,
                    s.Shift,
                    ss.ScrapSectionName as Section,
                    s.ScrapReason as Reason,
                    COUNT(*) as EntryCount,
                    SUM(s.Quantity) as TotalQuantity,
                    AVG(CAST(s.WeightKg AS FLOAT)) as AverageWeight
                FROM [Syspro].[dbo].[ScrapData] s
                LEFT JOIN [IntegrationCompanyB].[dbo].[scpScrapSection] ss ON s.ScrapSectionId = ss.ScrapSectionId
                WHERE s.CreatedAt BETWEEN @StartDate AND @EndDate
                AND (@Department IS NULL OR s.Department = @Department)
                AND (@Line IS NULL OR s.Line = @Line)
                AND (@Shift IS NULL OR s.Shift = @Shift)
                GROUP BY s.Department, s.Line, s.Shift, ss.ScrapSectionName, s.ScrapReason
                ORDER BY TotalQuantity DESC";

                var reportData = await connection.QueryAsync(query, new
                {
                    StartDate = startDate ?? DateTime.Today.AddDays(-30),
                    EndDate = (endDate ?? DateTime.Today).AddDays(1),
                    Department = department,
                    Line = line,
                    Shift = shift
                });

                return Json(new { success = true, data = reportData });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting detailed report");
                return Json(new { success = false, message = "Error loading report data" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetActivityLog(DateTime? startDate, DateTime? endDate, string activityType = null)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);

                string query = @"
                SELECT 
                    ActivityId,
                    Username,
                    ActivityType,
                    Description,
                    Status,
                    FORMAT(Timestamp, 'yyyy-MM-dd HH:mm:ss') as FormattedTimestamp
                FROM [IntegrationCompanyB].[dbo].[scpUserSession]
                WHERE Timestamp BETWEEN @StartDate AND @EndDate";

                if (!string.IsNullOrEmpty(activityType))
                {
                    query += " AND ActivityType = @ActivityType";
                }

                query += " ORDER BY Timestamp DESC";

                var activityLog = await connection.QueryAsync(query, new
                {
                    StartDate = startDate ?? DateTime.Today.AddDays(-7),
                    EndDate = (endDate ?? DateTime.Today).AddDays(1),
                    ActivityType = activityType
                });

                return Json(new { success = true, data = activityLog });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting activity log");
                return Json(new { success = false, message = "Error loading activity log" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDepartments()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get departments from the Departments table
                var query = @"
        SELECT DepartmentName
        FROM [IntegrationCompanyB].[dbo].[scpDepartment]
        WHERE IsActive = 1
        ORDER BY DepartmentName";

                var departments = await connection.QueryAsync<string>(query);

                return Json(new
                {
                    success = true,
                    departments = departments
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting departments");
                return Json(new { success = false, message = "Error loading departments" });
            }
        }

        public async Task<IActionResult> Dashboard(int departmentId = 0, int lineId = 0, int shiftId = 0,
     DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                var effectiveStartDate = startDate ?? DateTime.Today.AddMonths(-3);
                var effectiveEndDate = endDate ?? DateTime.Today;

                var viewModel = new AnalyticsDashboardViewModel
                {
                    TotalScrapEntries = 0,
                    TotalScrapQuantity = 0,
                    ApprovedScrapCount = 0,
                    RejectedScrapCount = 0,
                    PendingScrapCount = 0,
                    TotalScrapCost = 0,
                    TopScrapItems = new List<TopScrapItemViewModel>(),
                    DateRange = new DateRangeViewModel
                    {
                        StartDate = effectiveStartDate,
                        EndDate = effectiveEndDate
                    }
                };

                viewModel.SelectedDepartmentId = departmentId;
                viewModel.SelectedLineId = lineId;
                viewModel.SelectedShiftId = shiftId;

                viewModel.Departments = await GetDepartmentsForDropdown();

                if (departmentId > 0)
                    viewModel.Lines = await GetLinesForDropdown(departmentId);
                else
                    viewModel.Lines = await GetAllLinesForDropdown();

                viewModel.Shifts = GetShiftsForDropdown();

                viewModel.CurrentDepartment = departmentId > 0
                    ? viewModel.Departments.FirstOrDefault(d => d.DepartmentId == departmentId)?.DepartmentName ?? "All Departments"
                    : "All Departments";

                viewModel.CurrentLine = lineId > 0
                    ? viewModel.Lines.FirstOrDefault(l => l.LineId == lineId)?.LineName ?? "All Lines"
                    : "All Lines";

                viewModel.CurrentShift = shiftId switch
                {
                    1 => "A",
                    2 => "B",
                    3 => "Straight",
                    _ => "All Shifts"
                };

                string shiftName = shiftId > 0 ? viewModel.CurrentShift : null;
                var paramDepartment = departmentId > 0 ? viewModel.CurrentDepartment : null;
                var paramLine = lineId > 0 ? viewModel.CurrentLine : null;
                var paramShift = shiftName;

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // FIXED: Correct pending calculation
                var statQuery = @"
SELECT 
    COUNT(ScrapId) AS TotalEntries,
    ISNULL(SUM(Quantity), 0) AS TotalQuantity,
    -- Approved: Manager has approved
    ISNULL(SUM(CASE WHEN ManagerApprovalStatus = 'Approved' THEN 1 ELSE 0 END), 0) AS ApprovedCount,
    -- Rejected: Any level has rejected
    ISNULL(SUM(CASE WHEN 
        TeamLeaderApprovalStatus = 'Rejected' OR 
        GroupLeaderApprovalStatus = 'Rejected' OR 
        ManagerApprovalStatus = 'Rejected' 
        THEN 1 ELSE 0 END), 0) AS RejectedCount,
    -- Pending: Not approved and not rejected
    ISNULL(SUM(CASE WHEN 
        (ManagerApprovalStatus IS NULL OR ManagerApprovalStatus != 'Approved') AND
        (TeamLeaderApprovalStatus != 'Rejected' AND 
         GroupLeaderApprovalStatus != 'Rejected' AND 
         (ManagerApprovalStatus IS NULL OR ManagerApprovalStatus != 'Rejected'))
        THEN 1 ELSE 0 END), 0) AS PendingCount
FROM [IntegrationCompanyB].[dbo].[scpScrapData]
WHERE (@Department IS NULL OR Department = @Department)
AND (@Line IS NULL OR Line = @Line)
AND (@Shift IS NULL OR Shift = @Shift)
AND CreatedAt >= @StartDate 
AND CreatedAt <= @EndDate";

                var stats = await connection.QueryFirstOrDefaultAsync(statQuery, new
                {
                    Department = paramDepartment,
                    Line = paramLine,
                    Shift = paramShift,
                    StartDate = effectiveStartDate,
                    EndDate = effectiveEndDate.AddDays(1)
                });

                if (stats != null)
                {
                    viewModel.TotalScrapEntries = stats.TotalEntries;
                    viewModel.TotalScrapQuantity = stats.TotalQuantity;
                    viewModel.ApprovedScrapCount = stats.ApprovedCount;
                    viewModel.RejectedScrapCount = stats.RejectedCount;
                    viewModel.PendingScrapCount = stats.PendingCount;
                }

                viewModel.TotalScrapCost = await CalculateTotalScrapCost(effectiveStartDate, effectiveEndDate,
                    paramDepartment, paramLine, paramShift);

                // NEW: Get approval flow breakdown
                var approvalFlowQuery = @"
SELECT 
    ApprovalStage,
    COUNT(*) AS Count
FROM (
    SELECT 
        CASE 
            WHEN TeamLeaderApprovalStatus IS NULL THEN 'TL Pending'
            WHEN GroupLeaderApprovalStatus IS NULL THEN 'GL Pending'
            WHEN ManagerApprovalStatus IS NULL THEN 'Manager Pending'
            WHEN ManagerApprovalStatus = 'Approved' THEN 'Approved'
            ELSE 'Rejected'
        END AS ApprovalStage,
        CASE 
            WHEN TeamLeaderApprovalStatus IS NULL THEN 1
            WHEN GroupLeaderApprovalStatus IS NULL THEN 2
            WHEN ManagerApprovalStatus IS NULL THEN 3
            WHEN ManagerApprovalStatus = 'Approved' THEN 4
            ELSE 5
        END AS SortOrder
    FROM [IntegrationCompanyB].[dbo].[scpScrapData]
    WHERE (@Department IS NULL OR Department = @Department)
    AND (@Line IS NULL OR Line = @Line)
    AND (@Shift IS NULL OR Shift = @Shift)
    AND CreatedAt >= @StartDate AND CreatedAt <= @EndDate
) AS SubQuery
GROUP BY ApprovalStage, SortOrder
ORDER BY SortOrder";

                viewModel.ApprovalFlowData = await connection.QueryAsync<dynamic>(approvalFlowQuery, new
                {
                    Department = paramDepartment,
                    Line = paramLine,
                    Shift = paramShift,
                    StartDate = effectiveStartDate,
                    EndDate = effectiveEndDate.AddDays(1)
                });

                // NEW: Scrap by reason
                var query = @"
SELECT ISNULL(SUM(s.Quantity * ISNULL(im.MaterialCost, 0)), 0) AS TotalCost
FROM [IntegrationCompanyB].[dbo].[scpScrapData] s
LEFT JOIN [SysproCompanyB].[dbo].[InvMaster] im
    ON s.PartNumber = im.StockCode COLLATE Latin1_General_CI_AS
   AND im.StockCode COLLATE Latin1_General_BIN NOT IN ('EBD0532a', 'AVf30B/R', ' 4KB-927680-501')
WHERE (@Department IS NULL OR s.Department = @Department)
AND (@Line IS NULL OR s.Line = @Line)
AND (@Shift IS NULL OR s.Shift = @Shift)
AND (@StartDate IS NULL OR s.CreatedAt >= @StartDate)
AND (@EndDate IS NULL OR s.CreatedAt <= @EndDate)";

                viewModel.ScrapByReason = await connection.QueryAsync<dynamic>(query, new
                {
                    Department = paramDepartment,
                    Line = paramLine,
                    Shift = paramShift,
                    StartDate = effectiveStartDate,
                    EndDate = effectiveEndDate.AddDays(1)
                });

                // NEW: Top operators
                var operatorQuery = @"
SELECT TOP 10
    ClockNumber,
    COUNT(*) AS EntryCount,
    SUM(Quantity) AS TotalQuantity
FROM [IntegrationCompanyB].[dbo].[scpScrapData]
WHERE (@Department IS NULL OR Department = @Department)
AND (@Line IS NULL OR Line = @Line)
AND (@Shift IS NULL OR Shift = @Shift)
AND CreatedAt >= @StartDate AND CreatedAt <= @EndDate
GROUP BY ClockNumber
ORDER BY COUNT(*) DESC";

                viewModel.TopOperators = await connection.QueryAsync<dynamic>(operatorQuery, new
                {
                    Department = paramDepartment,
                    Line = paramLine,
                    Shift = paramShift,
                    StartDate = effectiveStartDate,
                    EndDate = effectiveEndDate.AddDays(1)
                });

                // NEW: Scrap by department
                var departmentQuery = @"
SELECT 
    Department,
    COUNT(*) AS Count,
    SUM(Quantity) AS TotalQuantity
FROM [IntegrationCompanyB].[dbo].[scpScrapData]
WHERE CreatedAt >= @StartDate AND CreatedAt <= @EndDate
GROUP BY Department
ORDER BY COUNT(*) DESC";

                viewModel.ScrapByDepartment = await connection.QueryAsync<dynamic>(departmentQuery, new
                {
                    StartDate = effectiveStartDate,
                    EndDate = effectiveEndDate.AddDays(1)
                });

                // NEW: Scrap by shift
                var shiftQuery = @"
SELECT 
    Shift,
    COUNT(*) AS Count,
    SUM(Quantity) AS TotalQuantity
FROM [IntegrationCompanyB].[dbo].[scpScrapData]
WHERE (@Department IS NULL OR Department = @Department)
AND (@Line IS NULL OR Line = @Line)
AND CreatedAt >= @StartDate AND CreatedAt <= @EndDate
GROUP BY Shift
ORDER BY COUNT(*) DESC";

                viewModel.ScrapByShift = await connection.QueryAsync<dynamic>(shiftQuery, new
                {
                    Department = paramDepartment,
                    Line = paramLine,
                    StartDate = effectiveStartDate,
                    EndDate = effectiveEndDate.AddDays(1)
                });

                // NEW: Scrap by line
                var lineQuery = @"
SELECT TOP 10
    Line,
    COUNT(*) AS Count,
    SUM(Quantity) AS TotalQuantity
FROM [IntegrationCompanyB].[dbo].[scpScrapData]
WHERE (@Department IS NULL OR Department = @Department)
AND CreatedAt >= @StartDate AND CreatedAt <= @EndDate
GROUP BY Line
ORDER BY COUNT(*) DESC";

                viewModel.ScrapByLine = await connection.QueryAsync<dynamic>(lineQuery, new
                {
                    Department = paramDepartment,
                    StartDate = effectiveStartDate,
                    EndDate = effectiveEndDate.AddDays(1)
                });

                // Existing top parts query
                var topItemsQuery = @"
SELECT TOP 10 
    s.PartNumber,
    ISNULL(im.LongDesc, 'No description') AS Description,
    ISNULL(SUM(s.Quantity), 0) AS TotalQuantity,
    COUNT(s.ScrapId) AS OccurrenceCount
FROM [IntegrationCompanyB].[dbo].[scpScrapData] s
LEFT JOIN [SysproCompanyB].[dbo].[InvMaster] im
    ON s.PartNumber = im.StockCode COLLATE Latin1_General_CI_AS
   AND im.StockCode COLLATE Latin1_General_BIN NOT IN ('EBD0532a', 'AVf30B/R', ' 4KB-927680-501')
WHERE (@Department IS NULL OR s.Department = @Department)
AND (@Line IS NULL OR s.Line = @Line)
AND (@Shift IS NULL OR s.Shift = @Shift)
AND s.CreatedAt >= @StartDate AND s.CreatedAt <= @EndDate
GROUP BY s.PartNumber, im.LongDesc
ORDER BY SUM(s.Quantity) DESC";

                // NEW: Get total weight statistics
                var weightStatsQuery = @"
SELECT 
    COUNT(*) AS TotalWeighings,
    ISNULL(SUM(Weight), 0) AS TotalWeight,
    ISNULL(AVG(Weight), 0) AS AverageWeight,
    ISNULL(MAX(Weight), 0) AS MaxWeight
FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
WHERE NOT (Line = 'ALL LINES' AND Shift = 'ALL')
AND NOT (Shift = 'MONTHLY')
AND (@Department IS NULL OR Department = @Department OR (IsBulkWeighing = 1 AND @Department IS NULL))
AND (@Line IS NULL OR Line = @Line OR (IsBulkWeighing = 1 AND @Line IS NULL))
AND (@Shift IS NULL OR Shift = @Shift OR (IsBulkWeighing = 1 AND @Shift IS NULL))
AND CreatedAt >= @StartDate AND CreatedAt <= @EndDate";
                var weightStats = await connection.QueryFirstOrDefaultAsync<dynamic>(weightStatsQuery, new
                {
                    Department = paramDepartment,
                    Line = paramLine,
                    Shift = paramShift,
                    StartDate = effectiveStartDate,
                    EndDate = effectiveEndDate.AddDays(1)
                });

                viewModel.TotalWeighings = weightStats?.TotalWeighings ?? 0;
                viewModel.TotalWeight = weightStats?.TotalWeight ?? 0;
                viewModel.AverageWeight = weightStats?.AverageWeight ?? 0;
                viewModel.MaxWeight = weightStats?.MaxWeight ?? 0;

                // NEW: Weight by department
                var weightByDeptQuery = @"
SELECT 
    CASE 
        WHEN IsBulkWeighing = 1 THEN 'BULK WEIGHING'
        WHEN Department IS NULL OR Department = '' THEN 'Unknown'
        ELSE Department 
    END AS Department,
    ISNULL(SUM(Weight), 0) AS TotalWeight,
    COUNT(*) AS Count
FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
WHERE NOT (Line = 'ALL LINES' AND Shift = 'ALL')
AND NOT (Shift = 'MONTHLY')
AND (@Department IS NULL OR Department = @Department)
AND (@Line IS NULL OR Line = @Line)
AND (@Shift IS NULL OR Shift = @Shift)
AND CreatedAt >= @StartDate AND CreatedAt <= @EndDate
GROUP BY 
    CASE 
        WHEN IsBulkWeighing = 1 THEN 'BULK WEIGHING'
        WHEN Department IS NULL OR Department = '' THEN 'Unknown'
        ELSE Department 
    END
ORDER BY SUM(Weight) DESC";

                viewModel.WeightByDepartment = await connection.QueryAsync<dynamic>(weightByDeptQuery, new
                {
                    Department = paramDepartment,   // ✅ now actually passed in
                    Line = paramLine,
                    Shift = paramShift,
                    StartDate = effectiveStartDate,
                    EndDate = effectiveEndDate.AddDays(1)
                });

                // NEW: Weight by shift
                var weightByShiftQuery = @"
SELECT 
    CASE 
        WHEN IsBulkWeighing = 1 THEN 'MULTIPLE'
        WHEN Shift IS NULL OR Shift = '' THEN 'Unknown'
        ELSE Shift 
    END AS Shift,
    ISNULL(SUM(Weight), 0) AS TotalWeight,
    COUNT(*) AS Count
FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
WHERE NOT (Line = 'ALL LINES' AND Shift = 'ALL')
AND NOT (Shift = 'MONTHLY')
AND (@Department IS NULL OR Department = @Department)
AND (@Line IS NULL OR Line = @Line)
AND CreatedAt >= @StartDate AND CreatedAt <= @EndDate
GROUP BY 
    CASE 
        WHEN IsBulkWeighing = 1 THEN 'MULTIPLE'
        WHEN Shift IS NULL OR Shift = '' THEN 'Unknown'
        ELSE Shift 
    END
ORDER BY SUM(Weight) DESC";

                viewModel.WeightByShift = await connection.QueryAsync<dynamic>(weightByShiftQuery, new
                {
                    Department = paramDepartment,
                    Line = paramLine,
                    StartDate = effectiveStartDate,
                    EndDate = effectiveEndDate.AddDays(1)
                });

                // NEW: Daily weight trend
                var dailyWeightTrendQuery = @"
SELECT 
    CAST(CreatedAt AS DATE) AS Date,
    ISNULL(SUM(Weight), 0) AS TotalWeight,
    COUNT(*) AS Count
FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
WHERE NOT (Line = 'ALL LINES' AND Shift = 'ALL')
AND NOT (Shift = 'MONTHLY')
AND (@Department IS NULL OR Department = @Department OR (IsBulkWeighing = 1 AND @Department IS NULL))
AND (@Line IS NULL OR Line = @Line OR (IsBulkWeighing = 1 AND @Line IS NULL))
AND (@Shift IS NULL OR Shift = @Shift OR (IsBulkWeighing = 1 AND @Shift IS NULL))
AND CreatedAt >= @StartDate AND CreatedAt <= @EndDate
GROUP BY CAST(CreatedAt AS DATE)
ORDER BY CAST(CreatedAt AS DATE)";

                viewModel.DailyWeightTrend = await connection.QueryAsync<dynamic>(dailyWeightTrendQuery, new
                {
                    Department = paramDepartment,
                    Line = paramLine,
                    Shift = paramShift,
                    StartDate = effectiveStartDate,
                    EndDate = effectiveEndDate.AddDays(1)
                });

                viewModel.TopScrapItems = (await connection.QueryAsync<TopScrapItemViewModel>(topItemsQuery, new
                {
                    Department = paramDepartment,
                    Line = paramLine,
                    Shift = paramShift,
                    StartDate = effectiveStartDate,
                    EndDate = effectiveEndDate.AddDays(1)
                })).ToList();

                if (!viewModel.TopScrapItems.Any())
                {
                    viewModel.TopScrapItems.Add(new TopScrapItemViewModel
                    {
                        PartNumber = "No data",
                        Description = "No data found for the selected filters",
                        TotalQuantity = 0,
                        OccurrenceCount = 0
                    });
                }

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading analytics dashboard: {Message}", ex.Message);
                ViewBag.ErrorMessage = "An error occurred while loading the dashboard. Technical details: " + ex.Message;

                var errorStartDate = startDate ?? DateTime.Today.AddMonths(-3);
                var errorEndDate = endDate ?? DateTime.Today;

                return View(new AnalyticsDashboardViewModel
                {
                    CurrentDepartment = "All Departments",
                    CurrentLine = "All Lines",
                    CurrentShift = "All Shifts",
                    TotalScrapEntries = 0,
                    TotalScrapQuantity = 0,
                    ApprovedScrapCount = 0,
                    RejectedScrapCount = 0,
                    PendingScrapCount = 0,
                    TotalScrapCost = 0,
                    DateRange = new DateRangeViewModel
                    {
                        StartDate = errorStartDate,
                        EndDate = errorEndDate
                    },
                    Departments = new List<DepartmentViewModel>(),
                    Lines = new List<ProductionLineViewModel>(),
                    Shifts = new List<ShiftViewModel>(),
                    TopScrapItems = new List<TopScrapItemViewModel>
            {
                new TopScrapItemViewModel
                {
                    PartNumber = "No data",
                    Description = "Error loading data",
                    TotalQuantity = 0,
                    OccurrenceCount = 0
                }
            },
                    ApprovalFlowData = new List<dynamic>(),
                    ScrapByReason = new List<dynamic>(),
                    TopOperators = new List<dynamic>(),
                    ScrapByDepartment = new List<dynamic>(),
                    ScrapByShift = new List<dynamic>(),
                    ScrapByLine = new List<dynamic>()
                });
            }
        }

        private async Task<List<DepartmentViewModel>> GetDepartmentsForDropdown()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                var query = @"
    SELECT DepartmentId, DepartmentName, Category
    FROM [IntegrationCompanyB].[dbo].[scpDepartment]
    WHERE IsActive = 1
    ORDER BY Category, DepartmentName";

                var departments = await connection.QueryAsync<DepartmentViewModel>(query);
                return departments.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving departments for dropdown");
                return new List<DepartmentViewModel>();
            }
        }

        private async Task<List<ProductionLineViewModel>> GetLinesForDropdown(int departmentId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                var query = @"
            SELECT 
                LineId,
                LineName,
                DepartmentId
            FROM [IntegrationCompanyB].[dbo].[scpLine]
            WHERE DepartmentId = @DepartmentId
            AND IsActive = 1
            ORDER BY LineName";

                var lines = await connection.QueryAsync<ProductionLineViewModel>(query, new { DepartmentId = departmentId });
                return lines.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving lines for department {DepartmentId}", departmentId);
                return new List<ProductionLineViewModel>();
            }
        }

        private async Task<List<ProductionLineViewModel>> GetAllLinesForDropdown()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                var query = @"
            SELECT 
                LineId,
                LineName,
                DepartmentId
            FROM [IntegrationCompanyB].[dbo].[scpLine]
            WHERE IsActive = 1
            ORDER BY LineName";

                var lines = await connection.QueryAsync<ProductionLineViewModel>(query);
                return lines.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all lines");
                return new List<ProductionLineViewModel>();
            }
        }

        private List<ShiftViewModel> GetShiftsForDropdown()
        {
            // Hardcoded list of shifts since no table exists
            return new List<ShiftViewModel>
    {
        new ShiftViewModel { ShiftId = 1, ShiftName = "A" },
        new ShiftViewModel { ShiftId = 2, ShiftName = "B" },
        new ShiftViewModel { ShiftId = 3, ShiftName = "Straight" }
    };
        }

        [HttpGet]
        public async Task<IActionResult> GetLinesByDepartment(int departmentId)
        {
            try
            {
                _logger.LogInformation("GetLinesByDepartment called with departmentId: {0}", departmentId);

                if (departmentId <= 0)
                {
                    _logger.LogWarning("Invalid department ID: {0}, returning all lines", departmentId);
                    // Return all lines instead of an empty list for better UX
                    using var allConnection = new SqlConnection(_connectionString);
                    await allConnection.OpenAsync();

                    var allLinesQuery = @"
        SELECT 
            LineId as lineId,
            LineName as lineName
        FROM [IntegrationCompanyB].[dbo].[scpLine]
        WHERE IsActive = 1
        ORDER BY LineName";

                    var allLines = await allConnection.QueryAsync<object>(allLinesQuery);
                    var allResults = allLines.ToList();
                    _logger.LogInformation("Found {0} lines (all departments)", allResults.Count);
                    return Json(allResults);
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var lineQuery = @"
        SELECT 
            LineId as lineId,
            LineName as lineName
        FROM [IntegrationCompanyB].[dbo].[scpLine]
        WHERE DepartmentId = @DepartmentId
        AND IsActive = 1
        ORDER BY LineName";

                var departmentLines = await connection.QueryAsync<object>(lineQuery, new { DepartmentId = departmentId });

                var lineResults = departmentLines.ToList();
                _logger.LogInformation("Found {0} lines for department ID {1}", lineResults.Count, departmentId);

                return Json(lineResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving lines for department ID {0}", departmentId);
                // Return empty array instead of error to prevent UI disruption
                return Json(new object[0]);
            }
        }   

        [HttpGet]
        public async Task<IActionResult> GetLinesByDepartmentName(string department)
        {
            try
            {
                _logger.LogInformation("GetLinesByDepartmentName called with department: {0}", department);

                if (string.IsNullOrEmpty(department) || department == "All Departments")
                {
                    return Json(new { success = true, lines = new string[0] });
                }

                // Use the same logic as in ScrapApprovalController for department/line mapping
                var validLines = GetLinesForDepartment(department);

                _logger.LogInformation("Found {0} lines for department {1}", validLines.Count, department);

                return Json(new
                {
                    success = true,
                    lines = validLines
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving lines for department {0}", department);
                return Json(new
                {
                    success = false,
                    message = "Error retrieving lines",
                    lines = new string[0]
                });
            }
        }

        // Helper method to get lines for a department (same logic as ScrapApprovalController)
        private List<string> GetLinesForDepartment(string department)
        {
            var preAssemblyAreas = new List<string> {
        "Komax", "Manual", "Battery", "Twisting", "WrapShield",
        "Bonders", "Shielded", "Sleeves", "Extrusion", "Internal Logistics", "Moulding"
    };

            var techDeptLines = new List<string> {
        "Projects", "Samples", "Obsolescence", "Capabilities", "Engineering Changes",
        "SQA", "Stores", "Engineering", "Process", "Kanban", "Project", "Quality"
    };

            var departmentLines = new Dictionary<string, List<string>>
    {
        { "FORD", new List<string> {
            "Line 1-ERM", "Line 2-ERM", "Line 3-ERM", "Line 4-ERM",
            "Line 5-IP", "Line 6-IP", "Line 7-IP", "Line 8-IP",
            "Line 9-Roofs", "Line 10-Bumper Line", "Line 12-Panther Engine",
            "Line 15-Blower Motor", "Line 16-Rear Doors DC or LHS & RHS",
            "Line 17-Transmission", "Line 18-Brake Switch", "Line 19-Tailgate",
            "Line 20-Rear Seats LHS & RHS", "Line 21-Front Seat", "Line 21-Front SEAT",
            "Line 22-Front Seat", "Line 23-Relay Battery Ground",
            "Line 24-Body", "Line 26-Body", "Line 27-Body", "Line 28-Body",
            "Line 29-Front Door LHS", "Line 30-Front Door LHS",
            "Line 31-Chassis", "Line 32-Chassis", "Line 34-Statics",
            "Line 35-ERM", "Line 36-ERM", "Line 36-ERM (No longer ERM family)"
        }},
        { "HILUX", new List<string> {
            "2022 Doors", "Floor LHD", "Floor RHD", "Floor SUV 640",
            "Floor UNDER & BATTERY", "Frame",
            "Hilux IP LHD Other", "Hilux IP LHD GD", "IP RHD Others",
            "Statics IP 1Statics IP 2", "Statics IP3 Battery", "Roofs-RR Doors",
            "Wire Engine GD", "Wire Engine GD 2", "IP RHD GD", "Wire Engine Other",
            "FLOOR RHD 581D", "FLOOR LHD 581D", "IP RHD 581D", "IP LHD 581D",
            "ENGINE LINE 1", "ENGINE LINE 2", "IP RHD LINE 2", "IP LHD LINE 2", "STATICS IP 3"
        }},
        { "HIACE", new List<string> { "Line-1", "Line-2", "Line-3" }},
        { "CSUV", new List<string> {
            "CSUV", "CSUV Statics", "Doors", "ERM", "Engine", "Floor", "Floor 6&8", "IP"
        }},
        { "CSUV SCRAP", new List<string> { "CSUV Scrap" }},
        { "FIELLI", new List<string> {
            "CSUV", "CSUV Statics", "Doors", "ERM", "Engine", "Floor", "Floor 6&8", "IP"
        }},
        { "PLANT C", new List<string> {
            "ERM", "IP", "Body", "Chassis", "Door", "Statics"
        }},
        { "PRE-ASSEMBLY FORD",  preAssemblyAreas },
        { "PRE-ASSEMBLY ISUZU", preAssemblyAreas },
        { "PRE-ASSEMBLY TSAM",  preAssemblyAreas },
        { "PRE-ASSEMBLY CSUV",  preAssemblyAreas },
        { "ENGINEERING PLANT A", new List<string> { "Engineering Plant A" }},
        { "ENGINEERING PLANT B", new List<string> { "Engineering Plant B" }},
        { "ENGINEERING PLANT C", new List<string> { "Engineering Plant C" }},
        { "ENGINEERING FIELLI",  new List<string> { "Engineering Fielli" }},
        { "SQA",         techDeptLines },
        { "STORES",      techDeptLines },
        { "ENGINEERING", techDeptLines },
        { "PROCESS",     techDeptLines },
        { "KANBAN",      techDeptLines },
        { "PROJECT",     techDeptLines },
        { "QUALITY",     techDeptLines },
        { "QUANTUM REWORK",          new List<string> { "Quantum Rework" }},
        { "FINAL ASSY-CSUV REWORK",  new List<string> { "Final Assy-Csuv Rework" }},
        { "FINAL ASSY-ISUZU REWORK", new List<string> { "Final Assy-Isuzu Rework" }},
        { "PRE ASSY-CSUV REWORK",    new List<string> { "Pre Assy-Csuv Rework" }},
        { "PRE ASSY-ISUZU REWORK",   new List<string> { "Pre Assy-Isuzu Rework" }},
        { "HILUX REWORK",            new List<string> { "Hilux Rework" }},
        { "PROJECT 581D REWORK",     new List<string> { "Project 581D Rework" }},
        { "PRE ASSY-TSAM REWORK",    new List<string> { "Pre Assy-Tsam Rework" }},
        { "FINAL ASSY-FORD REWORK",  new List<string> { "Final Assy-Ford Rework" }},
        { "PRE ASSY-FORD REWORK",    new List<string> { "Pre Assy-Ford Rework" }},
        { "FIELLI REWORK",           new List<string> { "Fielli Rework" }},
    };

            return departmentLines.TryGetValue(department, out var lines) ? lines : new List<string>();
        }

        // Helper method to calculate total scrap cost
        private async Task<decimal> CalculateTotalScrapCost(DateTime? startDate = null, DateTime? endDate = null,
                                                            string department = null, string line = null, string shift = null)
        {
            // Get default date range if not specified
            startDate ??= DateTime.Today.AddDays(-30);
            endDate ??= DateTime.Today;

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
SELECT ISNULL(SUM(s.Quantity * ISNULL(im.MaterialCost, 0)), 0) AS TotalCost
FROM [IntegrationCompanyB].[dbo].[scpScrapData] s
LEFT JOIN (
    SELECT StockCode, LongDesc, StockUom, MaterialCost
    FROM (
        SELECT StockCode, LongDesc, StockUom, MaterialCost,
               ROW_NUMBER() OVER (
                   PARTITION BY UPPER(LTRIM(RTRIM(StockCode)))
                   ORDER BY CASE WHEN StockCode COLLATE Latin1_General_BIN
                                    = UPPER(LTRIM(RTRIM(StockCode))) COLLATE Latin1_General_BIN
                                 THEN 0 ELSE 1 END,
                            StockCode) AS rn
        FROM [SysproCompanyB].[dbo].[InvMaster]
    ) x
    WHERE rn = 1
) im ON im.StockCode = s.PartNumber COLLATE Latin1_General_CI_AS
WHERE (@Department IS NULL OR s.Department = @Department)
AND (@Line IS NULL OR s.Line = @Line)
AND (@Shift IS NULL OR s.Shift = @Shift)
AND (@StartDate IS NULL OR s.CreatedAt >= @StartDate)
AND (@EndDate IS NULL OR s.CreatedAt <= @EndDate)";

                _logger.LogInformation($"Cost calculation parameters: Department={department ?? "null"}, Line={line ?? "null"}, Shift={shift ?? "null"}");

                var totalCost = await connection.QueryFirstOrDefaultAsync<decimal?>(query, new
                {
                    Department = string.IsNullOrEmpty(department) || department == "All Departments" ? null : department,
                    Line = string.IsNullOrEmpty(line) || line == "All Lines" ? null : line,
                    Shift = string.IsNullOrEmpty(shift) || shift == "All Shifts" ? null : shift,
                    StartDate = startDate,
                    EndDate = endDate.Value.AddDays(1) // Include end date
                });

                _logger.LogInformation($"Calculated total cost: {totalCost ?? 0}");
                return totalCost ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating total scrap cost");
                return 0;
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportToExcelWithCharts(
    DateTime startDate, DateTime endDate,
    string department = null, string line = null, string shift = null)
        {
            try
            {
                // ── Admin-only gate (same pattern as ScrapApprovalController) ──────
                var clockNumber = User?.Identity?.Name;
                var user = await _context.User.FirstOrDefaultAsync(
                    u => (u.ClockNumber == clockNumber || u.UserName == clockNumber) && u.IsActive);

                if (user == null || user.UserType?.ToLower() != "admin")
                {
                    TempData["ErrorMessage"] = "Chart export is available to administrators only.";
                    return RedirectToAction("Index", "Analytics");
                }

                // Normalise "All ..." selections to null (matches your existing filters)
                department = string.IsNullOrEmpty(department) || department == "All Departments" ? null : department;
                line = string.IsNullOrEmpty(line) || line == "All Lines" ? null : line;
                shift = string.IsNullOrEmpty(shift) || shift == "All Shifts" ? null : shift;

                // ── Pull filtered data (same joins as your other exports) ──────────
                using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await connection.OpenAsync();

                const string query = @"
            SELECT
                s.ScrapId,
                s.PartNumber,
                s.Quantity,
                s.ScrapReason,
                s.Department,
                s.Line,
                s.Shift,
                s.Status,
                s.ClockNumber AS SubmittedBy,
                s.SubmissionDate,
                COALESCE(im.MaterialCost, 0)              AS MaterialCost,
                COALESCE(im.MaterialCost, 0) * s.Quantity AS TotalCost
            FROM [IntegrationCompanyB].[dbo].[scpScrapData] s
            LEFT JOIN [SysproCompanyB].[dbo].[InvMaster] im
                ON RTRIM(LTRIM(s.PartNumber)) = RTRIM(LTRIM(im.StockCode)) COLLATE Latin1_General_CI_AS
               AND im.StockCode COLLATE Latin1_General_BIN NOT IN ('EBD0532a', 'AVf30B/R', ' 4KB-927680-501')
            WHERE s.SubmissionDate >= @StartDate
              AND s.SubmissionDate <  @EndDate
              AND (@Department IS NULL OR s.Department = @Department)
              AND (@Line       IS NULL OR s.Line = @Line OR s.Line LIKE @Line + ' - %')
              AND (@Shift      IS NULL OR s.Shift      = @Shift)
            ORDER BY s.SubmissionDate DESC";

                var rows = (await connection.QueryAsync(query, new
                {
                    StartDate = startDate.Date,
                    EndDate = endDate.Date.AddDays(1),   // inclusive end date
                    Department = department,
                    Line = line,
                    Shift = shift
                })).ToList();

                // ── Aggregations for the summary tables / charts ────────────────────
                var byStatus = rows.GroupBy(r => (string)(r.Status ?? "Pending TL"))
                    .Select(g => new { Key = g.Key, Items = g.Count(), Cost = g.Sum(x => (decimal)x.TotalCost) })
                    .OrderByDescending(x => x.Items).ToList();

                var byReason = rows.GroupBy(r => (string)(r.ScrapReason ?? "Unspecified"))
                    .Select(g => new { Key = g.Key, Items = g.Count(), Qty = g.Sum(x => (int)x.Quantity) })
                    .OrderByDescending(x => x.Items).Take(10).ToList();

                var byLine = rows.GroupBy(r => (string)(r.Line ?? "Unknown"))
                    .Select(g => new { Key = g.Key, Items = g.Count(), Cost = g.Sum(x => (decimal)x.TotalCost) })
                    .OrderByDescending(x => x.Cost).ToList();

                var topParts = rows.GroupBy(r => (string)(r.PartNumber ?? ""))
                    .Select(g => new { Key = g.Key, Qty = g.Sum(x => (int)x.Quantity), Cost = g.Sum(x => (decimal)x.TotalCost) })
                    .OrderByDescending(x => x.Qty).Take(10).ToList();

                // ── Build workbook ──────────────────────────────────────────────────
                using var package = new ExcelPackage();

                // 1) Raw data sheet
                var data = package.Workbook.Worksheets.Add("Data");
                string[] headers = { "ScrapId", "Part Number", "Qty", "Reason", "Department",
                             "Line", "Shift", "Status", "Submitted By", "Date", "Total Cost (R)" };
                for (int c = 0; c < headers.Length; c++) data.Cells[1, c + 1].Value = headers[c];
                using (var hdr = data.Cells[1, 1, 1, headers.Length])
                {
                    hdr.Style.Font.Bold = true;
                    hdr.Style.Font.Color.SetColor(System.Drawing.Color.White);
                    hdr.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    hdr.Style.Fill.BackgroundColor.SetColor(System.Drawing.ColorTranslator.FromHtml("#1F4E79"));
                }
                int r2 = 2;
                foreach (var row in rows)
                {
                    data.Cells[r2, 1].Value = (int)row.ScrapId;
                    data.Cells[r2, 2].Value = (string)row.PartNumber;
                    data.Cells[r2, 3].Value = (int)row.Quantity;
                    data.Cells[r2, 4].Value = (string)(row.ScrapReason ?? "");
                    data.Cells[r2, 5].Value = (string)row.Department;
                    data.Cells[r2, 6].Value = (string)row.Line;
                    data.Cells[r2, 7].Value = (string)row.Shift;
                    data.Cells[r2, 8].Value = (string)(row.Status ?? "Pending TL");
                    data.Cells[r2, 9].Value = (string)row.SubmittedBy;
                    data.Cells[r2, 10].Value = (DateTime?)row.SubmissionDate;
                    data.Cells[r2, 10].Style.Numberformat.Format = "yyyy-mm-dd";
                    data.Cells[r2, 11].Value = (decimal)row.TotalCost;
                    data.Cells[r2, 11].Style.Numberformat.Format = "#,##0.00";
                    r2++;
                }
                data.Cells[data.Dimension.Address].AutoFitColumns();

                // 2) Summary sheet (chart source ranges)
                var s = package.Workbook.Worksheets.Add("Summary");

                int WriteTable<T>(int startRow, string title, string col1, string col2,
                                  IEnumerable<T> items, Func<T, object> key, Func<T, object> val)
                {
                    s.Cells[startRow, 1].Value = title;
                    s.Cells[startRow, 1].Style.Font.Bold = true;
                    s.Cells[startRow + 1, 1].Value = col1;
                    s.Cells[startRow + 1, 2].Value = col2;
                    s.Cells[startRow + 1, 1, startRow + 1, 2].Style.Font.Bold = true;
                    int rr = startRow + 2;
                    foreach (var it in items)
                    {
                        s.Cells[rr, 1].Value = key(it);
                        s.Cells[rr, 2].Value = val(it);
                        rr++;
                    }
                    return rr; // first free row after the table
                }

                int statusStart = 1;
                int afterStatus = WriteTable(statusStart, "By Status", "Status", "Items",
                                             byStatus, x => x.Key, x => x.Items);
                int reasonStart = afterStatus + 1;
                int afterReason = WriteTable(reasonStart, "By Reason (Top 10)", "Reason", "Items",
                                             byReason, x => x.Key, x => x.Items);
                int lineStart = afterReason + 1;
                int afterLine = WriteTable(lineStart, "Cost by Line (R)", "Line", "Cost",
                                           byLine, x => x.Key, x => Math.Round(x.Cost, 2));
                int partsStart = afterLine + 1;
                int afterParts = WriteTable(partsStart, "Top 10 Parts (Qty)", "Part", "Qty",
                                            topParts, x => x.Key, x => x.Qty);
                s.Cells[s.Dimension.Address].AutoFitColumns();

                // 3) Charts sheet — native, editable Excel charts
                var ch = package.Workbook.Worksheets.Add("Charts");
                ch.Cells[1, 1].Value =
                    $"Scrap Report — {department ?? "All Departments"} / {line ?? "All Lines"} / {shift ?? "All Shifts"} — " +
                    $"{startDate:dd MMM yyyy} to {endDate:dd MMM yyyy}  ({rows.Count} records)";
                ch.Cells[1, 1].Style.Font.Bold = true;
                ch.Cells[1, 1].Style.Font.Size = 13;

                void AddChart(eChartType type, string name, string title,
                              int tblHeaderRow, int tblRowCount, int posRow, int posCol)
                {
                    if (tblRowCount == 0) return;
                    var chart = ch.Drawings.AddChart(name, type);
                    chart.Title.Text = title;
                    chart.SetPosition(posRow, 0, posCol, 0);
                    chart.SetSize(560, 320);
                    var series = chart.Series.Add(
                        s.Cells[tblHeaderRow + 1, 2, tblHeaderRow + tblRowCount, 2],   // values
                        s.Cells[tblHeaderRow + 1, 1, tblHeaderRow + tblRowCount, 1]);  // categories
                    if (type != eChartType.Doughnut && type != eChartType.Pie)
                        chart.Legend.Remove();
                }

                AddChart(eChartType.Doughnut, "chStatus", "Approval Status",
                         statusStart + 1, byStatus.Count, 2, 0);
                AddChart(eChartType.BarClustered, "chReason", "Scrap by Reason",
                         reasonStart + 1, byReason.Count, 2, 9);
                AddChart(eChartType.BarClustered, "chLine", "Cost by Line (R)",
                         lineStart + 1, byLine.Count, 20, 0);
                AddChart(eChartType.BarClustered, "chParts", "Top 10 Parts by Qty",
                         partsStart + 1, topParts.Count, 20, 9);

                // ── Return file ─────────────────────────────────────────────────────
                var fileName = $"ScrapReport_Charts_{department ?? "AllDepts"}_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.xlsx";
                var bytes = package.GetAsByteArray();

                return File(bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ExportToExcelWithCharts");
                TempData["ErrorMessage"] = "Error generating chart report";
                return RedirectToAction("Index", "Analytics");
            }
        }

        [HttpGet]
        public IActionResult GetShifts()
        {
            try
            {
                var shifts = new[] { "A", "B", "Straight" };
                return Json(new { success = true, shifts });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting shifts");
                return Json(new { success = false, message = "Error loading shifts" });
            }
        }
    }
}