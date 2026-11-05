using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using SCP_Control.Services.Interfaces;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SCP_Control.Middleware
{
    public class SessionActivityMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SessionActivityMiddleware> _logger;

        public SessionActivityMiddleware(RequestDelegate next, ILogger<SessionActivityMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InvokeAsync(HttpContext context, ISessionService sessionService)
        {
            // Skip session check for certain paths and static files
            if (ShouldSkipRequest(context))
            {
                await _next(context);
                return;
            }

            var user = context.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            var clockNumber = user.Identity?.Name;
            if (string.IsNullOrEmpty(clockNumber))
            {
                _logger.LogWarning("Authenticated user with empty Name claim detected");
                await _next(context);
                return;
            }

            // Get session ID from claims
            var securityStampClaim = user.FindFirst("SecurityStamp");
            if (securityStampClaim == null)
            {
                _logger.LogWarning($"No SecurityStamp claim found for user {clockNumber}");
                await HandleInvalidSession(context);
                return;
            }

            // Try to parse the securityStamp to Guid
            var securityStamp = securityStampClaim.Value;

            if (string.IsNullOrWhiteSpace(securityStamp))
            {
                _logger.LogWarning($"Empty SecurityStamp claim for user {clockNumber}");
                await HandleInvalidSession(context);
                return;
            }


            // Check if the session is valid
            try
            {
                // First, check if this is the MOST RECENT session for this user
                var isLatestSession = await sessionService.IsLatestSessionAsync(clockNumber, securityStamp);
                if (!isLatestSession)
                {
                    _logger.LogWarning($"Session {securityStamp} is not the latest session for user {clockNumber}");
                    await HandleInvalidSession(context, true);
                    return;
                }

                // Now check if the session is valid
                var isValid = await sessionService.ValidateSessionAsync(clockNumber, securityStamp);
                if (!isValid)
                {
                    _logger.LogWarning($"Invalid session {securityStamp} for user {clockNumber}");
                    await HandleInvalidSession(context);
                    return;
                }

                // Update the LastActivity timestamp in UserSessions table
                try
                {
                    await sessionService.UpdateSessionActivityAsync(securityStamp);
                }
                catch (Exception ex)
                {
                    // Log but don't fail the request if activity update fails
                    _logger.LogError(ex, $"Error updating session activity for session {securityStamp}");
                }

                // Continue with the request
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating session for user {clockNumber}");
                await HandleInvalidSession(context);
            }
        }

        private bool ShouldSkipRequest(HttpContext context)
        {
            var path = context.Request.Path.Value?.ToLower() ?? "";

            // Skip authentication/session-related paths
            if (path.StartsWith("/account/login") ||
                path.StartsWith("/account/forcelogin") ||
                path.StartsWith("/account/directforcelogin") ||
                path.StartsWith("/account/logout") ||
                path.StartsWith("/account/register") ||
                path.StartsWith("/account/userregistrationrequest"))
            {
                return true;
            }

            // Skip static files
            if (path.StartsWith("/css/") ||
                path.StartsWith("/js/") ||
                path.StartsWith("/lib/") ||
                path.StartsWith("/images/") ||
                path.StartsWith("/favicon.ico"))
            {
                return true;
            }

            // Skip health checks or API endpoints if needed
            if (path.StartsWith("/api/health") ||
                path.StartsWith("/health"))
            {
                return true;
            }

            return false;
        }

        private async Task HandleInvalidSession(HttpContext context, bool wasSuperseded = false)
        {
            // Sign out the current user
            try
            {
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error signing out user with invalid session");
            }

            // Check if this is an AJAX request
            bool isAjaxRequest = context.Request.Headers["X-Requested-With"] == "XMLHttpRequest";

            if (isAjaxRequest)
            {
                // For AJAX requests, return 401 with a JSON response
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";

                string message = wasSuperseded
                    ? "{\"redirectUrl\":\"/Account/Login?sessionExpired=true\",\"message\":\"Your session was invalidated by a login from another device.\"}"
                    : "{\"redirectUrl\":\"/Account/Login?sessionExpired=true\"}";

                await context.Response.WriteAsync(message);
            }
            else
            {
                // For regular requests, do a proper redirect with an appropriate message
                string redirectUrl = wasSuperseded
                    ? "/Account/Login?sessionExpired=true&sessionSuperseded=true"
                    : "/Account/Login?sessionExpired=true";

                context.Response.Redirect(redirectUrl, false);
                await context.Response.CompleteAsync();
            }
        }
    }
}