using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SCP_Control.DbContext;
using SCP_Control.Models;
using SCP_Control.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SCP_Control.Services
{
    public class ActivityLogService : IActivityLogService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<ActivityLogService> _logger;

        public ActivityLogService(
            ApplicationDbContext context,
            IHttpContextAccessor httpContextAccessor,
            ILogger<ActivityLogService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Logs user activity with comprehensive details
        /// </summary>
        public async Task LogActivityAsync(
            string clockNumber,
            string actionType,
            string actionDetails,
            string status,
            string ipAddress = null,
            string userAgent = null,
            string securityStamp = null,
            string departmentName = null,
            string lineName = null)
        {
            try
            {
                // Ensure none of the parameters are null
                clockNumber = clockNumber ?? "unknown";
                actionType = actionType ?? "Unknown Action";
                actionDetails = actionDetails ?? "No details provided";
                status = status ?? "Unknown";

                // Get IP and user agent from HttpContext if not provided
                ipAddress ??= _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
                userAgent ??= _httpContextAccessor?.HttpContext?.Request?.Headers["User-Agent"].ToString() ?? "unknown";

                // Get session ID from claims if not provided
                if (string.IsNullOrEmpty(securityStamp))
                {
                    securityStamp = _httpContextAccessor?.HttpContext?.User?.FindFirst("SecurityStamp")?.Value;
                }

                // Try to get user details from database if department/line not provided
                if (string.IsNullOrEmpty(departmentName) || string.IsNullOrEmpty(lineName))
                {
                    try
                    {
                        var user = await _context.Users
                            .AsNoTracking()
                            .FirstOrDefaultAsync(u => u.ClockNumber == clockNumber);

                        departmentName ??= user?.Department;
                        lineName ??= user?.Line;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not retrieve user details for activity log");
                    }
                }

                // Convert to South African time (UTC+2)
                DateTime saTime;
                try
                {
                    var saTimeZone = TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time");
                    saTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, saTimeZone);
                }
                catch
                {
                    // Fallback to manual UTC+2 if timezone isn't available
                    saTime = DateTime.UtcNow.AddHours(2);
                }

                var activity = new ActivityLog
                {
                    ClockNumber = clockNumber,
                    ActionType = actionType,
                    ActionDetails = actionDetails,
                    Status = status,
                    CreatedAt = saTime,
                    IPAddress = ipAddress,
                    UserAgent = userAgent,
                    SecurityStamp = securityStamp,
                    DepartmentName = departmentName,
                    LineName = lineName
                };

                _context.ActivityLogs.Add(activity);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Activity logged: User {ClockNumber} performed {ActionType} - {Status}",
                    clockNumber, actionType, status);
            }
            catch (Exception ex)
            {
                // Log the error but don't rethrow - we don't want activity logging to break core functionality
                _logger.LogError(ex, "Error logging activity for user {ClockNumber}, action {ActionType}",
                    clockNumber, actionType);
            }
        }

        /// <summary>
        /// Specialized method for logging login activity
        /// </summary>
        public async Task LogLoginAsync(
            string clockNumber,
            string securityStamp,
            bool isForced = false,
            string status = "Success")
        {
            string actionType = "Login";
            string actionDetails = isForced
                ? "User forced login (overriding existing session)"
                : "User logged in successfully";

            // Get device info for accountability
            var ipAddress = _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString();
            var userAgent = _httpContextAccessor?.HttpContext?.Request?.Headers["User-Agent"].ToString();

            try
            {
                // Try to get department/line from user
                var user = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.ClockNumber == clockNumber);

                await LogActivityAsync(
                    clockNumber,
                    actionType,
                    actionDetails,
                    status,
                    ipAddress,
                    userAgent,
                    securityStamp,
                    user?.Department,
                    user?.Line
                );
            }
            catch (Exception ex)
            {
                // Fall back to minimal logging if detailed logging fails
                _logger.LogError(ex, "Error in specialized login logging for {ClockNumber}", clockNumber);

                await LogActivityAsync(clockNumber, actionType, actionDetails, status);
            }
        }

        /// <summary>
        /// Specialized method for logging logout activity
        /// </summary>
        public async Task LogLogoutAsync(string clockNumber, string securityStamp = null)
        {
            try
            {
                string actionType = "Logout";
                string actionDetails = "User logged out successfully";

                // Get device info for accountability
                var ipAddress = _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString();
                var userAgent = _httpContextAccessor?.HttpContext?.Request?.Headers["User-Agent"].ToString();

                await LogActivityAsync(
                    clockNumber,
                    actionType,
                    actionDetails,
                    "Success",
                    ipAddress,
                    userAgent,
                    securityStamp
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging logout for user {ClockNumber}", clockNumber);
            }
        }

        /// <summary>
        /// Logs critical system operations
        /// </summary>
        public async Task LogSystemOperationAsync(
            string clockNumber,
            string operation,
            string details,
            string status = "Success")
        {
            await LogActivityAsync(
                clockNumber,
                $"System:{operation}",
                details,
                status
            );
        }

        /// <summary>
        /// Logs errors with detailed information
        /// </summary>
        public async Task LogErrorAsync(
            string clockNumber,
            Exception ex,
            string? additionalInfo = null)
        {
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                var user = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.ClockNumber == clockNumber);

                // Convert to South African time (UTC+2)
                DateTime saTime;
                try
                {
                    var saTimeZone = TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time");
                    saTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, saTimeZone);
                }
                catch
                {
                    // Fallback to manual UTC+2 if timezone isn't available
                    saTime = DateTime.UtcNow.AddHours(2);
                }

                var errorLog = new ErrorLog
                {
                    UserId = user?.Id,
                    ClockNumber = clockNumber,
                    ErrorMessage = $"{ex.Message} {additionalInfo}".Trim(),
                    StackTrace = ex.StackTrace,
                    ErrorSource = ex.Source,
                    IPAddress = httpContext?.Connection?.RemoteIpAddress?.ToString(),
                    UserAgent = httpContext?.Request?.Headers["User-Agent"].ToString(),
                    CreatedAt = saTime
                };

                await _context.ErrorLogs.AddAsync(errorLog);
                await _context.SaveChangesAsync();

                // Also log to the activity log for consolidated reporting
                await LogActivityAsync(
                    clockNumber,
                    "System Error",
                    $"Error in {ex.Source}: {ex.Message}",
                    "Error"
                );
            }
            catch (Exception logEx)
            {
                _logger.LogError(logEx, "Error logging error for user {ClockNumber}", clockNumber);
            }
        }

        /// <summary>
        /// Gets recent activities with paging
        /// </summary>
        public async Task<IEnumerable<ActivityLog>> GetRecentActivitiesAsync(int page = 1, int pageSize = 100)
        {
            var skip = (page - 1) * pageSize;
            return await _context.ActivityLogs
                .OrderByDescending(log => log.CreatedAt)
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync();
        }

        /// <summary>
        /// Gets activities for a specific user in a date range
        /// </summary>
        public async Task<IEnumerable<ActivityLog>> GetUserActivityHistoryAsync(
            string clockNumber,
            DateTime startDate,
            DateTime endDate)
        {
            return await _context.ActivityLogs
                .Where(l => l.ClockNumber == clockNumber &&
                           l.CreatedAt >= startDate &&
                           l.CreatedAt <= endDate)
                .OrderByDescending(l => l.CreatedAt)
                .Take(1000)
                .ToListAsync();
        }

        /// <summary>
        /// Gets activity logs with filtering options
        /// </summary>
        public async Task<IEnumerable<ActivityLog>> GetFilteredActivityLogsAsync(
            string clockNumber = null,
            string actionType = null,
            string departmentName = null,
            string lineName = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            int maxResults = 1000)
        {
            IQueryable<ActivityLog> query = _context.ActivityLogs;

            if (!string.IsNullOrEmpty(clockNumber))
                query = query.Where(log => log.ClockNumber == clockNumber);

            if (!string.IsNullOrEmpty(actionType))
                query = query.Where(log => log.ActionType == actionType);

            if (!string.IsNullOrEmpty(departmentName))
                query = query.Where(log => log.DepartmentName == departmentName);

            if (!string.IsNullOrEmpty(lineName))
                query = query.Where(log => log.LineName == lineName);

            if (startDate.HasValue)
                query = query.Where(log => log.CreatedAt >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(log => log.CreatedAt <= endDate.Value);

            return await query
                .OrderByDescending(log => log.CreatedAt)
                .Take(maxResults)
                .ToListAsync();
        }

        /// <summary>
        /// Gets activity logs for a specific user
        /// </summary>
        public async Task<IEnumerable<ActivityLogDto>> GetUserActivityLogsAsync(string clockNumber)
        {
            try
            {
                return await _context.ActivityLogs
                    .Where(log => log.ClockNumber == clockNumber)
                    .OrderByDescending(log => log.CreatedAt)
                    .Select(log => new ActivityLogDto
                    {
                        Id = log.LogId,
                        ActionType = log.ActionType ?? string.Empty,
                        ActionDetails = log.ActionDetails ?? string.Empty,
                        Status = log.Status ?? string.Empty,
                        CreatedAt = log.CreatedAt,
                        IPAddress = log.IPAddress ?? string.Empty,
                        DepartmentName = log.DepartmentName ?? string.Empty,
                        LineName = log.LineName ?? string.Empty,
                        UserId = log.UserId ?? 0
                    })
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving activity logs for user {ClockNumber}", clockNumber);
                throw;
            }
        }

        /// <summary>
        /// Gets error logs for a date range
        /// </summary>
        public async Task<IEnumerable<ErrorLog>> GetErrorLogsAsync(
            DateTime startDate,
            DateTime endDate)
        {
            return await _context.ErrorLogs
                .Where(e => e.CreatedAt >= startDate &&
                           e.CreatedAt <= endDate)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync();
        }
    }
}