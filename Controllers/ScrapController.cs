using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCP_Control.Models;
using SCP_Control.Services;
using SCP_Control.Services.Interfaces;
using SCP_Control.ViewModels;
using System.Security.Claims;


namespace SCP_Control.Controllers
{
    
        [Authorize]
        public class ScrapController : Controller
        {
            private readonly IScrapApprovalService _scrapApprovalService;
            private readonly ILogger<ScrapController> _logger;

            public ScrapController(
                IScrapApprovalService scrapApprovalService,
                ILogger<ScrapController> logger)
            {
                _scrapApprovalService = scrapApprovalService;
                _logger = logger;
            }

        private static LocationUpdateViewModel ConvertToLocationViewModel(LocationDetails location)
        {
            return new LocationUpdateViewModel
            {
                Department = location.Department,
                Line = location.Line,
                Shift = location.Shift
            };
        }


        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            try
            {
                var username = User.Identity?.Name ?? throw new InvalidOperationException("User not authenticated");
                var department = User.FindFirst("Department")?.Value ?? "";
                var line = User.FindFirst("Line")?.Value ?? "";
                var shift = User.FindFirst("Shift")?.Value ?? "";

                var location = new LocationDetails
                {
                    Department = department,
                    Line = line,
                    Shift = shift
                };

                var scrapData = await _scrapApprovalService.GetScrapDataByLocation(location);
                return View(scrapData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading dashboard");
                return RedirectToAction("Error", "Home");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetPendingApprovals()
        {
            try
            {
                var department = User.FindFirst("Department")?.Value ?? string.Empty;
                var data = await _scrapApprovalService.GetPendingApprovals(department);
                return Json(new { success = true, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving pending approvals");
                return Json(new { success = false, message = "Error retrieving pending approvals." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ProcessScrapApproval(int scrapId, bool isApproved, string reason)
        {
            try
            {
                var username = User.Identity?.Name ??
                    throw new InvalidOperationException("User not authenticated");

                var result = await _scrapApprovalService.ProcessFirstApproval(
                    scrapId,
                    isApproved,
                    reason,
                    username);

                return Json(new { success = result.Success, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing scrap approval for {ScrapId}", scrapId);
                return Json(new { success = false, message = "Error processing approval." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ApproveScrap(int scrapId, string decision, string reason)
        {
            try
            {
                var username = User.Identity?.Name ??
                    throw new InvalidOperationException("User not authenticated");
                var userType = User.FindFirst(ClaimTypes.Role)?.Value ??
                    throw new InvalidOperationException("User role not found");

                var isApproved = decision.Equals("Approved", StringComparison.OrdinalIgnoreCase);
                var result = await _scrapApprovalService.ProcessFirstApproval(
                    scrapId,
                    isApproved,
                    reason,
                    username);

                return Json(new { success = result.Success, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving scrap {ScrapId}", scrapId);
                return Json(new { success = false, message = "Error processing approval." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetScrapDataForApproval(string department, string line, string shift)
        {
            try
            {
                var data = await _scrapApprovalService.GetScrapDataForApproval(department, line, shift);
                return Json(new { success = true, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scrap data for approval");
                return Json(new { success = false, message = "Error retrieving scrap data." });
            }
        }


    }
}
