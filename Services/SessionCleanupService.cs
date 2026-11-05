using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SCP_Control.Services.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SCP_Control.Services
{
    public class SessionCleanupService : BackgroundService
    {
        private readonly ILogger<SessionCleanupService> _logger;
        private readonly ISessionService _sessionService;
        private readonly TimeSpan _cleanupInterval;
        private readonly TimeSpan _sessionExpiry;

        public SessionCleanupService(
            ILogger<SessionCleanupService> logger,
            ISessionService sessionService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));

            // Run cleanup every 30 minutes
            _cleanupInterval = TimeSpan.FromMinutes(30);

            // Consider sessions expired after 12 hours of inactivity
            _sessionExpiry = TimeSpan.FromHours(12);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Session cleanup service is starting");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Running scheduled session cleanup");

                    try
                    {
                        await _sessionService.CleanupExpiredSessionsAsync(_sessionExpiry);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during scheduled session cleanup");
                    }

                    // Wait for the next interval
                    await Task.Delay(_cleanupInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Session cleanup service is stopping due to cancellation");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in session cleanup service");
            }
            finally
            {
                _logger.LogInformation("Session cleanup service has stopped");
            }
        }
    }
}