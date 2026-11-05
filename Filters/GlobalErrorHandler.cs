using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SCP_Control.ViewModels;
using System.Diagnostics;

namespace SCP_Control.Filters
{
    public class GlobalErrorHandler : IExceptionFilter
    {
        private readonly ILogger<GlobalErrorHandler> _logger;
        private readonly IHostEnvironment _env;

        public GlobalErrorHandler(
            ILogger<GlobalErrorHandler> logger,
            IHostEnvironment env)
        {
            _logger = logger;
            _env = env;
        }

        public void OnException(ExceptionContext context)
        {
            var exception = context.Exception;
            _logger.LogError(exception, "Unhandled exception occurred");

            var errorModel = new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier,
                Message = _env.IsDevelopment()
                    ? $"{exception.Message}\n{exception.StackTrace}"
                    : "An error occurred while processing your request."
            };

            // Create a new ViewResult with the model
            var result = new ViewResult
            {
                ViewName = "Error",
                ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary<ErrorViewModel>(
                    new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                    new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary())
            };
            result.ViewData.Model = errorModel;

            if (_env.IsDevelopment())
            {
                result.ViewData["Exception"] = exception;
            }

            context.Result = result;
            context.ExceptionHandled = true;
        }
    }
}