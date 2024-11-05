using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCP_Control.Services;

namespace SCP_Control.Controllers
{
    [Authorize(Roles = "TeamLeader")]
    public class Team_LeaderController : Controller
    {
        private readonly ScrapApprovalService _scrapService;
        private readonly ILogger<Team_LeaderController> _logger;

        public Team_LeaderController(
            ScrapApprovalService scrapService,
            ILogger<Team_LeaderController> logger)
        {
            _scrapService = scrapService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var userDepartment = User.FindFirst("Department")?.Value;
                var userLine = User.FindFirst("Line")?.Value;
                var userShift = User.FindFirst("Shift")?.Value;

                var scrapData = await _scrapService.GetScrapDataForApproval(
                    "TeamLeader",
                    userDepartment,
                    userLine,
                    userShift);

                return View(scrapData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading Team Leader dashboard");
                return RedirectToAction("Error", "Home");
            }
        }

        [HttpPost]
        public async Task<IActionResult> ApproveScrap(int scrapId, string decision, string reason)
        {
            try
            {
                var result = await _scrapService.ApproveScrap(scrapId, "TeamLeader", decision, reason);
                return Json(new { success = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving scrap {ScrapId}", scrapId);
                return Json(new { success = false, message = "Error processing approval" });
            }
        }
    }
}