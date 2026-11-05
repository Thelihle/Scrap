using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SCP_Control.Middleware;

public class AntiForgeryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IAntiforgery _antiforgery;
    private readonly ILogger<AntiForgeryMiddleware> _logger;

    public AntiForgeryMiddleware(
        RequestDelegate next,
        IAntiforgery antiforgery,
        ILogger<AntiForgeryMiddleware> logger)
    {
        _next = next;
        _antiforgery = antiforgery;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Cookies.ContainsKey("XSRF-TOKEN") && ShouldGenerateToken(context))
        {
            try
            {
                var tokens = _antiforgery.GetAndStoreTokens(context);
                context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken,
                    new CookieOptions
                    {
                        HttpOnly = false,
                        Secure = true,
                        SameSite = SameSiteMode.Strict,
                        Path = "/",
                        IsEssential = true
                    });
                _logger.LogDebug("Generated new antiforgery token for path: {Path}", context.Request.Path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating antiforgery token");
            }
        }

        await _next(context);
    }

    private bool ShouldGenerateToken(HttpContext context)
    {
        // Skip token generation for non-HTML requests and specific paths
        if (!context.Request.Method.Equals(HttpMethods.Get, StringComparison.OrdinalIgnoreCase))
            return false;

        var acceptHeader = context.Request.Headers.Accept.ToString().ToLowerInvariant();
        if (!acceptHeader.Contains("text/html"))
            return false;

        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Skip static files, API endpoints, and AJAX requests
        return !path.StartsWith("/api/") &&
               !path.StartsWith("/lib/") &&
               !path.StartsWith("/css/") &&
               !path.StartsWith("/js/") &&
               !path.StartsWith("/images/") &&
               !path.Contains(".") && // Skip files with extensions
               !context.Request.Headers["X-Requested-With"].ToString()
                   .Equals("XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
    }
}