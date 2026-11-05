using System.Security.Claims;

namespace SCP_Control.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        public static string? GetClockNumber(this ClaimsPrincipal user)
        {
            return user?.FindFirstValue(ClaimTypes.NameIdentifier);
        }
    }

    public static class StringExtensions
    {
        public static string ToSafeString(this string? value)
        {
            return value ?? string.Empty;
        }
    }
}
