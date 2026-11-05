using SCP_Control.Models;
using System;
using System.Threading.Tasks;

namespace SCP_Control.Services.Interfaces
{
    public interface ISessionService
    {
        /// <summary>
        /// Creates a new user session
        /// </summary>
        Task<UserSession> CreateSessionAsync(
            string userIdentifier,
            string securityStamp,
            string ipAddress,
            string userAgent
        );

        /// <summary>
        /// Validates if a session is active
        /// </summary>
        Task<bool> ValidateSessionAsync(
            string userIdentifier,
            string securityStamp
        );

        /// <summary>
        /// Checks if the given session is the latest session for the user
        /// </summary>
        Task<bool> IsLatestSessionAsync(
            string userIdentifier,
            string securityStamp
        );

        /// <summary>
        /// Invalidates all active sessions for a user
        /// </summary>
        Task InvalidateSessionAsync(string userIdentifier);

        /// <summary>
        /// Gets the active session for a user
        /// </summary>
        Task<UserSession?> GetActiveSessionAsync(string userIdentifier);

        /// <summary>
        /// Updates the LastActivity timestamp of a session
        /// </summary>
        Task UpdateSessionActivityAsync(string securityStamp);

        /// <summary>
        /// Cleans up expired sessions in the database
        /// </summary>
        Task CleanupExpiredSessionsAsync(TimeSpan sessionExpiry);
    }
}
