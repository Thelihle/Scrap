using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SCP_Control.Models;
using SCP_Control.Services.Interfaces;
using System;
using System.Threading.Tasks;

namespace SCP_Control.Controllers
{
    [Authorize]
    public class ActivityController : Controller
    {
        private readonly IActivityLogService _activityLogService;
        private readonly ILogger<ActivityController> _logger;

        public ActivityController(
            IActivityLogService activityLogService,
            ILogger<ActivityController> logger)
        {
            _activityLogService = activityLogService;
            _logger = logger;
        }

        public async Task<IActionResult> Index(int page = 1)
        {
            try
            {
                var activities = await _activityLogService.GetRecentActivitiesAsync(50, page);
                return View(activities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving activity logs");
                return View("Error");
            }
        }

        [HttpGet("activity/user/{clockNumber}")]
        public async Task<IActionResult> UserActivity(
            string clockNumber,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var activities = await _activityLogService.GetUserActivityHistoryAsync(
                    clockNumber,
                    startDate ?? DateTime.UtcNow.AddDays(-30),
                    endDate ?? DateTime.UtcNow
                );
                return View("Index", activities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving activity logs for user {ClockNumber}", clockNumber);
                return View("Error");
            }
        }

        [HttpGet("activity/errors")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Errors(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var errors = await _activityLogService.GetErrorLogsAsync(
                    startDate ?? DateTime.UtcNow.AddDays(-7),
                    endDate ?? DateTime.UtcNow
                );
                return View(errors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving error logs");
                return View("Error");
            }
        }
    }
}