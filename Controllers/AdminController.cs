using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCP_Control.Models;
using SCP_Control.Services;

namespace SCP_Control.Controllers
{
    namespace SCP_Control.Controllers
    {
        [Authorize(Roles = "Admin")]
        public class AdminController : Controller
        {
            private readonly UserRegistrationService _userService;
            private readonly ILogger<AdminController> _logger;

            public AdminController(
                UserRegistrationService userService,
                ILogger<AdminController> logger)
            {
                _userService = userService ?? throw new ArgumentNullException(nameof(userService));
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }

            [HttpGet]
            public async Task<IActionResult> PendingApprovals()
            {
                var pendingRequests = await _userService.GetPendingRegistrationRequests();
                return View(pendingRequests);
            }

            [HttpPost]
            public async Task<IActionResult> ProcessUser(int requestId, bool approved)
            {
                try
                {
                    var result = await _userService.ProcessRegistrationRequest(
                        requestId,
                        "Admin",
                        approved ? "Approved" : "Rejected",
                        approved ? "Approved by admin" : "Rejected by admin");

                    return Json(new { success = result });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing user request");
                    return Json(new { success = false, message = "An error occurred processing the request" });
                }
            }

            [HttpPost]
            public async Task<IActionResult> ProcessScrap(int scrapId, decimal physicalWeight)
            {
                try
                {
                    var result = await _userService.ProcessScrapWeight(scrapId, physicalWeight);
                    return Json(new { success = result });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing scrap");
                    return Json(new { success = false, message = "An error occurred processing the scrap" });
                }
            }
        }
    }
}
