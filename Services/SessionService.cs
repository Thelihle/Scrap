using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SCP_Control.Models;
using SCP_Control.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace SCP_Control.Services
{
    /// <summary>
    /// Singleton implementation of ISessionService that uses direct ADO.NET instead of EF Core
    /// </summary>
    public class SessionService : ISessionService
    {
        private readonly ILogger<SessionService> _logger;
        private readonly string _connectionString;

        public SessionService(
            ILogger<SessionService> logger,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }


        public async Task<UserSession> CreateSessionAsync(
     string userIdentifier,
     string securityStamp,
     string ipAddress,
     string userAgent)
        {
            try
            {
                _logger.LogInformation($"Creating session for user {userIdentifier}");

                DateTime saTime;
                try
                {
                    var saTimeZone = TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time");
                    saTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, saTimeZone);
                }
                catch
                {
                    saTime = DateTime.UtcNow.AddHours(2);
                }

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // ✅ FIX: Store userIdentifier in BOTH ClockNumber columns
                    string insertSql = @"
                INSERT INTO [dbo].[scpUserSession] 
                    (SecurityStamp, ClockNumber, CreatedAt, LastActivity, IpAddress, UserAgent, IsActive)
                VALUES 
                    (@SecurityStamp, @UserIdentifier, @CreatedAt, @LastActivity, @IpAddress, @UserAgent, 1)";

                    using (var command = new SqlCommand(insertSql, connection))
                    {
                        command.Parameters.AddWithValue("@SecurityStamp", securityStamp);
                        command.Parameters.AddWithValue("@UserIdentifier", userIdentifier);
                        command.Parameters.AddWithValue("@CreatedAt", saTime);
                        command.Parameters.AddWithValue("@LastActivity", saTime);
                        command.Parameters.AddWithValue("@IpAddress", ipAddress ?? "unknown");
                        command.Parameters.AddWithValue("@UserAgent", userAgent ?? "unknown");

                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation($"✅ Session created for {userIdentifier}");

                        return new UserSession
                        {
                            SecurityStamp = securityStamp,
                            ClockNumber = userIdentifier,
                            CreatedAt = saTime,
                            LastActivity = saTime,
                            IpAddress = ipAddress,
                            UserAgent = userAgent,
                            IsActive = true
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Error creating session for user {userIdentifier}");
                throw;
            }
        }

        public async Task<UserSession> GetActiveSessionAsync(string userIdentifier)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // ✅ FIX: Search by ClockNumber (which stores the userIdentifier)
                const string query = @"
            SELECT TOP 1 
                SecurityStamp,
                ClockNumber,
                CreatedAt,
                LastActivity,
                IpAddress,
                UserAgent,
                IsActive
            FROM [dbo].[scpUserSession]
            WHERE ClockNumber = @UserIdentifier
              AND IsActive = 1
            ORDER BY CreatedAt DESC";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UserIdentifier", userIdentifier);

                using var reader = await command.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                    return null;

                return new UserSession
                {
                    ClockNumber = reader.GetString(reader.GetOrdinal("ClockNumber")),
                    SecurityStamp = reader.GetString(reader.GetOrdinal("SecurityStamp")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                    LastActivity = reader.GetDateTime(reader.GetOrdinal("LastActivity")),
                    IpAddress = reader.IsDBNull(reader.GetOrdinal("IpAddress")) ? null : reader.GetString(reader.GetOrdinal("IpAddress")),
                    UserAgent = reader.IsDBNull(reader.GetOrdinal("UserAgent")) ? null : reader.GetString(reader.GetOrdinal("UserAgent")),
                    IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive"))
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting active session for user {userIdentifier}");
                return null;
            }
        }

        public async Task InvalidateSessionAsync(string userIdentifier)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // ✅ FIX: Update and delete by ClockNumber
                const string query = @"
            UPDATE [dbo].[scpUserSession]
            SET IsActive = 0
            WHERE ClockNumber = @UserIdentifier;
            
            DELETE FROM [dbo].[scpUserSession]
            WHERE ClockNumber = @UserIdentifier";

                await connection.ExecuteAsync(query, new { UserIdentifier = userIdentifier });
                _logger.LogInformation($"✅ All sessions invalidated for {userIdentifier}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Error invalidating sessions for {userIdentifier}");
                throw;
            }
        }

        public async Task<bool> ValidateSessionAsync(string userIdentifier, string securityStamp)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                const string query = @"
            SELECT COUNT(1) 
            FROM [dbo].[scpUserSession]
            WHERE ClockNumber = @UserIdentifier
            AND SecurityStamp = @SecurityStamp
            AND IsActive = 1
            AND LastActivity > DATEADD(HOUR, -12, GETUTCDATE())";

                int count = await connection.ExecuteScalarAsync<int>(
                    query,
                    new { UserIdentifier = userIdentifier, SecurityStamp = securityStamp });

                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating session for {userIdentifier}");
                throw;
            }
        }

        public async Task<bool> IsLatestSessionAsync(string userIdentifier, string securityStamp)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                string query = @"
            SELECT TOP 1 SecurityStamp 
            FROM [dbo].[scpUserSession] 
            WHERE ClockNumber = @UserIdentifier 
              AND IsActive = 1 
            ORDER BY CreatedAt DESC";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@UserIdentifier", userIdentifier);

                var result = await command.ExecuteScalarAsync();
                if (result == null) return false;

                return result.ToString() == securityStamp;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking latest session for {userIdentifier}");
                return false;
            }
        }










        public async Task UpdateSessionActivityAsync(String securityStamp)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string updateSql = @"
                    UPDATE [dbo].[scpUserSession] 
                    SET LastActivity = @LastActivity 
                    WHERE SecurityStamp = @SecurityStamp AND IsActive = 1";

                    using (var command = new SqlCommand(updateSql, connection))
                    {
                        // Get current time in South Africa (UTC+2)
                        DateTime saTime;
                        try
                        {
                            var saTimeZone = TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time");
                            saTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, saTimeZone);
                        }
                        catch
                        {
                            // Fallback to manual offset if timezone not available
                            saTime = DateTime.UtcNow.AddHours(2);
                        }

                        command.Parameters.AddWithValue("@LastActivity", saTime);
                        command.Parameters.AddWithValue("@SecurityStamp", securityStamp);

                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating session activity for session {securityStamp}");
                // Don't throw - this is a background operation
            }
        }

        public async Task CleanupExpiredSessionsAsync(TimeSpan sessionExpiry)
        {
            try
            {
                _logger.LogInformation($"Cleaning up expired sessions (expiry: {sessionExpiry.TotalHours} hours)");

                // Get current time in South Africa (UTC+2)
                DateTime saTimeNow;
                try
                {
                    var saTimeZone = TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time");
                    saTimeNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, saTimeZone);
                }
                catch
                {
                    // Fallback to manual offset if timezone not available
                    saTimeNow = DateTime.UtcNow.AddHours(2);
                }

                // Calculate the cutoff time for expired sessions
                DateTime cutoffTime = saTimeNow.Subtract(sessionExpiry);

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // First, identify expired sessions
                    string selectSql = @"
                        SELECT SecurityStamp, ClockNumber 
                        FROM [dbo].[scpUserSession] 
                        WHERE IsActive = 1 
                        AND LastActivity < @CutoffTime";

                    var expiredSessions = new List<(string SecurityStamp, string ClockNumber)>();

                    using (var command = new SqlCommand(selectSql, connection))
                    {
                        command.Parameters.AddWithValue("@CutoffTime", cutoffTime);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                expiredSessions.Add((
                                    reader.GetString(reader.GetOrdinal("SecurityStamp")),
                                    reader.GetString(reader.GetOrdinal("ClockNumber"))
                                ));
                            }
                        }
                    }

                    _logger.LogInformation($"Found {expiredSessions.Count} expired sessions to clean up");

                    if (expiredSessions.Count > 0)
                    {
                        // Now update them all to inactive
                        string updateSql = @"
                            UPDATE [dbo].[scpUserSession]
                            SET IsActive = 0,
                                EndedAt = @EndedAt
                            WHERE SecurityStamp = @SecurityStamp";

                        foreach (var session in expiredSessions)
                        {
                            using (var command = new SqlCommand(updateSql, connection))
                            {
                                command.Parameters.AddWithValue("@EndedAt", saTimeNow);
                                command.Parameters.AddWithValue("@SecurityStamp", session.SecurityStamp);

                                await command.ExecuteNonQueryAsync();

                                _logger.LogInformation($"Cleaned up expired session {session.SecurityStamp} for user {session.ClockNumber}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up expired sessions");
                // Don't throw - this is a background operation
            }
        }
    }
}