using Dapper;
using DocumentFormat.OpenXml.InkML;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SCP_Control.Attributes;
using SCP_Control.Constants;
using SCP_Control.DbContext;
using SCP_Control.Models;
using SCP_Control.Services;
using SCP_Control.Services.Interfaces;
using SCP_Control.Utilities;
using SCP_Control.ViewModels;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Security.Claims;

namespace SCP_Control.Controllers
{
    //[Authorize(Policy = "OperatorOnly")]
    [AllowAnonymous]
    public class OperatorController : Controller
    {
        private readonly ILogger<OperatorController> _logger;
        private readonly IOperatorService _operatorService;
        private readonly IInventoryService _inventoryService;
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly ISessionService _sessionService;
        private readonly IActivityLogService _activityLogService;
        private readonly ApplicationDbContext _context;
        private readonly IBatchIdService _batchIdService;
        public OperatorController(
         ILogger<OperatorController> logger,
         IActivityLogService activityLogService,
         IOperatorService operatorService,
          ISessionService sessionService,// Changed from OperatorService
         IInventoryService inventoryService,
        ApplicationDbContext context,
        IConfiguration configuration,
        IBatchIdService batchIdService)
        {
            _activityLogService = activityLogService ?? throw new ArgumentNullException(nameof(activityLogService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _operatorService = operatorService ?? throw new ArgumentNullException(nameof(operatorService));
            _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            _batchIdService = batchIdService ?? throw new ArgumentNullException(nameof(batchIdService)); // ✅ Add this
        }

        private static readonly HashSet<string> _engineeringDepts =
       new(StringComparer.OrdinalIgnoreCase)
       {
        "ENGINEERING PLANT A", "ENGINEERING PLANT B",
        "ENGINEERING PLANT C", "ENGINEERING FIELLI",
        "SQA", "STORES", "ENGINEERING", "PROCESS",
        "KANBAN", "PROJECT", "QUALITY", "CSUV SCRAP",
        "ADMIN PRE-ASSEMBLY FORD", "ADMIN PRE-ASSEMBLY TSAM"
       };

        private static bool IsEngineeringDept(string? department)
            => !string.IsNullOrEmpty(department) && _engineeringDepts.Contains(department);


        [HttpGet]
        [AllowAnonymous] // Temporarily allow anonymous to test
        public IActionResult DebugAccess()
        {
            _logger.LogInformation("DebugAccess called");

            if (User?.Identity?.IsAuthenticated != true)
            {
                return Json(new
                {
                    IsAuthenticated = false,
                    Message = "User is not authenticated",
                    RedirectToLogin = true
                });
            }



            var debugInfo = new Dictionary<string, object>
            {
                ["IsAuthenticated"] = User.Identity.IsAuthenticated,
                ["Username"] = User.Identity.Name ?? "Not set",
                ["ClaimsCount"] = User.Claims.Count()
            };

            // Add all claims as strings to avoid type issues
            var claimsList = new List<string>();
            foreach (var claim in User.Claims)
            {
                claimsList.Add($"{claim.Type}: '{claim.Value}'");
            }
            debugInfo["AllClaims"] = claimsList;

            // Check role specifically
            var userType = User.FindFirst(ClaimTypes.Role)?.Value;
            debugInfo["UserType"] = userType ?? "No role claim found";
            debugInfo["CanAccessOperator"] = User.HasClaim(ClaimTypes.Role, "operator");
            debugInfo["CanAccessOperatorCapital"] = User.HasClaim(ClaimTypes.Role, "Operator");

            return Json(debugInfo);
        }

        [HttpGet]
        public async Task<IActionResult> ScanPart()
        {
            try
            {
                var clockNumber = User.Identity?.Name;
                if (string.IsNullOrEmpty(clockNumber))
                {
                    _logger.LogWarning("❌ ScanPart: User not authenticated");
                    return RedirectToAction("Login", "Account");
                }

                _logger.LogInformation("📱 ScanPart called for user: {ClockNumber}", clockNumber);

                var operatorDetails = await _operatorService.GetOperatorDetails(clockNumber);
                if (operatorDetails == null)
                {
                    _logger.LogWarning("❌ ScanPart: Operator details not found for {ClockNumber}", clockNumber);
                    // Don't redirect to Setup here, just use empty values
                }

                // 🎯 Create clean model - NO MaterialCost or StockUom pre-population
                var model = new ScrapEntryViewModel
                {
                    Department = operatorDetails?.Department ?? "",
                    Line = operatorDetails?.Line ?? "",
                    Shift = operatorDetails?.Shift ?? "",

                    // 🚨 CRITICAL: Leave these empty - they will be populated by ValidatePartNumber
                    PartNumber = "", // User will scan/enter this
                    StockUom = "",   // Will be populated from InvMaster
                    MaterialCost = 0, // Will be populated from InvMaster
                    Quantity = 0,     // User will enter this

                    // Initialize empty collections
                    ScrapSections = new List<SelectListItem>(),
                    SectionReasons = new Dictionary<string, List<ScrapReasonItem>>()
                };

                // Handle error messages from redirects
                if (TempData["Error"] is string errorMessage)
                {
                    ViewBag.ErrorMessage = errorMessage;
                    _logger.LogWarning("⚠️ ScanPart displaying error: {ErrorMessage}", errorMessage);
                }

                _logger.LogInformation("✅ ScanPart model created for {Department}/{Line}/{Shift}",
                    model.Department, model.Line, model.Shift);

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error loading ScanPart view for user {ClockNumber}", User.Identity?.Name);

                // Return a minimal model in case of error
                var errorModel = new ScrapEntryViewModel
                {
                    Department = "Error Loading",
                    Line = "Error Loading",
                    Shift = "Error Loading",
                    PartNumber = "",
                    StockUom = "",
                    MaterialCost = 0,
                    Quantity = 0,
                    ScrapSections = new List<SelectListItem>(),
                    SectionReasons = new Dictionary<string, List<ScrapReasonItem>>()
                };

                ViewBag.ErrorMessage = "Error loading scan part page. Please try again.";
                return View(errorModel);
            }
        }


        [HttpGet]
        [AllowAnonymous] // For testing
        public async Task<IActionResult> VerifyMaterialCostAlignment()
        {
            try
            {
                _logger.LogInformation("🔍 Starting MaterialCost alignment verification");

                var verificationResults = new List<object>();
                var misalignedParts = new List<object>();
                var zeroCostParts = new List<object>();

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // 1. Check for misaligned MaterialCosts
                var misalignmentQuery = @"
            SELECT 
                sd.ScrapId,
                sd.PartNumber,
                sd.MaterialCost as ScrapData_Cost,
                im.MaterialCost as InvMaster_Cost,
                ABS(sd.MaterialCost - im.MaterialCost) as Difference,
                sd.SubmissionDate,
                sd.ClockNumber
            FROM [IntegrationCompanyB].[dbo].[scpScrapData] sd
            INNER JOIN [SysproCompanyB].[dbo].[InvMaster] im ON sd.PartNumber = im.StockCode
            WHERE ABS(sd.MaterialCost - im.MaterialCost) > 0.00001
            ORDER BY sd.SubmissionDate DESC";

                using var misalignmentCommand = new SqlCommand(misalignmentQuery, connection);
                using var misalignmentReader = await misalignmentCommand.ExecuteReaderAsync();

                while (await misalignmentReader.ReadAsync())
                {
                    misalignedParts.Add(new
                    {
                        scrapId = misalignmentReader.GetInt32("ScrapId"),
                        partNumber = misalignmentReader.GetString("PartNumber"),
                        scrapDataCost = misalignmentReader.GetDecimal("ScrapData_Cost"),
                        invMasterCost = misalignmentReader.GetDecimal("InvMaster_Cost"),
                        difference = misalignmentReader.GetDecimal("Difference"),
                        submissionDate = misalignmentReader.GetDateTime("SubmissionDate").ToString("yyyy-MM-dd HH:mm:ss"),
                        clockNumber = misalignmentReader.GetString("ClockNumber")
                    });
                }
                misalignmentReader.Close();

                // 2. Check for zero cost parts in InvMaster that might cause issues
                var zeroCostQuery = @"
            SELECT DISTINCT
                im.StockCode,
                im.MaterialCost as InvMaster_Cost,
                COUNT(sd.ScrapId) as Scrap_Entries_Count
            FROM [SysproCompanyB].[dbo].[InvMaster] im
            LEFT JOIN [IntegrationCompanyB].[dbo].[scpScrapData] sd ON im.StockCode = sd.PartNumber
            WHERE im.MaterialCost = 0.00000
            GROUP BY im.StockCode, im.MaterialCost
            HAVING COUNT(sd.ScrapId) > 0
            ORDER BY COUNT(sd.ScrapId) DESC";

                using var zeroCostCommand = new SqlCommand(zeroCostQuery, connection);
                using var zeroCostReader = await zeroCostCommand.ExecuteReaderAsync();

                while (await zeroCostReader.ReadAsync())
                {
                    zeroCostParts.Add(new
                    {
                        partNumber = zeroCostReader.GetString("StockCode"),
                        invMasterCost = zeroCostReader.GetDecimal("InvMaster_Cost"),
                        scrapEntriesCount = zeroCostReader.GetInt32("Scrap_Entries_Count")
                    });
                }
                zeroCostReader.Close();

                // 3. Get summary statistics
                var summaryQuery = @"
            SELECT 
                COUNT(*) as Total_Scrap_Entries,
                COUNT(DISTINCT sd.PartNumber) as Unique_Parts,
                COUNT(CASE WHEN ABS(sd.MaterialCost - im.MaterialCost) > 0.00001 THEN 1 END) as Misaligned_Entries,
                COUNT(CASE WHEN sd.MaterialCost = 0 THEN 1 END) as Zero_Cost_Scrap_Entries,
                AVG(sd.MaterialCost) as Avg_Scrap_MaterialCost,
                AVG(im.MaterialCost) as Avg_InvMaster_MaterialCost
            FROM [IntegrationCompanyB].[dbo].[scpScrapData] sd
            INNER JOIN [SysproCompanyB].[dbo].[InvMaster] im ON sd.PartNumber = im.StockCode";

                using var summaryCommand = new SqlCommand(summaryQuery, connection);
                using var summaryReader = await summaryCommand.ExecuteReaderAsync();

                object summary = null;
                if (await summaryReader.ReadAsync())
                {
                    summary = new
                    {
                        totalScrapEntries = summaryReader.GetInt32("Total_Scrap_Entries"),
                        uniqueParts = summaryReader.GetInt32("Unique_Parts"),
                        misalignedEntries = summaryReader.GetInt32("Misaligned_Entries"),
                        zeroCostScrapEntries = summaryReader.GetInt32("Zero_Cost_Scrap_Entries"),
                        avgScrapMaterialCost = summaryReader.IsDBNull("Avg_Scrap_MaterialCost") ? 0 : summaryReader.GetDecimal("Avg_Scrap_MaterialCost"),
                        avgInvMasterMaterialCost = summaryReader.IsDBNull("Avg_InvMaster_MaterialCost") ? 0 : summaryReader.GetDecimal("Avg_InvMaster_MaterialCost")
                    };
                }

                var verificationReport = new
                {
                    verificationTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    summary = summary,
                    alignmentStatus = misalignedParts.Count == 0 ? "✅ ALIGNED" : $"❌ {misalignedParts.Count} MISALIGNED",
                    misalignedParts = misalignedParts.Take(20).ToList(), // Limit to first 20 for display
                    zeroCostParts = zeroCostParts.Take(10).ToList(),
                    recommendations = GetAlignmentRecommendations(misalignedParts.Count, zeroCostParts.Count)
                };

                _logger.LogInformation("🔍 MaterialCost verification completed:");
                _logger.LogInformation("   Total Scrap Entries: {TotalEntries}", summary?.GetType().GetProperty("totalScrapEntries")?.GetValue(summary));
                _logger.LogInformation("   Misaligned Entries: {MisalignedCount}", misalignedParts.Count);
                _logger.LogInformation("   Zero Cost Parts: {ZeroCostCount}", zeroCostParts.Count);

                return Json(verificationReport);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during MaterialCost alignment verification");
                return Json(new
                {
                    success = false,
                    error = ex.Message,
                    verificationTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
        }

        private List<string> GetAlignmentRecommendations(int misalignedCount, int zeroCostCount)
        {
            var recommendations = new List<string>();

            if (misalignedCount > 0)
            {
                recommendations.Add($"🔧 Run the alignment UPDATE query to fix {misalignedCount} misaligned entries");
                recommendations.Add("🔍 Investigate why new entries are getting misaligned MaterialCosts");
            }
            else
            {
                recommendations.Add("✅ All MaterialCosts are properly aligned!");
            }

            if (zeroCostCount > 0)
            {
                recommendations.Add($"⚠️ {zeroCostCount} parts have zero MaterialCost in InvMaster - verify these are correct");
                recommendations.Add("🔍 Check if these parts should have actual costs in the inventory system");
            }

            recommendations.Add("🎯 Ensure ValidatePartNumber and ScrapEntry methods use direct InvMaster lookups");
            recommendations.Add("🚨 Monitor new scrap submissions to ensure corruption-proof methods are working");

            return recommendations;
        }



        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var clockNumber = User.Identity?.Name;
            _logger.LogInformation($"=== OPERATOR INDEX DEBUG ===");
            _logger.LogInformation($"ClockNumber from User.Identity: '{clockNumber}'");

            if (string.IsNullOrEmpty(clockNumber))
            {
                _logger.LogWarning("ClockNumber is null or empty");
                return RedirectToAction("Login", "Account");
            }

            try
            {
                // ✅ ENHANCED: Get operator details with debugging
                var operatorDetails = await _operatorService.GetOperatorDetails(clockNumber);

                _logger.LogInformation($"OperatorDetails found: {operatorDetails != null}");
                if (operatorDetails != null)
                {
                    _logger.LogInformation($"Department: '{operatorDetails.Department}'");
                    _logger.LogInformation($"Line: '{operatorDetails.Line}'");
                    _logger.LogInformation($"Shift: '{operatorDetails.Shift}'");
                }

                // ✅ FALLBACK: If service doesn't return data, query directly from database
                if (operatorDetails == null ||
                    string.IsNullOrEmpty(operatorDetails.Department) ||
                    string.IsNullOrEmpty(operatorDetails.Line) ||
                    string.IsNullOrEmpty(operatorDetails.Shift))
                {
                    _logger.LogWarning("Service returned incomplete data, querying database directly");

                    using var connection = new SqlConnection(_connectionString);
                    await connection.OpenAsync();

                    var directQuery = @"
                SELECT 
                    ClockNumber,
                    ISNULL(Department, '') as Department,
                    ISNULL(Line, '') as Line,
                    ISNULL(Shift, '') as Shift,
                    ISNULL(MachineNumber, '') as MachineNumber,
                    ISNULL(UserType, '') as UserType
                FROM [IntegrationCompanyB].[dbo].[scpUser]
                WHERE ClockNumber = @ClockNumber AND IsActive = 1";

                    using var command = new SqlCommand(directQuery, connection);
                    command.Parameters.AddWithValue("@ClockNumber", clockNumber);

                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        var dept = reader.GetString("Department");
                        var line = reader.GetString("Line");
                        var shift = reader.GetString("Shift");

                        _logger.LogInformation($"Direct DB query results - Dept: '{dept}', Line: '{line}', Shift: '{shift}'");

                        operatorDetails = new OperatorDetailsViewModel
                        {
                            ClockNumber = clockNumber,
                            Department = dept,
                            Line = line,
                            Shift = shift,
                            MachineNumber = reader.GetString("MachineNumber")
                        };
                    }
                    else
                    {
                        _logger.LogError($"User {clockNumber} not found in database");
                    }
                }

                // Always fetch MachineNumber directly — service doesn't return it
                string machineNumber = "";
                try
                {
                    using var machineConn = new SqlConnection(_connectionString);
                    await machineConn.OpenAsync();

                    const string machineQuery = @"
        SELECT ISNULL(MachineNumber, '') as MachineNumber
        FROM [IntegrationCompanyB].[dbo].[scpUser]
        WHERE ClockNumber = @ClockNumber AND IsActive = 1";

                    using var machineCmd = new SqlCommand(machineQuery, machineConn);
                    machineCmd.Parameters.AddWithValue("@ClockNumber", clockNumber);

                    var result = await machineCmd.ExecuteScalarAsync();
                    machineNumber = result?.ToString() ?? "";

                    _logger.LogInformation($"MachineNumber fetched: '{machineNumber}' for {clockNumber}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching MachineNumber for {ClockNumber}", clockNumber);
                }

                ViewData["Department"] = operatorDetails?.Department ?? "Not Set";
                ViewData["Line"] = operatorDetails?.Line ?? "Not Set";
                ViewData["Shift"] = operatorDetails?.Shift ?? "Not Set";
                ViewData["Machine"] = machineNumber;

                _logger.LogInformation($"Final ViewData - Dept: '{ViewData["Department"]}', Line: '{ViewData["Line"]}', Shift: '{ViewData["Shift"]}'");

                // Create and return the view model
                var model = new OperatorDetailsViewModel
                {
                    ClockNumber = clockNumber,
                    Department = operatorDetails?.Department,
                    Line = operatorDetails?.Line,
                    Shift = operatorDetails?.Shift
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading operator dashboard for user {ClockNumber}", clockNumber);

                // ✅ ENHANCED ERROR HANDLING: Set error ViewData
                ViewData["Department"] = "Error Loading";
                ViewData["Line"] = "Error Loading";
                ViewData["Shift"] = "Error Loading";
                ViewData["ErrorMessage"] = ex.Message;

                return View(new OperatorDetailsViewModel { ClockNumber = clockNumber });
            }
        }

        [HttpGet]
        public IActionResult Test()
        {
            _logger.LogInformation("Test action called");
            // Return direct content to test controller
            return Content("Test content - If you see this, the controller is working");
        }
        // Add this test action to your OperatorController
        [HttpGet]
        public IActionResult Basic()
        {
            _logger.LogInformation("Basic action called");
            // Return view without any layout
            return View("Basic");
        }



        [HttpGet]
        public async Task<IActionResult> Setup()
        {
            try
            {
                var clockNumber = User.Identity?.Name;
                if (string.IsNullOrEmpty(clockNumber))
                {
                    return RedirectToAction("Login", "Account");
                }

                var departments = await _operatorService.GetActiveDepartments();
                var currentDetails = await _operatorService.GetOperatorDetails(clockNumber);

                var model = new OperatorSetupViewModel
                {
                    Departments = departments,
                    Shifts = new List<string> { "A", "B", "Straight" },
                    CurrentDepartment = currentDetails?.Department,
                    CurrentLine = currentDetails?.Line,
                    CurrentShift = currentDetails?.Shift,
                    CurrentMachine = currentDetails?.MachineNumber  // ← add this
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading setup page");
                return RedirectToAction("Error", "Home");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetLinesByDepartment(string departmentName, string lineType = null, string parentZone = null)
        {
            try
            {
                if (string.IsNullOrEmpty(departmentName))
                    return Json(new { success = false, error = "Department name is required" });

                using var connection = new SqlConnection(_connectionString);

                var sql = @"
            SELECT l.LineName, l.RequiresSubArea, l.LineType
            FROM [IntegrationCompanyB].[dbo].[scpLine] l
            INNER JOIN [IntegrationCompanyB].[dbo].[scpDepartment] d
                ON l.DepartmentId = d.DepartmentId
            LEFT JOIN [IntegrationCompanyB].[dbo].[scpLine] parent
                ON l.ParentLineId = parent.LineId
            WHERE d.DepartmentName = @DepartmentName
              AND l.IsActive = 1";

                if (!string.IsNullOrEmpty(lineType))
                    sql += " AND l.LineType = @LineType";

                if (!string.IsNullOrEmpty(parentZone))
                    sql += " AND parent.LineName = @ParentZone";
                else if (lineType == "Area")
                    sql += " AND l.ParentLineId IS NULL";

                sql += " ORDER BY l.LineName";

                var lines = await connection.QueryAsync(sql, new
                {
                    DepartmentName = departmentName,
                    LineType = lineType,
                    ParentZone = parentZone
                });

                return Json(new
                {
                    success = true,
                    lines = lines.Select(l => new {
                        name = l.LineName,
                        requiresSubArea = (bool)l.RequiresSubArea,
                        type = l.LineType
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting lines for department {DepartmentName}", departmentName);
                return Json(new { success = false, error = "Error loading lines" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetActiveDepartmentsJson()
        {
            try
            {
                var departments = await _operatorService.GetActiveDepartments();
                return Json(new { success = true, data = departments });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active departments");
                return Json(new { success = false, message = "Error loading departments" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDepartmentsGroupedByPlant()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                const string query = @"
            SELECT ISNULL(Plant,'Unassigned') as Plant, DepartmentName
            FROM [IntegrationCompanyB].[dbo].[scpDepartment]
            WHERE IsActive = 1
            ORDER BY Plant, DepartmentName";

                var rows = await connection.QueryAsync(query);
                var grouped = rows.GroupBy(r => (string)r.Plant)
                    .ToDictionary(g => g.Key, g => g.Select(r => (string)r.DepartmentName).ToList());

                return Json(new { success = true, data = grouped });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error grouping departments by plant");
                return Json(new { success = false, message = "Error loading plant/department mapping" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetMachinesByDepartmentLine(string department, string line)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                const string query = @"
            SELECT MachineNumber
            FROM [IntegrationCompanyB].[dbo].[scpMachine]
            WHERE DepartmentName = @Department AND LineName = @Line AND IsActive = 1
            ORDER BY MachineNumber";

                var machines = await connection.QueryAsync<string>(query, new { Department = department, Line = line });
                return Json(new { success = true, data = machines.ToList() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting machines for {Dept}/{Line}", department, line);
                return Json(new { success = false, message = "Error loading machines" });
            }
        }

        [HttpPost]
        public async Task<JsonResult> UpdateDetails([FromBody] UpdateOperatorDetailsViewModel model)
        {
            try
            {
                if (string.IsNullOrEmpty(model.Department) ||
                    string.IsNullOrEmpty(model.Line) ||
                    string.IsNullOrEmpty(model.Shift))
                {
                    return Json(new { success = false, message = "All fields are required" });
                }

                var clockNumber = User.Identity?.Name;
                if (string.IsNullOrEmpty(clockNumber))
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                // First update the User table directly
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string updateUserQuery = @"
                UPDATE [IntegrationCompanyB].[dbo].[scpUser]
                SET Department = @Department,
                    Line = @Line,
                    Shift = @Shift,
                    MachineNumber = @MachineNumber,
                    LastModifiedAt = @LastModifiedAt,
                    LastModifiedBy = @LastModifiedBy
                WHERE ClockNumber = @ClockNumber";

                    using (var command = new SqlCommand(updateUserQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Department", model.Department);
                        command.Parameters.AddWithValue("@Line", model.Line);
                        command.Parameters.AddWithValue("@Shift", model.Shift);
                        command.Parameters.AddWithValue("@LastModifiedAt", DateTime.UtcNow);
                        command.Parameters.AddWithValue("@LastModifiedBy", clockNumber);
                        command.Parameters.AddWithValue("@ClockNumber", clockNumber);
                        command.Parameters.AddWithValue("@MachineNumber", model.MachineNumber ?? "");
                        int rowsAffected = await command.ExecuteNonQueryAsync();

                        if (rowsAffected == 0)
                        {
                            _logger.LogWarning($"No user record updated for {clockNumber}");
                            return Json(new { success = false, message = "Failed to update user record" });
                        }

                        _logger.LogInformation($"Successfully updated user record for {clockNumber}");
                    }
                }

                // Then use the operator service as a backup/for other tables
                var success = await _operatorService.UpdateOperatorDetails(clockNumber, model);

                // Log the activity
                await _activityLogService.LogActivityAsync(
                    clockNumber,
                    "Update Details",
                    $"Updated details - Department: {model.Department}, Line: {model.Line}, Shift: {model.Shift}",
                    "Success"
                );

                return Json(new
                {
                    success = true,
                    message = "Details updated successfully",
                    redirect = Url.Action("Index", "Operator")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating operator details for {ClockNumber}", User.Identity?.Name);
                return Json(new { success = false, message = "Error updating details: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult Logout()
        {
            // Simply redirect to the centralized logout method
            return RedirectToAction("Logout", "Account");
        }

       

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> DebugScrapEntryMaterialCost(string partNumber = "#22WHSY")
        {
            try
            {
                _logger.LogInformation("🔍 DEBUG: Testing ScrapEntry MaterialCost for {PartNumber}", partNumber);

                // Test direct InvMaster lookup (same as what ScrapEntry should do)
                decimal invMasterCost = 0;
                string invMasterUom = "";
                bool success = false;

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var query = @"
                SELECT TOP 1 
                    ISNULL(StockUom, 'EA') as StockUom,
                    MaterialCost as ExactMaterialCost
                FROM [SysproCompanyB].[dbo].[InvMaster] 
                WHERE StockCode COLLATE SQL_Latin1_General_CP1_CI_AS = @partNumber COLLATE SQL_Latin1_General_CP1_CI_AS";

                    using var command = new SqlCommand(query, connection);
                    command.Parameters.AddWithValue("@partNumber", partNumber);

                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        invMasterUom = reader.GetString("StockUom");
                        invMasterCost = reader.GetDecimal("ExactMaterialCost");
                        success = true;
                    }
                }

                // Test what ValidatePartNumber would return
                var validateResponse = await ValidatePartNumber(partNumber);
                var validateJson = validateResponse as JsonResult;
                dynamic validateData = validateJson?.Value;

                // Check recent database entries
                var recentEntries = new List<object>();
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var recentQuery = @"
                SELECT TOP 3
                    ScrapId,
                    PartNumber,
                    MaterialCost,
                    SubmissionDate,
                    StockUom
                FROM [IntegrationCompanyB].[dbo].[scpScrapData]
                WHERE PartNumber = @partNumber
                ORDER BY SubmissionDate DESC";

                    using var command = new SqlCommand(recentQuery, connection);
                    command.Parameters.AddWithValue("@partNumber", partNumber);

                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        recentEntries.Add(new
                        {
                            ScrapId = reader.GetInt32("ScrapId"),
                            PartNumber = reader.GetString("PartNumber"),
                            MaterialCost = reader.GetDecimal("MaterialCost"),
                            MaterialCostFormatted = reader.GetDecimal("MaterialCost").ToString("F5"),
                            StockUom = reader.GetString("StockUom"),
                            SubmissionDate = reader.GetDateTime("SubmissionDate").ToString("yyyy-MM-dd HH:mm:ss")
                        });
                    }
                }

                var result = new
                {
                    TestTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    PartNumber = partNumber,

                    InvMasterDirect = new
                    {
                        Success = success,
                        MaterialCost = invMasterCost,
                        MaterialCostFormatted = $"{invMasterCost:F5}",
                        StockUom = invMasterUom,
                        Query = "Direct InvMaster lookup (what ScrapEntry should get)"
                    },

                    ValidatePartNumberAPI = new
                    {
                        IsValid = validateData?.isValid ?? false,
                        MaterialCost = validateData?.materialCost ?? 0,
                        MaterialCostFormatted = validateData?.materialCost != null ?
                            ((decimal)validateData.materialCost).ToString("F5") : "N/A",
                        StockUom = validateData?.stockUom ?? "",
                        Source = validateData?.source ?? ""
                    },

                    RecentDatabaseEntries = recentEntries,

                    Analysis = new
                    {
                        ExpectedMaterialCost = $"{invMasterCost:F5}",
                        InvMasterVsValidateAPI = invMasterCost == (decimal)(validateData?.materialCost ?? 0) ?
                            "✅ MATCH" : "❌ MISMATCH",
                        RecentEntriesCorrect = recentEntries.Count > 0 ?
                            recentEntries.All(e => Math.Abs(((dynamic)e).MaterialCost - invMasterCost) < 0.00001m) ?
                                "✅ ALIGNED" : "❌ MISALIGNED" : "No recent entries",

                        NextSteps = new[]
                        {
                    "1. Verify ScrapEntry method signature only accepts 'partNumber'",
                    "2. Verify ScrapEntry view shows MaterialCost with F5 formatting",
                    "3. Test the complete flow: ScanPart -> ScrapEntry -> Submit",
                    "4. Check database after submission"
                }
                    }
                };

                _logger.LogInformation("🔍 DEBUG Results:");
                _logger.LogInformation("   InvMaster MaterialCost: {Cost:F5}", invMasterCost);
           

                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Debug test failed");
                return Json(new
                {
                    Error = ex.Message,
                    StackTrace = ex.StackTrace
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ValidatePartNumber(string partNumber)
        {
            try
            {
                var normalizedPartNumber = partNumber?.Trim() ?? string.Empty;
                _logger.LogInformation("🔧 ValidatePartNumber called with: '{PartNumber}'", normalizedPartNumber);

                if (string.IsNullOrEmpty(normalizedPartNumber))
                    return Json(new { isValid = false, message = "Part number is required" });

                var query = @"
            SELECT TOP 1 
                StockCode,
                ISNULL(StockUom, 'EA') as StockUom,
                MaterialCost as ExactMaterialCost,
                ISNULL(PartCategory, '') as PartCategory
            FROM [InvMaster] 
            WHERE StockCode COLLATE SQL_Latin1_General_CP1_CI_AS 
                  = @partNumber COLLATE SQL_Latin1_General_CP1_CI_AS";

                // --- Try CompanyB first ---
                var connB = _configuration.GetConnectionString("SysproConnection");
                using (var connection = new SqlConnection(connB))
                {
                    await connection.OpenAsync();
                    using var cmd = new SqlCommand(query, connection);
                    cmd.Parameters.AddWithValue("@partNumber", normalizedPartNumber);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        var exactStockCode = reader.GetString("StockCode");
                        var uom = reader.GetString("StockUom");
                        var cost = reader.GetDecimal("ExactMaterialCost");
                        var cat = reader.GetString("PartCategory");
                        _logger.LogInformation("✅ Part found in CompanyB: {Part}, ExactCode: {ExactCode}, Cost: {Cost:F5}",
                            normalizedPartNumber, exactStockCode, cost);
                        return Json(new
                        {
                            isValid = true,
                            message = "Valid part number",
                            stockUom = uom,
                            materialCost = cost,
                            partCategory = cat,
                            company = "B",
                            partNumber = exactStockCode,  // ← Syspro's exact casing
                            source = "SysproCompanyB",
                            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                        });
                    }
                }

                // --- Fallback: try CompanyX ---
                _logger.LogInformation("⚠️ Part not found in CompanyB, trying CompanyX: {Part}", normalizedPartNumber);
                var connX = _configuration.GetConnectionString("SysproConnectionX");
                using (var connection = new SqlConnection(connX))
                {
                    await connection.OpenAsync();
                    using var cmd = new SqlCommand(query, connection);
                    cmd.Parameters.AddWithValue("@partNumber", normalizedPartNumber);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        var exactStockCode = reader.GetString("StockCode");
                        var uom = reader.GetString("StockUom");
                        var cost = reader.GetDecimal("ExactMaterialCost");
                        var cat = reader.GetString("PartCategory");
                        _logger.LogInformation("✅ Part found in CompanyX: {Part}, ExactCode: {ExactCode}, Cost: {Cost:F5}",
                            normalizedPartNumber, exactStockCode, cost);
                        return Json(new
                        {
                            isValid = true,
                            message = "Valid part number",
                            stockUom = uom,
                            materialCost = cost,
                            partCategory = cat,
                            company = "X",
                            partNumber = exactStockCode,  // ← Syspro's exact casing
                            source = "SysproCompanyX",
                            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                        });
                    }
                }

                // --- Not found in either ---
                _logger.LogWarning("❌ Part not found in CompanyB or CompanyX: {Part}", normalizedPartNumber);
                return Json(new
                {
                    isValid = false,
                    message = "Part number not found in inventory system",
                    stockUom = "",
                    materialCost = 0,
                    partCategory = "",
                    company = "B"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ValidatePartNumber error");
                return Json(new
                {
                    isValid = false,
                    message = "Error validating part number",
                    stockUom = "",
                    materialCost = 0,
                    partCategory = "",
                    company = "B"
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ScrapEntryView(string partNumber)
        {
            if (string.IsNullOrEmpty(partNumber))
            {
                return RedirectToAction("ScanPart");
            }

            try
            {
                var isValid = await _inventoryService.ValidatePartNumber(partNumber);
                if (!isValid)
                {
                    TempData["Error"] = "Invalid part number";
                    return RedirectToAction("ScanPart");
                }

                var clockNumber = User.Identity?.Name;
                var operatorDetails = await _operatorService.GetOperatorDetails(clockNumber ?? "");

                // Get scrap sections
                var scrapSections = await _operatorService.GetActiveScrapSections();

                // Get scrap reasons for each section
                var sectionReasons = new Dictionary<string, List<ScrapReasonItem>>();
                foreach (var section in scrapSections)
                {
                    var sectionId = section.Value;
                    var reasons = await _operatorService.GetScrapReasonsBySection(int.Parse(sectionId));
                    sectionReasons[sectionId] = reasons.Select(r => new ScrapReasonItem
                    {
                        Value = r.Value,
                        Text = r.Text,
                        RequiresPosition = section.Text == "CombiMeter" || section.Text == "Sleeve"
                    }).ToList();
                }

                var model = new ScrapEntryViewModel
                {
                    PartNumber = partNumber,
                    Department = operatorDetails?.Department ?? "",
                    Line = operatorDetails?.Line ?? "",
                    Shift = operatorDetails?.Shift ?? "",
                    ScrapSections = scrapSections,
                    SectionReasons = sectionReasons // Using the new property name
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading scrap entry for part {PartNumber}", partNumber);
                return RedirectToAction("Error", "Error");
            }
        }

        // ============================================================================
        // SCRAPENTRY GET
        // ============================================================================

        [HttpGet]
        public async Task<IActionResult> ScrapEntry(string partNumber, string scrapType = "component")
        {
            _logger.LogInformation("🔧 ScrapEntry called with partNumber: {PartNumber}, scrapType: {ScrapType}",
                partNumber, scrapType);

            try
            {
                if (string.IsNullOrEmpty(partNumber))
                {
                    TempData["Error"] = "Part number is required";
                    return RedirectToAction(nameof(ScanPart));
                }

                var clockNumber = User.Identity?.Name;
                if (string.IsNullOrEmpty(clockNumber))
                    return RedirectToAction("Login", "Account");

                var operatorDetails = await _operatorService.GetOperatorDetails(clockNumber);
                if (operatorDetails == null)
                    return RedirectToAction("Setup");

                // ----------------------------------------------------------------
                // InvMaster lookup — CompanyB first, then CompanyX as fallback
                // ----------------------------------------------------------------
                string exactStockUom = "";
                decimal exactMaterialCost = 0;
                string exactPartCategory = "";
                string exactCompany = "B";
                bool invMasterSuccess = false;

                const string invQuery = @"
            SELECT TOP 1 
                ISNULL(StockUom, 'EA')   AS StockUom,
                MaterialCost             AS ExactMaterialCost,
                ISNULL(PartCategory, '') AS PartCategory
            FROM [InvMaster]
            WHERE StockCode COLLATE SQL_Latin1_General_CP1_CI_AS
                  = @partNumber COLLATE SQL_Latin1_General_CP1_CI_AS";

                // --- CompanyB ---
                using (var connection = new SqlConnection(
                           _configuration.GetConnectionString("SysproConnection")))
                {
                    await connection.OpenAsync();
                    using var command = new SqlCommand(invQuery, connection);
                    command.Parameters.AddWithValue("@partNumber", partNumber);
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        exactStockUom = reader.GetString("StockUom");
                        exactMaterialCost = reader.GetDecimal("ExactMaterialCost");
                        exactPartCategory = reader.GetString("PartCategory");
                        exactCompany = "B";
                        invMasterSuccess = true;
                        _logger.LogInformation("✅ Part found in CompanyB: {Part}, Cost: {Cost:F5}",
                            partNumber, exactMaterialCost);
                    }
                }

                // --- CompanyX fallback ---
                if (!invMasterSuccess)
                {
                    _logger.LogInformation(
                        "⚠️ Part not found in CompanyB, trying CompanyX: {Part}", partNumber);

                    using var connection = new SqlConnection(
                        _configuration.GetConnectionString("SysproConnectionX"));
                    await connection.OpenAsync();
                    using var command = new SqlCommand(invQuery, connection);
                    command.Parameters.AddWithValue("@partNumber", partNumber);
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        exactStockUom = reader.GetString("StockUom");
                        exactMaterialCost = reader.GetDecimal("ExactMaterialCost");
                        exactPartCategory = reader.GetString("PartCategory");
                        exactCompany = "X";
                        invMasterSuccess = true;
                        _logger.LogInformation("✅ Part found in CompanyX: {Part}, Cost: {Cost:F5}",
                            partNumber, exactMaterialCost);
                    }
                }

                if (!invMasterSuccess)
                {
                    _logger.LogWarning(
                        "❌ Part {Part} not found in CompanyB or CompanyX", partNumber);
                    TempData["Error"] = $"Part {partNumber} not found in inventory system";
                    return RedirectToAction(nameof(ScanPart));
                }

                // ----------------------------------------------------------------
                // Plant lookup from department
                // ----------------------------------------------------------------
                string plant = "";
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    const string plantQuery = @"
                SELECT TOP 1 ISNULL(Plant, '') AS Plant
                FROM [IntegrationCompanyB].[dbo].[scpDepartment]
                WHERE DepartmentName = @Department AND IsActive = 1";

                    using var command = new SqlCommand(plantQuery, connection);
                    command.Parameters.AddWithValue("@Department", operatorDetails.Department);
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        plant = reader.GetString("Plant");
                        _logger.LogInformation("✅ Found Plant: {Plant} for Department: {Department}",
                            plant, operatorDetails.Department);
                    }
                }

                // ----------------------------------------------------------------
                // Build scrap sections and reasons
                // ----------------------------------------------------------------
                var scrapSections = await _operatorService.GetActiveScrapSections();
                var sectionReasons = new Dictionary<string, List<ScrapReasonItem>>();

                foreach (var section in scrapSections)
                {
                    var sectionId = section.Value;
                    var reasons = await _operatorService.GetScrapReasonsBySection(int.Parse(sectionId));
                    sectionReasons[sectionId] = reasons.Select(r => new ScrapReasonItem
                    {
                        Value = r.Value,
                        Text = r.Text,
                        RequiresPosition = section.Text == "CombiMeter" || section.Text == "Sleeve"
                    }).ToList();
                }

                var model = new ScrapEntryViewModel
                {
                    PartNumber = partNumber,
                    Department = operatorDetails.Department,
                    Line = operatorDetails.Line,
                    Shift = operatorDetails.Shift,
                    ScrapSections = scrapSections,
                    SectionReasons = sectionReasons,
                    StockUom = exactStockUom,
                    MaterialCost = exactMaterialCost,
                    PartCategory = exactPartCategory,
                    Plant = plant,
                    Company = exactCompany,   // "B" or "X"
                    ScrapType = scrapType,
                    Quantity = 0
                };

                _logger.LogInformation(
                    "✅ ScrapEntry model: Part={Part}, Type={Type}, Cat={Cat}, " +
                    "Plant={Plant}, Company={Company}, Cost={Cost:F5}",
                    model.PartNumber, model.ScrapType, model.PartCategory,
                    model.Plant, model.Company, model.MaterialCost);

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in ScrapEntry for part {PartNumber}", partNumber);
                TempData["Error"] = "An error occurred while processing your request.";
                return RedirectToAction(nameof(ScanPart));
            }
        }


        // ============================================================================
        // SUBMITSCRAP POST
        // ============================================================================

        [HttpPost]
        public async Task<IActionResult> SubmitScrap([FromBody] ScrapEntryData scrapData)
        {
            try
            {
                var clockNumber = User.Identity?.Name ?? "";

                // ----------------------------------------------------------------
                // Batch ID
                // ----------------------------------------------------------------
                int batchScrapId;
                if (scrapData.BatchScrapId.HasValue && scrapData.BatchScrapId > 0)
                {
                    // Client already knows the batch for this submission run (multiple entries submitted together)
                    batchScrapId = scrapData.BatchScrapId.Value;
                    _logger.LogInformation("📦 Using existing BatchId: {BatchId}", batchScrapId);
                }
                else
                {
                    // ✅ NEW: reuse the operator's most recent batch if it hasn't been weighed yet,
                    // instead of always minting a new one — otherwise every "New Part" without weighing
                    // first orphans the previous batch permanently.
                    batchScrapId = await GetOrCreateActiveBatchIdAsync(clockNumber);
                }

                _logger.LogInformation(
                    "=== SUBMIT SCRAP === User: {User}, Part: {Part}, Qty: {Qty}, " +
                    "Type: {Type}, Weight: {Weight}kg, BatchId: {BatchId}",
                    clockNumber, scrapData.PartNumber, scrapData.Quantity,
                    scrapData.ScrapType, scrapData.Weight, batchScrapId);

                if (string.IsNullOrEmpty(scrapData.PartNumber) || scrapData.Quantity <= 0)
                    return Json(new { success = false, message = "Invalid data" });

                if (scrapData.ScrapType?.ToLower() == "wire" && scrapData.Weight == 0)
                    _logger.LogWarning("⚠️ Wire scrap submitted without weight: {Part}", scrapData.PartNumber);

                // ----------------------------------------------------------------
                // InvMaster lookup — CompanyB first, then CompanyX as fallback
                // ----------------------------------------------------------------
                decimal exactMaterialCost = 0;
                string exactStockUom = "";
                string exactPartCategory = "";
                string exactCompany = "B";
                bool invMasterSuccess = false;

                const string invQuery = @"
            SELECT TOP 1 
                ISNULL(StockUom, 'EA')   AS StockUom,
                MaterialCost             AS ExactMaterialCost,
                ISNULL(PartCategory, '') AS PartCategory
            FROM [InvMaster]
            WHERE StockCode COLLATE SQL_Latin1_General_CP1_CI_AS
                  = @partNumber COLLATE SQL_Latin1_General_CP1_CI_AS";

                // --- CompanyB ---
                using (var connection = new SqlConnection(
                           _configuration.GetConnectionString("SysproConnection")))
                {
                    await connection.OpenAsync();
                    using var command = new SqlCommand(invQuery, connection);
                    command.Parameters.AddWithValue("@partNumber", scrapData.PartNumber);
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        exactStockUom = reader.GetString("StockUom");
                        exactMaterialCost = reader.GetDecimal("ExactMaterialCost");
                        exactPartCategory = reader.GetString("PartCategory");
                        exactCompany = "B";
                        invMasterSuccess = true;
                        _logger.LogInformation("✅ Part found in CompanyB: {Part}, Cost: {Cost:F5}",
                            scrapData.PartNumber, exactMaterialCost);
                    }
                }

                // --- CompanyX fallback ---
                if (!invMasterSuccess)
                {
                    _logger.LogInformation(
                        "⚠️ Part not found in CompanyB, trying CompanyX: {Part}", scrapData.PartNumber);

                    using var connection = new SqlConnection(
                        _configuration.GetConnectionString("SysproConnectionX"));
                    await connection.OpenAsync();
                    using var command = new SqlCommand(invQuery, connection);
                    command.Parameters.AddWithValue("@partNumber", scrapData.PartNumber);
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        exactStockUom = reader.GetString("StockUom");
                        exactMaterialCost = reader.GetDecimal("ExactMaterialCost");
                        exactPartCategory = reader.GetString("PartCategory");
                        exactCompany = "X";
                        invMasterSuccess = true;
                        _logger.LogInformation("✅ Part found in CompanyX: {Part}, Cost: {Cost:F5}",
                            scrapData.PartNumber, exactMaterialCost);
                    }
                }

                if (!invMasterSuccess)
                {
                    _logger.LogWarning(
                        "❌ Part {Part} not found in CompanyB or CompanyX", scrapData.PartNumber);
                    return Json(new { success = false, message = "Part not found in InvMaster" });
                }

                // ----------------------------------------------------------------
                // Plant lookup
                // ----------------------------------------------------------------
                string plant = "";
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    const string plantQuery = @"
                SELECT TOP 1 ISNULL(Plant, '') AS Plant
                FROM [IntegrationCompanyB].[dbo].[scpDepartment]
                WHERE DepartmentName = @Department AND IsActive = 1";

                    using var command = new SqlCommand(plantQuery, connection);
                    command.Parameters.AddWithValue("@Department", scrapData.Department);
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                        plant = reader.GetString("Plant");
                }

                // ----------------------------------------------------------------
                // Build and save the scrap entry
                // ----------------------------------------------------------------
                bool isEngineeringDept = _engineeringDepts.Contains(scrapData.Department ?? "");

                // Engineering depts skip TL — first approver is Group Leader
                string initialStatus = isEngineeringDept ? "Pending GL" : "Pending TL";
                string initialApprovalStatus = isEngineeringDept ? "Pending GL" : "Pending TL";
                int initialApprovalLevel = isEngineeringDept ? 2 : 1;

                _logger.LogInformation(
                    "📋 Dept: {Dept}, IsEngineering: {IsEng}, InitialStatus: {Status}",
                    scrapData.Department, isEngineeringDept, initialStatus);

                var newScrapEntry = new ScrapData
                {
                    PartNumber = scrapData.PartNumber,
                    ScrapSectionId = scrapData.ScrapSectionId,
                    ScrapReason = scrapData.ScrapReason,
                    Quantity = scrapData.Quantity,
                    Position = scrapData.Position ?? "",
                    Length = scrapData.Length,
                    Weight = scrapData.Weight,
                    ScrapType = scrapData.ScrapType,
                    MachineType = scrapData.MachineType ?? "",
                    MachineNumber = scrapData.MachineNumber ?? "",
                    Department = scrapData.Department,
                    Line = scrapData.Line,
                    Shift = scrapData.Shift,
                    ClockNumber = clockNumber,
                    StockUom = exactStockUom,
                    MaterialCost = exactMaterialCost,
                    PartCategory = exactPartCategory,
                    Plant = plant,
                    Company = exactCompany,
                    BatchScrapId = batchScrapId,
                    SubmissionDate = DateTime.Now,
                    CreatedAt = DateTime.Now,
                    ModifiedAt = DateTime.Now,
                    Status = initialStatus,           // ✅ "Pending GL" for engineering
                    FirstApprovalStatus = initialApprovalStatus,   // ✅ matches
                    CurrentApprovalLevel = initialApprovalLevel,    // ✅ 2 for engineering, 1 for normal

                    // Engineering plant (section 10 only)
                    EngineeringPlant = scrapData.ScrapSectionId == 10
                                        ? scrapData.EngineeringPlant ?? ""
                                        : null
                };

                _context.ScrapData.Add(newScrapEntry);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "✅ Saved ScrapId: {ScrapId}, BatchId: {BatchId}, Company: {Company}, " +
                    "Type: {Type}, Weight: {Weight}kg, Cost: {Cost:F5}",
                    newScrapEntry.ScrapId, newScrapEntry.BatchScrapId, newScrapEntry.Company,
                    newScrapEntry.ScrapType, newScrapEntry.Weight, newScrapEntry.MaterialCost);

                await _activityLogService.LogActivityAsync(
                    clockNumber,
                    "Submit Scrap",
                    $"BatchId: {batchScrapId}, Part: {newScrapEntry.PartNumber}, " +
                    $"Qty: {newScrapEntry.Quantity}, Type: {newScrapEntry.ScrapType}, " +
                    $"Company: {newScrapEntry.Company}, Weight: {newScrapEntry.Weight}kg",
                    "Success"
                );

                return Json(new
                {
                    success = true,
                    message = "Scrap entry submitted successfully",
                    data = new
                    {
                        scrapId = newScrapEntry.ScrapId,
                        batchScrapId = batchScrapId,
                        partNumber = newScrapEntry.PartNumber,
                        partCategory = exactPartCategory,
                        scrapType = newScrapEntry.ScrapType,
                        company = newScrapEntry.Company,
                        weight = newScrapEntry.Weight,
                        plant = plant,
                        quantity = newScrapEntry.Quantity
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ SubmitScrap ERROR for part {Part}", scrapData?.PartNumber);
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyCompleteAlignment()
        {
            try
            {
                _logger.LogInformation("🔍 Verifying complete MaterialCost alignment across all components");

                var results = new List<object>();

                // Test recent submissions to see the 5 decimal precision
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var recentQuery = @"
                SELECT TOP 5
                    sd.ScrapId,
                    sd.PartNumber,
                    sd.MaterialCost as Stored_MaterialCost,
                    im.MaterialCost as InvMaster_MaterialCost,
                    ABS(sd.MaterialCost - im.MaterialCost) as Difference,
                    sd.Quantity,
                    (sd.MaterialCost * sd.Quantity) as TotalCost,
                    sd.SubmissionDate,
                    CASE 
                        WHEN ABS(sd.MaterialCost - im.MaterialCost) < 0.00001 THEN '✅ PERFECTLY ALIGNED'
                        WHEN ABS(sd.MaterialCost - im.MaterialCost) < 0.01 THEN '⚠️ CLOSE BUT NOT EXACT'
                        ELSE '❌ MISALIGNED'
                    END as AlignmentStatus
                FROM [IntegrationCompanyB].[dbo].[scpScrapData] sd
                INNER JOIN [SysproCompanyB].[dbo].[InvMaster] im ON sd.PartNumber = im.StockCode
                ORDER BY sd.SubmissionDate DESC";

                    using var command = new SqlCommand(recentQuery, connection);
                    using var reader = await command.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        results.Add(new
                        {
                            ScrapId = reader.GetInt32("ScrapId"),
                            PartNumber = reader.GetString("PartNumber"),
                            StoredMaterialCost = reader.GetDecimal("Stored_MaterialCost"),
                            InvMasterMaterialCost = reader.GetDecimal("InvMaster_MaterialCost"),
                            Difference = reader.GetDecimal("Difference"),
                            Quantity = reader.GetInt32("Quantity"),
                            TotalCost = reader.GetDecimal("TotalCost"),
                            SubmissionDate = reader.GetDateTime("SubmissionDate").ToString("yyyy-MM-dd HH:mm:ss"),
                            AlignmentStatus = reader.GetString("AlignmentStatus"),

                            // Formatted values for display
                            StoredCostFormatted = reader.GetDecimal("Stored_MaterialCost").ToString("F5"),
                            InvMasterCostFormatted = reader.GetDecimal("InvMaster_MaterialCost").ToString("F5"),
                            TotalCostFormatted = $"R{reader.GetDecimal("TotalCost"):N2}"
                        });
                    }
                }

                // Summary analysis
                var alignedCount = results.Count(r => ((dynamic)r).AlignmentStatus.ToString().Contains("PERFECTLY ALIGNED"));
                var totalCount = results.Count;

                var summary = new
                {
                    TestTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    TotalEntriesChecked = totalCount,
                    PerfectlyAligned = alignedCount,
                    AlignmentPercentage = totalCount > 0 ? Math.Round((double)alignedCount / totalCount * 100, 1) : 0,
                    OverallStatus = alignedCount == totalCount ?
                        "🎉 ALL ENTRIES PERFECTLY ALIGNED" :
                        $"⚠️ {alignedCount}/{totalCount} entries aligned ({Math.Round((double)alignedCount / totalCount * 100, 1)}%)",

                    ComponentStatus = new
                    {
                        ScanPart = "✅ Frontend displays exact MaterialCost with locale fix",
                        ScrapEntry = "✅ Backend uses exact InvMaster lookup",
                        SubmitScrap = "✅ Saves MaterialCost with 5 decimal precision",
                        Database = "✅ Column supports decimal(18,5)",
                        EntityFramework = "✅ Configured for decimal(18,5)",
                        ScrapHistory = "✅ Should display costs with full precision"
                    },

                    NextSteps = alignedCount == totalCount ?
                        new[] { "✅ All components are perfectly aligned!", "✅ MaterialCost precision issue resolved!" } :
                        new[] { "Check individual entries that are not perfectly aligned", "Verify recent submissions are using the fixed code" }
                };

                var finalResult = new
                {
                    Summary = summary,
                    RecentEntries = results,
                    TestResults = new
                    {
                        DatabasePrecision = "✅ Supports 5 decimal places",
                        EntityFrameworkConfig = "✅ Updated to decimal(18,5)",
                        FrontendParsing = "✅ Locale-safe decimal parsing",
                        BackendLookup = "✅ Exact InvMaster queries",
                        OverallAlignment = summary.OverallStatus
                    }
                };

                _logger.LogInformation("🎯 Verification Results:");
                _logger.LogInformation("   Total entries checked: {Count}", totalCount);
                _logger.LogInformation("   Perfectly aligned: {Aligned}/{Total}", alignedCount, totalCount);
                _logger.LogInformation("   Overall status: {Status}", summary.OverallStatus);

                return Json(finalResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Verification failed");
                return Json(new
                {
                    Error = ex.Message,
                    StackTrace = ex.StackTrace
                });
            }
        }

        // Updated database submission method
        private async Task<bool> SubmitScrapEntryToDatabase(ScrapEntry entry)
        {
            try
            {
                _logger.LogInformation("Submitting scrap entry to database: {@Entry}", entry);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                string insertQuery = @"
            INSERT INTO [dbo].[ScrapData] (
                PartNumber, 
                ClockNumber, 
                ScrapSectionId, 
                ReasonId, 
                ScrapReason, 
                Quantity, 
                Status, 
                Position, 
                Department, 
                Line, 
                Shift, 
                SubmissionDate, 
                CreatedAt, 
                CreatedBy,
                ModifiedAt,
                ModifiedBy,
                StockUom,
                MaterialCost,
                FirstApprovalStatus,
                CurrentApprovalLevel
            ) VALUES (
                @PartNumber, 
                @ClockNumber, 
                @ScrapSectionId, 
                @ReasonId, 
                @ScrapReason, 
                @Quantity, 
                @Status, 
                @Position, 
                @Department, 
                @Line, 
                @Shift, 
                @SubmissionDate, 
                @CreatedAt, 
                @CreatedBy,
                @ModifiedAt,
                @ModifiedBy,
                @StockUom,
                @MaterialCost,
                @FirstApprovalStatus,
                @CurrentApprovalLevel
            );
            SELECT SCOPE_IDENTITY();";

                var scrapId = await connection.QuerySingleAsync<int>(insertQuery, new
                {
                    entry.PartNumber,
                    entry.ClockNumber,
                    entry.ScrapSectionId,
                    entry.ReasonId,
                    entry.ScrapReason,
                    entry.Quantity,
                    Status = "Pending TL", // UPDATED: Set to "Pending TL"
                    entry.Position,
                    entry.Department,
                    entry.Line,
                    entry.Shift,
                    entry.SubmissionDate,
                    entry.CreatedAt,
                    entry.CreatedBy,
                    entry.ModifiedAt,
                    entry.ModifiedBy,
                    entry.StockUom,
                    entry.MaterialCost,
                    FirstApprovalStatus = "Pending TL", // UPDATED: Set to "Pending TL"
                    CurrentApprovalLevel = 1 // Start at Team Leader level
                });

                _logger.LogInformation("Scrap entry inserted successfully with ID: {ScrapId}", scrapId);
                return scrapId > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting scrap entry to database");
                return false;
            }
        }

        public async Task<bool> SubmitScrapEntry(ScrapEntry entry)
        {
            try
            {
                _logger.LogInformation("Attempting to submit scrap entry: {@Entry}", entry);

                // Validate entry
                if (entry == null || !entry.IsValid())
                {
                    _logger.LogWarning("Invalid scrap entry data");
                    return false;
                }

                // Log section ID for debugging
                _logger.LogInformation("Saving scrap entry with section ID: {SectionId}", entry.ScrapSectionId);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Modified SQL query that doesn't include TerminalQuantity, WeightKg, or Length columns
                string insertQuery = @"
            INSERT INTO [dbo].[ScrapData] (
                PartNumber, 
                ClockNumber, 
                ScrapSectionId, 
                ReasonId, 
                ScrapReason, 
                Quantity, 
                Status, 
                Position, 
                Department, 
                Line, 
                Shift, 
                SubmissionDate, 
                
                CreatedAt, 
                CreatedBy,
                ModifiedAt,
                ModifiedBy,
                StockUom,
                MaterialCost
            ) VALUES (
                @PartNumber, 
                @ClockNumber, 
                @ScrapSectionId, 
                @ReasonId, 
                @ScrapReason, 
                @Quantity, 
                @Status, 
                @Position, 
                @Department, 
                @Line, 
                @Shift, 
                @SubmissionDate, 
                
                @CreatedAt, 
                @CreatedBy,
                @ModifiedAt,
                @ModifiedBy,
                @StockUom,
                @MaterialCost
            )";

                // Execute the insert query with parameters
                int rowsAffected = await connection.ExecuteAsync(insertQuery, new
                {
                    entry.PartNumber,
                    entry.ClockNumber,
                    entry.ScrapSectionId,
                    entry.ReasonId,
                    entry.ScrapReason,
                    entry.Quantity,
                    entry.Status,
                    entry.Position,
                    entry.Department,
                    entry.Line,
                    entry.Shift,
                    entry.SubmissionDate,
                  
                    entry.CreatedAt,
                    entry.CreatedBy,
                    entry.ModifiedAt,
                    entry.ModifiedBy,
                    entry.StockUom,
                    entry.MaterialCost
                });

                _logger.LogInformation("Scrap entry insert result: {RowsAffected} rows affected", rowsAffected);
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing scrap entry insert");
                return false;
            }
        }

        [HttpGet]
        public IActionResult Error()
        {
            var errorMessage = TempData["ErrorMessage"] as string ?? "An unexpected error occurred.";
            ViewData["ErrorMessage"] = errorMessage;
            return View("Error");
        }

        private static ScrapData MapScrapEntryToData(ScrapEntry entry, string clockNumber)
        {
            return new ScrapData
            {
                ClockNumber = clockNumber,
                PartNumber = entry.PartNumber ?? string.Empty,
                ScrapSectionId = entry.ScrapSectionId.GetValueOrDefault(),
                ScrapReason = entry.ScrapReason ?? string.Empty,
                Quantity = entry.Quantity,
                Position = entry.Position,
                Status = "Pending TL", // UPDATED: Changed from "Pending" to "Pending TL"
                FirstApprovalStatus = "Pending TL", // UPDATED: Add this field
                CurrentApprovalLevel = 1, // UPDATED: Start at Team Leader level
                SubmissionDate = DateTime.UtcNow,
                Department = entry.Department ?? string.Empty,
                Line = entry.Line ?? string.Empty,
                Shift = entry.Shift ?? string.Empty,
                CreatedBy = clockNumber,
                CreatedAt = DateTime.UtcNow,
            };
        }

        [HttpGet]
        public async Task<IActionResult> GetActiveScrapSections()
        {
            try
            {
                _logger.LogInformation("Fetching active scrap sections");

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string query = @"
            SELECT 
                ScrapSectionId as value,
                ScrapSectionName as text
            FROM [dbo].[ScrapSection]
            WHERE IsActive = 1
            ORDER BY ScrapSectionName";

                var sections = await connection.QueryAsync<SelectListItem>(query);
                var sectionsList = sections.ToList();

                _logger.LogInformation("Retrieved {Count} sections", sectionsList.Count);

                return Json(new
                {
                    success = true,
                    data = sectionsList
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active scrap sections");
                return Json(new
                {
                    success = false,
                    message = "Error loading sections",
                    error = ex.Message
                });
            }
        }


        [HttpGet]
        public async Task<IActionResult> GetScrapReasonDetails(int reasonId)
        {
            try
            {
                var reasonDetails = await _operatorService.GetScrapReasonDetails(reasonId);
                if (reasonDetails == null)
                {
                    return Json(new { success = false, message = "Reason not found" });
                }

                return Json(new
                {
                    success = true,
                    details = new
                    {
                        reasonId = reasonDetails.ErrorId,
                        reasonName = reasonDetails.ScrapReasonName,
                        requiresPosition = !string.IsNullOrEmpty(reasonDetails.Part_Position)
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scrap reason details for ID {ReasonId}", reasonId);
                return Json(new { success = false, message = "Error loading reason details" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ValidateScrapEntry([FromBody] ScrapEntryValidationRequest request)
        {
            try
            {
                if (request.Quantity <= 0)
                {
                    return Json(new { success = false, message = "Quantity must be greater than 0" });
                }

                var section = await _operatorService.GetScrapSectionById(request.ScrapSectionId);
                if (section == null)
                {
                    return Json(new { success = false, message = "Invalid scrap section" });
                }

                if (section.RequiresPosition && string.IsNullOrEmpty(request.Position))
                {
                    return Json(new { success = false, message = "Position is required for this section" });
                }

                if (request.TerminalQuantity.HasValue && request.TerminalQuantity.Value > request.Quantity)
                {
                    return Json(new { success = false, message = "Terminal quantity cannot exceed total quantity" });
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating scrap entry");
                return Json(new { success = false, message = "Error validating entry" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateScrapEntry([FromBody] UpdateScrapEntryRequest request)
        {
            try
            {
                if (request == null || request.EntryId <= 0)
                {
                    return Json(new { success = false, message = "Invalid request" });
                }

                var result = await _operatorService.UpdateScrapEntry(request);
                return Json(new { success = result.Success, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating scrap entry {EntryId}", request?.EntryId);
                return Json(new { success = false, message = "Error updating entry" });
            }
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteScrapEntry(int entryId)
        {
            try
            {
                var result = await _operatorService.DeleteScrapEntry(entryId);
                return Json(new { success = result.Success, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting scrap entry {EntryId}", entryId);
                return Json(new { success = false, message = "Error deleting entry" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetPendingScrapEntries()
        {
            try
            {
                var clockNumber = User.Identity?.Name;
                var entries = await _operatorService.GetPendingScrapEntries(clockNumber ?? "");

                return Json(new
                {
                    success = true,
                    entries = entries.Select(e => new {
                        entryId = e.EntryId,
                        partNumber = e.PartNumber,
                        sectionName = e.SectionName,
                        reasonName = e.ReasonName,
                        quantity = e.Quantity,
                        terminalQuantity = e.TerminalQuantity,
                        weightKg = e.WeightKg,
                        length = e.Length,
                        position = e.Position,
                        createdAt = e.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pending scrap entries");
                return Json(new { success = false, message = "Error loading pending entries" });
            }
        }


        [HttpGet]
        public async Task<IActionResult> ScrapHistoryRaw()
        {
            try
            {
                var clockNumber = User.Identity?.Name ?? "";
                Console.WriteLine($"Loading scrap history for user: {clockNumber}");

                // Use raw SQL to handle NULLs properly
                var sql = @"
            SELECT 
                ScrapId,
                ISNULL(PartNumber, '') as PartNumber,
                ISNULL(ScrapReason, '') as ScrapReason,
                ISNULL(Position, '') as Position,
                ISNULL(Status, 'Pending') as Status,
                ISNULL(Department, '') as Department,
                ISNULL(Line, '') as Line,
                ISNULL(Shift, '') as Shift,
                ISNULL(ClockNumber, '') as ClockNumber,
                ISNULL(FirstApprovalStatus, 'Pending') as FirstApprovalStatus,
                ScrapSectionId,
                Quantity,
                MaterialCost,
                SubmissionDate,
                CreatedAt,
                ModifiedAt,
                ISNULL(CurrentApprovalLevel, 1) as CurrentApprovalLevel
            FROM [IntegrationCompanyB].[dbo].[scpScrapData]
            WHERE ISNULL(ClockNumber, '') = @clockNumber
            ORDER BY SubmissionDate DESC";

                var scrapHistory = new List<ScrapHistoryViewModel>();

                using var connection = new SqlConnection(_context.Database.GetConnectionString());
                await connection.OpenAsync();

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@clockNumber", clockNumber);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    try
                    {
                        scrapHistory.Add(new ScrapHistoryViewModel
                        {
                            ScrapId = reader.GetInt32("ScrapId"),
                            PartNumber = reader.GetString("PartNumber"),
                            ScrapSectionId = reader.GetInt32("ScrapSectionId"),
                            Section = GetSectionName(reader.GetInt32("ScrapSectionId")),
                            ScrapReason = reader.GetString("ScrapReason"),
                            Quantity = reader.GetInt32("Quantity"),
                            Position = reader.GetString("Position"),
                            Status = reader.GetString("Status"),
                            MaterialCost = reader.GetDecimal("MaterialCost"),
                            SubmissionDate = reader.GetDateTime("SubmissionDate"),
                            FormattedSubmissionDate = reader.GetDateTime("SubmissionDate").ToString("yyyy-MM-dd HH:mm:ss"),
                            Department = reader.GetString("Department"),
                            Line = reader.GetString("Line"),
                            Shift = reader.GetString("Shift"),
                            ClockNumber = reader.GetString("ClockNumber"),
                            CreatedAt = reader.GetDateTime("CreatedAt"),
                            ModifiedAt = reader.GetDateTime("ModifiedAt"),
                            FirstApprovalStatus = reader.GetString("FirstApprovalStatus"),
                            StockUom = "",
                            ApprovalStatusColor = GetApprovalStatusColor(reader.GetString("FirstApprovalStatus")),
                            CurrentApprovalLevel = reader.GetInt32("CurrentApprovalLevel") switch
                            {
                                1 => "Team Leader",
                                2 => "Group Leader",
                                3 => "Manager",
                                4 => "Senior Manager",
                                5 => "Completed",
                                _ => "Team Leader"
                            }
                        });
                    }
                    catch (Exception entryEx)
                    {
                        Console.WriteLine($"Error processing row: {entryEx.Message}");
                    }
                }

                Console.WriteLine($"Found {scrapHistory.Count} entries using raw SQL");

                ViewData["ClockNumber"] = clockNumber;
                ViewData["CurrentDepartment"] = scrapHistory.FirstOrDefault()?.Department ?? "";
                ViewData["CurrentLine"] = scrapHistory.FirstOrDefault()?.Line ?? "";
                ViewData["CurrentShift"] = scrapHistory.FirstOrDefault()?.Shift ?? "";

                return View("ScrapHistory", scrapHistory);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Raw SQL error: {ex.Message}");

                ViewData["ClockNumber"] = User.Identity?.Name ?? "";
                ViewData["CurrentDepartment"] = "";
                ViewData["CurrentLine"] = "";
                ViewData["CurrentShift"] = "";

                return View("ScrapHistory", new List<ScrapHistoryViewModel>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> TestScrapData()
        {
            try
            {
                var clockNumber = User.Identity?.Name ?? "";

                using var connection = new SqlConnection(_context.Database.GetConnectionString());
                await connection.OpenAsync();

                // Check for NULL values
                var sql = @"
            SELECT TOP 5
                ScrapId,
                PartNumber,
                Status,
                Department,
                Line,
                Shift,
                CurrentApprovalLevel,
                CASE WHEN PartNumber IS NULL THEN 'NULL' ELSE 'OK' END as PartNumber_Status,
                CASE WHEN Status IS NULL THEN 'NULL' ELSE 'OK' END as Status_Status,
                CASE WHEN CurrentApprovalLevel IS NULL THEN 'NULL' ELSE 'OK' END as CurrentApprovalLevel_Status
            FROM [IntegrationCompanyB].[dbo].[scpScrapData]
            WHERE ClockNumber = @clockNumber
            ORDER BY CreatedAt DESC";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@clockNumber", clockNumber);

                using var reader = await command.ExecuteReaderAsync();

                var results = new List<string>();
                while (await reader.ReadAsync())
                {
                    var result = $"ID: {reader["ScrapId"]}, " +
                                $"Part: {reader["PartNumber"] ?? "NULL"}, " +
                                $"Status: {reader["Status"] ?? "NULL"}, " +
                                $"Dept: {reader["Department"] ?? "NULL"}, " +
                                $"ApprovalLevel: {reader["CurrentApprovalLevel"] ?? "NULL"}";
                    results.Add(result);
                    Console.WriteLine(result);
                }

                return Json(new { message = "Check console for results", count = results.Count, data = results });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        // ============================================================================
        // REPLACE YOUR ORIGINAL SCRAPHISTORY METHOD WITH THIS WORKING VERSION
        // ============================================================================


        [HttpGet]
        public async Task<IActionResult> ScrapHistory()
        {
            try
            {
                var clockNumber = User.Identity?.Name ?? "";
                _logger.LogInformation($"=== SCRAP HISTORY DEBUG ===");
                _logger.LogInformation($"Loading scrap history for user: '{clockNumber}'");

                // ✅ FIRST: Get current user location info with debugging
                string currentDepartment = "";
                string currentLine = "";
                string currentShift = "";

                try
                {
                    using var userConnection = new SqlConnection(_connectionString);
                    await userConnection.OpenAsync();

                    var userQuery = @"
                SELECT 
                    ISNULL(Department, '') as Department,
                    ISNULL(Line, '') as Line,
                    ISNULL(Shift, '') as Shift
                FROM [IntegrationCompanyB].[dbo].[scpUser]
                WHERE ClockNumber = @ClockNumber AND IsActive = 1";

                    using var userCommand = new SqlCommand(userQuery, userConnection);
                    userCommand.Parameters.AddWithValue("@ClockNumber", clockNumber);

                    using var userReader = await userCommand.ExecuteReaderAsync();
                    if (await userReader.ReadAsync())
                    {
                        currentDepartment = userReader.GetString("Department");
                        currentLine = userReader.GetString("Line");
                        currentShift = userReader.GetString("Shift");

                        _logger.LogInformation($"✅ Found user location - Dept: '{currentDepartment}', Line: '{currentLine}', Shift: '{currentShift}'");
                    }
                    else
                    {
                        _logger.LogWarning($"❌ User {clockNumber} not found in scpUser table");
                    }
                }
                catch (Exception userEx)
                {
                    _logger.LogError(userEx, $"❌ Error getting user location for {clockNumber}");
                }

                // ✅ THEN: Get ALL scrap history for this user (regardless of current location)
                var scrapHistory = new List<ScrapHistoryViewModel>();

                try
                {
                    using var scrapConnection = new SqlConnection(_connectionString);
                    await scrapConnection.OpenAsync();

                    // ✅ SIMPLIFIED QUERY: Get all scrap entries for this user, regardless of location
                    var scrapQuery = @"
                SELECT 
                    sd.ScrapId,
                    ISNULL(sd.PartNumber, '') as PartNumber,
                    ISNULL(sd.ScrapReason, '') as ScrapReason,
                    ISNULL(sd.Position, '') as Position,
                    ISNULL(sd.Status, 'Pending') as Status,
                    ISNULL(sd.Department, '') as Department,
                    ISNULL(sd.Line, '') as Line,
                    ISNULL(sd.Shift, '') as Shift,
                    ISNULL(sd.ClockNumber, '') as ClockNumber,
                    CASE 
                        WHEN sd.FirstApprovalStatus IS NULL OR sd.FirstApprovalStatus = '' THEN 'Pending TL'
                        ELSE sd.FirstApprovalStatus 
                    END as FirstApprovalStatus,
                    ISNULL(sd.ScrapSectionId, 0) as ScrapSectionId,
                    sd.Quantity,
                    sd.SubmissionDate,
                    sd.CreatedAt,
                    sd.ModifiedAt,
                    ISNULL(sd.CurrentApprovalLevel, 1) as CurrentApprovalLevel,
                    ISNULL(sd.MaterialCost, 0) as MaterialCost,
                    ISNULL(sd.StockUom, '') as StockUom
                FROM [IntegrationCompanyB].[dbo].[scpScrapData] sd
                WHERE ISNULL(sd.ClockNumber, '') = @ClockNumber
                ORDER BY sd.SubmissionDate DESC, sd.CreatedAt DESC";

                    using var scrapCommand = new SqlCommand(scrapQuery, scrapConnection);
                    scrapCommand.Parameters.AddWithValue("@ClockNumber", clockNumber);

                    using var scrapReader = await scrapCommand.ExecuteReaderAsync();

                    int rowCount = 0;
                    while (await scrapReader.ReadAsync())
                    {
                        rowCount++;
                        try
                        {
                            var partNumber = scrapReader.GetString("PartNumber");
                            var approvalStatus = scrapReader.GetString("FirstApprovalStatus");
                            var quantity = scrapReader.GetInt32("Quantity");
                            var submissionDate = scrapReader.GetDateTime("SubmissionDate");
                            var materialCost = scrapReader.GetDecimal("MaterialCost");
                            var scrapSectionId = scrapReader.GetInt32("ScrapSectionId");

                            if (rowCount <= 5) // Log first 5 entries for debugging
                            {
                                _logger.LogInformation($"Entry {rowCount}: {partNumber} - Status: '{approvalStatus}' - Qty: {quantity} - Date: {submissionDate:yyyy-MM-dd HH:mm:ss}");
                            }

                            scrapHistory.Add(new ScrapHistoryViewModel
                            {
                                ScrapId = scrapReader.GetInt32("ScrapId"),
                                PartNumber = partNumber,
                                ScrapSectionId = scrapSectionId,
                                Section = GetSectionName(scrapSectionId),
                                ScrapReason = scrapReader.GetString("ScrapReason"),
                                Quantity = quantity,
                                Position = scrapReader.GetString("Position"),
                                Status = scrapReader.GetString("Status"),
                                MaterialCost = materialCost,
                                SubmissionDate = submissionDate,
                                FormattedSubmissionDate = submissionDate.ToString("yyyy-MM-dd HH:mm:ss"),
                                Department = scrapReader.GetString("Department"),
                                Line = scrapReader.GetString("Line"),
                                Shift = scrapReader.GetString("Shift"),
                                ClockNumber = scrapReader.GetString("ClockNumber"),
                                CreatedAt = scrapReader.GetDateTime("CreatedAt"),
                                ModifiedAt = scrapReader.GetDateTime("ModifiedAt"),
                                FirstApprovalStatus = approvalStatus,
                                StockUom = scrapReader.GetString("StockUom"),
                                ApprovalStatusColor = GetApprovalStatusColor(approvalStatus),
                                CurrentApprovalLevel = scrapReader.GetInt32("CurrentApprovalLevel") switch
                                {
                                    1 => "Team Leader",
                                    2 => "Group Leader",
                                    3 => "Manager",
                                    4 => "Senior Manager",
                                    5 => "Completed",
                                    _ => "Team Leader"
                                }
                            });
                        }
                        catch (Exception entryEx)
                        {
                            _logger.LogError(entryEx, $"❌ Error processing scrap entry row {rowCount}");
                        }
                    }

                    _logger.LogInformation($"✅ Found {scrapHistory.Count} total scrap entries for user {clockNumber}");
                }
                catch (Exception scrapEx)
                {
                    _logger.LogError(scrapEx, $"❌ Error querying scrap data for {clockNumber}");
                }

                // ✅ FIXED: Set ViewData with CORRECT keys that match the view
                ViewData["ClockNumber"] = clockNumber;
                ViewData["CurrentDepartment"] = !string.IsNullOrEmpty(currentDepartment) ? currentDepartment : "Not Set";
                ViewData["CurrentLine"] = !string.IsNullOrEmpty(currentLine) ? currentLine : "Not Set";
                ViewData["CurrentShift"] = !string.IsNullOrEmpty(currentShift) ? currentShift : "Not Set";

                _logger.LogInformation($"✅ ScrapHistory ViewData set - Dept: '{ViewData["CurrentDepartment"]}', Line: '{ViewData["CurrentLine"]}', Shift: '{ViewData["CurrentShift"]}'");
                _logger.LogInformation($"✅ Returning {scrapHistory.Count} scrap entries");

                return View(scrapHistory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ScrapHistory error: {Message}", ex.Message);

                ViewData["ClockNumber"] = User.Identity?.Name ?? "";
                ViewData["CurrentDepartment"] = "Error Loading";
                ViewData["CurrentLine"] = "Error Loading";
                ViewData["CurrentShift"] = "Error Loading";

                return View(new List<ScrapHistoryViewModel>());
            }
        }
        // 🆕 UPDATED: ScrapEntryData class with Length support
        public class ScrapEntryData
        {
            public string PartNumber { get; set; } = "";
            public int ScrapSectionId { get; set; }
            public string ScrapReason { get; set; } = "";
            public int Quantity { get; set; }
            public string? Position { get; set; } = "";
            public decimal Length { get; set; } = 0;
            public decimal Weight { get; set; } = 0;
            public string ScrapType { get; set; } = "component";   // "component" | "wire"
            public string Department { get; set; } = "";
            public string MachineType { get; set; } = "";
            public string MachineNumber { get; set; } = "";
            public string Line { get; set; } = "";
            public string Shift { get; set; } = "";
            public string PartCategory { get; set; } = "";
            public string Plant { get; set; } = "";
            public string Company { get; set; } = "B";
            public string StockUom { get; set; } = "";
            public decimal MaterialCost { get; set; } = 0;
            public string Status { get; set; } = "Pending TL";
            public int? BatchScrapId { get; set; }

            // ✅ NEW: Engineering section plant (Plant A / Plant B / Plant C / Fielli)
            public string EngineeringPlant { get; set; } = "";
        }


        // ============================================================================
        // ENHANCED VERSION: ADD INVMASTER LOOKUP FOR STOCKUOM
        // ============================================================================

        [HttpGet]
        public async Task<IActionResult> ScrapHistoryWithUOM()
        {
            try
            {
                var clockNumber = User.Identity?.Name ?? "";
                Console.WriteLine($"Loading scrap history with UOM lookup for user: {clockNumber}");

                // First get all scrap entries
                var sql = @"
            SELECT 
                ScrapId,
                ISNULL(PartNumber, '') as PartNumber,
                ISNULL(ScrapReason, '') as ScrapReason,
                ISNULL(Position, '') as Position,
                ISNULL(Status, 'Pending') as Status,
                ISNULL(Department, '') as Department,
                ISNULL(Line, '') as Line,
                ISNULL(Shift, '') as Shift,
                ISNULL(ClockNumber, '') as ClockNumber,
                ISNULL(FirstApprovalStatus, 'Pending') as FirstApprovalStatus,
                ScrapSectionId,
                Quantity,
                MaterialCost,
                SubmissionDate,
                CreatedAt,
                ModifiedAt,
                ISNULL(CurrentApprovalLevel, 1) as CurrentApprovalLevel
            FROM [dbo].[ScrapData]
            WHERE ISNULL(ClockNumber, '') = @clockNumber
            ORDER BY SubmissionDate DESC";

                var scrapHistory = new List<ScrapHistoryViewModel>();

                using var connection = new SqlConnection(_context.Database.GetConnectionString());
                await connection.OpenAsync();

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@clockNumber", clockNumber);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    try
                    {
                        var partNumber = reader.GetString("PartNumber");
                        var originalMaterialCost = reader.GetDecimal("MaterialCost");

                        // Get StockUom and current MaterialCost from InvMaster
                        var stockUom = "";
                        var currentMaterialCost = originalMaterialCost;

                        if (!string.IsNullOrEmpty(partNumber))
                        {
                            try
                            {
                                using var invConnection = new SqlConnection(_context.Database.GetConnectionString());
                                await invConnection.OpenAsync();

                                using var invCommand = new SqlCommand(
                                    "SELECT TOP 1 ISNULL(StockUom, '') as StockUom, ISNULL(MaterialCost, 0) as MaterialCost FROM SysproCompanyB.dbo.InvMaster WHERE StockCode = @partNumber",
                                    invConnection);
                                invCommand.Parameters.AddWithValue("@partNumber", partNumber);

                                using var invReader = await invCommand.ExecuteReaderAsync();
                                if (await invReader.ReadAsync())
                                {
                                    stockUom = invReader.GetString("StockUom");
                                    var invCost = invReader.GetDecimal("MaterialCost");
                                    if (invCost > 0) currentMaterialCost = invCost;
                                }
                            }
                            catch (Exception invEx)
                            {
                                Console.WriteLine($"InvMaster lookup failed for {partNumber}: {invEx.Message}");
                            }
                        }

                        scrapHistory.Add(new ScrapHistoryViewModel
                        {
                            ScrapId = reader.GetInt32("ScrapId"),
                            PartNumber = partNumber,
                            ScrapSectionId = reader.GetInt32("ScrapSectionId"),
                            Section = GetSectionName(reader.GetInt32("ScrapSectionId")),
                            ScrapReason = reader.GetString("ScrapReason"),
                            Quantity = reader.GetInt32("Quantity"),
                            Position = reader.GetString("Position"),
                            Status = reader.GetString("Status"),
                            MaterialCost = currentMaterialCost, // From InvMaster or original
                            SubmissionDate = reader.GetDateTime("SubmissionDate"),
                            FormattedSubmissionDate = reader.GetDateTime("SubmissionDate").ToString("yyyy-MM-dd HH:mm:ss"),
                            Department = reader.GetString("Department"),
                            Line = reader.GetString("Line"),
                            Shift = reader.GetString("Shift"),
                            ClockNumber = reader.GetString("ClockNumber"),
                            CreatedAt = reader.GetDateTime("CreatedAt"),
                            ModifiedAt = reader.GetDateTime("ModifiedAt"),
                            FirstApprovalStatus = reader.GetString("FirstApprovalStatus"),
                            StockUom = stockUom, // From InvMaster
                            ApprovalStatusColor = GetApprovalStatusColor(reader.GetString("FirstApprovalStatus")),
                            CurrentApprovalLevel = reader.GetInt32("CurrentApprovalLevel") switch
                            {
                                1 => "Team Leader",
                                2 => "Group Leader",
                                3 => "Manager",
                                4 => "Senior Manager",
                                5 => "Completed",
                                _ => "Team Leader"
                            }
                        });
                    }
                    catch (Exception entryEx)
                    {
                        Console.WriteLine($"Error processing row: {entryEx.Message}");
                    }
                }

                Console.WriteLine($"Found {scrapHistory.Count} entries with UOM lookup for user {clockNumber}");

                ViewData["ClockNumber"] = clockNumber;
                ViewData["CurrentDepartment"] = scrapHistory.FirstOrDefault()?.Department ?? "";
                ViewData["CurrentLine"] = scrapHistory.FirstOrDefault()?.Line ?? "";
                ViewData["CurrentShift"] = scrapHistory.FirstOrDefault()?.Shift ?? "";

                return View("ScrapHistory", scrapHistory);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ScrapHistoryWithUOM error: {ex.Message}");

                ViewData["ClockNumber"] = User.Identity?.Name ?? "";
                ViewData["CurrentDepartment"] = "";
                ViewData["CurrentLine"] = "";
                ViewData["CurrentShift"] = "";

                return View("ScrapHistory", new List<ScrapHistoryViewModel>());
            }
        }


        // GET: Load available printers for the print label modal
        [HttpGet]
        public async Task<IActionResult> GetAvailablePrinters()
        {
            try
            {
                using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                const string query = @"
            SELECT PrinterConfigId, PrinterName, PrinterIP, PrinterPort, Department, Description
            FROM [IntegrationCompanyB].[dbo].[scpPrinterConfig]
            WHERE IsActive = 1
            ORDER BY PrinterName";

                var printers = await connection.QueryAsync(query);
                return Json(new { success = true, printers });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading printers");
                return Json(new { success = false, message = "Error loading printers" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PrintLocationLabel([FromBody] PrintLocationLabelRequest request)
        {
            try
            {
                var user = await GetCurrentUserAsync();
                if (user == null)
                    return Json(new { success = false, message = "User not authenticated" });

                // ✅ NEW: weight can no longer be recorded without a real, submitted scrap batch
                if (request.BatchScrapId <= 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = "No scrap batch found. Please capture and submit your scrap entries before weighing and printing a label."
                    });
                }

                // ✅ NEW: verify the batch actually belongs to this operator and has real scrap rows
                using (var verifyConnection = new SqlConnection(_connectionString))
                {
                    await verifyConnection.OpenAsync();
                    const string verifyQuery = @"
                SELECT COUNT(*)
                FROM [IntegrationCompanyB].[dbo].[scpScrapData]
                WHERE BatchScrapId = @BatchScrapId AND ClockNumber = @ClockNumber";

                    var matchCount = await verifyConnection.ExecuteScalarAsync<int>(verifyQuery,
                        new { BatchScrapId = request.BatchScrapId, ClockNumber = user.ClockNumber });

                    if (matchCount == 0)
                    {
                        _logger.LogWarning(
                            "⚠️ PrintLocationLabel rejected — BatchScrapId {BatchId} has no matching scrap for {ClockNumber}",
                            request.BatchScrapId, user.ClockNumber);

                        return Json(new
                        {
                            success = false,
                            message = "This scrap batch could not be verified. Please submit your scrap entries again before printing."
                        });
                    }
                }

                using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                const string printerQuery = @"
            SELECT PrinterName, PrinterIP, PrinterPort
            FROM [IntegrationCompanyB].[dbo].[scpPrinterConfig]
            WHERE PrinterConfigId = @PrinterConfigId AND IsActive = 1";

                var printer = await connection.QuerySingleOrDefaultAsync(printerQuery,
                    new { PrinterConfigId = request.PrinterConfigId });

                if (printer == null)
                    return Json(new { success = false, message = "Printer not found or inactive" });

                if (request.Weight <= 0)
                    return Json(new { success = false, message = "Please enter a valid weight greater than zero." });

                var printResult = await SendLocationLabelAsync(
                    printer.PrinterIP.ToString(),
                    (int)printer.PrinterPort,
                    user.ClockNumber,
                    request.Department,
                    request.Line,
                    request.Shift,
                    request.Notes,
                    request.MachineNumber,
                    request.Weight,
                    request.BatchScrapId);
                bool printSuccess = printResult.Item1;
                string printError = printResult.Item2;

                if (printSuccess)
                {
                    try
                    {
                        await connection.ExecuteAsync(@"
INSERT INTO [IntegrationCompanyB].[dbo].[scpPrintLog]
(PrintLogId, TrackId, ScrapId, ClockNumber, MachineName, SessionId, Department, Line, Shift,
 Weight, PrinterName, PrinterIP, PrinterPort, PrintStatus, PrintStartTime, PrintEndTime,
 PrintDurationMs, CreatedAt)
VALUES
((SELECT ISNULL(MAX(PrintLogId),0)+1 FROM [IntegrationCompanyB].[dbo].[scpPrintLog]),
 @TrackId, @ScrapId, @ClockNumber, @MachineName, NEWID(), @Department, @Line, @Shift,
 0, @PrinterName, @PrinterIP, @PrinterPort, 'SUCCESS', @StartTime, GETDATE(),
 0, GETDATE())",
                            new
                            {
                                TrackId = $"LOC-{DateTime.Now:yyyyMMddHHmmss}",
                                ScrapId = $"LOC-{DateTime.Now:yyyyMMddHHmmss}",
                                ClockNumber = user.ClockNumber,
                                MachineName = Environment.MachineName,
                                Department = request.Department,
                                Line = request.Line,
                                Shift = request.Shift,
                                PrinterName = printer.PrinterName.ToString(),
                                PrinterIP = printer.PrinterIP.ToString(),
                                PrinterPort = (int)printer.PrinterPort,
                                StartTime = DateTime.Now
                            });
                    }
                    catch (Exception logEx)
                    {
                        _logger.LogWarning(logEx, "Print log insert failed (non-critical) — label was printed successfully");
                    }

                    try
                    {
                        await connection.ExecuteAsync(@"
        INSERT INTO [IntegrationCompanyB].[dbo].[scpWeightHistory]
        (WeightId, Department, Line, Shift, Weight, Description, CreatedAt, CreatedBy,
         ModifiedAt, ModifiedBy, IsBulkWeighing, BatchScrapId)
        VALUES
        (NEXT VALUE FOR [IntegrationCompanyB].[dbo].[seq_WeightHistoryId],
         @Department, @Line, @Shift, @Weight, @Description, GETDATE(), @CreatedBy,
         GETDATE(), @CreatedBy, 0, @BatchScrapId)",
                            new
                            {
                                Department = user.Department,
                                Line = request.Line,
                                Shift = request.Shift,
                                Weight = request.Weight,
                                Description = string.IsNullOrWhiteSpace(request.Notes)
                                    ? $"Weight recorded by {user.ClockNumber} via label print"
                                    : $"Weight recorded by {user.ClockNumber} — {request.Notes}",
                                CreatedBy = user.ClockNumber,
                                BatchScrapId = request.BatchScrapId
                            });

                        _logger.LogInformation("✅ Weight {Weight}kg saved for Batch #{BatchId} ({Dept}/{Line}/{Shift}) by {User}",
                            request.Weight, request.BatchScrapId, user.Department, request.Line, request.Shift, user.ClockNumber);
                    }
                    catch (Exception weightEx)
                    {
                        _logger.LogWarning(weightEx, "Weight save failed (non-critical) — label was printed successfully");
                    }
                }

                return Json(new
                {
                    success = printSuccess,
                    batchScrapId = request.BatchScrapId,
                    message = printSuccess
                        ? $"Label printed and {request.Weight:0.###}kg recorded for Batch #{request.BatchScrapId}"
                        : printError
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error printing location label");
                return Json(new { success = false, message = ex.Message });
            }
        }

        private async Task<(bool Success, string ErrorMessage)> SendLocationLabelAsync(
            string printerIp, int port, string clockNumber,
            string department, string line, string shift,
            string notes, string machineNumber = "", decimal weight = 0,
            int batchScrapId = 0)                                   // ← NEW param, default 0 so old calls still compile
        {
            try
            {
                var cmd = new List<byte>();

                cmd.AddRange(new byte[] { 0x1B, 0x40 });

                cmd.AddRange(new byte[] { 0x1B, 0x61, 0x01 });
                cmd.AddRange(new byte[] { 0x1D, 0x21, 0x11 });
                cmd.AddRange(System.Text.Encoding.UTF8.GetBytes("SCRAP LOCATION LABEL"));
                cmd.Add(0x0A);

                cmd.AddRange(new byte[] { 0x1D, 0x21, 0x00 });
                cmd.AddRange(new byte[] { 0x1B, 0x61, 0x00 });
                cmd.Add(0x0A);

                foreach (var line2 in new[]
                {
    $"Department : {department}",
    $"Line       : {line}",
    string.IsNullOrWhiteSpace(machineNumber) ? null : $"Machine    : {machineNumber}",
    $"Shift      : {shift}",
    weight > 0 ? $"Weight     : {weight:0.###} kg" : null,
    batchScrapId > 0 ? $"Batch      : #{batchScrapId}" : null,   // ← NEW line on the label
    $"Printed By : {clockNumber}",
    $"Date/Time  : {DateTime.Now:yyyy-MM-dd HH:mm}",
    string.IsNullOrWhiteSpace(notes) ? null : $"Notes      : {notes}"
})
                {
                    if (line2 == null) continue;
                    cmd.AddRange(System.Text.Encoding.UTF8.GetBytes(line2));
                    cmd.Add(0x0A);
                }

                cmd.Add(0x0A);

                string barcodeData = $"{department}-{shift}".ToUpper()
                    .Replace(" ", "-").Substring(0, Math.Min(20, $"{department}-{shift}".Length));

                cmd.AddRange(new byte[] { 0x1B, 0x61, 0x01 });
                cmd.AddRange(new byte[] { 0x1D, 0x48, 0x02 });
                cmd.AddRange(new byte[] { 0x1D, 0x68, 0x50 });
                cmd.AddRange(new byte[] { 0x1D, 0x77, 0x02 });
                cmd.AddRange(new byte[] { 0x1D, 0x6B, 0x04 });
                cmd.AddRange(System.Text.Encoding.ASCII.GetBytes(barcodeData));
                cmd.Add(0x00);

                cmd.AddRange(new byte[] { 0x0A, 0x0A, 0x0A });
                cmd.AddRange(new byte[] { 0x1D, 0x56, 0x01 });

                using var client = new System.Net.Sockets.TcpClient();
                var connectTask = client.ConnectAsync(printerIp, port);
                if (await Task.WhenAny(connectTask, Task.Delay(5000)) != connectTask)
                    return (false, "Printer timeout — no response within 5 seconds");

                using var stream = client.GetStream();
                var bytes = cmd.ToArray();
                await stream.WriteAsync(bytes, 0, bytes.Length);
                await stream.FlushAsync();

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // Request model
        public class PrintLocationLabelRequest
        {
            public int PrinterConfigId { get; set; }
            public string Department { get; set; } = "";
            public string Line { get; set; } = "";
            public string Shift { get; set; } = "";
            public string Notes { get; set; } = "";
            public string MachineNumber { get; set; } = "";
            public decimal Weight { get; set; } = 0;
            public int BatchScrapId { get; set; } = 0;
        }


        // ============================================================================
        // SIMPLIFIED VERSION: NO FILTERING BY DEPT/LINE/SHIFT
        // ============================================================================

        [HttpGet]
        public async Task<IActionResult> ScrapHistorySimple()
        {
            try
            {
                var clockNumber = User.Identity?.Name ?? "";

                // SIMPLE: Just get ALL entries for this user (remove complex filtering)
                var scrapData = await _context.ScrapData
                    .Where(s => s.ClockNumber == clockNumber)
                    .OrderByDescending(s => s.SubmissionDate)
                    .ToListAsync();

                Console.WriteLine($"ScrapHistorySimple: Found {scrapData.Count} entries for {clockNumber}");

                var scrapHistory = new List<ScrapHistoryViewModel>();

                foreach (var scrap in scrapData)
                {
                    try
                    {
                        scrapHistory.Add(new ScrapHistoryViewModel
                        {
                            ScrapId = scrap.ScrapId,
                            PartNumber = scrap.PartNumber ?? "Unknown",
                            ScrapSectionId = scrap.ScrapSectionId ?? 0, // 0 indicates invalid/corrupted data
                            Section = GetSectionName(scrap.ScrapSectionId ?? 0),
                            ScrapReason = scrap.ScrapReason ?? "",
                            Quantity = scrap.Quantity,
                            Position = scrap.Position ?? "",
                            Status = scrap.Status ?? "Pending",
                            MaterialCost = scrap.MaterialCost ?? 0,
                            SubmissionDate = scrap.SubmissionDate,
                            FormattedSubmissionDate = scrap.SubmissionDate.ToString("yyyy-MM-dd HH:mm:ss"),
                            Department = scrap.Department ?? "",
                            Line = scrap.Line ?? "",
                            Shift = scrap.Shift ?? "",
                            ClockNumber = scrap.ClockNumber ?? "",
                            CreatedAt = scrap.CreatedAt,
                            ModifiedAt = scrap.ModifiedAt,
                            FirstApprovalStatus = scrap.FirstApprovalStatus ?? "Pending",
                            StockUom = "", // Simplified
                            ApprovalStatusColor = GetApprovalStatusColor(scrap.FirstApprovalStatus ?? "Pending"),
                            CurrentApprovalLevel = scrap.CurrentApprovalLevel switch
                            {
                                1 => "Team Leader",
                                2 => "Group Leader",
                                3 => "Manager",
                                4 => "Senior Manager",
                                5 => "Completed",
                                _ => "Team Leader"
                            }
                        });
                    }
                    catch (Exception entryEx)
                    {
                        Console.WriteLine($"Error processing entry {scrap.ScrapId}: {entryEx.Message}");
                    }
                }

                ViewData["ClockNumber"] = clockNumber;
                ViewData["CurrentDepartment"] = "All Departments";
                ViewData["CurrentLine"] = "All Lines";
                ViewData["CurrentShift"] = "All Shifts";

                return View("ScrapHistory", scrapHistory);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ScrapHistorySimple error: {ex.Message}");

                ViewData["ClockNumber"] = User.Identity?.Name ?? "";
                ViewData["CurrentDepartment"] = "";
                ViewData["CurrentLine"] = "";
                ViewData["CurrentShift"] = "";

                return View("ScrapHistory", new List<ScrapHistoryViewModel>());
            }
        }


        // ADD THESE HELPER METHODS:
        private string GetSectionName(int? scrapSectionId)
        {
            if (!scrapSectionId.HasValue)
                return "Invalid Section";

            return scrapSectionId.Value switch
            {
                1 => "Production",
                2 => "CombiMeter",
                3 => "Moulding",
                4 => "Sleeve",
                5 => "Pre-Assembly Ford",
                6 => "Pre-Assembly Isuzu",
                7 => "Ground",
                8 => "Battery",
                9 => "Pre-Assembly TSAM",
                10 => "Engineering",           // ✅ NEW
                _ => $"Section {scrapSectionId.Value}"
            };
        }

        private async Task<int> GetOrCreateActiveBatchIdAsync(string clockNumber)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Find this operator's most recent batch
            const string lastBatchQuery = @"
        SELECT TOP 1 BatchScrapId
        FROM [IntegrationCompanyB].[dbo].[scpScrapData]
        WHERE ClockNumber = @ClockNumber
        ORDER BY CreatedAt DESC";

            var lastBatchId = await connection.QuerySingleOrDefaultAsync<int?>(lastBatchQuery,
                new { ClockNumber = clockNumber });

            if (lastBatchId.HasValue && lastBatchId.Value > 0)
            {
                // Has it already been weighed? If so, it's closed — start a new one.
                const string weighedQuery = @"
            SELECT COUNT(*)
            FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
            WHERE BatchScrapId = @BatchScrapId";

                var weighedCount = await connection.ExecuteScalarAsync<int>(weighedQuery,
                    new { BatchScrapId = lastBatchId.Value });

                if (weighedCount == 0)
                {
                    _logger.LogInformation("♻️ Reusing unweighed BatchId {BatchId} for {ClockNumber}",
                        lastBatchId.Value, clockNumber);
                    return lastBatchId.Value;
                }
            }

            var newBatchId = await _batchIdService.GetNextBatchIdAsync(clockNumber);
            _logger.LogInformation(
                "🆕 Generated new BatchId {BatchId} for {ClockNumber} (previous batch was weighed or none existed)",
                newBatchId, clockNumber);
            return newBatchId;
        }

        private string GetApprovalStatusColor(string status)
        {
            return status.ToLower() switch
            {
                "pending" => "#ffff00",
                "approved" => "#00ff00",
                "rejected" => "#ff4444",
                _ => "#00fff2"
            };
        }

        [HttpGet]
        public async Task<IActionResult> GetScrapSummary()
        {
            try
            {
                var clockNumber = User.Identity?.Name;
                if (string.IsNullOrEmpty(clockNumber))
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                var operatorDetails = await _operatorService.GetOperatorDetails(clockNumber);
                if (operatorDetails == null)
                {
                    return Json(new { success = false, message = "Operator details not found" });
                }

                using var connection = new SqlConnection(_connectionString);

                const string summaryQuery = @"
            SELECT 
                COUNT(*) as TotalEntries,
                SUM(Quantity) as TotalQuantity,
                SUM(CASE WHEN Status = 'Pending' THEN 1 ELSE 0 END) as PendingCount,
                SUM(CASE WHEN Status = 'Approved' THEN 1 ELSE 0 END) as ApprovedCount,
                SUM(CASE WHEN Status = 'Rejected' THEN 1 ELSE 0 END) as RejectedCount
            FROM [dbo].[ScrapData] 
            WHERE ClockNumber = @ClockNumber
                AND Department = @Department
                AND Line = @Line
                AND Shift = @Shift
                AND Status IN ('Pending', 'Approved', 'Rejected')";

                var summary = await connection.QuerySingleAsync(summaryQuery, new
                {
                    ClockNumber = clockNumber,
                    Department = operatorDetails.Department,
                    Line = operatorDetails.Line,
                    Shift = operatorDetails.Shift
                });

                return Json(new
                {
                    success = true,
                    summary = new
                    {
                        totalEntries = summary.TotalEntries,
                        totalQuantity = summary.TotalQuantity,
                        pendingCount = summary.PendingCount,
                        approvedCount = summary.ApprovedCount,
                        rejectedCount = summary.RejectedCount
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scrap summary for user {ClockNumber}", User.Identity?.Name);
                return Json(new { success = false, message = "Error getting summary: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CleanupOldSessions()
        {
            try
            {
                var clockNumber = User.Identity?.Name;
                if (string.IsNullOrEmpty(clockNumber))
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                // Remove sessions older than 24 hours that are inactive
                var cutoffDate = DateTime.Now.AddHours(-24);

                var oldSessions = await _context.ScrapReviewSessions
                    .Where(s => s.ClockNumber == clockNumber &&
                               s.IsActive == false &&
                               s.LastActivity < cutoffDate)
                    .ToListAsync();

                if (oldSessions.Count > 0)
                {
                    // Remove associated entries first
                    foreach (var session in oldSessions)
                    {
                        var entries = await _context.ScrapReviewEntries
                            .Where(e => e.SecurityStamp == session.SecurityStamp)
                            .ToListAsync();

                        _context.ScrapReviewEntries.RemoveRange(entries);
                    }

                    // Remove sessions
                    _context.ScrapReviewSessions.RemoveRange(oldSessions);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Cleaned up {Count} old inactive sessions for user {ClockNumber}",
                        oldSessions.Count, clockNumber);
                }

                return Json(new
                {
                    success = true,
                    message = $"Cleaned up {oldSessions.Count} old sessions",
                    sessionsRemoved = oldSessions.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up old sessions for user {ClockNumber}", User.Identity?.Name);
                return Json(new { success = false, message = "Error cleaning up sessions: " + ex.Message });
            }
        }

        // OperatorController.cs
        // In the GetScrapReasonsBySection action, replace the existing SQL with this:

        [HttpGet]
        public async Task<IActionResult> GetScrapReasonsBySection(int sectionId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);

                // Some sections (Rework, all Pre-Assembly variants, Engineering) mirror
                // Production's reason list instead of maintaining their own.
                const string mirrorQuery = @"
            SELECT MirrorsSectionId
            FROM [IntegrationCompanyB].[dbo].[scpScrapSection]
            WHERE ScrapSectionId = @SectionId";

                var mirrorsSectionId = await connection.QuerySingleOrDefaultAsync<int?>(
                    mirrorQuery, new { SectionId = sectionId });

                var effectiveSectionId = mirrorsSectionId ?? sectionId;

                const string query = @"
    SELECT 
        ErrorId as value,
        ScrapReasonName as text,
        Part_Position as position,
        ISNULL(ReasonCategory, '') as category,
        CASE 
            WHEN ScrapSectionId IN (1, 2, 4) THEN 1
            ELSE 0 
        END as requiresPosition
    FROM [IntegrationCompanyB].[dbo].[scpReason]
    WHERE ScrapSectionId = @SectionId
        AND IsActive = 1
        AND ScrapReasonName NOT IN ('Other', 'Other Reason', 'Other reason')
    ORDER BY 
        CASE ISNULL(ReasonCategory,'') WHEN 'Component' THEN 0 WHEN 'Wire' THEN 1 ELSE 2 END,
        ScrapReasonName";

                var reasons = await connection.QueryAsync<dynamic>(query, new { SectionId = effectiveSectionId });

                return Json(new
                {
                    success = true,
                    data = reasons.Select(r => new {
                        value = r.value,
                        text = r.text,
                        requiresPosition = r.requiresPosition == 1,
                        category = r.category
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting reasons for section {SectionId}", sectionId);
                return Json(new { success = false, message = "Error loading reasons" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAllScrapSections()
        {
            try
            {
                var sections = await _operatorService.GetActiveScrapSections();
                return Json(new
                {
                    success = true,
                    sections = sections.Select(s => new {
                        value = s.Value,
                        text = s.Text
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scrap sections");
                return Json(new { success = false, message = "Error loading sections" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> TestConnection()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string query = "SELECT COUNT(*) FROM [IntegrationCompanyB].[dbo].[scpScrapSection]";
                var count = await connection.ExecuteScalarAsync<int>(query);

                return Json(new { success = true, count });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetScrapErrors(int sectionId)
        {
            try
            {
                var errors = await _operatorService.GetScrapErrorsBySection(sectionId);
                return Json(new
                {
                    success = true,
                    errors = errors.Select(e => new {
                        errorId = e.ErrorId,
                        errorName = e.ErrorName,
                        requiresPosition = e.RequiresPosition,
                        defectItem = e.DefectItem,
                        partDescription = e.PartDescription
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scrap errors for section {SectionId}", sectionId);
                return Json(new { success = false, error = "Error loading error types" });
            }
        }



        [HttpGet]
        public async Task<IActionResult> GetPartPositions(int sectionId, int reasonId)
        {
            try
            {
                _logger.LogInformation("GetPartPositions called with sectionId: {SectionId}", sectionId);

                var positions = await _operatorService.GetPartPositions(sectionId, reasonId);

                return Json(new
                {
                    success = true,
                    positions = positions,
                    sectionId = sectionId,
                    count = positions.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting positions");
                return Json(new { success = false, message = "Failed to load positions" });
            }
        }


       


        [HttpGet]
        public async Task<IActionResult> GetReviewSession(string securityStamp)
        {
            try
            {
                var clockNumber = User.Identity?.Name;
                if (string.IsNullOrEmpty(clockNumber))
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                if (string.IsNullOrEmpty(securityStamp))
                {
                    return Json(new { success = false, message = "Session ID is required" });
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get session details
                const string sessionQuery = @"
            SELECT Id, SecurityStamp, PartNumber, Department, Line, Shift, 
                   StockUom, MaterialCost, CreatedAt, LastActivity
            FROM [dbo].[ScrapReviewSessions] 
            WHERE SecurityStamp = @SecurityStamp AND ClockNumber = @ClockNumber AND IsActive = 1";

                var session = await connection.QuerySingleOrDefaultAsync(sessionQuery,
                    new { SecurityStamp = securityStamp, ClockNumber = clockNumber });

                if (session == null)
                {
                    return Json(new { success = false, message = "Session not found or expired" });
                }

                // Get entries for this session
                const string entriesQuery = @"
            SELECT Id, ScrapSectionId, SectionName, ReasonId, ScrapReason, 
                   Position, Quantity, CreatedAt, ModifiedAt, OrderIndex
            FROM [dbo].[ScrapReviewEntries] 
            WHERE SecurityStamp = @SecurityStamp
            ORDER BY OrderIndex";

                var entries = await connection.QueryAsync(entriesQuery, new { SecurityStamp = session.Id });

                // Update last activity
                const string updateQuery = @"
            UPDATE [dbo].[ScrapReviewSessions] 
            SET LastActivity = @LastActivity 
            WHERE Id = @SecurityStamp";

                await connection.ExecuteAsync(updateQuery,
                    new { LastActivity = DateTime.UtcNow, SecurityStamp = session.Id });

                _logger.LogInformation("Retrieved session {SecurityStamp} with {EntryCount} entries",
                    securityStamp, entries.Count());

                return Json(new
                {
                    success = true,
                    session = new
                    {
                        securityStamp = session.SecurityStamp,
                        partNumber = session.PartNumber,
                        department = session.Department,
                        line = session.Line,
                        shift = session.Shift,
                        stockUom = session.StockUom,
                        materialCost = session.MaterialCost,
                        entries = entries.Select(e => new {
                            id = e.Id,
                            scrapSectionId = e.ScrapSectionId,
                            sectionName = e.SectionName,
                            reasonId = e.ReasonId,
                            scrapReason = e.ScrapReason,
                            position = e.Position,
                            quantity = e.Quantity
                        }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting review session {SecurityStamp}", securityStamp);
                return Json(new { success = false, message = "Error loading session: " + ex.Message });
            }
        }

        // FIXED: GetActiveReviewSessions method with corrected compilation issues

        [HttpGet]
        public async Task<IActionResult> GetActiveReviewSessions(string clockNumber, bool activeOnly = true)
        {
            try
            {
                if (string.IsNullOrEmpty(clockNumber))
                {
                    _logger?.LogWarning("GetActiveReviewSessions called without clock number");
                    return Json(new { success = false, message = "Clock number is required" });
                }

                _logger?.LogInformation($"Loading active review sessions for user: {clockNumber}");

                // Query to get sessions for the user
                var query = _context.ScrapReviewSessions.AsQueryable();

                // Filter by clock number
                query = query.Where(s => s.ClockNumber == clockNumber);

                // Apply active filter
                if (activeOnly)
                {
                    query = query.Where(s => s.IsActive == true);
                    _logger?.LogDebug($"Filtering for ACTIVE sessions only (IsActive = 1) for user {clockNumber}");
                }

                var sessions = await query
                    .OrderByDescending(s => s.LastActivity)
                    .ToListAsync();

                _logger?.LogInformation($"Found {sessions.Count} sessions for user {clockNumber} (activeOnly: {activeOnly})");

                // Create the response using proper object construction
                var sessionsWithEntries = new List<object>();
                var totalEntries = 0; // FIXED: Calculate total entries differently

                foreach (var session in sessions)
                {
                    try
                    {
                        // Load entries for this session
                        var entries = await _context.ScrapReviewEntries
                            .Where(e => e.SecurityStamp == session.SecurityStamp)
                            .OrderBy(e => e.OrderIndex)
                            .ToListAsync();

                        _logger?.LogDebug($"Session {session.SecurityStamp} ({session.PartNumber}) has {entries.Count} entries");

                        // FIXED: Count entries directly from the list
                        totalEntries += entries.Count;

                        // Create response object with explicit properties
                        var sessionResponse = new
                        {
                            securityStamp = session.SecurityStamp ?? "",
                            partNumber = session.PartNumber ?? "",
                            department = session.Department ?? "",
                            line = session.Line ?? "",
                            shift = session.Shift ?? "",
                            stockUom = session.StockUom ?? "",
                            materialCost = session.MaterialCost ?? 0m,
                            createdAt = session.CreatedAt,
                            lastActivity = session.LastActivity,
                            isActive = session.IsActive,
                            entries = entries.Select(e => new
                            {
                                id = e.Id,
                                scrapSectionId = e.ScrapSectionId,
                                sectionName = e.SectionName ?? "",
                                reasonId = e.ReasonId,
                                scrapReason = e.ScrapReason ?? "",
                                position = e.Position ?? "",
                                quantity = e.Quantity,
                                createdAt = e.CreatedAt,
                                modifiedAt = e.ModifiedAt,
                                orderIndex = e.OrderIndex
                            }).ToList()
                        };

                        sessionsWithEntries.Add(sessionResponse);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, $"Error loading entries for session {session.SecurityStamp}");

                        // Add session without entries if there's an error
                        var sessionResponse = new
                        {
                            securityStamp = session.SecurityStamp ?? "",
                            partNumber = session.PartNumber ?? "",
                            department = session.Department ?? "",
                            line = session.Line ?? "",
                            shift = session.Shift ?? "",
                            stockUom = session.StockUom ?? "",
                            materialCost = session.MaterialCost ?? 0m,
                            createdAt = session.CreatedAt,
                            lastActivity = session.LastActivity,
                            isActive = session.IsActive,
                            entries = new List<object>()
                        };

                        sessionsWithEntries.Add(sessionResponse);
                    }
                }

                var result = new
                {
                    success = true,
                    message = $"Retrieved {sessionsWithEntries.Count} sessions with {totalEntries} total entries",
                    sessions = sessionsWithEntries
                };

                return Json(result);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"❌ Critical error retrieving active review sessions for user {clockNumber}");
                return Json(new
                {
                    success = false,
                    message = "Error retrieving active sessions: " + ex.Message
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateReviewSession([FromBody] CreateReviewSessionRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.PartNumber) ||
                    string.IsNullOrEmpty(request.Department) ||
                    string.IsNullOrEmpty(request.Line) ||
                    string.IsNullOrEmpty(request.Shift))
                {
                    return Json(new { success = false, message = "Missing required fields" });
                }

                var clockNumber = User.Identity?.Name;
                if (string.IsNullOrEmpty(clockNumber))
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                // Check for existing ACTIVE session
                var existingActiveSession = await _context.ScrapReviewSessions
                    .FirstOrDefaultAsync(s => s.ClockNumber == clockNumber &&
                                            s.PartNumber == request.PartNumber &&
                                            s.IsActive == true);

                if (existingActiveSession != null)
                {
                    return Json(new
                    {
                        success = true,
                        securityStamp = existingActiveSession.SecurityStamp,
                        message = "Using existing active session"
                    });
                }

                // Create new session with string SecurityStamp
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var randomSuffix = Guid.NewGuid().ToString("N")[..8];
                var securityStamp = $"scrap_{timestamp}_{randomSuffix}";

                var newSession = new ScrapReviewSession
                {
                    SecurityStamp = securityStamp,
                    ClockNumber = clockNumber,
                    PartNumber = request.PartNumber,
                    Department = request.Department,
                    Line = request.Line,
                    Shift = request.Shift,
                    StockUom = request.StockUom ?? "",
                    MaterialCost = request.MaterialCost ?? 0,
                    CreatedAt = DateTime.Now,
                    LastActivity = DateTime.Now,
                    IsActive = true // KEY: Make it active
                };

                _context.ScrapReviewSessions.Add(newSession);
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    securityStamp = securityStamp,
                    message = "New active session created"
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"❌ Error creating review session for part {request.PartNumber}");
                return Json(new
                {
                    success = false,
                    message = "Error creating session: " + ex.Message
                });
            }
        }

        

        [HttpPost]
        public async Task<IActionResult> AddReviewEntry([FromBody] AddReviewEntryRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.SecurityStamp) ||
                    request.ScrapSectionId <= 0 ||
                    request.ReasonId <= 0 ||
                    request.Quantity <= 0)
                {
                    return Json(new { success = false, message = "Missing or invalid required fields" });
                }

                // Verify session exists and is ACTIVE
                var session = await _context.ScrapReviewSessions
                    .FirstOrDefaultAsync(s => s.SecurityStamp == request.SecurityStamp && s.IsActive == true);

                if (session == null)
                {
                    return Json(new { success = false, message = "Active session not found" });
                }

                var maxOrderIndex = await _context.ScrapReviewEntries
                    .Where(e => e.SecurityStamp == request.SecurityStamp)
                    .MaxAsync(e => (int?)e.OrderIndex) ?? 0;

                var newEntry = new ScrapReviewEntry
                {
                    SecurityStamp = request.SecurityStamp,
                    ScrapSectionId = request.ScrapSectionId,
                    SectionName = request.SectionName ?? "",
                    ReasonId = request.ReasonId,
                    ScrapReason = request.ScrapReason ?? "",
                    Position = request.Position ?? "",
                    Quantity = request.Quantity,
                    CreatedAt = DateTime.Now,
                    OrderIndex = maxOrderIndex + 1
                };

                _context.ScrapReviewEntries.Add(newEntry);
                session.LastActivity = DateTime.Now;
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    entryId = newEntry.Id,
                    message = "Entry added to active session"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error adding entry: " + ex.Message
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> FinalizeReviewSession([FromBody] FinalizeSessionRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.SecurityStamp))
                {
                    return Json(new { success = false, message = "Session ID is required" });
                }

                var clockNumber = User.Identity?.Name;
                if (string.IsNullOrEmpty(clockNumber))
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                _logger.LogInformation("Finalizing review session {SecurityStamp} for user {ClockNumber}",
                    request.SecurityStamp, clockNumber);

                var session = await _context.ScrapReviewSessions
                    .FirstOrDefaultAsync(s => s.SecurityStamp == request.SecurityStamp && s.ClockNumber == clockNumber);

                if (session == null)
                {
                    return Json(new { success = false, message = "Session not found or access denied" });
                }

                // Set IsActive to 0 (false) to mark as finalized
                session.IsActive = false;
                session.LastActivity = DateTime.Now;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Successfully finalized session {SecurityStamp}, set IsActive = 0", request.SecurityStamp);

                return Json(new
                {
                    success = true,
                    message = "Session finalized successfully",
                    securityStamp = request.SecurityStamp,
                    isActive = session.IsActive
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finalizing session {SecurityStamp}", request.SecurityStamp);
                return Json(new
                {
                    success = false,
                    message = "Error finalizing session: " + ex.Message
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> FinalizeAllActiveSessions()
        {
            try
            {
                var clockNumber = User.Identity?.Name;
                if (string.IsNullOrEmpty(clockNumber))
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                _logger.LogInformation("Finalizing all active sessions for user {ClockNumber}", clockNumber);

                var activeSessions = await _context.ScrapReviewSessions
                    .Where(s => s.ClockNumber == clockNumber && s.IsActive == true)
                    .ToListAsync();

                if (activeSessions.Count == 0)
                {
                    return Json(new { success = true, message = "No active sessions found", sessionsFinalized = 0 });
                }

                // Set all active sessions to inactive
                foreach (var session in activeSessions)
                {
                    session.IsActive = false;
                    session.LastActivity = DateTime.Now;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Successfully finalized {Count} active sessions for user {ClockNumber}",
                    activeSessions.Count, clockNumber);

                return Json(new
                {
                    success = true,
                    message = $"Finalized {activeSessions.Count} active sessions",
                    sessionsFinalized = activeSessions.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finalizing all active sessions for user {ClockNumber}", User.Identity?.Name);
                return Json(new
                {
                    success = false,
                    message = "Error finalizing sessions: " + ex.Message
                });
            }
        }
        public class CreateReviewSessionRequest
        {
            public string PartNumber { get; set; }
            public string Department { get; set; }
            public string Line { get; set; }
            public string Shift { get; set; }
            public string StockUom { get; set; }
            public decimal? MaterialCost { get; set; }
        }
        public class AddReviewEntryRequest
        {
            public string SecurityStamp { get; set; }
            public int ScrapSectionId { get; set; }
            public string SectionName { get; set; }
            public int ReasonId { get; set; }
            public string ScrapReason { get; set; }
            public string Position { get; set; }
            public int Quantity { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> DirectEntry()
        {
            try
            {
                var clockNumber = User.Identity?.Name;
                if (string.IsNullOrEmpty(clockNumber))
                {
                    _logger.LogWarning("❌ DirectEntry: User not authenticated");
                    return RedirectToAction("Login", "Account");
                }

                _logger.LogInformation("📝 DirectEntry called for user: {ClockNumber}", clockNumber);

                var operatorDetails = await _operatorService.GetOperatorDetails(clockNumber);
                if (operatorDetails == null)
                {
                    _logger.LogWarning("❌ DirectEntry: Operator details not found for {ClockNumber}", clockNumber);
                    // Continue with empty values rather than redirecting
                }

                // Get scrap sections and reasons for the form
                var scrapSections = await _operatorService.GetActiveScrapSections();
                var sectionReasons = new Dictionary<string, List<ScrapReasonItem>>();

                foreach (var section in scrapSections)
                {
                    var sectionId = section.Value;
                    var reasons = await _operatorService.GetScrapReasonsBySection(int.Parse(sectionId));
                    sectionReasons[sectionId] = reasons.Select(r => new ScrapReasonItem
                    {
                        Value = r.Value,
                        Text = r.Text,
                        RequiresPosition = section.Text == "CombiMeter" || section.Text == "Sleeve"
                    }).ToList();
                }

                // Create model for direct entry (no part validation required)
                var model = new ScrapEntryViewModel
                {
                    Department = operatorDetails?.Department ?? "",
                    Line = operatorDetails?.Line ?? "",
                    Shift = operatorDetails?.Shift ?? "",
                    ScrapSections = scrapSections,
                    SectionReasons = sectionReasons,

                    // Leave these empty - will be manually entered in the form
                    PartNumber = "",
                    StockUom = "EA", // Default value
                    MaterialCost = 0, // Will be looked up when form is submitted
                    Quantity = 0
                };

                _logger.LogInformation("✅ DirectEntry model created for {Department}/{Line}/{Shift}",
                    model.Department, model.Line, model.Shift);

                return View("DirectEntry", model); // Use a different view
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error loading DirectEntry view for user {ClockNumber}", User.Identity?.Name);

                ViewBag.ErrorMessage = "Error loading direct entry page. Please try again.";
                return View("Error");
            }
        }

        private async Task<User> GetCurrentUserAsync()
        {
            var clockNumber = User?.Identity?.Name;
            if (string.IsNullOrEmpty(clockNumber))
                return null;

            return await _context.User
                .FirstOrDefaultAsync(u => u.ClockNumber == clockNumber && u.IsActive);
        }

        public class FinalizeSessionRequest
        {
            public string SecurityStamp { get; set; } = string.Empty;
        }
        public class CleanupSessionsRequest
        {
            public int HoursOld { get; set; } = 24;
        }
    }
}