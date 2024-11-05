using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Data.SqlClient;
using Dapper;
using SCP_Control.Models;
using SCP_Control.ViewModels;

namespace SCP_Control.Controllers
{
    public class AccountController : Controller
    {
        private readonly string _connectionString;
        private readonly ILogger<AccountController> _logger;

        public AccountController(IConfiguration configuration, ILogger<AccountController> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new InvalidOperationException("Connection string not found.");
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            var model = new LoginViewModel { ReturnUrl = returnUrl };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return Json(new { success = false, message = "Please fill in all required fields." });
                }

                using var connection = new SqlConnection(_connectionString);
                var query = @"
                    SELECT * FROM Users 
                    WHERE ClockNumber = @ClockNumber 
                    AND Password = @Password 
                    AND UserType IN ('Operator', 'TeamLeader')
                    AND IsActive = 1";

                var user = await connection.QuerySingleOrDefaultAsync<User>(
                    query, new { model.ClockNumber, model.Password });

                if (user == null)
                {
                    return Json(new { success = false, message = "Invalid login credentials." });
                }

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.ClockNumber),
                    new Claim(ClaimTypes.Role, user.UserType),
                    new Claim("Department", user.Department ?? string.Empty),
                    new Claim("Line", user.Line ?? string.Empty),
                    new Claim("Shift", user.Shift ?? string.Empty)
                };

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties { IsPersistent = true });

                return Json(new { success = true, redirectUrl = GetRedirectUrl(user) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login error for user {ClockNumber}", model.ClockNumber);
                return Json(new { success = false, message = "An error occurred during login." });
            }
        }

        private static string GetRedirectUrl(User user)
        {
            if (string.IsNullOrEmpty(user.Department) ||
                string.IsNullOrEmpty(user.Line) ||
                string.IsNullOrEmpty(user.Shift))
            {
                return "/Operator/Setup";
            }

            return user.UserType.ToLower() switch
            {
                "operator" => "/Operator/Index",
                "teamleader" => "/Team_Leader/Index",
                _ => "/Home/Index"
            };
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }
    }
}