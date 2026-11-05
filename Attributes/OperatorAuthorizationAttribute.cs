// SCP_Control/Attributes/OperatorAuthorizationAttribute.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SCP_Control.Attributes
{
    public class OperatorAuthorizationAttribute : TypeFilterAttribute
    {
        public OperatorAuthorizationAttribute() : base(typeof(OperatorAuthorizationFilter))
        {
        }
    }

    public class OperatorAuthorizationFilter : IAuthorizationFilter
    {
        private readonly ILogger<OperatorAuthorizationFilter> _logger;

        public OperatorAuthorizationFilter(ILogger<OperatorAuthorizationFilter> logger)
        {
            _logger = logger;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (!context.HttpContext.User.Identity?.IsAuthenticated == true)
            {
                _logger.LogWarning("Unauthorized access attempt");
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            if (!context.HttpContext.User.IsInRole("Operator"))
            {
                _logger.LogWarning("Non-operator access attempt");
                context.Result = new RedirectToActionResult("AccessDenied", "Account", null);
                return;
            }
        }
    }
}