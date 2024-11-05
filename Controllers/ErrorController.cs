using Microsoft.AspNetCore.Mvc;

namespace SCP_Control.Controllers
{
    public class ErrorController : Controller
    {
        [Route("Error/{statusCode}")]
        public IActionResult HttpStatusCodeHandler(int statusCode)
        {
            switch (statusCode)
            {
                case 404:
                    ViewBag.ErrorMessage = "Sorry, the resource you requested could not be found";
                    ViewBag.ErrorTitle = "Page Not Found";
                    ViewBag.ErrorCode = "404";
                    break;
                case 500:
                    ViewBag.ErrorMessage = "An error occurred while processing your request";
                    ViewBag.ErrorTitle = "Server Error";
                    ViewBag.ErrorCode = "500";
                    break;
                default:
                    ViewBag.ErrorMessage = "An error occurred while processing your request";
                    ViewBag.ErrorTitle = "Error";
                    ViewBag.ErrorCode = statusCode.ToString();
                    break;
            }
            return View("Error");
        }

        [Route("Error/Network")]
        public IActionResult NetworkError()
        {
            ViewBag.ErrorMessage = "Unable to connect to the server. Please check your network connection.";
            ViewBag.ErrorTitle = "Network Error";
            ViewBag.ErrorCode = "NET";
            return View("Error");
        }
    }
}