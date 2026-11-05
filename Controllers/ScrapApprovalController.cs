using BCrypt.Net;
using Dapper;
using DocumentFormat.OpenXml.Bibliography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SCP_Control.DbContext;
using SCP_Control.Models;
using SCP_Control.Services;
using SCP_Control.Services.Interfaces;
using SCP_Control.ViewModels;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace SCP_Control.Controllers;

[Authorize(Policy = "ManagementOnly")]
public class ScrapApprovalController : Controller
{
    private readonly IScrapApprovalService _scrapApprovalService;
    private readonly IAuthService _authService;
    private readonly IUserService _userService;
    private readonly IActivityLogService _activityLogService;
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ScrapApprovalController> _logger;
    private readonly UserManager<User> _userManager;
    private readonly ISessionService _sessionService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOperatorService _operatorService;
    private readonly IBomProcessingService _bomProcessingService;
    public ScrapApprovalController(
        IScrapApprovalService scrapApprovalService,
        IAuthService authService,
        IOperatorService operatorService,
        IUserService userService,
        IActivityLogService activityLogService,
        ApplicationDbContext context,
        ILogger<ScrapApprovalController> logger,
        IConfiguration configuration,
        ISessionService sessionService,
        UserManager<User> userManager,
        IHttpContextAccessor httpContextAccessor,
        IBomProcessingService bomProcessingService)
    {
        _scrapApprovalService = scrapApprovalService ?? throw new ArgumentNullException(nameof(scrapApprovalService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _operatorService = operatorService ?? throw new ArgumentNullException(nameof(operatorService));
        _activityLogService = activityLogService ?? throw new ArgumentNullException(nameof(activityLogService));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _bomProcessingService = bomProcessingService ?? throw new ArgumentNullException(nameof(bomProcessingService)); // ✅ ADD THIS LINE
    }

    [HttpGet]
    public IActionResult Index()
    {
        return RedirectToAction("Dashboard");
    }

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Dashboard()
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            // Determine user access level
            var userLevel = GetUserApprovalLevel(user.UserType);
            var canViewAll = CanUserViewAllData(user.UserType);

            _logger.LogInformation($"Dashboard accessed by {user.ClockNumber} (Type: {user.UserType}, Level: {userLevel})");

            // Get scrap data with error handling for database restore
            List<ScrapDataViewModel> scrapItems;
            try
            {
                if (canViewAll)
                {
                    scrapItems = new List<ScrapDataViewModel>(); // empty — JS will fetch
                    ViewBag.AdminDeferLoad = true;
                }
                else
                {
                    scrapItems = await GetScrapDataForUser(user, userLevel, canViewAll);
                    ViewBag.AdminDeferLoad = false;
                }
            }
            catch (SqlException ex) when (ex.Number == 927)
            {
                // Database restore in progress - provide minimal dashboard
                _logger.LogWarning("Dashboard loaded with limited data due to database restore");
                scrapItems = new List<ScrapDataViewModel>();
                ViewBag.DatabaseRestoreMessage = "System maintenance in progress. Some features may be limited.";
            }

            // Calculate stats
            var viewModel = new ScrapApprovalDashboardViewModel
            {
                ScrapItems = scrapItems,
                CurrentDepartment = user.Department ?? "",
                CurrentLine = user.Line ?? "",
                CurrentShift = user.Shift ?? "",
                TotalCount = scrapItems.Count,
                TotalCost = scrapItems.Sum(x => x.TotalItemCost),
                PendingApprovalCount = scrapItems.Count(x => CanUserApproveItem(user, x)),
                TotalWeight = 0,
                WeightHistory = new List<WeightHistoryViewModel>()
            };

            // Set ViewBag data for UI logic
            ViewBag.UserLevel = userLevel;
            ViewBag.UserType = user.UserType;
            ViewBag.CanViewAllData = canViewAll;
            ViewBag.CanManageWeight = userLevel == 1 || user.UserType?.ToLower() == "admin";
            ViewBag.ItemsUserCanApprove = scrapItems.Count(x => CanUserApproveItem(user, x));
            ViewBag.ItemsAtOtherLevels = scrapItems.Count(x => !CanUserApproveItem(user, x) && x.Status != "Approved" && x.Status != "Rejected");
            ViewBag.CurrentMachineNumber = user.MachineNumber ?? "";

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Dashboard method");
            return RedirectToAction("Login", "Account");
        }
    }

    private async Task<List<ScrapDataViewModel>> GetScrapDataForUser(User user, int userLevel, bool canViewAll)
    {
        using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();

        string query = @"
    SELECT 
        s.ScrapId,
        s.ClockNumber as SubmittedBy,
        s.PartNumber,
        s.Quantity,
        s.ScrapReason as ErrorName,
        s.Department,
        s.Line,
        s.Shift,
        s.SubmissionDate,
        s.CreatedAt,
        s.Status,
        s.CurrentApprovalLevel,
        s.ScrapType,

        COALESCE(im.MaterialCost, 0) as MaterialCost,
        COALESCE(im.StockUom, 'EA') as StockUom,
        
        s.TeamLeaderApprovalStatus,
        s.TeamLeaderApprovalBy,
        s.TeamLeaderApprovalDate,
        s.GroupLeaderApprovalStatus,
        s.GroupLeaderApprovalBy,
        s.GroupLeaderApprovalDate,
        s.ManagerApprovalStatus,
        s.ManagerApprovalBy,
        s.ManagerApprovalDate,
        s.SeniorManagerApprovalStatus,
        s.SeniorManagerApprovalBy,
        s.SeniorManagerApprovalDate,
        s.RejectedBy,
        s.RejectedReason,
        s.FirstApprovalStatus,
        s.FirstApprovalBy,
        s.FirstApprovalDate,
        s.FirstApprovalReason,
        submitterUser.ClockNumber as SubmitterClockNumber,
        tlUser.ClockNumber as TeamLeaderClockNumber,
        glUser.ClockNumber as GroupLeaderClockNumber,
        mgrUser.ClockNumber as ManagerClockNumber,
        (SELECT TOP 1 Weight 
         FROM [IntegrationCompanyB].[dbo].[scpWeightHistory] wh
         WHERE wh.Department = s.Department 
           AND wh.Line = s.Line 
           AND wh.Shift = s.Shift
         ORDER BY wh.CreatedAt DESC) as RecordedWeight
         
       FROM [IntegrationCompanyB].[dbo].[scpScrapData] s
    LEFT JOIN [SysproCompanyB].[dbo].[InvMaster] im 
        ON s.PartNumber = im.StockCode COLLATE Latin1_General_CI_AS
       AND im.StockCode COLLATE Latin1_General_BIN NOT IN ('EBD0532a', 'AVf30B/R', ' 4KB-927680-501')
    LEFT JOIN [IntegrationCompanyB].[dbo].[scpUser] submitterUser
        ON s.ClockNumber = submitterUser.ClockNumber
    LEFT JOIN [IntegrationCompanyB].[dbo].[scpUser] tlUser
        ON s.TeamLeaderApprovalBy = tlUser.ClockNumber
    LEFT JOIN [IntegrationCompanyB].[dbo].[scpUser] glUser
        ON s.GroupLeaderApprovalBy = glUser.ClockNumber
    LEFT JOIN [IntegrationCompanyB].[dbo].[scpUser] mgrUser
        ON s.ManagerApprovalBy = mgrUser.ClockNumber
    WHERE 1=1";

        var parameters = new DynamicParameters();

        if (!canViewAll)
        {
            // Always enforce location filter for non-admin approvers
            if (string.IsNullOrEmpty(user.Department) || string.IsNullOrEmpty(user.Line) || string.IsNullOrEmpty(user.Shift))
            {
                // User hasn't set their location yet — return empty
                query += " AND 1=0";
            }
            else
            {
                parameters.Add("Department", user.Department);
                parameters.Add("Shift", user.Shift);
                query += " AND s.Department = @Department AND s.Shift = @Shift";

                if (user.Department.Equals("PRE-ASSEMBLY FORD", StringComparison.OrdinalIgnoreCase)
                    && user.Line != null)
                {
                    // Always derive the bare zone (e.g. "ZONE 4" from "ZONE 4 - Komax", or "ZONE 4" as-is)
                    string zonePart = user.Line.Contains(" - ")
                        ? user.Line.Split(new[] { " - " }, 2, StringSplitOptions.None)[0].Trim()
                        : user.Line.Trim();

                    // A bare-zone TL/GL/Manager sees the whole zone: the bare line AND any "ZONE X - <Area>" sub-lines.
                    // An area-specific TL (e.g. "ZONE 4 - Komax") sees the bare zone plus their own specific sub-line
                    // (kept for backward compatibility with existing area-specific assignments).
                    query += @" AND (
        s.Line = @ZonePart
        OR s.Line = @UserLine
        OR s.Line LIKE @ZoneLikePattern
    )";

                    parameters.Add("ZonePart", zonePart);
                    parameters.Add("UserLine", user.Line);
                    parameters.Add("ZoneLikePattern", zonePart + " - %");
                }
                else
                {
                    query += " AND s.Line = @Line";
                    parameters.Add("Line", user.Line);
                }
            }

            // Filter to only items at THEIR approval level
            query += GetStatusFilterForUser(user.UserType);
        }

        query += " ORDER BY s.SubmissionDate DESC, s.CreatedAt DESC";

        var scrapData = await connection.QueryAsync(query, parameters);

