using SCP_Control.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SCP_Control.Services.Interfaces
{
    public interface IActivityLogService
    {
        /// <summary>
        /// Logs user activity with comprehensive details
        /// </summary>
        Task LogActivityAsync(
            string clockNumber,
            string actionType,
            string actionDetails,
            string status,
            string ipAddress = null,
            string userAgent = null,
            string securityStamp = null,
            string departmentName = null,
            string lineName = null);

        /// <summary>
        /// Specialized method for logging login activity
        /// </summary>
        Task LogLoginAsync(
            string clockNumber,
            string securityStamp,
            bool isForced = false,
            string status = "Success");

        /// <summary>
        /// Specialized method for logging logout activity
        /// </summary>
        Task LogLogoutAsync(string clockNumber, string securityStamp = null);

        /// <summary>
        /// Logs critical system operations
        /// </summary>
        Task LogSystemOperationAsync(
            string clockNumber,
            string operation,
            string details,
            string status = "Success");

        /// <summary>
        /// Logs errors with detailed information
        /// </summary>
        Task LogErrorAsync(
            string clockNumber,
            Exception ex,
            string additionalInfo = null);

        /// <summary>
        /// Gets recent activities with paging
        /// </summary>
        Task<IEnumerable<ActivityLog>> GetRecentActivitiesAsync(int page = 1, int pageSize = 100);

        /// <summary>
        /// Gets activities for a specific user in a date range
        /// </summary>
        Task<IEnumerable<ActivityLog>> GetUserActivityHistoryAsync(
            string clockNumber,
            DateTime startDate,
            DateTime endDate);

        /// <summary>
        /// Gets activity logs with filtering options
        /// </summary>
        Task<IEnumerable<ActivityLog>> GetFilteredActivityLogsAsync(
            string clockNumber = null,
            string actionType = null,
            string departmentName = null,
            string lineName = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            int maxResults = 1000);

        /// <summary>
        /// Gets activity logs for a specific user
        /// </summary>
        Task<IEnumerable<ActivityLogDto>> GetUserActivityLogsAsync(string clockNumber);

        /// <summary>
        /// Gets error logs for a date range
        /// </summary>
        Task<IEnumerable<ErrorLog>> GetErrorLogsAsync(
            DateTime startDate,
            DateTime endDate);
    }
}