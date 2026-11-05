using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SCP_Control.DbContext;
using SCP_Control.Models;

namespace SCP_Control.Middleware
{
    public class PasswordChangeRequiredMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<PasswordChangeRequiredMiddleware> _logger;

        public PasswordChangeRequiredMiddleware(
            RequestDelegate next,
            ILogger<PasswordChangeRequiredMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
        {
            try
            {
                if (context.User.Identity?.IsAuthenticated == true)
                {
                    var username = context.User.Identity.Name;

                    // Skip check for these paths to avoid infinite loops
                    var skipPaths = new[] {
                        "/Account/ChangePassword",
                        "/Account/Logout",
                        "/Account/HandlePasswordChange"
                    };

                    if (!skipPaths.Any(p => context.Request.Path.StartsWithSegments(p)))
                    {
                        var user = await dbContext.Users
                            .AsNoTracking()
                            .Where(u => u.ClockNumber == username)
                            .FirstOrDefaultAsync();

                        if (user?.PasswordResetRequired == true)
                        {
                            _logger.LogInformation(
                                "User {Username} requires password change, redirecting to change password page",
                                username);

                            context.Response.Redirect("/Account/ChangePassword");
                            return;
                        }
                    }
                }

                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PasswordChangeRequiredMiddleware");
                await _next(context);
            }
        }
    }

    // Extension method to make middleware registration cleaner
    public static class PasswordChangeRequiredMiddlewareExtensions
    {
        public static IApplicationBuilder UsePasswordChangeRequired(
            this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<PasswordChangeRequiredMiddleware>();
        }
    }
}