        return scrapData.Select(item => new ScrapDataViewModel
        {
            ScrapId = item.ScrapId,
            PartNumber = item.PartNumber ?? "",
            SubmittedBy = item.SubmittedBy ?? "",
            ErrorName = item.ErrorName ?? "",
            Quantity = item.Quantity,
            MaterialCost = item.MaterialCost,
            StockUom = item.StockUom ?? "",          

            Department = item.Department ?? "",
            Line = item.Line ?? "",
            Shift = item.Shift ?? "",
            SubmissionDate = item.SubmissionDate,
            CreatedAt = item.CreatedAt,
            Status = DetermineDisplayStatus(item),
            CurrentApprovalLevel = item.CurrentApprovalLevel ?? 1,

            // Team Leader
            TeamLeaderApprovalStatus = item.TeamLeaderApprovalStatus ?? "",
            TeamLeaderApprovalBy = item.TeamLeaderApprovalBy ?? "",
            TeamLeaderApprovalDate = item.TeamLeaderApprovalDate,
            TeamLeaderClockNumber = item.TeamLeaderClockNumber ?? "N/A",

            // Group Leader
            GroupLeaderApprovalStatus = item.GroupLeaderApprovalStatus ?? "",
            GroupLeaderApprovalBy = item.GroupLeaderApprovalBy ?? "",
            GroupLeaderApprovalDate = item.GroupLeaderApprovalDate,
            GroupLeaderClockNumber = item.GroupLeaderClockNumber ?? "N/A",

            // Manager
            ManagerApprovalStatus = item.ManagerApprovalStatus ?? "",
            ManagerApprovalBy = item.ManagerApprovalBy ?? "",
            ManagerApprovalDate = item.ManagerApprovalDate,
            ManagerClockNumber = item.ManagerClockNumber ?? "N/A",

            // Senior Manager
            SeniorManagerApprovalStatus = item.SeniorManagerApprovalStatus ?? "",
            SeniorManagerApprovalBy = item.SeniorManagerApprovalBy ?? "",
            SeniorManagerApprovalDate = item.SeniorManagerApprovalDate,

            // Rejection
            RejectedReason = item.RejectedReason ?? "",

            // First Approval
            FirstApprovalStatus = item.FirstApprovalStatus ?? "",
            FirstApprovalBy = item.FirstApprovalBy ?? "",
            FirstApprovalDate = item.FirstApprovalDate,
            FirstApprovalReason = item.FirstApprovalReason ?? "",

            // Submitter Clock Number
            SubmitterClockNumber = item.SubmitterClockNumber ?? item.SubmittedBy ?? "N/A",

            // Weight Information
            RecordedWeight = item.RecordedWeight,

            // Approval capability
            CanApprove = CanUserApproveItem(user, item) ? 1 : 0
        }).ToList();
    }

    private string GetStatusFilterForUser(string userType)
    {
        return userType?.ToLower() switch
        {
            "teamleader" => " AND (s.Status = 'Pending TL' OR s.Status IS NULL OR s.Status = '')",
            "groupleader" => " AND (s.Status = 'Pending GL')",
            "manager" => " AND (s.Status = 'Pending Manager')",
            "seniormanager" => " AND (s.Status = 'Pending Senior Manager')",
            _ => ""
        };
    }

    [HttpGet]
    public async Task<IActionResult> DebugZoneItems(string zone = "ZONE 1", string shift = "A")
    {
        try
        {
            var user = await GetCurrentUserAsync();

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            const string query = @"
            SELECT 
                s.ScrapId,
                s.Department,
                s.Line,
                s.Shift,
                s.Status,
                s.PartNumber,
                s.Quantity,
                s.ClockNumber as SubmittedBy,
                s.CreatedAt,
                s.SubmissionDate
            FROM [IntegrationCompanyB].[dbo].[scpScrapData] s
            WHERE s.Department = 'PRE-ASSEMBLY FORD'
              AND s.Line = @Zone
              AND s.Shift = @Shift
            ORDER BY s.CreatedAt DESC";

            var items = await connection.QueryAsync(query, new { Zone = zone, Shift = shift });
            var itemList = items.ToList();

            // Also check what the current user's stored location is
            return Json(new
            {
                success = true,
                queryUsed = $"Department=PRE-ASSEMBLY FORD, Line={zone}, Shift={shift}",
                currentUserDept = user.Department,
                currentUserLine = user.Line,
                currentUserShift = user.Shift,
                totalFound = itemList.Count,
                statusBreakdown = itemList
                    .GroupBy(x => x.Status)
                    .Select(g => new { status = g.Key, count = g.Count() }),
                sampleItems = itemList.Take(5)
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> DebugPreAssemblyLines()
    {
        try
        {
            var user = await GetCurrentUserAsync();

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            const string query = @"
            SELECT DISTINCT 
                s.Department,
                s.Line,
                s.Shift,
                COUNT(*) as ItemCount,
                MIN(s.Status) as SampleStatus
            FROM [IntegrationCompanyB].[dbo].[scpScrapData] s
            WHERE s.Department LIKE '%PRE-ASSEMBLY%'
            GROUP BY s.Department, s.Line, s.Shift
            ORDER BY s.Department, s.Line, s.Shift";

            var results = await connection.QueryAsync(query);

            return Json(new
            {
                success = true,
                currentUserDept = user.Department,
                currentUserLine = user.Line,
                currentUserShift = user.Shift,
                data = results
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }



    private string DetermineDisplayStatus(dynamic item)
    {
        var status = item.Status?.ToString() ?? "";

        if (status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)) return "Rejected";
        if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase)) return "Approved";

        return status switch
        {
            "Pending TL" => "Pending TL",
            "Pending GL" => "Pending GL",
            "Pending Manager" => "Pending Manager",
            "Pending Senior Manager" => "Pending Senior Manager",
            "" or null => "Pending TL",
            _ => status
        };
    }

    private bool CanUserApproveItem(User user, dynamic item)
    {
        var userType = user.UserType?.ToLower();
        var status = item.Status?.ToString() ?? "";
        var dept = item.Department?.ToString() ?? "";

        // Already finalised
        if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
            return false;

        bool isEngineering = IsEngineeringDept(dept);

        // Check the right level for each user type
        switch (userType)
        {
            case "teamleader":
                // ✅ ADD THIS — TL cannot approve Engineering/tech dept scrap
                if (IsEngineeringDept(item.Department?.ToString()))
                    return false;

                if (!string.IsNullOrEmpty(item.TeamLeaderApprovalStatus?.ToString()) &&
                    item.TeamLeaderApprovalStatus == "Approved") return false;

                return status == "Pending TL" || string.IsNullOrEmpty(status);

            case "groupleader":
                if (!string.IsNullOrEmpty(item.GroupLeaderApprovalStatus?.ToString()) &&
                    item.GroupLeaderApprovalStatus == "Approved") return false;
                // Engineering depts: GL is first approver (Pending GL set at submission)
                // Normal depts: GL is second (Pending GL set after TL approves)
                return status == "Pending GL";

            case "manager":
                if (!string.IsNullOrEmpty(item.ManagerApprovalStatus?.ToString()) &&
                    item.ManagerApprovalStatus == "Approved") return false;
                return status == "Pending Manager";

            case "seniormanager":
                if (!string.IsNullOrEmpty(item.SeniorManagerApprovalStatus?.ToString()) &&
                    item.SeniorManagerApprovalStatus == "Approved") return false;
                // Senior Manager is final for Engineering depts; not used in normal flow
                return status == "Pending Senior Manager";

            default:
                return false;
        }
    }

    private int GetUserApprovalLevel(string userType)
    {
        var normalised = userType?.ToLower().Replace("engineering ", "").Trim();
        return normalised switch
        {
            "teamleader" => 1,
            "groupleader" => 2,
            "manager" => 3,
            "seniormanager" => 4,
            "admin" => 5,
            _ => 0
        };
    }

    private bool CanUserViewAllData(string userType)
    {
        return userType?.ToLower() switch
        {
            "admin" => true,   // Admin sees everything, no filter
            _ => false         // Everyone else (including seniormanager) goes through status filter
        };
    }

    private static readonly HashSet<string> EngineeringDepts = new(StringComparer.OrdinalIgnoreCase)
{
    "ENGINEERING PLANT A",
    "ENGINEERING PLANT B",
    "ENGINEERING PLANT C",
    "ENGINEERING FIELLI",
    "SQA",
    "STORES",
    "ENGINEERING",
    "PROCESS",
    "KANBAN",
    "PROJECT",
    "QUALITY",
    "CSUV SCRAP",
    "ADMIN PRE-ASSEMBLY FORD",
    "ADMIN PRE-ASSEMBLY TSAM"
};

    // Helper — call this anywhere you have a department string
    private static bool IsEngineeringDept(string? department)
        => !string.IsNullOrEmpty(department) && EngineeringDepts.Contains(department);



    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessApproval([FromBody] ApprovalRequest request)
    {
        try
        {
            // Add this check at the start
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = "Invalid request data" });
            }

            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            var userType = user.UserType?.ToLower();
            _logger.LogInformation($"Processing approval: User={user.ClockNumber}, Type={userType}, Items={request.ScrapIds.Count}, Approved={request.IsApproved}");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                int processedCount = 0;
                var currentTime = DateTime.Now;

                foreach (var scrapId in request.ScrapIds)
                {
                    var item = await _context.ScrapData.FirstOrDefaultAsync(s => s.ScrapId == scrapId);
                    if (item == null)
                    {
                        _logger.LogWarning($"Scrap item {scrapId} not found");
                        continue;
                    }

                    // Validate user can approve this item
                    if (!CanUserApproveItem(user, item))
                    {
                        _logger.LogWarning($"User {user.ClockNumber} cannot approve item {scrapId} (Status: {item.Status})");
                        continue;
                    }

                    // Process the approval/rejection
                    if (request.IsApproved)
                    {
                        await ProcessItemApproval(item, user, userType, currentTime); // ✅ Add await
                    }
                    else
                    {
                        ProcessItemRejection(item, user, userType, currentTime, request.RejectReason);
                    }

                    processedCount++;
                }

                if (processedCount > 0)
                {
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // Log activity
                    await _activityLogService.LogActivityAsync(
                        user.ClockNumber,
                        request.IsApproved ? "Approval" : "Rejection",
                        $"{(request.IsApproved ? "Approved" : "Rejected")} {processedCount} items at {userType} level",
                        "Success"
                    );

                    return Json(new
                    {
                        success = true,
                        message = $"Successfully {(request.IsApproved ? "approved" : "rejected")} {processedCount} item(s)",
                        processedCount = processedCount
                    });
                }
                else
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "No items were processed. Please check item status and permissions." });
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Transaction error in ProcessApproval");
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ProcessApproval");
            return Json(new { success = false, message = "Error processing approval request" });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BackfillBomComponentsBatch([FromBody] BackfillBatchRequest request)
    {
        try
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser.UserType?.ToLower() != "admin")
                return Json(new { success = false, message = "Access denied" });

            int batchSize = request.BatchSize > 0 ? request.BatchSize : 100;
            int skip = request.Skip > 0 ? request.Skip : 0;

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();

            const string query = @"
    SELECT TOP (@BatchSize)
        s.ScrapId,
        COALESCE(s.PartNumber, '') AS PartNumber,
        COALESCE(s.PartCategory, '') AS PartCategory,
        s.Quantity,
        s.Status
    FROM [IntegrationCompanyB].[dbo].[scpScrapData] s
    WHERE s.Status = 'Approved'
        AND s.PartCategory IS NOT NULL
        AND s.PartCategory != ''
        AND s.PartNumber IS NOT NULL
        AND s.PartNumber != ''
        AND s.ProcessedData IS NULL
        AND NOT EXISTS (
            SELECT 1 
            FROM [IntegrationCompanyB].[dbo].[scpBomComponents] bc 
            WHERE bc.ScrapId = s.ScrapId
        )
    ORDER BY s.ScrapId";

            var items = await connection.QueryAsync(query, new { BatchSize = batchSize, Skip = skip });
            var itemList = items.ToList();

            if (!itemList.Any())
                return Json(new { success = true, message = "No more items to process", remaining = 0 });

            int processed = 0, errors = 0;
            var currentTime = DateTime.UtcNow;

            using var sysproConnection = new SqlConnection(_configuration.GetConnectionString("SysproConnection"));
            await sysproConnection.OpenAsync();

            foreach (var item in itemList)
            {
                try
                {
                    string partNumber = item.PartNumber?.ToString() ?? "";
                    string partCategory = item.PartCategory?.ToString() ?? "";
                    int scrapId = (int)item.ScrapId;

                    if (partCategory.ToUpper() == "B")
                    {
                        const string getPartQuery = @"
    SELECT TOP 1 StockCode, Description, LongDesc, StockUom, ProductClass
    FROM [dbo].[InvMaster]
    WHERE StockCode COLLATE SQL_Latin1_General_CP1_CI_AS 
        = @StockCode COLLATE SQL_Latin1_General_CP1_CI_AS
    ORDER BY CASE WHEN StockCode COLLATE Latin1_General_BIN = @StockCode THEN 0 ELSE 1 END";

                        var part = await sysproConnection.QueryFirstOrDefaultAsync(
                            getPartQuery, new { StockCode = partNumber });

                        if (part != null)
                        {
                            var bom = new BomComponent
                            {
                                ScrapId = scrapId,
                                ParentPart = partNumber,
                                Component = partNumber,
                                Description = part.Description?.ToString() ?? "",
                                LongDesc = part.LongDesc?.ToString() ?? "",
                                QtyPer = 1,
                                ScrapQty = item.Quantity,
                                Uom = part.StockUom?.ToString() ?? "EA",
                                ProductClass = part.ProductClass?.ToString() ?? "",
                                PartCategory = "B",
                                UserField1 = "",
                                UserField2 = "0",
                                ProcessedAt = currentTime,
                                ProcessedBy = currentUser.ClockNumber
                            };
                            _context.BomComponents.Add(bom);

                            await connection.ExecuteAsync(
                                "UPDATE [dbo].[scpScrapData] SET ProcessedData = @pd WHERE ScrapId = @id",
                                new { pd = "Raw Material (B): Stored as-is", id = scrapId });

                            processed++;
                        }
                        else
                        {
                            await connection.ExecuteAsync(
                                "UPDATE [dbo].[scpScrapData] SET ProcessedData = @pd WHERE ScrapId = @id",
                                new { pd = $"Error: Part {partNumber} not found in InvMaster", id = scrapId });
                            errors++;
                        }
                    }
                    else if (partCategory.ToUpper() == "M")
                    {
                        var bomResult = await _bomProcessingService.ProcessApprovedScrapAsync(
                            partNumber, 1, currentUser.ClockNumber);

                        if (bomResult.Success && bomResult.BomComponents?.Any() == true)
                        {
                            foreach (var component in bomResult.BomComponents)
                            {
                                _context.BomComponents.Add(new BomComponent
                                {
                                    ScrapId = scrapId,
                                    ParentPart = partNumber,
                                    Component = component.Component ?? "",
                                    Description = component.Description ?? "",
                                    LongDesc = "",
                                    QtyPer = component.QtyPer,
                                    ScrapQty = item.Quantity,
                                    Uom = component.Uom ?? "",
                                    ProductClass = component.ProductClass ?? "",
                                    PartCategory = component.PartCategory ?? "",
                                    UserField1 = "",
                                    UserField2 = "0",
                                    SequenceNum = component.SequenceNum ?? "",
                                    ProcessedAt = currentTime,
                                    ProcessedBy = currentUser.ClockNumber
                                });
                            }

                            await connection.ExecuteAsync(
                                "UPDATE [dbo].[scpScrapData] SET ProcessedData = @pd WHERE ScrapId = @id",
                                new { pd = $"BOM Exploded: {bomResult.ComponentsProcessed} raw materials", id = scrapId });

                            processed++;
                        }
                        else
                        {
                            await connection.ExecuteAsync(
                                "UPDATE [dbo].[scpScrapData] SET ProcessedData = @pd WHERE ScrapId = @id",
                                new { pd = $"BOM Error: {bomResult.Message}", id = scrapId });
                            errors++;
                        }
                    }

                    if (processed % 50 == 0)
                        await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing ScrapId {item.ScrapId}");
                    errors++;
                }
            }

            await _context.SaveChangesAsync();

            // Get remaining count
            var remaining = await connection.QuerySingleAsync<int>(@"
            SELECT COUNT(*) FROM [IntegrationCompanyB].[dbo].[scpScrapData]
            WHERE Status = 'Approved' AND ProcessedData IS NULL
            AND PartCategory IS NOT NULL AND PartCategory != ''");

            return Json(new
            {
                success = true,
                processed,
                errors,
                remaining,
                nextSkip = skip + batchSize,
                message = $"Processed {processed} items. {remaining} remaining."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch BOM backfill");
            return Json(new { success = false, message = ex.Message });
        }
    }

    public class BackfillBatchRequest
    {
        public int BatchSize { get; set; } = 100;
        public int Skip { get; set; } = 0;
    }

    private async Task ProcessItemApproval(ScrapData item, User user, string userType, DateTime currentTime)
    {
        bool isEngineering = IsEngineeringDept(item.Department);

        switch (userType)
        {
            case "teamleader":
                if (IsEngineeringDept(item.Department))
                {
                    _logger.LogWarning("⛔ TL {User} attempted to approve Engineering dept item {ScrapId}",
                        user.ClockNumber, item.ScrapId);
                    return; // silently skip, ProcessApproval already filters via CanUserApproveItem
                }
                item.TeamLeaderApprovalStatus = "Approved";
                item.TeamLeaderApprovalBy = user.ClockNumber;
                item.TeamLeaderApprovalDate = currentTime;
                item.Status = "Pending GL";
                item.CurrentApprovalLevel = 2;

                if (string.IsNullOrEmpty(item.FirstApprovalStatus))
                {
                    item.FirstApprovalStatus = "Approved";
                    item.FirstApprovalBy = user.ClockNumber;
                    item.FirstApprovalDate = currentTime;
                }
                break;

            case "groupleader":
                item.GroupLeaderApprovalStatus = "Approved";
                item.GroupLeaderApprovalBy = user.ClockNumber;
                item.GroupLeaderApprovalDate = currentTime;

                if (isEngineering)
                {
                    // Engineering: GL is first approver — record as FirstApproval too
                    if (string.IsNullOrEmpty(item.FirstApprovalStatus))
                    {
                        item.FirstApprovalStatus = "Approved";
                        item.FirstApprovalBy = user.ClockNumber;
                        item.FirstApprovalDate = currentTime;
                    }
                    item.Status = "Pending Manager";
                    item.CurrentApprovalLevel = 3;
                }
                else
                {
                    item.Status = "Pending Manager";
                    item.CurrentApprovalLevel = 3;
                }
                break;

            case "manager":
                item.ManagerApprovalStatus = "Approved";
                item.ManagerApprovalBy = user.ClockNumber;
                item.ManagerApprovalDate = currentTime;

                if (isEngineering)
                {
                    // Engineering: Manager is 2nd approver, Senior Manager still needed
                    item.Status = "Pending Senior Manager";
                    item.CurrentApprovalLevel = 4;
                }
                else
                {
                    // Normal depts: Manager is final approver
                    item.Status = "Approved";
                    item.CurrentApprovalLevel = 4;
                    item.ApprovedBy = user.ClockNumber;
                    item.ApprovalDate = currentTime;

                    // BOM processing (existing logic — keep as-is)
                    try
                    {
                        if (item.PartCategory?.ToUpper() == "M")
                        {
                            _logger.LogInformation("🔄 Manufactured part (M): Exploding {Part}", item.PartNumber);
                            var bomResult = await _bomProcessingService.ProcessApprovedScrapAsync(
                                item.PartNumber, 1, user.ClockNumber);

                            if (bomResult.Success && bomResult.BomComponents?.Any() == true)
                            {
                                foreach (var component in bomResult.BomComponents)
                                {
                                    _context.BomComponents.Add(new BomComponent
                                    {
                                        ScrapId = item.ScrapId,
                                        ParentPart = item.PartNumber,
                                        Component = component.Component ?? "",
                                        Description = component.Description ?? "",
                                        LongDesc = "",
                                        QtyPer = component.QtyPer,
                                        ScrapQty = item.Quantity,            // ✅ ADD
                                        Uom = component.Uom ?? "",
                                        ProductClass = component.ProductClass ?? "",
                                        PartCategory = component.PartCategory ?? "",
                                        UserField1 = "",                     // ✅ CHANGE (was component.UserField1)
                                        UserField2 = "0",                    // ✅ CHANGE (was component.UserField2)
                                        SequenceNum = component.SequenceNum ?? "",  // ✅ ADD
                                        ProcessedAt = bomResult.ProcessedAt,
                                        ProcessedBy = user.ClockNumber
                                    });
                                }
                                await _context.SaveChangesAsync();
                                item.ProcessedData = $"BOM Exploded: {bomResult.ComponentsProcessed} raw materials";
                            }
                            else
                            {
                                var msg = bomResult.Message.Length > 200
                                    ? bomResult.Message[..197] + "..." : bomResult.Message;
                                item.ProcessedData = $"BOM Error: {msg}";
                            }
                        }
                        else if (item.PartCategory?.ToUpper() == "B")
                        {
                            _logger.LogInformation("🔄 Bought-out part (B): Storing {Part}", item.PartNumber);
                            using var conn = new SqlConnection(_configuration.GetConnectionString("SysproConnection"));
                            await conn.OpenAsync();
                            var partDetails = await conn.QueryFirstOrDefaultAsync(
    @"SELECT TOP 1 StockCode, Description, LongDesc, StockUom, ProductClass
      FROM [dbo].[InvMaster]
      WHERE StockCode = @StockCode
      ORDER BY CASE WHEN StockCode COLLATE Latin1_General_BIN = @StockCode THEN 0 ELSE 1 END",
    new { StockCode = item.PartNumber });

                            if (partDetails != null)
                            {
                                _context.BomComponents.Add(new BomComponent
                                {
                                    ScrapId = item.ScrapId,
                                    ParentPart = item.PartNumber,
                                    Component = item.PartNumber,
                                    Description = partDetails.Description?.ToString() ?? "",
                                    LongDesc = partDetails.LongDesc?.ToString() ?? "",
                                    QtyPer = 1,
                                    Uom = partDetails.StockUom?.ToString() ?? "",
                                    ProductClass = partDetails.ProductClass?.ToString() ?? "",
                                    PartCategory = "B",
                                    UserField1 = "",
                                    UserField2 = "0",
                                    ScrapQty = item.Quantity,
                                    ProcessedAt = currentTime,
                                    ProcessedBy = user.ClockNumber
                                });
                                await _context.SaveChangesAsync();
                                item.ProcessedData = "Raw Material (B): Stored as-is";
                            }
                            else
                            {
                                item.ProcessedData = $"Error: Part {item.PartNumber} not found in InvMaster";
                            }
                        }
                        else
                        {
                            item.ProcessedData = $"Unknown PartCategory: {item.PartCategory ?? "NULL"}";
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ BOM processing error for {Part}", item.PartNumber);
                        var msg = ex.Message.Length > 200 ? ex.Message[..197] + "..." : ex.Message;
                        item.ProcessedData = $"Processing Error: {msg}";
                    }
                }
                break;

            case "seniormanager":
                // Only reached for Engineering depts (final sign-off)
                item.SeniorManagerApprovalStatus = "Approved";
                item.SeniorManagerApprovalBy = user.ClockNumber;
                item.SeniorManagerApprovalDate = currentTime;
                item.Status = "Approved";
                item.CurrentApprovalLevel = 5;
                item.ApprovedBy = user.ClockNumber;
                item.ApprovalDate = currentTime;

                // BOM processing for Engineering depts on final approval
                try
                {
                    if (item.PartCategory?.ToUpper() == "M")
                    {
                        var bomResult = await _bomProcessingService.ProcessApprovedScrapAsync(
                            item.PartNumber, 1, user.ClockNumber);
                        if (bomResult.Success && bomResult.BomComponents?.Any() == true)
                        {
                            foreach (var component in bomResult.BomComponents)
                            {
                                _context.BomComponents.Add(new BomComponent
                                {
                                    ScrapId = item.ScrapId,
                                    ParentPart = item.PartNumber,
                                    Component = component.Component ?? "",
                                    Description = component.Description ?? "",
                                    LongDesc = "",
                                    QtyPer = component.QtyPer,
                                    ScrapQty = item.Quantity,            // ✅ ADD
                                    Uom = component.Uom ?? "",
                                    ProductClass = component.ProductClass ?? "",
                                    PartCategory = component.PartCategory ?? "",
                                    UserField1 = "",                     // ✅ CHANGE
                                    UserField2 = "0",                    // ✅ CHANGE
                                    SequenceNum = component.SequenceNum ?? "",  // ✅ ADD
                                    ProcessedAt = bomResult.ProcessedAt,
                                    ProcessedBy = user.ClockNumber
                                });
                            }
                            await _context.SaveChangesAsync();
                            item.ProcessedData = $"BOM Exploded: {bomResult.ComponentsProcessed} raw materials";
                        }
                    }
                    else if (item.PartCategory?.ToUpper() == "B")
                    {
                        using var conn = new SqlConnection(_configuration.GetConnectionString("SysproConnection"));
                        await conn.OpenAsync();
                        var partDetails = await conn.QuerySingleOrDefaultAsync(
                            @"SELECT StockCode, Description, LongDesc, StockUom, ProductClass
                          FROM [dbo].[InvMaster] WHERE StockCode = @StockCode",
                            new { StockCode = item.PartNumber });
                        if (partDetails != null)
                        {
                            _context.BomComponents.Add(new BomComponent
                            {
                                ScrapId = item.ScrapId,
                                ParentPart = item.PartNumber,
                                Component = item.PartNumber,
                                Description = partDetails.Description?.ToString() ?? "",
                                LongDesc = partDetails.LongDesc?.ToString() ?? "",
                                QtyPer = 1,
                                Uom = partDetails.StockUom?.ToString() ?? "",
                                ProductClass = partDetails.ProductClass?.ToString() ?? "",
                                PartCategory = "B",
                                ScrapQty = item.Quantity,
                                UserField1 = "",
                                UserField2 = "0",
                                ProcessedAt = currentTime,
                                ProcessedBy = user.ClockNumber
                            });
                            await _context.SaveChangesAsync();
                            item.ProcessedData = "Raw Material (B): Stored as-is";
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ BOM processing error (Senior Manager) for {Part}", item.PartNumber);
                    item.ProcessedData = $"Processing Error: {ex.Message[..Math.Min(197, ex.Message.Length)]}...";
                }
                break;
        }

        item.ModifiedAt = currentTime;
        item.ModifiedBy = user.ClockNumber;
        _logger.LogInformation("Item {ScrapId} approved by {UserType}, new status: {Status}",
            item.ScrapId, userType, item.Status);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BackfillBomComponents()
    {
        try
        {
            var currentUser = await GetCurrentUserAsync();

            if (currentUser.UserType?.ToLower() != "admin")
            {
                return Json(new { success = false, message = "Access denied - Admin only" });
            }

            _logger.LogInformation($"🔄 Starting BOM backfill process by {currentUser.ClockNumber}");

            // ✅ Use raw SQL with explicit NULL handling
            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();

            const string query = @"
            SELECT 
                s.ScrapId,
                COALESCE(s.PartNumber, '') AS PartNumber,
                COALESCE(s.PartCategory, '') AS PartCategory,
                s.Quantity,
                s.Status
            FROM [IntegrationCompanyB].[dbo].[scpScrapData] s
            WHERE s.Status = 'Approved'
                AND s.PartCategory IS NOT NULL
                AND s.PartCategory != ''
                AND s.PartNumber IS NOT NULL
                AND s.PartNumber != ''
                AND NOT EXISTS (
                    SELECT 1 
                    FROM [IntegrationCompanyB].[dbo].[scpBomComponents] bc 
                    WHERE bc.ScrapId = s.ScrapId
                )
            ORDER BY s.ScrapId";

            var approvedScrapWithoutBom = await connection.QueryAsync(query);

            if (!approvedScrapWithoutBom.Any())
            {
                _logger.LogInformation("✅ No items to backfill");
                return Json(new { success = true, message = "No approved scrap items need BOM backfill" });
            }

            _logger.LogInformation($"📊 Found {approvedScrapWithoutBom.Count()} approved items without BOM components");

            int categoryBCount = 0;
            int categoryMCount = 0;
            int componentsAdded = 0;
            int errorCount = 0;
            var currentTime = DateTime.UtcNow;

            using var sysproConnection = new SqlConnection(_configuration.GetConnectionString("SysproConnection"));
            await sysproConnection.OpenAsync();

            foreach (var item in approvedScrapWithoutBom)
            {
                try
                {
                    string partNumber = item.PartNumber?.ToString() ?? "";
                    string partCategory = item.PartCategory?.ToString() ?? "";
                    int scrapId = (int)item.ScrapId;

                    if (string.IsNullOrEmpty(partNumber) || string.IsNullOrEmpty(partCategory))
                    {
                        _logger.LogWarning($"⚠️ Skipping ScrapId {scrapId} - missing PartNumber or PartCategory");
                        errorCount++;
                        continue;
                    }

                    if (partCategory.ToUpper() == "B")
                    {
                        // Category B: Store part as-is
                        const string getPartDetailsQuery = @"
    SELECT TOP 1
        COALESCE(StockCode, '') AS StockCode,
        COALESCE(Description, '') AS Description,
        COALESCE(LongDesc, '') AS LongDesc,
        COALESCE(StockUom, 'EA') AS StockUom,
        COALESCE(ProductClass, '') AS ProductClass
    FROM [dbo].[InvMaster]
    WHERE StockCode = @StockCode
    ORDER BY CASE WHEN StockCode COLLATE Latin1_General_BIN = @StockCode THEN 0 ELSE 1 END";

                        var partDetails = await sysproConnection.QueryFirstOrDefaultAsync(
                            getPartDetailsQuery,
                            new { StockCode = partNumber }
                        );

                       

                        if (partDetails != null)
                        {
                            var bomComponent = new BomComponent
                            {
                                ScrapId = scrapId,
                                ParentPart = partNumber,
                                Component = partNumber,
                                Description = partDetails.Description?.ToString() ?? "",
                                LongDesc = partDetails.LongDesc?.ToString() ?? "",
                                QtyPer = 1,
                                Uom = partDetails.StockUom?.ToString() ?? "EA",
                                ProductClass = partDetails.ProductClass?.ToString() ?? "",
                                PartCategory = "B",
                                UserField1 = "",
                                UserField2 = "0",
                                ProcessedAt = currentTime,
                                ProcessedBy = currentUser.ClockNumber
                            };

                            _context.BomComponents.Add(bomComponent);
                            categoryBCount++;
                            componentsAdded++;
                        }
                        else
                        {
                            _logger.LogWarning($"⚠️ Part {partNumber} not found in InvMaster");
                            errorCount++;
                        }
                    }
                    else if (partCategory.ToUpper() == "M")
                    {
                        // Category M: Explode to components
                        var bomResult = await _bomProcessingService.ProcessApprovedScrapAsync(
                            partNumber,
                            1,
                            currentUser.ClockNumber
                        );

                        if (bomResult.Success && bomResult.BomComponents != null && bomResult.BomComponents.Any())
                        {
                            foreach (var component in bomResult.BomComponents)
                            {
                                try
                                {
                                    var bomComponent = new BomComponent
                                    {
                                        ScrapId = scrapId,
                                        ParentPart = partNumber,
                                        Component = component.Component ?? "",
                                        Description = component.Description ?? "",
                                        LongDesc = "",
                                        QtyPer = component.QtyPer,
                                        Uom = component.Uom ?? "",
                                        ProductClass = component.ProductClass ?? "",
                                        PartCategory = component.PartCategory ?? "",
                                        UserField1 = "",
                                        UserField2 = "0",
                                        ProcessedAt = currentTime,
                                        ProcessedBy = currentUser.ClockNumber
                                    };

                                    _context.BomComponents.Add(bomComponent);
                                    componentsAdded++;
                                }
                                catch (Exception componentEx)
                                {
                                    _logger.LogError(componentEx, $"❌ Error adding component {component.Component} for part {partNumber}");
                                    errorCount++;
                                }
                            }
                            categoryMCount++;
                        }
                        else
                        {
                            _logger.LogWarning($"⚠️ Failed to process M part: {partNumber} - {bomResult.Message}");
                            errorCount++;
                        }
                    }

                    // Save in batches of 100
                    if ((categoryBCount + categoryMCount) % 100 == 0)
                    {
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"💾 Saved batch: {categoryBCount} B-parts, {categoryMCount} M-parts, {componentsAdded} components");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"❌ Error processing ScrapId {item.ScrapId}, Part: {item.PartNumber}");
                    errorCount++;
                }
            }

            // Save remaining items
            await _context.SaveChangesAsync();

            await _activityLogService.LogActivityAsync(
                currentUser.ClockNumber,
                "BOM Backfill",
                $"Backfilled BOM components: {categoryBCount} B-parts, {categoryMCount} M-parts, {componentsAdded} total components, {errorCount} errors",
                "Success"
            );

            var resultMessage = $"BOM Backfill Complete: {categoryBCount} bought-out parts, {categoryMCount} manufactured parts, {componentsAdded} total components added. Errors: {errorCount}";
            _logger.LogInformation($"✅ BOM backfill complete: B={categoryBCount}, M={categoryMCount}, Components={componentsAdded}, Errors={errorCount}");

            return Json(new
            {
                success = true,
                message = resultMessage,
                details = new
                {
                    categoryBCount,
                    categoryMCount,
                    componentsAdded,
                    errorCount
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in BOM backfill process");
            return Json(new { success = false, message = $"Error during BOM backfill: {ex.Message}" });
        }
    }

    private void ProcessItemRejection(ScrapData item, User user, string userType,
    DateTime currentTime, string rejectReason)
    {
        switch (userType)
        {
            case "teamleader":
                item.TeamLeaderApprovalStatus = "Rejected";
                item.TeamLeaderApprovalBy = user.ClockNumber;
                item.TeamLeaderApprovalDate = currentTime;
                break;
            case "groupleader":
                item.GroupLeaderApprovalStatus = "Rejected";
                item.GroupLeaderApprovalBy = user.ClockNumber;
                item.GroupLeaderApprovalDate = currentTime;
                break;
            case "manager":
                item.ManagerApprovalStatus = "Rejected";
                item.ManagerApprovalBy = user.ClockNumber;
                item.ManagerApprovalDate = currentTime;
                break;
            case "seniormanager":
                item.SeniorManagerApprovalStatus = "Rejected";
                item.SeniorManagerApprovalBy = user.ClockNumber;
                item.SeniorManagerApprovalDate = currentTime;
                break;
        }

        item.Status = "Rejected";
        item.RejectedBy = user.ClockNumber;
        item.RejectedReason = $"{userType}: {rejectReason}";
        item.ModifiedAt = currentTime;
        item.ModifiedBy = user.ClockNumber;

        if (string.IsNullOrEmpty(item.FirstApprovalStatus))
        {
            item.FirstApprovalStatus = "Rejected";
            item.FirstApprovalBy = user.ClockNumber;
            item.FirstApprovalDate = currentTime;
            item.FirstApprovalReason = rejectReason;
        }

        _logger.LogInformation("Item {ScrapId} rejected by {UserType}: {Reason}",
            item.ScrapId, userType, rejectReason);
    }

    private async Task<User> GetCurrentUserAsync()
    {
        try
        {
            var clockNumber = User?.Identity?.Name;
            if (string.IsNullOrEmpty(clockNumber))
            {
                throw new InvalidOperationException("User not authenticated");
            }

            var user = await _context.User
                .FirstOrDefaultAsync(u => (u.ClockNumber == clockNumber || u.UserName == clockNumber) && u.IsActive);

            if (user == null)
            {
                throw new InvalidOperationException($"User {clockNumber} not found or inactive");
            }

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current user");
            throw;
        }
    }

    // Weight management for Admin/Senior Manager (bulk operations)
    [HttpGet]
    public async Task<IActionResult> GetApprovedLocations()
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
            {
                return Json(new { success = false, message = "Access denied" });
            }

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            const string query = @"
                SELECT DISTINCT 
                    s.Department,
                    s.Line,
                    s.Shift,
                    COUNT(*) as ApprovedCount,
                    SUM(COALESCE(im.MaterialCost, 0) * s.Quantity) as TotalCost
                                FROM [IntegrationCompanyB].[dbo].[scpScrapData] s
                LEFT JOIN [SysproCompanyB].[dbo].[InvMaster] im 
                    ON s.PartNumber = im.StockCode COLLATE Latin1_General_CI_AS
                   AND im.StockCode COLLATE Latin1_General_BIN NOT IN ('EBD0532a', 'AVf30B/R', ' 4KB-927680-501')
                WHERE s.Status = 'Approved'
                GROUP BY s.Department, s.Line, s.Shift
                ORDER BY s.Department, s.Line, s.Shift";

            var locations = await connection.QueryAsync(query);

            return Json(new { success = true, locations });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting approved locations");
            return Json(new { success = false, message = "Error retrieving locations" });
        }
    }

    private async Task<(bool isValid, string message)> ValidateLocationCombination(string department, string line, string shift)
    {
        var validShifts = new[] { "A", "B", "Straight", "ALL" };
        if (!validShifts.Contains(shift))
            return (false, $"Invalid shift: {shift}");

        using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

        // ALL/plant-wide values bypass line validation but still require permission (checked by caller)
        if (department.StartsWith("ALL ") || line.Contains("ALL"))
            return (true, "Valid combination (ALL scope)");

        const string deptQuery = "SELECT COUNT(*) FROM [IntegrationCompanyB].[dbo].[scpDepartment] WHERE DepartmentName = @Department AND IsActive = 1";
        var deptExists = await connection.QuerySingleAsync<int>(deptQuery, new { Department = department });
        if (deptExists == 0)
            return (false, $"Invalid department: {department}");

        // Pre-assembly lines are stored as "ZONE X - Area" — validate the area part against scpLine
        var lineToCheck = line.Contains(" - ") ? line.Split(new[] { " - " }, 2, StringSplitOptions.None)[1].Trim() : line;

        const string lineQuery = @"
        SELECT COUNT(*)
        FROM [IntegrationCompanyB].[dbo].[scpLine] l
        INNER JOIN [IntegrationCompanyB].[dbo].[scpDepartment] d
            ON l.DepartmentId = d.DepartmentId
        WHERE d.DepartmentName = @Department AND l.LineName = @Line AND l.IsActive = 1";
        var lineExists = await connection.QuerySingleAsync<int>(lineQuery, new { Department = department, Line = lineToCheck });

        if (lineExists == 0)
            return (false, $"Line '{line}' is not valid for department '{department}'");

        return (true, "Valid combination");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateLocation([FromBody] UpdateLocationRequest request)
    {
        request.Department = System.Net.WebUtility.HtmlDecode(request.Department?.Trim() ?? "");
        request.Line = System.Net.WebUtility.HtmlDecode(request.Line?.Trim() ?? "");
        request.Shift = System.Net.WebUtility.HtmlDecode(request.Shift?.Trim() ?? "");

        try
        {
            _logger.LogInformation($"🔧 DEBUG UpdateLocation - Raw request received:");
            _logger.LogInformation($"Department: '{request.Department}' (Length: {request.Department?.Length})");
            _logger.LogInformation($"Line: '{request.Line}' (Length: {request.Line?.Length})");
            _logger.LogInformation($"Shift: '{request.Shift}' (Length: {request.Shift?.Length})");

            if (request.Line == "Line 6-IP")
            {
                _logger.LogInformation($"🔧 DEBUG - Line 6-IP character analysis:");
                _logger.LogInformation($"Char codes: [{string.Join(", ", request.Line.Select(c => ((int)c).ToString()))}]");
            }

            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            if (request.Department == "FORD" && request.Line == "Line 6-IP")
            {
                _logger.LogInformation($"🔧 BYPASS: Processing Line 6-IP update directly");

                user.Department = request.Department;
                user.Line = request.Line;
                user.Shift = request.Shift;
                user.MachineNumber = request.MachineNumber ?? "";
                user.LastModifiedAt = DateTime.UtcNow;
                user.LastModifiedBy = user.ClockNumber;

                await _context.SaveChangesAsync();

                await _activityLogService.LogActivityAsync(
                    user.ClockNumber,
                    "Location Update",
                    $"Updated location to {request.Department}/{request.Line}/{request.Shift}" +
                        (string.IsNullOrEmpty(request.MachineNumber) ? "" : $" (Machines: {request.MachineNumber})"),
                    "Success"
                );

                return Json(new { success = true, message = "Location updated successfully (bypass mode)" });
            }

            if (string.IsNullOrWhiteSpace(request.Department) ||
                string.IsNullOrWhiteSpace(request.Line) ||
                string.IsNullOrWhiteSpace(request.Shift))
            {
                return Json(new { success = false, message = "All fields are required" });
            }

            if ((request.Department.StartsWith("ALL ") || request.Line.Contains("ALL") || request.Shift == "ALL") &&
                !CanUserViewAllData(user.UserType))
            {
                return Json(new { success = false, message = "You don't have permission to use ALL location settings" });
            }

            var validationResult = await ValidateLocationCombination(request.Department, request.Line, request.Shift);
            if (!validationResult.isValid)
            {
                return Json(new { success = false, message = validationResult.message });
            }

            user.Department = request.Department;
            user.Line = request.Line;
            user.Shift = request.Shift;
            user.MachineNumber = request.MachineNumber ?? "";   // ✅ THE FIX
            user.LastModifiedAt = DateTime.UtcNow;
            user.LastModifiedBy = user.ClockNumber;

            await _context.SaveChangesAsync();

            await _activityLogService.LogActivityAsync(
                user.ClockNumber,
                "Location Update",
                $"Updated location to {request.Department}/{request.Line}/{request.Shift}" +
                    (string.IsNullOrEmpty(request.MachineNumber) ? "" : $" (Machines: {request.MachineNumber})"),
                "Success"
            );

            return Json(new { success = true, message = "Location updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating location");
            return Json(new { success = false, message = "Error updating location" });
        }
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestNewUser(string userClockNumber)
    {
        SqlConnection connection = null;
        SqlTransaction transaction = null;

        try
        {
            var currentUser = await GetCurrentUserAsync();
            var userLevel = GetUserApprovalLevel(currentUser.UserType);

            if (userLevel < 1)
            {
                TempData["ErrorMessage"] = "Access denied - insufficient privileges";
                return RedirectToAction("Dashboard");
            }

            if (string.IsNullOrWhiteSpace(userClockNumber))
            {
                TempData["ErrorMessage"] = "Clock number is required";
                return RedirectToAction("Dashboard");
            }

            connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();

            // Check if user already exists
            string checkUserSql = "SELECT COUNT(*) FROM [IntegrationCompanyB].[dbo].[scpUser] WHERE ClockNumber = @ClockNumber";
            using (var checkCmd = new SqlCommand(checkUserSql, connection))
            {
                checkCmd.Parameters.AddWithValue("@ClockNumber", userClockNumber);
                int userCount = (int)await checkCmd.ExecuteScalarAsync();

                if (userCount > 0)
                {
                    TempData["ErrorMessage"] = $"User with clock number {userClockNumber} already exists";
                    connection.Close();
                    return RedirectToAction("Dashboard");
                }
            }

            // Check pending request
            string checkRequestSql = @"
    SELECT r.ClockNumber, r.Name, r.Surname, r.Email, r.UserType, r.Shift, r.ZoneName,
           d.DepartmentName, l.LineName
    FROM [IntegrationCompanyB].[dbo].[scpRequest] r
    LEFT JOIN [IntegrationCompanyB].[dbo].[scpDepartment] d ON r.DepartmentId = d.DepartmentId
    LEFT JOIN [IntegrationCompanyB].[dbo].[scpLine] l ON r.LineId = l.LineId
    WHERE r.ClockNumber = @ClockNumber AND r.Status = 'Pending'";

            string clockNumber = null;
            string name = null;
            string surname = null;
            string email = null;
            string userType = null;
            string shift = null;
            string departmentName = null;
            string lineName = null;
            string zoneName = null;   // ✅ NEW

            using (var checkReqCmd = new SqlCommand(checkRequestSql, connection))
            {
                checkReqCmd.Parameters.AddWithValue("@ClockNumber", userClockNumber);
                using (var reader = await checkReqCmd.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync())
                    {
                        reader.Close();
                        TempData["ErrorMessage"] = $"No pending registration request found for clock number {userClockNumber}";
                        connection.Close();
                        return RedirectToAction("Dashboard");
                    }

                    clockNumber = reader["ClockNumber"].ToString();
                    name = reader["Name"].ToString();
                    surname = reader["Surname"].ToString();
                    email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader["Email"].ToString();
                    userType = reader["UserType"].ToString();
                    shift = reader["Shift"].ToString();
                    departmentName = reader.IsDBNull(reader.GetOrdinal("DepartmentName")) ? "" : reader["DepartmentName"].ToString();
                    lineName = reader.IsDBNull(reader.GetOrdinal("LineName")) ? "" : reader["LineName"].ToString();
                    zoneName = reader.IsDBNull(reader.GetOrdinal("ZoneName")) ? null : reader["ZoneName"].ToString();
                }
            }

            // ✅ NEW: combine Zone + Line
            string finalLineName = lineName;
            if (!string.IsNullOrWhiteSpace(zoneName)
                && !string.IsNullOrWhiteSpace(lineName)
                && !lineName.Equals("Engineering Scrap", StringComparison.OrdinalIgnoreCase)
                && !lineName.StartsWith(zoneName + " - ", StringComparison.OrdinalIgnoreCase))
            {
                finalLineName = $"{zoneName} - {lineName}";
            }

            var tempPassword = GenerateTemporaryPassword();
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(tempPassword);
            var currentTime = DateTime.UtcNow;
            

            bool isManagementRole = new[] { "groupleader", "manager", "seniormanager", "admin" }
                .Contains(userType?.ToLower());

            string userName = isManagementRole
                ? (!string.IsNullOrWhiteSpace(surname)
                    ? $"{name?.Trim().ToLower()}.{surname?.Trim().ToLower()}"
                    : name?.Trim().ToLower() ?? userClockNumber)
                : userClockNumber;

            string userEmail = isManagementRole ? email : null;

            _logger.LogInformation($"🔍 Creating user: CN={userClockNumber}, UN={userName}, Email={userEmail ?? "NULL"}");

            transaction = connection.BeginTransaction();

            try
            {
                // ✅ GET NEXT USERID (since it's not IDENTITY)
                string getMaxIdSql = "SELECT ISNULL(MAX(UserId), 0) + 1 FROM [IntegrationCompanyB].[dbo].[scpUser]";
                int nextUserId;
                using (var maxIdCmd = new SqlCommand(getMaxIdSql, connection, transaction))
                {
                    nextUserId = (int)await maxIdCmd.ExecuteScalarAsync();
                }

                _logger.LogInformation($"🔍 Next UserId will be: {nextUserId}");

                // ✅ INSERT USER WITH EXPLICIT USERID
                string insertUserSql = @"
                INSERT INTO [IntegrationCompanyB].[dbo].[scpUser] 
                (UserId, ClockNumber, UserName, Email, PasswordHash, UserType, Department, Line, Shift, 
                 IsActive, CreatedAt, CreatedBy, LastModifiedAt, LastModifiedBy, PasswordResetRequired, 
                 LastPasswordChange, LockoutEnabled, AccessFailedCount)
                VALUES 
                (@UserId, @ClockNumber, @UserName, @Email, @PasswordHash, @UserType, @Department, @Line, @Shift,
                 @IsActive, @CreatedAt, @CreatedBy, @LastModifiedAt, @LastModifiedBy, @PasswordResetRequired, 
                 @LastPasswordChange, @LockoutEnabled, @AccessFailedCount)";

                using (var insertCmd = new SqlCommand(insertUserSql, connection, transaction))
                {
                    insertCmd.Parameters.AddWithValue("@UserId", nextUserId);
                    insertCmd.Parameters.AddWithValue("@ClockNumber", userClockNumber);
                    insertCmd.Parameters.AddWithValue("@UserName", userName);
                    insertCmd.Parameters.AddWithValue("@Email", string.IsNullOrEmpty(userEmail) ? (object)DBNull.Value : userEmail);
                    insertCmd.Parameters.AddWithValue("@PasswordHash", hashedPassword);
                    insertCmd.Parameters.AddWithValue("@UserType", userType);
                    insertCmd.Parameters.AddWithValue("@Department", departmentName);
                    insertCmd.Parameters.AddWithValue("@Line", finalLineName);   // ✅ CHANGED from lineName
                    insertCmd.Parameters.AddWithValue("@Shift", shift);
                    insertCmd.Parameters.AddWithValue("@IsActive", true);
                    insertCmd.Parameters.AddWithValue("@CreatedAt", currentTime);
                    insertCmd.Parameters.AddWithValue("@CreatedBy", currentUser.ClockNumber);
                    insertCmd.Parameters.AddWithValue("@LastModifiedAt", currentTime);
                    insertCmd.Parameters.AddWithValue("@LastModifiedBy", currentUser.ClockNumber);
                    insertCmd.Parameters.AddWithValue("@PasswordResetRequired", true);
                    insertCmd.Parameters.AddWithValue("@LastPasswordChange", currentTime);
                    insertCmd.Parameters.AddWithValue("@LockoutEnabled", true);
                    insertCmd.Parameters.AddWithValue("@AccessFailedCount", 0);

                    int rowsAffected = await insertCmd.ExecuteNonQueryAsync();

                    if (rowsAffected == 0)
                    {
                        throw new Exception("Failed to create user - no rows inserted");
                    }
                }

                _logger.LogInformation($"✅ User created with UserId: {nextUserId}");

                // UPDATE REQUEST
                string updateRequestSql = @"
                UPDATE [IntegrationCompanyB].[dbo].[scpRequest]
                SET Status = @Status, ProcessedBy = @ProcessedBy, ProcessedDate = @ProcessedDate
                WHERE ClockNumber = @ClockNumber AND Status = 'Pending'";

                using (var updateCmd = new SqlCommand(updateRequestSql, connection, transaction))
                {
                    updateCmd.Parameters.AddWithValue("@Status", "Approved");
                    updateCmd.Parameters.AddWithValue("@ProcessedBy", currentUser.ClockNumber);
                    updateCmd.Parameters.AddWithValue("@ProcessedDate", currentTime);
                    updateCmd.Parameters.AddWithValue("@ClockNumber", userClockNumber);

                    int updated = await updateCmd.ExecuteNonQueryAsync();
                    if (updated == 0)
                    {
                        throw new Exception("Failed to update registration request");
                    }
                }

                transaction.Commit();

                await _activityLogService.LogActivityAsync(
                    currentUser.ClockNumber,
                    "User Creation",
                    $"Created user {userClockNumber} ({userType}) with UserId {nextUserId}",
                    "Success"
                );

                TempData["SuccessMessage"] = $"User account created successfully for {userClockNumber}";
                TempData["TempPassword"] = tempPassword;
                TempData["ClockNumber"] = userClockNumber;
                TempData["Username"] = userName;

                return RedirectToAction("Dashboard");
            }
            catch (Exception ex)
            {
                transaction?.Rollback();
                _logger.LogError(ex, $"❌ Transaction error: {ex.Message}");
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Error creating user: {ex.Message}");
            TempData["ErrorMessage"] = $"Error: {ex.Message}";
            return RedirectToAction("Dashboard");
        }
        finally
        {
            transaction?.Dispose();
            connection?.Close();
            connection?.Dispose();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string userClockNumber)
    {
        try
        {
            var currentUser = await GetCurrentUserAsync();
            var userLevel = GetUserApprovalLevel(currentUser.UserType);

            // Allow Level 1+ users to reset passwords
            if (userLevel < 1)
            {
                TempData["ErrorMessage"] = "Access denied - insufficient privileges";
                return RedirectToAction("Dashboard");
            }

            if (string.IsNullOrWhiteSpace(userClockNumber))
            {
                TempData["ErrorMessage"] = "Clock number is required";
                return RedirectToAction("Dashboard");
            }

            var targetUser = await _context.User
                .FirstOrDefaultAsync(u => u.ClockNumber == userClockNumber && u.IsActive);

            if (targetUser == null)
            {
                TempData["ErrorMessage"] = $"Active user with clock number {userClockNumber} not found";
                return RedirectToAction("Dashboard");
            }


            // Generate new temporary password
            var tempPassword = GenerateTemporaryPassword();
            targetUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword); // FIXED: Use BCrypt instead of Identity hasher
            targetUser.LastModifiedAt = DateTime.UtcNow;
            targetUser.LastModifiedBy = currentUser.ClockNumber;
            targetUser.PasswordResetRequired = true;
            targetUser.LastPasswordChange = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Log activity
            await _activityLogService.LogActivityAsync(
                currentUser.ClockNumber,
                "Password Reset",
                $"Reset password for user {userClockNumber}",
                "Success"
            );

            TempData["SuccessMessage"] = $"Password reset successfully for {userClockNumber}";
            TempData["TempPassword"] = tempPassword;
            TempData["ClockNumber"] = userClockNumber;
            TempData["Username"] = targetUser.UserName;

            return RedirectToAction("Dashboard");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting password");
            TempData["ErrorMessage"] = "Error resetting password";
            return RedirectToAction("Dashboard");
        }
    }

    // ADD THIS ACTION METHOD TO YOUR ScrapApprovalController class

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> AdminWeightDashboard()
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            // Check if user has permission to access weight management dashboard
            if (!CanUserViewAllData(user.UserType))
            {
                TempData["ErrorMessage"] = "Access denied - insufficient privileges for weight management dashboard";
                return RedirectToAction("Dashboard");
            }

            _logger.LogInformation($"AdminWeightDashboard accessed by {user.ClockNumber} (Type: {user.UserType})");

            // Use the same ViewModel type that the view expects
            var viewModel = new ScrapApprovalDashboardViewModel
            {
                ScrapItems = new List<ScrapDataViewModel>(), // Empty list for weight dashboard
                CurrentDepartment = user.Department ?? "",
                CurrentLine = user.Line ?? "",
                CurrentShift = user.Shift ?? "",
                TotalCount = 0,
                TotalCost = 0,
                PendingApprovalCount = 0,
                TotalWeight = 0,
                WeightHistory = new List<WeightHistoryViewModel>()
            };

            // Set ViewBag data for weight management dashboard
            ViewBag.UserLevel = GetUserApprovalLevel(user.UserType);
            ViewBag.UserType = user.UserType;
            ViewBag.CanViewAllData = CanUserViewAllData(user.UserType);
            ViewBag.CanManageWeight = true; // Always true for admin weight dashboard
            ViewBag.ItemsUserCanApprove = 0; // No approval functionality on weight dashboard
            ViewBag.ItemsAtOtherLevels = 0;
            ViewBag.IsWeightDashboard = true; // Flag to indicate this is the weight dashboard

            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AdminWeightDashboard method");
            TempData["ErrorMessage"] = "Error accessing weight management dashboard";
            return RedirectToAction("Dashboard");
        }
    }

    

    // ALSO ADD THIS TEST METHOD FOR DEBUGGING
    [HttpGet]
    public async Task<IActionResult> TestApprovedScrap()
    {
        try
        {
            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            var query = @"
            SELECT 
                Department, 
                Line, 
                Shift, 
                COUNT(*) as ApprovedCount,
                Status
            FROM [IntegrationCompanyB].[dbo].[scpScrapData] 
            WHERE Status = 'Approved'
            GROUP BY Department, Line, Shift, Status
            ORDER BY Department, Line, Shift";

            var results = await connection.QueryAsync(query);

            return Json(new
            {
                success = true,
                message = "Test query successful",
                totalLocations = results.Count(),
                data = results
            });
        }
        catch (Exception ex)
        {
            return Json(new
            {
                success = false,
                message = "Test query failed: " + ex.Message,
                error = ex.ToString()
            });
        }
    }



    [HttpGet]
    public async Task<IActionResult> GetApprovalStatistics(string? date = null)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
            {
                return Json(new { success = false, message = "Access denied" });
            }

            DateTime targetDate;
            if (string.IsNullOrWhiteSpace(date) || !DateTime.TryParse(date, out targetDate))
            {
                targetDate = DateTime.Today;
            }
            else
            {
                targetDate = targetDate.Date;
            }

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            // Get approved items count
            const string approvedItemsQuery = @"
        SELECT COUNT(*) 
        FROM [IntegrationCompanyB].[dbo].[scpScrapData] 
        WHERE Status = 'Approved'";

            // Get ready locations count (locations with approved items that haven't been weighed recently)
            const string readyLocationsQuery = @"
            SELECT COUNT(DISTINCT CONCAT(s.Department, '|', s.Line, '|', s.Shift))
            FROM [IntegrationCompanyB].[dbo].[scpScrapData]  s
            LEFT JOIN (
                SELECT 
                    Department, 
                    Line, 
                    Shift,
                    MAX(CreatedAt) as LastWeightDate
                FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
                GROUP BY Department, Line, Shift
            ) wh ON s.Department = wh.Department 
                AND s.Line = wh.Line 
                AND s.Shift = wh.Shift
            WHERE s.Status = 'Approved'
                AND (wh.LastWeightDate IS NULL OR wh.LastWeightDate < DATEADD(day, -1, GETDATE()))";

            // Get departments count
            const string departmentsQuery = @"
            SELECT COUNT(DISTINCT Department) 
            FROM [IntegrationCompanyB].[dbo].[scpScrapData] 
            WHERE Status = 'Approved'";

            // Get today's weight total
            // Weight total for the selected date (defaults to today)
            const string dayWeightQuery = @"
        SELECT COALESCE(SUM(Weight), 0) 
        FROM [IntegrationCompanyB].[dbo].[scpWeightHistory] 
        WHERE CAST(CreatedAt AS DATE) = @TargetDate";

            var approvedItemsCount = await connection.QuerySingleAsync<int>(approvedItemsQuery);
            var readyLocationsCount = await connection.QuerySingleAsync<int>(readyLocationsQuery);
            var departmentsCount = await connection.QuerySingleAsync<int>(departmentsQuery);
            var totalWeightForDate = await connection.QuerySingleAsync<decimal>(dayWeightQuery, new { TargetDate = targetDate });

            return Json(new
            {
                success = true,
                approvedItemsCount,
                readyLocationsCount,
                departmentsCount,
                totalWeightToday = Math.Round(totalWeightForDate, 2),
                statsDate = targetDate.ToString("yyyy-MM-dd")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting approval statistics");
            return Json(new { success = false, message = "Error retrieving statistics" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetLocationsWithApprovedScrap(string department = null)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
            {
                return Json(new { success = false, message = "Access denied" });
            }

            _logger.LogInformation($"GetLocationsWithApprovedScrap called with department: '{department}'");

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();

            // Enhanced query that excludes locations where weight has been recorded after the latest approval
            string query = @"
            WITH LatestApprovals AS (
                SELECT 
                    s.Department,
                    s.Line,
                    s.Shift,
                    COUNT(*) as ApprovedItemCount,
                    MAX(s.ApprovalDate) as LatestApprovalDate,
                    MAX(s.ModifiedAt) as LatestModifiedDate
                FROM [IntegrationCompanyB].[dbo].[scpScrapData] s
                WHERE s.Status = 'Approved'
                GROUP BY s.Department, s.Line, s.Shift
            ),
            LatestWeights AS (
                SELECT 
                    wh.Department,
                    wh.Line,
                    wh.Shift,
                    MAX(wh.CreatedAt) as LatestWeightDate
                FROM [IntegrationCompanyB].[dbo].[scpWeightHistory] wh
                GROUP BY wh.Department, wh.Line, wh.Shift
            )
            SELECT 
                la.Department,
                la.Line,
                la.Shift,
                la.ApprovedItemCount,
                CASE 
                    WHEN lw.LatestWeightDate IS NULL THEN 'Never'
                    ELSE CONVERT(varchar, lw.LatestWeightDate, 120)
                END as LastWeighedDate,
                la.ApprovedItemCount as UnweighedScrapCount,
                1 as ShouldAutoSelect
            FROM LatestApprovals la
            LEFT JOIN LatestWeights lw ON la.Department = lw.Department 
                                      AND la.Line = lw.Line 
                                      AND la.Shift = lw.Shift
            WHERE (
                -- Show locations that have never been weighed
                lw.LatestWeightDate IS NULL
                OR 
                -- Show locations where new approvals came after the last weighing
                COALESCE(la.LatestApprovalDate, la.LatestModifiedDate) > lw.LatestWeightDate
            )";

            var parameters = new DynamicParameters();

            // Add department filter if specified
            if (!string.IsNullOrEmpty(department) && department != "All Departments")
            {
                query += " AND la.Department = @Department";
                parameters.Add("Department", department);
                _logger.LogInformation($"Filtering by department: {department}");
            }

            query += " ORDER BY la.Department, la.Line, la.Shift";

            _logger.LogInformation($"Executing enhanced query with weight logic");

            var locations = await connection.QueryAsync(query, parameters);

            _logger.LogInformation($"Query returned {locations.Count()} locations ready for weighing");

            // Convert to the expected format
            var result = locations.Select(loc => new
            {
                department = loc.Department?.ToString() ?? "",
                line = loc.Line?.ToString() ?? "",
                shift = loc.Shift?.ToString() ?? "",
                approvedItemCount = (int)(loc.ApprovedItemCount ?? 0),
                unweighedScrapCount = (int)(loc.UnweighedScrapCount ?? 0),
                lastWeighedDate = loc.LastWeighedDate?.ToString() ?? "Never",
                shouldAutoSelect = (int)(loc.ShouldAutoSelect ?? 0) == 1
            }).ToList();

            _logger.LogInformation($"Returning {result.Count} locations that need weighing");

            // Log each location for debugging
            foreach (var loc in result)
            {
                _logger.LogInformation($"Ready for weighing: {loc.department}/{loc.line}/{loc.shift} - {loc.approvedItemCount} approved items (Last weighed: {loc.lastWeighedDate})");
            }

            return Json(new { success = true, locations = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting locations with approved scrap");
            return Json(new
            {
                success = false,
                message = "Error retrieving locations: " + ex.Message,
                error = ex.ToString()
            });
        }
    }


    [HttpGet]
    public async Task<IActionResult> GetFamilyWeightSummary()
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
                return Json(new { success = false, message = "Access denied" });

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            // Operator-captured weight, recorded at submission time — regardless of approval status
            const string lineQuery = @"
            SELECT
                Department,
                COUNT(DISTINCT Line + '|' + Shift) AS LinesReporting,
                SUM(Weight) AS TotalWeight
            FROM [IntegrationCompanyB].[dbo].[scpScrapData]
            WHERE Weight IS NOT NULL AND Weight > 0
            GROUP BY Department";

            // Approved-item count stays a separate, informational figure
            const string deptQuery = @"
            SELECT
                Department,
                COUNT(*) AS TotalItemCount,
                SUM(CASE WHEN Status = 'Approved' THEN 1 ELSE 0 END) AS ApprovedItemCount
            FROM [IntegrationCompanyB].[dbo].[scpScrapData]
            GROUP BY Department";

            // Admin family-level totals — unchanged, still the sentinel-based bulk entries
            const string familyQuery = @"
            SELECT Department, SUM(Weight) AS TotalWeight, MAX(CreatedAt) AS LastRecorded
            FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
            WHERE Line = 'ALL LINES' AND Shift = 'ALL'
            GROUP BY Department";

            const string lastByQuery = @"
            SELECT TOP 1 CreatedBy
            FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
            WHERE Department = @Department AND Line = 'ALL LINES' AND Shift = 'ALL'
            ORDER BY CreatedAt DESC";

            var lineRows = (await connection.QueryAsync(lineQuery)).ToDictionary(r => (string)r.Department, r => r);
            var familyRows = (await connection.QueryAsync(familyQuery)).ToDictionary(r => (string)r.Department, r => r);
            var deptRows = await connection.QueryAsync(deptQuery);

            var result = new List<object>();
            foreach (var d in deptRows)
            {
                string dept = d.Department;
                decimal lineTotal = lineRows.TryGetValue(dept, out var lr) ? (decimal)(lr.TotalWeight ?? 0m) : 0m;
                int linesReporting = lineRows.TryGetValue(dept, out var lr2) ? (int)(lr2.LinesReporting ?? 0) : 0;
                decimal familyTotal = familyRows.TryGetValue(dept, out var fr) ? (decimal)(fr.TotalWeight ?? 0m) : 0m;
                DateTime? lastDate = familyRows.TryGetValue(dept, out var fr2) ? (DateTime?)fr2.LastRecorded : null;

                string lastBy = "";
                if (lastDate.HasValue)
                    lastBy = await connection.QueryFirstOrDefaultAsync<string>(lastByQuery, new { Department = dept }) ?? "";

                result.Add(new
                {
                    department = dept,
                    approvedItemCount = (int)d.ApprovedItemCount,
                    totalItemCount = (int)d.TotalItemCount,
                    lineTotalKg = lineTotal,
                    linesReporting,
                    familyTotalKg = familyTotal,
                    varianceKg = familyTotal - lineTotal,
                    lastFamilyRecordedBy = lastBy,
                    lastFamilyRecordedDate = lastDate
                });
            }

            return Json(new { success = true, data = result.OrderBy(r => ((dynamic)r).department).ToList() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting family weight summary");
            return Json(new { success = false, message = "Error retrieving family weight summary" });
        }
    }

    // Add to ScrapApprovalController.cs

    public class SaveFamilyWeightRequest
    {
        public string Department { get; set; } = "";
        public decimal Weight { get; set; }
        public string Description { get; set; } = "";
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveFamilyWeight([FromBody] SaveFamilyWeightRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
                return Json(new { success = false, message = "Access denied" });

            if (string.IsNullOrWhiteSpace(request.Department))
                return Json(new { success = false, message = "Department is required" });

            if (request.Weight <= 0)
                return Json(new { success = false, message = "Enter a valid weight greater than zero" });

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();

            const string insertQuery = @"
    INSERT INTO [IntegrationCompanyB].[dbo].[scpWeightHistory]
    (WeightId, Department, Line, Shift, Weight, Description, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy, IsBulkWeighing)
    VALUES
    (NEXT VALUE FOR [IntegrationCompanyB].[dbo].[seq_WeightHistoryId],
     @Department, 'ALL LINES', 'ALL', @Weight, @Description, GETDATE(), @CreatedBy, GETDATE(), @CreatedBy, 1)";

            await connection.ExecuteAsync(insertQuery, new
            {
                Department = request.Department,   // ✅ kept as-is — see note below
                Weight = request.Weight,
                Description = string.IsNullOrWhiteSpace(request.Description)
                    ? $"Family weight recorded by {user.ClockNumber}"
                    : request.Description,
                CreatedBy = user.ClockNumber
            });

            return Json(new { success = true, message = $"Saved {request.Weight}kg for {request.Department}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving family weight");
            return Json(new { success = false, message = "Error saving family weight" });
        }
    }

    public class SaveBinWeightRequest
    {
        public string Department { get; set; } = "";
        public int BinNumber { get; set; }
        public decimal Weight { get; set; }
        public string Month { get; set; } = "";
        public string Description { get; set; } = "";
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBinWeight([FromBody] SaveBinWeightRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
                return Json(new { success = false, message = "Access denied" });

            if (string.IsNullOrWhiteSpace(request.Department) || request.BinNumber <= 0 || request.Weight <= 0)
                return Json(new { success = false, message = "Department, bin number, and a weight greater than zero are required" });

            if (!DateTime.TryParseExact(request.Month + "-01", "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out var recordMonth))
                return Json(new { success = false, message = "Invalid month" });

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();

            const string insertSql = @"
    INSERT INTO [IntegrationCompanyB].[dbo].[scpWeightHistory]
    (WeightId, Department, Line, Shift, Weight, Description, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy, IsBulkWeighing, BinNumber, RecordMonth)
    VALUES
    (NEXT VALUE FOR [IntegrationCompanyB].[dbo].[seq_WeightHistoryId],
     @Department, @Line, 'MONTHLY', @Weight, @Description, GETDATE(), @CreatedBy, GETDATE(), @CreatedBy, 1, @BinNumber, @RecordMonth)";

            await connection.ExecuteAsync(insertSql, new
            {
                Department = request.Department,   // ✅ kept as-is — see note below
                Line = $"BIN {request.BinNumber}",
                Weight = request.Weight,
                Description = string.IsNullOrWhiteSpace(request.Description)
                    ? $"Bin {request.BinNumber} recorded by {user.ClockNumber} for {request.Month}"
                    : request.Description,
                CreatedBy = user.ClockNumber,
                BinNumber = request.BinNumber,
                RecordMonth = recordMonth
            });

            return Json(new { success = true, message = $"Saved Bin {request.BinNumber}: {request.Weight}kg for {request.Department} ({request.Month})" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving bin weight");
            return Json(new { success = false, message = "Error saving bin weight" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetBinRecordingConfig(string plant)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
                return Json(new { success = false, message = "Access denied" });

            if (plant != "Plant A" && plant != "Plant B")
                return Json(new { success = false, message = "Invalid plant" });

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            var binsRaw = await connection.QueryAsync(
                "SELECT BinId, BinNumber, TareWeightKg FROM scpBin WHERE Plant = @Plant AND IsActive = 1 ORDER BY BinNumber",
                new { Plant = plant });

            var categoriesRaw = await connection.QueryAsync(
                "SELECT CategoryId, CategoryName, ParentCategory, DisplayOrder FROM scpScrapCategory WHERE Plant = @Plant AND IsActive = 1 ORDER BY DisplayOrder",
                new { Plant = plant });

            // ✅ explicit camelCase projection — matches every other endpoint in this app
            var bins = binsRaw.Select(b => new {
                binId = (int)b.BinId,
                binNumber = (int)b.BinNumber,
                tareWeightKg = (decimal)b.TareWeightKg
            }).ToList();

            var categories = categoriesRaw.Select(c => new {
                categoryId = (int)c.CategoryId,
                categoryName = (string)c.CategoryName,
                parentCategory = (string?)c.ParentCategory,
                displayOrder = (int)c.DisplayOrder
            }).ToList();

            return Json(new { success = true, bins, categories });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bin recording config");
            return Json(new { success = false, message = "Error loading configuration" });
        }
    }

    public class SaveBinCategoryWeightRequest
    {
        public string Plant { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public int BinNumber { get; set; }
        public decimal TareWeightKg { get; set; }
        public decimal GrossWeightKg { get; set; }
        public string Description { get; set; } = "";
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBinCategoryWeight([FromBody] SaveBinCategoryWeightRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
                return Json(new { success = false, message = "Access denied" });

            if (string.IsNullOrWhiteSpace(request.Plant) || string.IsNullOrWhiteSpace(request.CategoryName)
                || request.BinNumber <= 0 || request.TareWeightKg < 0 || request.GrossWeightKg <= 0)
                return Json(new { success = false, message = "Plant, category, bin number, tare weight, and a gross weight greater than zero are required" });

            decimal net = request.GrossWeightKg - request.TareWeightKg;

            if (net <= 0)
                return Json(new
                {
                    success = false,
                    message = $"Net weight must be greater than zero — gross ({request.GrossWeightKg}kg) is not more than the entered tare weight ({request.TareWeightKg}kg)"
                });

            if (request.Plant != "Plant A" && request.Plant != "Plant B")
                return Json(new { success = false, message = "Invalid plant" });

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();

            // Category must be an active category for this plant
            var categoryExists = await connection.ExecuteScalarAsync<int>(@"
        SELECT COUNT(*) FROM [IntegrationCompanyB].[dbo].[scpScrapCategory]
        WHERE Plant = @Plant AND CategoryName = @CategoryName AND IsActive = 1",
                new { request.Plant, request.CategoryName });

            if (categoryExists == 0)
                return Json(new { success = false, message = $"'{request.CategoryName}' is not an active category for {request.Plant}" });

            // If bins are configured for this plant, the bin must be one of them
            var configuredBins = (await connection.QueryAsync<int>(@"
        SELECT BinNumber FROM [IntegrationCompanyB].[dbo].[scpBin]
        WHERE Plant = @Plant AND IsActive = 1",
                new { request.Plant })).ToList();

            if (configuredBins.Count > 0 && !configuredBins.Contains(request.BinNumber))
                return Json(new { success = false, message = $"Bin {request.BinNumber} is not an active bin for {request.Plant}" });

            // Uses the same ID sequence as operator weighings (MAX+1 here could collide with it)
            const string insertSql = @"
        INSERT INTO [IntegrationCompanyB].[dbo].[scpWeightHistory]
        (WeightId, Department, Line, Shift, Weight, Description, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy,
         IsBulkWeighing, Plant, CategoryName, BinNumber, GrossWeightKg, TareWeightKg)
        VALUES
        (NEXT VALUE FOR [IntegrationCompanyB].[dbo].[seq_WeightHistoryId],
         @Department, @Line, 'BULK-BIN', @Weight, @Description, GETDATE(), @CreatedBy, GETDATE(), @CreatedBy,
         1, @Plant, @CategoryName, @BinNumber, @GrossWeightKg, @TareWeightKg)";

            await connection.ExecuteAsync(insertSql, new
            {
                Department = request.Plant,
                Line = $"Bin {request.BinNumber}",
                Weight = net,
                Description = string.IsNullOrWhiteSpace(request.Description)
                    ? $"{request.CategoryName} (Bin {request.BinNumber}) recorded by {user.ClockNumber}"
                    : request.Description,
                CreatedBy = user.ClockNumber,
                Plant = request.Plant,
                CategoryName = request.CategoryName,
                BinNumber = request.BinNumber,
                GrossWeightKg = request.GrossWeightKg,
                TareWeightKg = request.TareWeightKg
            });

            _logger.LogInformation("⚖️ Bin weight saved: {Plant} / {Category} / Bin {Bin} — {Net}kg net by {User}",
                request.Plant, request.CategoryName, request.BinNumber, net, user.ClockNumber);

            return Json(new
            {
                success = true,
                message = $"{request.CategoryName}: {net:0.##}kg net (Bin {request.BinNumber}, {request.GrossWeightKg}kg gross − {request.TareWeightKg}kg tare)"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving bin category weight");
            return Json(new { success = false, message = "Error saving weight" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetBinCategoryHistory(string plant, int days = 30)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
                return Json(new { success = false, message = "Access denied" });

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            var historyRaw = await connection.QueryAsync(@"
SELECT CategoryName, Line, Weight AS NetWeightKg, GrossWeightKg, TareWeightKg, CreatedBy, CreatedAt, Description
FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
WHERE Plant = @Plant AND Shift = 'BULK-BIN'
  AND CreatedAt >= DATEADD(DAY, -@Days, GETDATE())
ORDER BY CreatedAt DESC",
                new { Plant = plant, Days = days });

            var history = historyRaw.Select(h => new {
                categoryName = (string)h.CategoryName,
                binLabel = (string)h.Line,
                netWeightKg = (decimal)h.NetWeightKg,
                grossWeightKg = (decimal)h.GrossWeightKg,
                tareWeightKg = (decimal)h.TareWeightKg,
                createdBy = (string)h.CreatedBy,
                createdAt = (DateTime)h.CreatedAt,
                description = (string?)h.Description
            }).ToList();

            return Json(new { success = true, history });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bin category history");
            return Json(new { success = false, message = "Error retrieving history" });
        }
    }

    // ENHANCED SaveBulkWeight method that properly records weights and refreshes the UI
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBulkWeight([FromBody] BulkWeightSaveRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
            {
                return Json(new { success = false, message = "Access denied" });
            }

            if (request.Locations == null || !request.Locations.Any())
            {
                return Json(new { success = false, message = "No locations specified" });
            }

            if (request.Weight <= 0)
            {
                return Json(new { success = false, message = "Invalid weight value" });
            }

            _logger.LogInformation($"SaveBulkWeight called by {user.ClockNumber} for {request.Locations.Count} locations, weight: {request.Weight}kg");

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();

            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                int recordsCreated = 0;
                var currentTime = DateTime.Now;
                var securityStamp = Guid.NewGuid().ToString();

                foreach (var location in request.Locations)
                {
                    // Verify there are approved items for this location
                    const string verifyQuery = @"
                    SELECT COUNT(*) 
                    FROM [IntegrationCompanyB].[dbo].[scpScrapData] 
                    WHERE Department = @Department 
                        AND Line = @Line 
                        AND Shift = @Shift 
                        AND Status = 'Approved'";

                    var approvedCount = await connection.QuerySingleAsync<int>(
                        verifyQuery,
                        new
                        {
                            Department = location.Department,
                            Line = location.Line,
                            Shift = location.Shift
                        },
                        transaction);

                    if (approvedCount > 0)
                    {
                        // Insert weight record for this location
                        const string insertQuery = @"
                        INSERT INTO [IntegrationCompanyB].[dbo].[scpWeightHistory]
                        (WeightId, Department, Line, Shift, Weight, Description, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy, SecurityStamp, IsBulkWeighing)
                        VALUES (NEXT VALUE FOR [IntegrationCompanyB].[dbo].[seq_WeightHistoryId],
                                @Department, @Line, @Shift, @Weight, @Description, @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy, @SecurityStamp, 1)";

                        await connection.ExecuteAsync(
                            insertQuery,
                            new
                            {
                                Department = location.Department,
                                Line = location.Line,
                                Shift = location.Shift,
                                Weight = request.Weight,
                                Description = request.Description ?? $"Bulk weight for {location.Department}/{location.Line}/{location.Shift} ({approvedCount} approved items)",
                                CreatedAt = currentTime,
                                CreatedBy = user.ClockNumber,
                                ModifiedAt = currentTime,
                                ModifiedBy = user.ClockNumber,
                                SecurityStamp = securityStamp
                            },
                            transaction);

                        recordsCreated++;

                        _logger.LogInformation($"Weight recorded for {location.Department}/{location.Line}/{location.Shift}: {request.Weight}kg ({approvedCount} items)");
                    }
                }

                await transaction.CommitAsync();

                // Log activity
                await _activityLogService.LogActivityAsync(
                    user.ClockNumber,
                    "Bulk Weight Record",
                    $"Recorded bulk weight {request.Weight}kg for {recordsCreated} locations",
                    "Success"
                );

                _logger.LogInformation($"Bulk weight save completed: {recordsCreated} locations processed");

                return Json(new
                {
                    success = true,
                    message = $"Successfully recorded weight for {recordsCreated} location(s). These locations will now be removed from the weighing list.",
                    recordsCreated,
                    weightRecorded = request.Weight,
                    locationsProcessed = recordsCreated
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Transaction error in SaveBulkWeight");
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving bulk weight");
            return Json(new { success = false, message = "Error saving weight records: " + ex.Message });
        }
    }

    public class SaveBinSaleRequest
    {
        public string Plant { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public decimal WeightSoldKg { get; set; }
        public string Buyer { get; set; } = "";
        public string Reference { get; set; } = "";
        public string SaleDate { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBinSale([FromBody] SaveBinSaleRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
                return Json(new { success = false, message = "Access denied" });

            if (string.IsNullOrWhiteSpace(request.Plant) || string.IsNullOrWhiteSpace(request.CategoryName)
                || request.WeightSoldKg <= 0)
                return Json(new { success = false, message = "Plant, category, and a weight sold greater than zero are required" });

            if (request.Plant != "Plant A" && request.Plant != "Plant B")
                return Json(new { success = false, message = "Invalid plant" });

            if (!DateTime.TryParse(request.SaleDate, out var saleDate))
                return Json(new { success = false, message = "Invalid sale date" });

            if (saleDate.Date > DateTime.Today)
                return Json(new { success = false, message = "Sale date cannot be in the future" });

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();

            const string insertSql = @"
        INSERT INTO [IntegrationCompanyB].[dbo].[scpBinSale]
        (Plant, CategoryName, WeightSoldKg, Buyer, Reference, SaleDate, Notes, CreatedBy, CreatedAt)
        VALUES
        (@Plant, @CategoryName, @WeightSoldKg, @Buyer, @Reference, @SaleDate, @Notes, @CreatedBy, GETDATE())";

            await connection.ExecuteAsync(insertSql, new
            {
                request.Plant,
                request.CategoryName,
                request.WeightSoldKg,
                Buyer = string.IsNullOrWhiteSpace(request.Buyer) ? (object)DBNull.Value : request.Buyer,
                Reference = string.IsNullOrWhiteSpace(request.Reference) ? (object)DBNull.Value : request.Reference,
                SaleDate = saleDate.Date,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? (object)DBNull.Value : request.Notes,
                CreatedBy = user.ClockNumber
            });

            return Json(new
            {
                success = true,
                message = $"Recorded sale: {request.WeightSoldKg:0.##}kg of {request.CategoryName}" +
                          (string.IsNullOrWhiteSpace(request.Buyer) ? "" : $" to {request.Buyer}")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving bin sale");
            return Json(new { success = false, message = "Error saving sale" });
        }
    }

    // On-hand balance per category (all-time weighed − sold) and the plant's recent sales.
    [HttpGet]
    public async Task<IActionResult> GetBinSaleSummary(string plant, int salesDays = 90)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
                return Json(new { success = false, message = "Access denied" });

            if (plant != "Plant A" && plant != "Plant B")
                return Json(new { success = false, message = "Invalid plant" });

            salesDays = Math.Clamp(salesDays, 1, 366);

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            var balancesRaw = await connection.QueryAsync(@"
WITH cats AS (
    SELECT CategoryName, MIN(DisplayOrder) AS SortOrder
    FROM (
        SELECT CategoryName, DisplayOrder FROM [IntegrationCompanyB].[dbo].[scpScrapCategory]
        WHERE Plant = @Plant AND IsActive = 1
        UNION ALL
        SELECT CategoryName, 9999 FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
        WHERE Plant = @Plant AND Shift = 'BULK-BIN' AND CategoryName IS NOT NULL
        UNION ALL
        SELECT CategoryName, 9999 FROM [IntegrationCompanyB].[dbo].[scpBinSale]
        WHERE Plant = @Plant
    ) x
    GROUP BY CategoryName
)
SELECT c.CategoryName,
       ISNULL((SELECT SUM(CAST(w.Weight AS decimal(18,3)))
               FROM [IntegrationCompanyB].[dbo].[scpWeightHistory] w
               WHERE w.Plant = @Plant AND w.Shift = 'BULK-BIN' AND w.CategoryName = c.CategoryName), 0) AS TotalWeighedKg,
       ISNULL((SELECT SUM(CAST(s.WeightSoldKg AS decimal(18,3)))
               FROM [IntegrationCompanyB].[dbo].[scpBinSale] s
               WHERE s.Plant = @Plant AND s.CategoryName = c.CategoryName), 0) AS TotalSoldKg
FROM cats c
ORDER BY c.SortOrder, c.CategoryName",
                new { Plant = plant });

            var balances = balancesRaw.Select(b =>
            {
                decimal weighed = (decimal)b.TotalWeighedKg;
                decimal sold = (decimal)b.TotalSoldKg;
                return new
                {
                    categoryName = (string)b.CategoryName,
                    totalWeighedKg = weighed,
                    totalSoldKg = sold,
                    onHandKg = weighed - sold
                };
            }).ToList();

            var salesRaw = await connection.QueryAsync(@"
SELECT TOP 100 CategoryName, CAST(WeightSoldKg AS decimal(18,3)) AS WeightSoldKg,
       Buyer, Reference, SaleDate, Notes, CreatedBy
FROM [IntegrationCompanyB].[dbo].[scpBinSale]
WHERE Plant = @Plant AND SaleDate >= DATEADD(DAY, -@Days, CAST(GETDATE() AS date))
ORDER BY SaleDate DESC, CreatedAt DESC",
                new { Plant = plant, Days = salesDays });

            var sales = salesRaw.Select(s => new
            {
                categoryName = (string)s.CategoryName,
                weightSoldKg = (decimal)s.WeightSoldKg,
                buyer = (string?)s.Buyer ?? "",
                reference = (string?)s.Reference ?? "",
                saleDate = ((DateTime)s.SaleDate).ToString("yyyy-MM-dd"),
                notes = (string?)s.Notes ?? "",
                createdBy = (string?)s.CreatedBy ?? ""
            }).ToList();

            return Json(new { success = true, balances, sales });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bin sale summary");
            return Json(new { success = false, message = "Error loading bin sale summary" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetPlantDailyConfig(string plant, string date)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
                return Json(new { success = false, message = "Access denied" });

            if (!DateTime.TryParse(date, out var recordDate))
                return Json(new { success = false, message = "Invalid date" });

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            var deptsRaw = await connection.QueryAsync(
                "SELECT DepartmentName, DisplayOrder FROM scpPlantDepartment WHERE Plant = @Plant ORDER BY DisplayOrder",
                new { Plant = plant });

            var existingRaw = await connection.QueryAsync(@"
            SELECT Department, Weight FROM scpWeightHistory
            WHERE Plant = @Plant AND Shift = 'DAILY' AND RecordDate = @RecordDate",
                new { Plant = plant, RecordDate = recordDate.Date });

            var existingWeights = existingRaw.ToDictionary(r => (string)r.Department, r => (decimal)r.Weight);

            var noteRaw = await connection.QueryFirstOrDefaultAsync(
                "SELECT Notes FROM scpDailyReportNote WHERE Plant = @Plant AND RecordDate = @RecordDate",
                new { Plant = plant, RecordDate = recordDate.Date });

            var departments = deptsRaw.Select(d => new {
                departmentName = (string)d.DepartmentName,
                existingWeight = existingWeights.TryGetValue((string)d.DepartmentName, out var w) ? w : (decimal?)null
            }).ToList();

            return Json(new
            {
                success = true,
                departments,
                existingNote = noteRaw?.Notes as string ?? ""
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting plant daily config");
            return Json(new { success = false, message = "Error loading daily config" });
        }
    }

    public class SaveDailyDepartmentWeightRequest
    {
        public string Plant { get; set; } = "";
        public string Department { get; set; } = "";
        public string Date { get; set; } = "";
        public decimal Weight { get; set; }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDailyDepartmentWeight([FromBody] SaveDailyDepartmentWeightRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
                return Json(new { success = false, message = "Access denied" });

            if (string.IsNullOrWhiteSpace(request.Department) || request.Weight <= 0)
                return Json(new { success = false, message = "Department and a weight greater than zero are required" });

            if (!DateTime.TryParse(request.Date, out var recordDate))
                return Json(new { success = false, message = "Invalid date" });

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();

            // One entry per department per day — replace if already recorded today, rather than duplicating
            await connection.ExecuteAsync(@"
            DELETE FROM scpWeightHistory
            WHERE Plant = @Plant AND Department = @Department AND Shift = 'DAILY' AND RecordDate = @RecordDate",
                new { request.Plant, request.Department, RecordDate = recordDate.Date });

            const string insertSql = @"
            INSERT INTO [IntegrationCompanyB].[dbo].[scpWeightHistory]
            (WeightId, Department, Line, Shift, Weight, Description, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy,
             IsBulkWeighing, Plant, RecordDate)
            VALUES
            (NEXT VALUE FOR [IntegrationCompanyB].[dbo].[seq_WeightHistoryId],
             @Department, 'DAILY-TOTAL', 'DAILY', @Weight, @Description, GETDATE(), @CreatedBy, GETDATE(), @CreatedBy,
             1, @Plant, @RecordDate)";

            await connection.ExecuteAsync(insertSql, new
            {
                request.Department,
                Weight = request.Weight,
                Description = $"Daily weight for {request.Date} recorded by {user.ClockNumber}",
                CreatedBy = user.ClockNumber,
                request.Plant,
                RecordDate = recordDate.Date
            });

            return Json(new { success = true, message = $"{request.Department}: {request.Weight}kg saved for {request.Date}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving daily department weight");
            return Json(new { success = false, message = "Error saving weight" });
        }
    }

    public class SaveDailyReportNoteRequest
    {
        public string Plant { get; set; } = "";
        public string Date { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDailyReportNote([FromBody] SaveDailyReportNoteRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
                return Json(new { success = false, message = "Access denied" });

            if (!DateTime.TryParse(request.Date, out var recordDate))
                return Json(new { success = false, message = "Invalid date" });

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();

            var existing = await connection.QueryFirstOrDefaultAsync(
                "SELECT NoteId FROM scpDailyReportNote WHERE Plant = @Plant AND RecordDate = @RecordDate",
                new { request.Plant, RecordDate = recordDate.Date });

            if (existing != null)
            {
                await connection.ExecuteAsync(
                    "UPDATE scpDailyReportNote SET Notes = @Notes, ModifiedAt = GETDATE(), ModifiedBy = @ClockNumber WHERE NoteId = @NoteId",
                    new { request.Notes, ClockNumber = user.ClockNumber, NoteId = (int)existing.NoteId });
            }
            else
            {
                await connection.ExecuteAsync(@"
                INSERT INTO scpDailyReportNote (NoteId, Plant, RecordDate, Notes, CreatedBy, CreatedAt)
                VALUES ((SELECT ISNULL(MAX(NoteId),0)+1 FROM scpDailyReportNote), @Plant, @RecordDate, @Notes, @ClockNumber, GETDATE())",
                    new { request.Plant, RecordDate = recordDate.Date, request.Notes, ClockNumber = user.ClockNumber });
            }

            return Json(new { success = true, message = "Note saved" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving daily report note");
            return Json(new { success = false, message = "Error saving note" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetDailyReport(string plant, string date)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
                return Json(new { success = false, message = "Access denied" });

            if (!DateTime.TryParse(date, out var recordDate))
                return Json(new { success = false, message = "Invalid date" });

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            var weightsRaw = await connection.QueryAsync(@"
            SELECT Department, Weight, CreatedBy, CreatedAt
            FROM scpWeightHistory
            WHERE Plant = @Plant AND Shift = 'DAILY' AND RecordDate = @RecordDate
            ORDER BY Department",
                new { Plant = plant, RecordDate = recordDate.Date });

            var noteRaw = await connection.QueryFirstOrDefaultAsync(
                "SELECT Notes, CreatedBy, ModifiedAt FROM scpDailyReportNote WHERE Plant = @Plant AND RecordDate = @RecordDate",
                new { Plant = plant, RecordDate = recordDate.Date });

            var weights = weightsRaw.Select(w => new {
                department = (string)w.Department,
                weightKg = (decimal)w.Weight,
                recordedBy = (string)w.CreatedBy,
                recordedAt = (DateTime)w.CreatedAt
            }).ToList();

            return Json(new
            {
                success = true,
                plant,
                date,
                weights,
                totalKg = weights.Sum(w => w.weightKg),
                note = noteRaw?.Notes as string ?? "",
                noteRecordedBy = noteRaw?.CreatedBy as string ?? ""
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting daily report");
            return Json(new { success = false, message = "Error retrieving report" });
        }
    }

    // Request model for bulk weight saving
    public class BulkWeightSaveRequest
    {
        public List<BulkWeightLocation> Locations { get; set; } = new List<BulkWeightLocation>();
        public decimal Weight { get; set; }
        public string Description { get; set; } = "";
    }

    public class BulkWeightLocation
    {
        public string Department { get; set; } = "";
        public string Line { get; set; } = "";
        public string Shift { get; set; } = "";
    }

    // ADD THIS API ENDPOINT FOR LOADING APPROVED LOCATIONS (if not already exists)
    [HttpGet]
    public async Task<IActionResult> LoadApprovedLocations()
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
            {
                return Json(new { success = false, message = "Access denied" });
            }

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            const string query = @"
            SELECT DISTINCT 
                s.Department,
                s.Line,
                s.Shift,
                COUNT(*) as ApprovedCount,
                SUM(COALESCE(im.MaterialCost, 0) * s.Quantity) as TotalCost
                            FROM [IntegrationCompanyB].[dbo].[scpScrapData] s
                LEFT JOIN [SysproCompanyB].[dbo].[InvMaster] im 
                    ON s.PartNumber = im.StockCode COLLATE Latin1_General_CI_AS
                   AND im.StockCode COLLATE Latin1_General_BIN NOT IN ('EBD0532a', 'AVf30B/R', ' 4KB-927680-501')
                WHERE s.Status = 'Approved'
            GROUP BY s.Department, s.Line, s.Shift
            HAVING COUNT(*) > 0
            ORDER BY s.Department, s.Line, s.Shift";

            var locations = await connection.QueryAsync(query);

            return Json(new { success = true, locations });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading approved locations");
            return Json(new { success = false, message = "Error retrieving locations" });
        }
    }

    // NEW METHOD: Get all users (for admin/senior manager)
    [HttpGet]
    public async Task<IActionResult> GetAllUsers()
    {
        try
        {
            var currentUser = await GetCurrentUserAsync();
            if (!CanUserViewAllData(currentUser.UserType))
            {
                return Json(new { success = false, message = "Access denied" });
            }

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            const string query = @"
            SELECT 
                UserId,
                ClockNumber,
                UserName,
                Email,
                UserType,
                Department,
                Line,
                Shift,
                IsActive,
                CreatedAt,
                CreatedBy,
                LastModifiedAt,
                LastModifiedBy,
                PasswordResetRequired,
                LastPasswordChange,
                LockoutEnd,
                AccessFailedCount
            FROM [IntegrationCompanyB].[dbo].[scpUser]
            ORDER BY CreatedAt DESC";

            var users = await connection.QueryAsync(query);

            return Json(new
            {
                success = true,
                data = users,
                totalUsers = users.Count()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all users");
            return Json(new { success = false, message = "Error retrieving users" });
        }
    }

    // NEW METHOD: Get pending registration requests
    [HttpGet]
    public async Task<IActionResult> GetPendingRequests()
    {
        try
        {
            var currentUser = await GetCurrentUserAsync();
            if (!CanUserViewAllData(currentUser.UserType))
            {
                return Json(new { success = false, message = "Access denied" });
            }

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            const string query = @"
            SELECT 
                r.RequestId,
                r.ClockNumber,
                r.Name,
                r.Surname,
                r.Email,
                r.UserType,
                r.Shift,
                r.Status,
                r.RequestDate,
                r.ProcessedBy,
                r.ProcessedDate,
                d.DepartmentName,
                l.LineName
            FROM [IntegrationCompanyB].[dbo].[scpRequest] r
            LEFT JOIN [IntegrationCompanyB].[dbo].[scpDepartment] d ON r.DepartmentId = d.DepartmentId
            LEFT JOIN [IntegrationCompanyB].[dbo].[scpLine] l ON r.LineId = l.LineId
            WHERE r.Status = 'Pending'
            ORDER BY r.RequestDate DESC";

            var requests = await connection.QueryAsync(query);

            return Json(new
            {
                success = true,
                data = requests,
                totalRequests = requests.Count()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending requests");
            return Json(new { success = false, message = "Error retrieving requests" });
        }
    }



    // NEW METHOD: Get users by location (for team leaders and group leaders)
    [HttpGet]
    public async Task<IActionResult> GetLocationUsers()
    {
        try
        {
            var currentUser = await GetCurrentUserAsync();
            var userLevel = GetUserApprovalLevel(currentUser.UserType);

            if (userLevel < 1)
            {
                return Json(new { success = false, message = "Access denied" });
            }

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            string query;
            object parameters;

            if (CanUserViewAllData(currentUser.UserType))
            {
                // Admin can see all users
                query = @"
                SELECT 
                    UserId,
                    ClockNumber,
                    UserName,
                    UserType,
                    Department,
                    Line,
                    Shift,
                    IsActive,
                    CreatedAt,
                    LastPasswordChange
                FROM [IntegrationCompanyB].[dbo].[scpUser]
                WHERE IsActive = 1
                ORDER BY Department, Line, Shift, UserType, ClockNumber";
                parameters = new { };
            }
            else
            {
                // Non-admin can only see users in their department/line
                query = @"
                SELECT 
                    UserId,
                    ClockNumber,
                    UserName,
                    UserType,
                    Department,
                    Line,
                    Shift,
                    IsActive,
                    CreatedAt,
                    LastPasswordChange
                FROM [IntegrationCompanyB].[dbo].[scpUser]
                WHERE IsActive = 1
                    AND Department = @Department 
                    AND Line = @Line
                ORDER BY Shift, UserType, ClockNumber";
                parameters = new
                {
                    Department = currentUser.Department,
                    Line = currentUser.Line
                };
            }

            var users = await connection.QueryAsync(query, parameters);

            return Json(new
            {
                success = true,
                data = users,
                location = $"{currentUser.Department}/{currentUser.Line}",
                canViewAll = CanUserViewAllData(currentUser.UserType)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting location users");
            return Json(new { success = false, message = "Error retrieving users" });
        }
    }

    // Helper method to generate temporary passwords
    private string GenerateTemporaryPassword()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 8)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    // FIXED WEIGHT METHODS FOR GUID SecurityStamp - Replace your existing methods:

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveWeight([FromBody] SaveWeightRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null || user.UserType?.ToLower() != "teamleader")
            {
                return Json(new { success = false, message = "Access denied" });
            }

            if (request.Department != user.Department ||
                request.Line != user.Line ||
                request.Shift != user.Shift)
            {
                return Json(new { success = false, message = "Can only record weight for your assigned location" });
            }

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();

            const string insertQuery = @"
    INSERT INTO [IntegrationCompanyB].[dbo].[scpWeightHistory]
    (WeightId, Department, Line, Shift, Weight, Description, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy, IsBulkWeighing)
    VALUES (
        NEXT VALUE FOR [IntegrationCompanyB].[dbo].[seq_WeightHistoryId],
        @Department, @Line, @Shift, @Weight, @Description, GETDATE(), @CreatedBy, GETDATE(), @ModifiedBy, 0
    )";

            await connection.ExecuteAsync(insertQuery, new
            {
                Department = request.Department,
                Line = request.Line,
                Shift = request.Shift,
                Weight = request.Weight,
                Description = request.Description ?? $"Weight recorded by TeamLeader {user.ClockNumber}",
                CreatedBy = user.ClockNumber,
                ModifiedBy = user.ClockNumber
            });

            await _activityLogService.LogActivityAsync(
                user.ClockNumber,
                "Weight Record",
                $"Recorded {request.Weight}kg for {request.Department}/{request.Line}/{request.Shift} (Single)",
                "Success"
            );

            return Json(new
            {
                success = true,
                message = "Weight recorded successfully",
                isBulkWeighing = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving weight for team leader");
            return Json(new { success = false, message = "Error recording weight. Please try again." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordBulkWeight([FromBody] BulkWeightRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
            {
                return Json(new { success = false, message = "Access denied" });
            }

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();

            // Verify approved items first
            const string verifyQuery = @"
            SELECT COUNT(*) as ApprovedCount
            FROM [IntegrationCompanyB].[dbo].[scpScrapData] 
            WHERE Department = @Department 
                AND Line = @Line 
                AND Shift = @Shift 
                AND Status = 'Approved'";

            var approvedCount = await connection.QuerySingleAsync<int>(verifyQuery, new
            {
                request.Department,
                request.Line,
                request.Shift
            });

            if (approvedCount == 0)
            {
                return Json(new
                {
                    success = false,
                    message = "No fully approved items found for this location."
                });
            }

            // Insert bulk weight with GUID SecurityStamp
            const string insertQuery = @"
            INSERT INTO [IntegrationCompanyB].[dbo].[scpWeightHistory]
            (WeightId, Department, Line, Shift, Weight, Description, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy, SecurityStamp, IsBulkWeighing)
            OUTPUT INSERTED.WeightId, INSERTED.SecurityStamp
            VALUES (NEXT VALUE FOR [IntegrationCompanyB].[dbo].[seq_WeightHistoryId],
                    @Department, @Line, @Shift, @Weight, @Description, GETDATE(), @CreatedBy, GETDATE(), @ModifiedBy, NEWID(), 1);";

            var result = await connection.QuerySingleAsync(insertQuery, new
            {
                Department = request.Department,
                Line = request.Line,
                Shift = request.Shift,
                Weight = request.Weight,
                Description = request.Description ?? $"Bulk weight for {request.Department}/{request.Line}/{request.Shift} ({approvedCount} approved items)",
                CreatedBy = user.ClockNumber,
                ModifiedBy = user.ClockNumber
            });

            // Log activity
            await _activityLogService.LogActivityAsync(
                user.ClockNumber,
                "Bulk Weight Record",
                $"Recorded {request.Weight}kg for {request.Department}/{request.Line}/{request.Shift} (Bulk - {approvedCount} items)",
                "Success"
            );

            return Json(new
            {
                success = true,
                message = "Bulk weight recorded successfully",
                weightId = result.WeightId,
                securityStamp = result.SecurityStamp.ToString(),
                isBulkWeighing = true,
                approvedItemCount = approvedCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording bulk weight");
            return Json(new { success = false, message = "Error recording weight. Please try again." });
        }
    }


    [HttpGet]
    public async Task<IActionResult> GetWeightHistory(string? department = null, string? line = null, string? shift = null, int days = 30)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            var query = _context.WeightHistory.AsQueryable();

            // Apply location filters based on user permissions
            if (!CanUserViewAllData(user.UserType))
            {
                // Non-admin users can only see their own location
                query = query.Where(w => w.Department == user.Department &&
                                       w.Line == user.Line &&
                                       w.Shift == user.Shift);
            }
            else
            {
                // Admin users can filter by specific locations
                if (!string.IsNullOrEmpty(department))
                    query = query.Where(w => w.Department == department);
                if (!string.IsNullOrEmpty(line))
                    query = query.Where(w => w.Line == line);
                if (!string.IsNullOrEmpty(shift))
                    query = query.Where(w => w.Shift == shift);
            }

            // Apply date filter
            var cutoffDate = DateTime.Now.AddDays(-days);
            query = query.Where(w => w.CreatedAt >= cutoffDate);

            var weightHistory = await query
                .OrderByDescending(w => w.CreatedAt)
                .Select(w => new
                {
                    w.WeightId,
                    w.Department,
                    w.Line,
                    w.Shift,
                    w.Weight,
                    w.CreatedAt,
                    w.CreatedBy,
                    w.Description,
                    w.SecurityStamp,
                    w.IsBulkWeighing,
                    WeightType = w.IsBulkWeighing ? "Bulk" : "Single"
                })
                .ToListAsync();

            return Json(new
            {
                success = true,
                data = weightHistory,
                totalRecords = weightHistory.Count,
                dateRange = new { from = cutoffDate, to = DateTime.Now }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting weight history");
            return Json(new { success = false, message = "Error retrieving weight history" });
        }
    }

    // 5. UPDATED REQUEST MODELS
    public class SaveWeightRequest
    {
        public string Department { get; set; } = "";
        public string Line { get; set; } = "";
        public string Shift { get; set; } = "";
        public decimal Weight { get; set; }
        public string Description { get; set; } = "";
    }

    public class BulkWeightRequest
    {
        public string Department { get; set; } = "";
        public string Line { get; set; } = "";
        public string Shift { get; set; } = "";
        public decimal Weight { get; set; }
        public string Description { get; set; } = "";
    }

    [HttpGet]
    public async Task<IActionResult> GetWeightSummary()
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            string query;
            object parameters;

            if (CanUserViewAllData(user.UserType))
            {
                // Admin can see all locations
                query = @"
                SELECT 
                    Department,
                    Line,
                    Shift,
                    COUNT(*) as TotalRecords,
                    SUM(CASE WHEN IsBulkWeighing = 1 THEN 1 ELSE 0 END) as BulkRecords,
                    SUM(CASE WHEN IsBulkWeighing = 0 THEN 1 ELSE 0 END) as SingleRecords,
                    SUM(Weight) as TotalWeight,
                    AVG(Weight) as AverageWeight,
                    MIN(Weight) as MinWeight,
                    MAX(Weight) as MaxWeight,
                    MAX(CreatedAt) as LastWeightDate
                FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
                WHERE CreatedAt >= DATEADD(day, -30, GETDATE())
                GROUP BY Department, Line, Shift
                ORDER BY Department, Line, Shift";

                parameters = new { };
            }
            else
            {
                // Regular users can only see their location
                query = @"
                SELECT 
                    Department,
                    Line,
                    Shift,
                    COUNT(*) as TotalRecords,
                    SUM(CASE WHEN IsBulkWeighing = 1 THEN 1 ELSE 0 END) as BulkRecords,
                    SUM(CASE WHEN IsBulkWeighing = 0 THEN 1 ELSE 0 END) as SingleRecords,
                    SUM(Weight) as TotalWeight,
                    AVG(Weight) as AverageWeight,
                    MIN(Weight) as MinWeight,
                    MAX(Weight) as MaxWeight,
                    MAX(CreatedAt) as LastWeightDate
                FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
                WHERE CreatedAt >= DATEADD(day, -30, GETDATE())
                    AND Department = @Department 
                    AND Line = @Line 
                    AND Shift = @Shift
                GROUP BY Department, Line, Shift";

                parameters = new
                {
                    Department = user.Department,
                    Line = user.Line,
                    Shift = user.Shift
                };
            }

            var summary = await connection.QueryAsync(query, parameters);

            return Json(new
            {
                success = true,
                data = summary,
                reportDate = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting weight summary");
            return Json(new { success = false, message = "Error retrieving weight summary" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetRecentWeightActivity(int hours = 24)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            string query;
            object parameters;

            if (CanUserViewAllData(user.UserType))
            {
                query = @"
                SELECT TOP 50
                    WeightId,
                    Department,
                    Line,
                    Shift,
                    Weight,
                    CreatedAt,
                    CreatedBy,
                    Description,
                    SecurityStamp,
                    IsBulkWeighing,
                    CASE WHEN IsBulkWeighing = 1 THEN 'Bulk' ELSE 'Single' END as WeightType
                FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
                WHERE CreatedAt >= DATEADD(hour, @Hours, GETDATE())
                ORDER BY CreatedAt DESC";

                parameters = new { Hours = -hours };
            }
            else
            {
                query = @"
                SELECT TOP 50
                    WeightId,
                    Department,
                    Line,
                    Shift,
                    Weight,
                    CreatedAt,
                    CreatedBy,
                    Description,
                    SecurityStamp,
                    IsBulkWeighing,
                    CASE WHEN IsBulkWeighing = 1 THEN 'Bulk' ELSE 'Single' END as WeightType
                FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
                WHERE CreatedAt >= DATEADD(hour, @Hours, GETDATE())
                    AND Department = @Department 
                    AND Line = @Line 
                    AND Shift = @Shift
                ORDER BY CreatedAt DESC";

                parameters = new
                {
                    Hours = -hours,
                    Department = user.Department,
                    Line = user.Line,
                    Shift = user.Shift
                };
            }

            var recentActivity = await connection.QueryAsync(query, parameters);

            return Json(new
            {
                success = true,
                data = recentActivity,
                timeRange = $"Last {hours} hours"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent weight activity");
            return Json(new { success = false, message = "Error retrieving recent activity" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetDashboardWeightInfo()
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            // Get recent weight records for this user's location
            const string recentWeightsQuery = @"
            SELECT TOP 5
                WeightId,
                Weight,
                CreatedAt,
                CreatedBy,
                Description,
                IsBulkWeighing
            FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
            WHERE Department = @Department 
                AND Line = @Line 
                AND Shift = @Shift
            ORDER BY CreatedAt DESC";

            // Get weight summary for this location
            const string summaryQuery = @"
            SELECT 
                COUNT(*) as TotalRecords,
                SUM(Weight) as TotalWeight,
                AVG(Weight) as AverageWeight,
                MAX(CreatedAt) as LastWeightDate
            FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
            WHERE Department = @Department 
                AND Line = @Line 
                AND Shift = @Shift
                AND CreatedAt >= DATEADD(day, -30, GETDATE())";

            var parameters = new
            {
                Department = user.Department,
                Line = user.Line,
                Shift = user.Shift
            };

            var recentWeights = await connection.QueryAsync(recentWeightsQuery, parameters);
            var summary = await connection.QuerySingleOrDefaultAsync(summaryQuery, parameters);

            return Json(new
            {
                success = true,
                recentWeights = recentWeights,
                summary = summary ?? new { TotalRecords = 0, TotalWeight = 0m, AverageWeight = 0m, LastWeightDate = (DateTime?)null },
                location = $"{user.Department}/{user.Line}/{user.Shift}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard weight info");
            return Json(new { success = false, message = "Error retrieving weight information" });
        }
    }

    public class UpdateLocationRequest
    {
        public string Department { get; set; } = "";
        public string Line { get; set; } = "";
        public string Shift { get; set; } = "";
        public string MachineNumber { get; set; } = "";
    }


    // Request models
    public class ApprovalRequest
    {
        public List<int> ScrapIds { get; set; } = new List<int>();
        public bool IsApproved { get; set; }
        public string RejectReason { get; set; } = "";
    }

    // SCANNER HANDOVER CODE

    [HttpGet]
    public async Task<IActionResult> ScannerHandover()
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            return View();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ScannerHandover method");
            TempData["ErrorMessage"] = "Error loading scanner handover page";
            return RedirectToAction("Dashboard");
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAvailableScanners()
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            const string query = @"
            SELECT 
                si.SerialNumber,
                si.ScannerModel,
                si.AssignedDepartment,
                si.AssignedLine,
                si.Status
            FROM [IntegrationCompanyB].[dbo].[scpScannerInventory] si
            LEFT JOIN [IntegrationCompanyB].[dbo].[scpScannerHandover] sh 
                ON si.SerialNumber = sh.ScannerSerialNumber AND sh.Status = 'Active'
            WHERE si.AssignedDepartment = @Department 
            AND si.AssignedLine = @Line 
            AND si.Status = 'Available'
            AND sh.ScannerSerialNumber IS NULL
            ORDER BY si.SerialNumber";

            var scanners = await connection.QueryAsync(query, new
            {
                Department = user.Department,
                Line = user.Line
            });

            return Json(new { success = true, scanners = scanners });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available scanners");
            return Json(new { success = false, message = "Error retrieving scanners" });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessScannerHandover([FromBody] ScannerHandoverRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = "Invalid request data" });
            }

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();

            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                // Check if scanner is already assigned
                const string checkQuery = @"
                SELECT COUNT(*) FROM [IntegrationCompanyB].[dbo].[scpScannerHandover] 
                WHERE ScannerSerialNumber = @ScannerSerialNumber 
                AND Status = 'Active'";

                var existingAssignment = await connection.QuerySingleAsync<int>(
                    checkQuery,
                    new { ScannerSerialNumber = request.ScannerSerialNumber },
                    transaction);

                if (existingAssignment > 0)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "Scanner is already assigned to another user" });
                }

                // Create handover record
                const string insertQuery = @"
                INSERT INTO [IntegrationCompanyB].[dbo].[scpScannerHandover]
                (ScannerSerialNumber, AssignedToClockNumber, AssignedToName, Department, Line, Shift,
                 IsInGoodCondition, ConditionNotes, IsFullyCharged, ChargeLevel, HandoverDate, 
                 HandoverBy, Status, CreatedAt, CreatedBy)
                VALUES 
                (@ScannerSerialNumber, @AssignedToClockNumber, @AssignedToName, @Department, @Line, @Shift,
                 @IsInGoodCondition, @ConditionNotes, @IsFullyCharged, @ChargeLevel, @HandoverDate,
                 @HandoverBy, 'Active', @CreatedAt, @CreatedBy)";

                await connection.ExecuteAsync(insertQuery, new
                {
                    ScannerSerialNumber = request.ScannerSerialNumber,
                    AssignedToClockNumber = user.ClockNumber,
                    AssignedToName = user.UserName,
                    Department = user.Department,
                    Line = user.Line,
                    Shift = user.Shift,
                    IsInGoodCondition = request.IsInGoodCondition,
                    ConditionNotes = request.ConditionNotes ?? "",
                    IsFullyCharged = request.IsFullyCharged,
                    ChargeLevel = request.ChargeLevel,
                    HandoverDate = DateTime.UtcNow,
                    HandoverBy = user.ClockNumber,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = user.ClockNumber
                }, transaction);

                await transaction.CommitAsync();

                // Log activity
                await _activityLogService.LogActivityAsync(
                    user.ClockNumber,
                    "Scanner Handover",
                    $"Scanner {request.ScannerSerialNumber} assigned - Condition: {(request.IsInGoodCondition ? "Good" : "Issues")}, Charged: {(request.IsFullyCharged ? "Yes" : "No")}",
                    "Success"
                );

                return Json(new
                {
                    success = true,
                    message = "Scanner handover completed successfully",
                    scannerSerial = request.ScannerSerialNumber,
                    handoverTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Transaction error in ProcessScannerHandover");
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing scanner handover");
            return Json(new { success = false, message = "Error processing scanner handover" });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReturnScanner([FromBody] ScannerReturnRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            const string updateQuery = @"
            UPDATE [IntegrationCompanyB].[dbo].[scpScannerHandover]
            SET Status = 'Returned',
                ReturnDate = @ReturnDate,
                ReturnConditionGood = @ReturnConditionGood,
                ReturnConditionNotes = @ReturnConditionNotes,
                ReturnChargeLevel = @ReturnChargeLevel,
                ModifiedAt = @ModifiedAt,
                ModifiedBy = @ModifiedBy
            WHERE ScannerSerialNumber = @ScannerSerialNumber 
            AND AssignedToClockNumber = @AssignedToClockNumber 
            AND Status = 'Active'";

            var rowsAffected = await connection.ExecuteAsync(updateQuery, new
            {
                ScannerSerialNumber = request.ScannerSerialNumber,
                AssignedToClockNumber = user.ClockNumber,
                ReturnDate = DateTime.UtcNow,
                ReturnConditionGood = request.ReturnConditionGood,
                ReturnConditionNotes = request.ReturnConditionNotes ?? "",
                ReturnChargeLevel = request.ReturnChargeLevel,
                ModifiedAt = DateTime.UtcNow,
                ModifiedBy = user.ClockNumber
            });

            if (rowsAffected == 0)
            {
                return Json(new { success = false, message = "No active scanner assignment found" });
            }

            await _activityLogService.LogActivityAsync(
                user.ClockNumber,
                "Scanner Return",
                $"Scanner {request.ScannerSerialNumber} returned - Condition: {(request.ReturnConditionGood ? "Good" : "Issues")}, Charge: {request.ReturnChargeLevel}%",
                "Success"
            );

            return Json(new
            {
                success = true,
                message = "Scanner returned successfully",
                scannerSerial = request.ScannerSerialNumber,
                returnTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error returning scanner");
            return Json(new { success = false, message = "Error returning scanner" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetScannerStatus()
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            const string query = @"
            SELECT 
                sh.ScannerSerialNumber,
                si.ScannerModel,
                sh.AssignedToClockNumber,
                sh.AssignedToName,
                sh.Department,
                sh.Line,
                sh.Shift,
                sh.IsInGoodCondition,
                sh.ConditionNotes,
                sh.IsFullyCharged,
                sh.ChargeLevel,
                sh.HandoverDate,
                sh.Status
            FROM [IntegrationCompanyB].[dbo].[scpScannerHandover] sh
            INNER JOIN [IntegrationCompanyB].[dbo].[scpScannerInventory] si 
                ON sh.ScannerSerialNumber = si.SerialNumber
            WHERE sh.AssignedToClockNumber = @ClockNumber 
            AND sh.Status = 'Active'
            ORDER BY sh.HandoverDate DESC";

            var scannerAssignments = await connection.QueryAsync(query, new { ClockNumber = user.ClockNumber });

            return Json(new
            {
                success = true,
                assignments = scannerAssignments,
                hasActiveScanner = scannerAssignments.Any()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting scanner status");
            return Json(new { success = false, message = "Error retrieving scanner status" });
        }
    }

    // Request Models
    public class ScannerHandoverRequest
    {
        public string ScannerSerialNumber { get; set; }
        public bool IsInGoodCondition { get; set; }
        public string ConditionNotes { get; set; }
        public bool IsFullyCharged { get; set; }
        public int ChargeLevel { get; set; }
        
    }

    public class ScannerReturnRequest
    {
        public string ScannerSerialNumber { get; set; }
        public bool ReturnConditionGood { get; set; }
        public string ReturnConditionNotes { get; set; }
        public int ReturnChargeLevel { get; set; }
    }

    // ADD THESE METHODS TO YOUR ScrapApprovalController.cs

    /// <summary>
    /// Enhanced validation to check if a scrap item is in a valid state for approval
    /// </summary>
    private (bool isValid, string reason) ValidateItemForApproval(ScrapData item, User user)
    {
        var userType = user.UserType?.ToLower();

        // Basic status checks
        if (item.Status?.Equals("Approved", StringComparison.OrdinalIgnoreCase) == true)
            return (false, "Item is already fully approved");

        if (item.Status?.Equals("Rejected", StringComparison.OrdinalIgnoreCase) == true)
            return (false, "Item has been rejected");

        // Validate approval flow consistency
        switch (userType)
        {
            case "teamleader":
                if (!string.IsNullOrEmpty(item.TeamLeaderApprovalStatus) &&
                    item.TeamLeaderApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                    return (false, "You have already approved this item");

                // Item must be in "Pending TL" state or new submission
                if (!string.IsNullOrEmpty(item.Status) &&
                    !item.Status.Equals("Pending TL", StringComparison.OrdinalIgnoreCase))
                    return (false, $"Item status '{item.Status}' is not ready for Team Leader approval");

                break;

            case "groupleader":
                // GL can only approve if TL has approved first
                if (string.IsNullOrEmpty(item.TeamLeaderApprovalStatus) ||
                    !item.TeamLeaderApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                    return (false, "Team Leader must approve this item first");

                if (!string.IsNullOrEmpty(item.GroupLeaderApprovalStatus) &&
                    item.GroupLeaderApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                    return (false, "You have already approved this item");

                if (!item.Status?.Equals("Pending GL", StringComparison.OrdinalIgnoreCase) == true)
                    return (false, $"Item status '{item.Status}' is not ready for Group Leader approval");

                break;

            case "manager":
                // Manager can only approve if both TL and GL have approved
                if (string.IsNullOrEmpty(item.TeamLeaderApprovalStatus) ||
                    !item.TeamLeaderApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                    return (false, "Team Leader must approve this item first");

                if (string.IsNullOrEmpty(item.GroupLeaderApprovalStatus) ||
                    !item.GroupLeaderApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                    return (false, "Group Leader must approve this item first");

                if (!string.IsNullOrEmpty(item.ManagerApprovalStatus) &&
                    item.ManagerApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                    return (false, "You have already approved this item");

                if (!item.Status?.Equals("Pending Manager", StringComparison.OrdinalIgnoreCase) == true)
                    return (false, $"Item status '{item.Status}' is not ready for Manager approval");

                break;

            default:
                return (false, $"User type '{userType}' cannot approve scrap items");
        }

        return (true, "Item is valid for approval");
    }

    /// <summary>
    /// Auto-fix inconsistent approval data (run this before approval processing)
    /// </summary>
    private async Task<bool> AutoFixInconsistentApprovalData(ScrapData item)
    {
        bool wasFixed = false;
        var currentTime = DateTime.Now;

        try
        {
            // Fix missing FirstApproval data when TL is approved
            if (!string.IsNullOrEmpty(item.TeamLeaderApprovalStatus) &&
                item.TeamLeaderApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(item.FirstApprovalStatus) ||
                 item.FirstApprovalStatus.Equals("Pending TL", StringComparison.OrdinalIgnoreCase)))
            {
                item.FirstApprovalStatus = "Approved";
                item.FirstApprovalBy = item.TeamLeaderApprovalBy;
                item.FirstApprovalDate = item.TeamLeaderApprovalDate;
                item.ModifiedAt = currentTime;
                item.ModifiedBy = "AUTO_FIX";
                wasFixed = true;

                _logger.LogInformation($"Auto-fixed FirstApproval data for ScrapId {item.ScrapId}");
            }

            // Fix CurrentApprovalLevel based on status
            int expectedLevel = item.Status?.ToLower() switch
            {
                "pending tl" or "" or null => 1,
                "pending gl" => 2,
                "pending manager" => 3,
                "approved" => 4,
                _ => item.CurrentApprovalLevel ?? 1
            };

            if (item.CurrentApprovalLevel != expectedLevel)
            {
                item.CurrentApprovalLevel = expectedLevel;
                item.ModifiedAt = currentTime;
                item.ModifiedBy = "AUTO_FIX";
                wasFixed = true;

                _logger.LogInformation($"Auto-fixed CurrentApprovalLevel for ScrapId {item.ScrapId}: {item.CurrentApprovalLevel} -> {expectedLevel}");
            }

            // Fix status based on approval states
            if (!string.IsNullOrEmpty(item.TeamLeaderApprovalStatus) &&
                item.TeamLeaderApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(item.GroupLeaderApprovalStatus) &&
                    item.GroupLeaderApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(item.ManagerApprovalStatus) &&
                        item.ManagerApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                    {
                        // All levels approved - should be "Approved"
                        if (!item.Status?.Equals("Approved", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            item.Status = "Approved";
                            item.CurrentApprovalLevel = 4;
                            wasFixed = true;
                        }
                    }
                    else
                    {
                        // TL and GL approved - should be "Pending Manager"
                        if (!item.Status?.Equals("Pending Manager", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            item.Status = "Pending Manager";
                            item.CurrentApprovalLevel = 3;
                            wasFixed = true;
                        }
                    }
                }
                else
                {
                    // Only TL approved - should be "Pending GL"
                    if (!item.Status?.Equals("Pending GL", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        item.Status = "Pending GL";
                        item.CurrentApprovalLevel = 2;
                        wasFixed = true;
                    }
                }
            }

            if (wasFixed)
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Auto-fix completed for ScrapId {item.ScrapId}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error auto-fixing ScrapId {item.ScrapId}");
            return false;
        }

        return wasFixed;
    }


    [HttpGet]
    public async Task<IActionResult> GetScrapDataFiltered(
        string? department = null,
        string? departments = null,
        string? line = null,
        string? shift = null,
        string? status = null,
        int days = 30)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return Json(new { success = false, message = "Not authenticated" });

            if (!CanUserViewAllData(user.UserType))
                return Json(new { success = false, message = "Access denied" });

            using var connection = new SqlConnection(
                _configuration.GetConnectionString("DefaultConnection"));

            // ✅ Build the SELECT with TOP 500 directly — no subquery wrapper
            var sb = new StringBuilder(@"
                SELECT
                    s.ScrapId, s.ClockNumber as SubmittedBy, s.PartNumber,
                    s.Quantity, s.Department, s.Line, s.Shift,
                    s.SubmissionDate, s.CreatedAt, s.Status,
                    s.CurrentApprovalLevel,
                    COALESCE(im.MaterialCost, 0) as MaterialCost,
                    COALESCE(im.StockUom, 'EA') as StockUom,
                    s.TeamLeaderApprovalStatus, s.TeamLeaderApprovalBy, s.TeamLeaderApprovalDate,
                    s.GroupLeaderApprovalStatus, s.GroupLeaderApprovalBy, s.GroupLeaderApprovalDate,
                    s.ManagerApprovalStatus, s.ManagerApprovalBy, s.ManagerApprovalDate,
                    s.SeniorManagerApprovalStatus, s.SeniorManagerApprovalBy, s.SeniorManagerApprovalDate,
                    s.RejectedReason,
                    submitterUser.ClockNumber as SubmitterClockNumber,
                    (COALESCE(im.MaterialCost, 0) * s.Quantity) as TotalItemCost
                                FROM [IntegrationCompanyB].[dbo].[scpScrapData] s
                LEFT JOIN [SysproCompanyB].[dbo].[InvMaster] im 
                    ON RTRIM(LTRIM(s.PartNumber)) = RTRIM(LTRIM(im.StockCode)) COLLATE Latin1_General_CI_AS
                   AND im.StockCode COLLATE Latin1_General_BIN NOT IN ('EBD0532a', 'AVf30B/R', ' 4KB-927680-501')
                LEFT JOIN [IntegrationCompanyB].[dbo].[scpUser] submitterUser
                    ON s.ClockNumber = submitterUser.ClockNumber
                WHERE 1=1");

            var parameters = new DynamicParameters();

            if (!string.IsNullOrEmpty(department))
            {
                sb.Append(" AND s.Department = @Department");
                parameters.Add("Department", department);
            }
            else if (!string.IsNullOrEmpty(departments))
            {
                // ✅ Filter by multiple departments (plant-level filter)
                var deptList = departments.Split(',')
                    .Select(d => d.Trim())
                    .Where(d => !string.IsNullOrEmpty(d))
                    .ToList();

                if (deptList.Any())
                {
                    sb.Append(" AND s.Department IN @Departments");
                    parameters.Add("Departments", deptList);
                }
            }
            if (!string.IsNullOrEmpty(line))
            {
                sb.Append(" AND s.Line = @Line");
                parameters.Add("Line", line);
            }
            if (!string.IsNullOrEmpty(shift))
            {
                sb.Append(" AND s.Shift = @Shift");
                parameters.Add("Shift", shift);
            }
            if (!string.IsNullOrEmpty(status))
            {
                sb.Append(" AND s.Status = @Status");
                parameters.Add("Status", status);
            }
            if (days > 0)
            {
                sb.Append(" AND s.CreatedAt >= DATEADD(day, @NegDays, GETDATE())");
                parameters.Add("NegDays", -days);
            }

            sb.Append(" ORDER BY s.SubmissionDate DESC, s.CreatedAt DESC");

            var items = await connection.QueryAsync(sb.ToString(), parameters);

            // ✅ Map to anonymous object with explicit camelCase names
            var result = items.Select(item => new
            {
                scrapId = (int)(item.ScrapId ?? 0),
                submittedBy = (string)(item.SubmittedBy ?? ""),
                submitterClockNumber = (string)(item.SubmitterClockNumber ?? ""),
                partNumber = (string)(item.PartNumber ?? ""),
                quantity = (int)(item.Quantity ?? 0),
                department = (string)(item.Department ?? ""),
                line = (string)(item.Line ?? ""),
                shift = (string)(item.Shift ?? ""),
                submissionDate = (DateTime?)(item.SubmissionDate),
                createdAt = (DateTime)(item.CreatedAt ?? DateTime.Now),
                status = (string)(item.Status ?? "Pending TL"),
                currentApprovalLevel = (int?)(item.CurrentApprovalLevel ?? 1),
                materialCost = (decimal)(item.MaterialCost ?? 0m),
                stockUom = (string)(item.StockUom ?? "EA"),
                totalItemCost = (decimal)(item.TotalItemCost ?? 0m),

                teamLeaderApprovalStatus = (string)(item.TeamLeaderApprovalStatus ?? ""),
                teamLeaderApprovalBy = (string)(item.TeamLeaderApprovalBy ?? ""),
                teamLeaderApprovalDate = (DateTime?)(item.TeamLeaderApprovalDate),
                groupLeaderApprovalStatus = (string)(item.GroupLeaderApprovalStatus ?? ""),
                groupLeaderApprovalBy = (string)(item.GroupLeaderApprovalBy ?? ""),
                groupLeaderApprovalDate = (DateTime?)(item.GroupLeaderApprovalDate),
                managerApprovalStatus = (string)(item.ManagerApprovalStatus ?? ""),
                managerApprovalBy = (string)(item.ManagerApprovalBy ?? ""),
                managerApprovalDate = (DateTime?)(item.ManagerApprovalDate),
                seniorManagerApprovalStatus = (string)(item.SeniorManagerApprovalStatus ?? ""),
                seniorManagerApprovalBy = (string)(item.SeniorManagerApprovalBy ?? ""),
                seniorManagerApprovalDate = (DateTime?)(item.SeniorManagerApprovalDate),
                rejectedReason = (string)(item.RejectedReason ?? ""),
                recordedWeight = (decimal?)null
            }).ToList();

            return Json(new
            {
                success = true,
                data = result,
                count = result.Count,
                filters = new { department, line, shift, status, days }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetScrapDataFiltered");
            return Json(new { success = false, message = "Error loading data: " + ex.Message });
        }
    }







    /// <summary>
    /// UPDATED ProcessApproval method with enhanced validation
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessApprovalEnhanced([FromBody] ApprovalRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = "Invalid request data" });
            }

            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            var userType = user.UserType?.ToLower();
            _logger.LogInformation($"Enhanced approval process: User={user.ClockNumber}, Type={userType}, Items={request.ScrapIds.Count}, Approved={request.IsApproved}");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                int processedCount = 0;
                int autoFixedCount = 0;
                var currentTime = DateTime.Now;
                var failedItems = new List<string>();

                foreach (var scrapId in request.ScrapIds)
                {
                    var item = await _context.ScrapData.FirstOrDefaultAsync(s => s.ScrapId == scrapId);
                    if (item == null)
                    {
                        _logger.LogWarning($"Scrap item {scrapId} not found");
                        failedItems.Add($"Item {scrapId}: Not found");
                        continue;
                    }

                    // 🔧 AUTO-FIX: Attempt to fix inconsistent data
                    bool wasAutoFixed = await AutoFixInconsistentApprovalData(item);
                    if (wasAutoFixed)
                    {
                        autoFixedCount++;
                        // Reload the item after auto-fix
                        await _context.Entry(item).ReloadAsync();
                    }

                    // 🔧 ENHANCED VALIDATION: Check if item can be approved
                    var validation = ValidateItemForApproval(item, user);
                    if (!validation.isValid)
                    {
                        _logger.LogWarning($"User {user.ClockNumber} cannot approve item {scrapId}: {validation.reason}");
                        failedItems.Add($"Item {scrapId}: {validation.reason}");
                        continue;
                    }

                    // Process the approval/rejection
                    if (request.IsApproved)
                    {
                        await ProcessItemApproval(item, user, userType, currentTime); // ✅ Add await
                    }
                    else
                    {
                        ProcessItemRejection(item, user, userType, currentTime, request.RejectReason);
                    }

                    processedCount++;
                }

                if (processedCount > 0)
                {
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // Log activity with additional details
                    var activityDetails = $"{(request.IsApproved ? "Approved" : "Rejected")} {processedCount} items at {userType} level";
                    if (autoFixedCount > 0)
                    {
                        activityDetails += $" (Auto-fixed {autoFixedCount} inconsistent records)";
                    }

                    await _activityLogService.LogActivityAsync(
                        user.ClockNumber,
                        request.IsApproved ? "Approval" : "Rejection",
                        activityDetails,
                        "Success"
                    );

                    // Build response message
                    string responseMessage;
                    if (failedItems.Any())
                    {
                        responseMessage = $"Processed {processedCount} item(s). {failedItems.Count} items could not be processed.";
                    }
                    else
                    {
                        responseMessage = $"Successfully {(request.IsApproved ? "approved" : "rejected")} {processedCount} item(s)";
                    }

                    // Build activity details (reuse existing variable or create new one)
                    string logActivityDetails = $"{(request.IsApproved ? "Approved" : "Rejected")} {processedCount} items at {userType} level";
                    if (autoFixedCount > 0)
                    {
                        logActivityDetails += $" (Auto-fixed {autoFixedCount} inconsistent records)";
                    }

                    await _activityLogService.LogActivityAsync(
                        user.ClockNumber,
                        request.IsApproved ? "Approval" : "Rejection",
                        logActivityDetails,
                        "Success"
                    );

                    // Return consistent response structure
                    return Json(new
                    {
                        success = true,
                        message = responseMessage,
                        processedCount = processedCount,
                        autoFixedCount = autoFixedCount,
                        failedCount = failedItems.Count,
                        failedItems = failedItems.Take(5).ToList() // Always include this property
                    });
                }
                else
                {
                    await transaction.RollbackAsync();
                    var errorMessage = failedItems.Any()
                        ? $"No items processed. Issues: {string.Join("; ", failedItems.Take(3))}"
                        : "No items were processed. Please check item status and permissions.";

                    return Json(new
                    {
                        success = false,
                        message = errorMessage,
                        failedItems = failedItems.Take(5).ToList()
                    });
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Transaction error in ProcessApprovalEnhanced");
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ProcessEnhancedApproval");
            return Json(new { success = false, message = "Error processing approval request" });
        }
    }

    public class UpdateScrapLocationRequest
    {
        public List<int> ScrapIds { get; set; } = new List<int>();
        public string Department { get; set; } = "";
        public string Line { get; set; } = "";
        public string Shift { get; set; } = "";
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateScrapLocation([FromBody] UpdateScrapLocationRequest request)
    {
        try
        {
            if (request.ScrapIds == null || request.ScrapIds.Count == 0)
            {
                return Json(new { success = false, message = "No scrap items selected" });
            }

            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Json(new { success = false, message = "User not authenticated" });
            }

            // Permission check for ALL locations
            if ((request.Department.StartsWith("ALL ") ||
                 request.Line.Contains("ALL") ||
                 request.Shift == "ALL") &&
                !CanUserViewAllData(user.UserType))
            {
                return Json(new { success = false, message = "You do not have permission to assign ALL locations" });
            }

            // Validate location combination
            var validation = await ValidateLocationCombination(
                request.Department,
                request.Line,
                request.Shift
            );

            if (!validation.isValid)
            {
                return Json(new { success = false, message = validation.message });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            var scrapItems = await _context.ScrapData
                .Where(s => request.ScrapIds.Contains(s.ScrapId))
                .ToListAsync();

            if (!scrapItems.Any())
            {
                return Json(new { success = false, message = "No matching scrap items found" });
            }

            var now = DateTime.Now;
            int updatedCount = 0;

            foreach (var scrap in scrapItems)
            {
                // 🔒 Do not move already approved/rejected scrap
                if (scrap.Status == "Approved" || scrap.Status == "Rejected")
                    continue;

                scrap.Department = request.Department;
                scrap.Line = request.Line;
                scrap.Shift = request.Shift;
                scrap.ModifiedAt = now;
                scrap.ModifiedBy = user.ClockNumber;
                updatedCount++;
            }

            if (updatedCount == 0)
            {
                return Json(new { success = false, message = "No scrap items were eligible for update" });
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            await _activityLogService.LogActivityAsync(
                user.ClockNumber,
                "Scrap Location Update",
                $"Moved {updatedCount} scrap items to {request.Department}/{request.Line}/{request.Shift}",
                "Success"
            );

            return Json(new
            {
                success = true, 
                message = $"Updated {updatedCount} scrap item(s) successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating scrap location");
            return Json(new { success = false, message = "Error updating scrap location" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetDepartmentCostSummary(string? shift = null)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (!CanUserViewAllData(user.UserType))
                return Json(new { success = false, message = "Access denied" });

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            // Approved-item cost + count, per department (+ optional shift filter)
            var costSql = @"
            SELECT
                Department,
                COUNT(*) AS ApprovedItemCount,
                SUM(ISNULL(MaterialCost, 0)) AS TotalCost,
                SUM(ISNULL(ActualWeight, Weight)) AS TotalEstWeightKg
            FROM [IntegrationCompanyB].[dbo].[scpScrapData]
            WHERE Status = 'Approved'
              AND (@Shift IS NULL OR Shift = @Shift)
            GROUP BY Department";

            // Actual recorded weight per department (line + bulk, excludes family/bin sentinels)
            var weighedSql = @"
            SELECT Department, SUM(Weight) AS TotalWeighedKg, MAX(CreatedAt) AS LastWeighed
            FROM [IntegrationCompanyB].[dbo].[scpWeightHistory]
            WHERE NOT (Line = 'ALL LINES' AND Shift = 'ALL')
              AND NOT (Shift = 'MONTHLY')
            GROUP BY Department";

            var costRows = (await connection.QueryAsync(costSql, new { Shift = shift }))
                                  .ToDictionary(r => (string)r.Department, r => r);
            var weighedRows = (await connection.QueryAsync(weighedSql))
                                  .ToDictionary(r => (string)r.Department, r => r);

            // Pull Category alongside each department so the client can group without a second call
            var deptCatSql = "SELECT DepartmentName, Category FROM [IntegrationCompanyB].[dbo].[scpDepartment] WHERE IsActive = 1";
            var deptCats = (await connection.QueryAsync(deptCatSql))
                                .ToDictionary(r => (string)r.DepartmentName, r => (string)(r.Category ?? "OTHER"));

            var result = costRows.Select(kv =>
            {
                var dept = kv.Key;
                var c = kv.Value;
                var w = weighedRows.TryGetValue(dept, out var wr) ? wr : null;

                return new
                {
                    department = dept,
                    category = deptCats.TryGetValue(dept, out var cat) ? cat : "OTHER",
                    approvedItemCount = (int)c.ApprovedItemCount,
                    totalCost = (decimal)(c.TotalCost ?? 0m),
                    estWeightKg = (decimal)(c.TotalEstWeightKg ?? 0m),
                    weighedKg = w != null ? (decimal)(w.TotalWeighedKg ?? 0m) : 0m,
                    lastWeighed = w?.LastWeighed
                };
            })
            .OrderBy(r => r.category)
            .ThenBy(r => r.department)
            .ToList();

            return Json(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting department cost summary");
            return Json(new { success = false, message = "Error retrieving cost summary" });
        }
    }
}