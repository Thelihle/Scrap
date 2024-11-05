using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCP_Control.Models;
using SCP_Control.Services;
using System.Security.Claims;

namespace SCP_Control.Controllers
{
    [Authorize]
    public class ScrapController : Controller
    {
        private readonly ScrapApprovalService _scrapService;

        public ScrapController(ScrapApprovalService scrapService)
        {
            _scrapService = scrapService;
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var userType = User.FindFirst(ClaimTypes.Role)?.Value ??
                throw new InvalidOperationException("User role not found");
            var department = User.FindFirst("Department")?.Value;
            var line = User.FindFirst("Line")?.Value;
            var shift = User.FindFirst("Shift")?.Value;

            var scrapData = await _scrapService.GetScrapDataForApproval(userType, department, line, shift);
            return View(scrapData);
        }

        [HttpPost]
        public async Task<IActionResult> ApproveScrap(int scrapId, string decision, string reason)
        {
            var userType = User.FindFirst(ClaimTypes.Role)?.Value ??
                throw new InvalidOperationException("User role not found");
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                throw new InvalidOperationException("User ID not found");

            var result = await _scrapService.ApproveScrap(scrapId, userType, decision, reason);
            return Json(new { success = result });
        }
    }
}
