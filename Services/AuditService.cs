// Services/AuditService.cs
using SCP_Control.Services.Interfaces;
using SCP_Control.DbContext;
using SCP_Control.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

namespace SCP_Control.Services
{
    public class AuditService : IAuditService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AuditService> _logger;

        public AuditService(
            ApplicationDbContext context,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AuditService> logger)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }
        public async Task<IEnumerable<AuditLogDto>> GetUserAuditLogsAsync(string clockNumber)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.ClockNumber == clockNumber);

                if (user == null)
                    return Enumerable.Empty<AuditLogDto>();

                return await _context.AuditLogs
                    .Where(log => log.UserId == user.Id) // Changed from UserId to Id
                    .OrderByDescending(log => log.Timestamp)
                    .Select(log => new AuditLogDto
                    {
                        Action = log.Action,
                        Details = log.Details,
                        Timestamp = log.Timestamp,
                        Department = log.Department,
                        Line = log.Line
                    })
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving audit logs for user {ClockNumber}", clockNumber);
                throw;
            }
        }

        public async Task LogAuditAsync(string clockNumber, string action, string details)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.ClockNumber == clockNumber);

                var auditLog = new AuditLog
                {
                    UserId = user?.Id,
                    ClockNumber = clockNumber,
                    Action = action,
                    Details = details,
                    Timestamp = DateTime.UtcNow,
                    Department = user?.Department,
                    Line = user?.Line
                };

                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating audit log for user {ClockNumber}", clockNumber);
                throw;
            }
        }

        public async Task LogActivityAsync(string clockNumber, string actionType, string actionDetails, string status)
        {
            try
            {
                var context = _httpContextAccessor.HttpContext;
                var user = await _context.Users.FirstOrDefaultAsync(u => u.ClockNumber == clockNumber);

                var activity = new ActivityLog
                {
                    UserId = user?.Id,
                    ClockNumber = clockNumber,
                    ActionType = actionType,
                    ActionDetails = actionDetails,
                    Status = status,
                    IPAddress = context?.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = context?.Request.Headers["User-Agent"].ToString(),
                    SecurityStamp = context?.Session.Id
                };

                _context.ActivityLogs.Add(activity);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging activity for user {ClockNumber}", clockNumber);
            }
        }

        public async Task LogErrorAsync(string clockNumber, Exception ex)
        {
            try
            {
                var context = _httpContextAccessor.HttpContext;
                var user = await _context.Users.FirstOrDefaultAsync(u => u.ClockNumber == clockNumber);

                var error = new ErrorLog
                {
                    UserId = user?.Id,
                    ClockNumber = clockNumber,
                    ErrorMessage = ex.Message,
                    StackTrace = ex.StackTrace,
                    ErrorSource = ex.Source,
                    IPAddress = context?.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = context?.Request.Headers["User-Agent"].ToString()
                };

                _context.ErrorLogs.Add(error);
                await _context.SaveChangesAsync();
            }
            catch (Exception logEx)
            {
                _logger.LogError(logEx, "Error logging error for user {ClockNumber}", clockNumber);
            }
        }
    }
}