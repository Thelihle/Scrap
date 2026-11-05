using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using SCP_Control.Services.Interfaces;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SCP_Control.Middleware
{
    public class SessionValidationMiddleware
    {
        private readonly RequestDelegate _next;

        public SessionValidationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, ISessionService sessionService, ILogger<SessionValidationMiddleware> logger)
        {
            // Skip validation for non-authenticated requests or login/logout paths
            string path = context.Request.Path.Value?.ToLower() ?? "";
            if (!context.User.Identity.IsAuthenticated ||
                path.StartsWith("/account/login") ||
                path.StartsWith("/account/loginpost") ||  // Add this
                path.StartsWith("/account/logout") ||
                path.StartsWith("/account/clearusersessions") ||  // ADD THIS LINE
                path.StartsWith("/account/userregistrationrequest") ||  // Add this too
                path.Contains("/lib/") ||
                path.Contains("/css/") ||
                path.Contains("/js/") ||
                path.Contains("/images/"))
            {
                await _next(context);
                return;
            }

            try
            {
                // Get the current user's identity
                string userKey = context.User.FindFirst(ClaimTypes.Name)?.Value;



                // Get SecurityStamp from claims
                var securityStamp = context.User.FindFirst("SecurityStamp")?.Value;

                if (string.IsNullOrEmpty(userKey))
                {
                    logger.LogWarning("Session validation failed: Missing user identity");
                    await RedirectToLogin(context, "sessionExpired=true");
                    return;
                }

                logger.LogDebug($"Validating session for userKey={userKey}, securityStamp={securityStamp}");

                bool isValidSession = await sessionService.ValidateSessionAsync(userKey, securityStamp);
                if (!isValidSession)
                {
                    logger.LogWarning($"Session validation failed: Session {securityStamp} not found or inactive for user {userKey}");
                    await RedirectToLogin(context, "sessionExpired=true");
                    return;
                }

                bool isLatestSession = await sessionService.IsLatestSessionAsync(userKey, securityStamp);
                if (!isLatestSession)
                {
                    logger.LogWarning($"Session validation failed: Session {securityStamp} for user {userKey} is not the latest");
                    await RedirectToLogin(context, "sessionExpired=true&sessionSuperseded=true");
                    return;
                }


                // Update the session activity
                await sessionService.UpdateSessionActivityAsync(securityStamp);

                // Continue with the request
                await _next(context);




            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in session validation middleware");

                // On error, proceed to next middleware to avoid breaking the application
                await _next(context);
            }
        }

        private async Task RedirectToLogin(HttpContext context, string queryParams)
        {
            // Sign the user out
            await context.SignOutAsync();

            // If AJAX request, return 401 with redirect URL
            if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync($"{{\"redirectUrl\": \"/Account/Login?{queryParams}\"}}");
                return;
            }

            // Otherwise perform a redirect
            context.Response.Redirect($"/Account/Login?{queryParams}");
        }
    }

    // Extension method to use the middleware
    public static class SessionValidationMiddlewareExtensions
    {
        public static IApplicationBuilder UseSessionValidation(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<SessionValidationMiddleware>();
        }
    }
}