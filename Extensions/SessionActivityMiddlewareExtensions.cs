using Microsoft.AspNetCore.Builder;
using SCP_Control.Middleware;

namespace SCP_Control.Extensions
{
    public static class SessionActivityMiddlewareExtensions
    {
        /// <summary>
        /// Adds the session activity middleware to the request pipeline
        /// </summary>
        public static IApplicationBuilder UseSessionActivity(this IApplicationBuilder app)
        {
            return app.UseMiddleware<SessionActivityMiddleware>();
        }
    }
}