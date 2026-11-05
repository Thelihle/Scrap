using Microsoft.AspNetCore.Mvc;

namespace SCP_Control.Controllers
{
    public class ErrorController : Controller
    {
        [Route("Error/{statusCode}")]
        public IActionResult HttpStatusCodeHandler(int statusCode)
        {
            var errorMessage = statusCode switch
            {
                404 => "Page not found",
                401 => "Unauthorized access",
                _ => "An error occurred"
            };

            ViewData["ErrorMessage"] = errorMessage;
            return View("Error");
        }

        [Route("Error")]
        public IActionResult Error()
        {
            ViewData["ErrorMessage"] = "An unexpected error occurred";
            return View("Error");
        }
    }
}