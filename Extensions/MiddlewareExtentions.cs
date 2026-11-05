using Microsoft.AspNetCore.Builder;
using SCP_Control.Middleware;

namespace SCP_Control.Extensions;

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseCustomAntiForgery(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AntiForgeryMiddleware>();
    }
}