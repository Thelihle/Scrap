using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using Dapper;
using SCP_Control.Models;
using SCP_Control.ViewModels;
using SCP_Control.Services;

namespace SCP_Control.Controllers
{
    [Authorize(Roles = "Operator")]
    public class OperatorController : Controller
    {
        private readonly string _connectionString;
        private readonly ILogger<OperatorController> _logger;
        private readonly InventoryService _inventoryService;

        public OperatorController(
           IConfiguration configuration,
           ILogger<OperatorController> logger,
           InventoryService inventoryService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
            _inventoryService = inventoryService;
        }

        [HttpGet]
        public async Task<IActionResult> ScanPart()
        {
            try
            {
                var clockNumber = User.Identity?.Name;
                if (string.IsNullOrEmpty(clockNumber))
                {
                    return RedirectToAction("Login", "Account");
                }

                using var connection = new SqlConnection(_connectionString);
                var query = @"
                    SELECT Department, Line, Shift 
                    FROM Users 
                    WHERE ClockNumber = @ClockNumber";

                var operatorDetails = await connection.QueryFirstOrDefaultAsync<OperatorDetailsViewModel>(
                    query, new { ClockNumber = clockNumber });

                var model = new ScrapEntryViewModel
                {
                    Department = operatorDetails?.Department ?? "",
                    Line = operatorDetails?.Line ?? "",
                    Shift = operatorDetails?.Shift ?? ""
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading ScanPart view");
                return View("Error");
            }
        }

        public async Task<IActionResult> Setup()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                var departments = await connection.QueryAsync<Department>(
                    "SELECT * FROM Departments WHERE IsActive = 1");

                var model = new OperatorSetupViewModel
                {
                    Departments = departments.ToList()
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading setup page"); // Changed from logger to _logger
                return RedirectToAction("Error", "Error");
            }
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var clockNumber = User.Identity?.Name;
                if (string.IsNullOrEmpty(clockNumber))
                {
                    return RedirectToAction("Login", "Account");
                }

                using var connection = new SqlConnection(_connectionString);
                var query = @"
                    SELECT Department, Line, Shift 
                    FROM Users 
                    WHERE ClockNumber = @ClockNumber";

                var operatorDetails = await connection.QueryFirstOrDefaultAsync<OperatorDetailsViewModel>(
                    query, new { ClockNumber = clockNumber });

                if (operatorDetails == null ||
                    string.IsNullOrEmpty(operatorDetails.Department) ||
                    string.IsNullOrEmpty(operatorDetails.Line) ||
                    string.IsNullOrEmpty(operatorDetails.Shift))
                {
                    return RedirectToAction("Setup");
                }

                return View(operatorDetails);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading operator dashboard");
                return RedirectToAction("Error", "Error");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetLinesByDepartment(int departmentId)
        {
            try
            {
                var lines = await _inventoryService.GetLinesByDepartment(departmentId);
                return Json(new
                {
                    success = true,
                    lines = lines.Select(l => new { id = l.LineId, name = l.LineName })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting lines for department {DepartmentId}", departmentId);
                return Json(new { success = false, error = "Error loading lines" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ScrapEntry(string partNumber)
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

                var model = new ScrapEntryViewModel
                {
                    PartNumber = partNumber,
                    Department = User.FindFirst("Department")?.Value ?? "",
                    Line = User.FindFirst("Line")?.Value ?? "",
                    Shift = User.FindFirst("Shift")?.Value ?? ""
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading scrap entry for part {PartNumber}", partNumber);
                return RedirectToAction("Error", "Error");
            }
        }

        [HttpPost]
        public async Task<IActionResult> ValidatePartNumber(string partNumber)
        {
            try
            {
                var isValid = await _inventoryService.ValidatePartNumber(partNumber);
                return Json(new { isValid });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating part number {PartNumber}", partNumber);
                return Json(new { isValid = false, error = "Error validating part number" });
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
                using var connection = new SqlConnection(_connectionString);

                var updateQuery = @"
                    UPDATE Users 
                    SET Department = @Department,
                        Line = @Line,
                        Shift = @Shift,
                        LastModifiedAt = GETDATE()
                    WHERE ClockNumber = @ClockNumber";

                await connection.ExecuteAsync(updateQuery, new
                {
                    model.Department,
                    model.Line,
                    model.Shift,
                    ClockNumber = clockNumber
                });

                return Json(new { success = true, redirect = Url.Action("Index", "Operator") });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating operator details");
                return Json(new { success = false, message = "Error updating details" });
            }
        }
    }
}