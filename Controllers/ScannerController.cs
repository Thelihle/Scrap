using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SCP_Control.DbContext;
using SCP_Control.Models;
using SCP_Control.Services.Interfaces;
using System.Security.Claims;

namespace SCP_Control.Controllers
{
    [AllowAnonymous] // Allow anonymous access for scanner authentication
    public class ScannerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuthService _authService;
        private readonly ILogger<ScannerController> _logger;

        public ScannerController(ApplicationDbContext context, IAuthService authService, ILogger<ScannerController> logger)
        {
            _context = context;
            _authService = authService;
            _logger = logger;
        }

        // GET: Scanner/RoleSelection
        public async Task<IActionResult> RoleSelection(int scannerId, string returnUrl = null)
        {
            try
            {
                var scanner = await _context.ScannerDevices.FindAsync(scannerId);
                if (scanner == null || !scanner.IsActive)
                {
                    _logger.LogWarning("Invalid or inactive scanner ID: {ScannerId}", scannerId);
                    return View("ScannerError", "Scanner device not found or inactive.");
                }

                var model = new ScannerRoleSelectionViewModel
                {
                    ScannerDeviceId = scanner.Id,
                    ScannerDeviceName = scanner.DeviceName ?? "Unknown Scanner",
                    ScannerLocation = scanner.Location ?? "Unknown Location",
                    ReturnUrl = returnUrl ?? GetRedirectPathForRole(scanner.DefaultRole ?? "Operator"),
                    ShowQuickOperatorAccess = (scanner.DefaultRole == "Operator" || scanner.DefaultRole == null)
                };

                ViewBag.Scanner = scanner;
                _logger.LogInformation("Scanner role selection loaded for {DeviceName} (ID: {ScannerId})",
                    scanner.DeviceName ?? "Unknown", scannerId);

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading scanner role selection for scanner ID {ScannerId}", scannerId);
                return View("ScannerError", "An error occurred while loading the scanner interface. Please try again.");
            }
        }

        // POST: Scanner/Authenticate
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Authenticate(ScannerRoleSelectionViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var scanner = await _context.ScannerDevices.FindAsync(model.ScannerDeviceId);
                ViewBag.Scanner = scanner;
                return View("RoleSelection", model);
            }

            try
            {
                var scanner = await _context.ScannerDevices.FindAsync(model.ScannerDeviceId);
                if (scanner == null || !scanner.IsActive)
                {
                    ModelState.AddModelError("", "Scanner device not found or inactive.");
                    return View("RoleSelection", model);
                }

                // Authenticate user manually (replace with your actual authentication logic)
                var authResult = await AuthenticateUserManually(model.ClockNumber, model.Password);

                if (authResult.IsSuccess)
                {
                    // Get user's available roles and determine best role
                    var userRole = await DetermineUserRole(model.ClockNumber, model.SelectedRole);
                    var redirectPath = GetRedirectPathForRole(userRole);

                    // Create scanner session
                    var securityStamp = Guid.NewGuid().ToString();
                    HttpContext.Session.SetString("ScannerSecurityStamp", securityStamp);
                    HttpContext.Session.SetString("ScannerUserId", model.ClockNumber);
                    HttpContext.Session.SetString("ScannerRole", userRole);
                    HttpContext.Session.SetString("ScannerDeviceId", scanner.Id.ToString());

                    // Log successful scanner authentication
                    var accessLog = new ScannerAccessLog
                    {
                        ScannerDeviceId = scanner.Id,
                        AccessPath = "/Scanner/Authenticate",
                        AccessDateTime = DateTime.UtcNow,
                        UserAgent = Request.Headers["User-Agent"].ToString(),
                        UserClockNumber = model.ClockNumber,
                        UserRole = userRole,
                        AccessType = "FullAuth",
                        WasSuccessful = true,
                        Notes = $"Full authentication successful - User {model.ClockNumber} with role {userRole} from scanner {scanner.DeviceName} at {scanner.Location}"
                    };

                    _context.ScannerAccessLogs.Add(accessLog);

                    // Update scanner stats
                    scanner.LastAccessDate = DateTime.UtcNow;
                    scanner.LastAccessUser = model.ClockNumber;
                    scanner.AccessCount++;

                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Scanner authentication successful: User {ClockNumber} with role {Role} from scanner {ScannerName}",
                        model.ClockNumber, userRole, scanner.DeviceName);

                    // Redirect to appropriate dashboard
                    return Redirect(model.ReturnUrl ?? redirectPath);
                }
                else
                {
                    ModelState.AddModelError("", "Invalid clock number or password.");

                    // Log failed authentication
                    var failedLog = new ScannerAccessLog
                    {
                        ScannerDeviceId = scanner.Id,
                        AccessPath = "/Scanner/Authenticate",
                        AccessDateTime = DateTime.UtcNow,
                        UserAgent = Request.Headers["User-Agent"].ToString(),
                        UserClockNumber = model.ClockNumber,
                        UserRole = null,
                        AccessType = "FullAuth",
                        WasSuccessful = false,
                        Notes = $"Failed authentication attempt for clock number {model.ClockNumber} from scanner {scanner.DeviceName} at {scanner.Location}"
                    };

                    _context.ScannerAccessLogs.Add(failedLog);
                    await _context.SaveChangesAsync();

                    _logger.LogWarning("Scanner authentication failed: Clock number {ClockNumber} from scanner {ScannerName}",
                        model.ClockNumber, scanner.DeviceName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scanner authentication for clock number {ClockNumber}", model.ClockNumber);
                ModelState.AddModelError("", "An error occurred during authentication. Please try again.");
            }

            var scannerForView = await _context.ScannerDevices.FindAsync(model.ScannerDeviceId);
            ViewBag.Scanner = scannerForView;
            return View("RoleSelection", model);
        }

        // POST: Scanner/QuickOperatorAccess (for operators who don't want to enter credentials)
        [HttpPost]
        public async Task<IActionResult> QuickOperatorAccess(int scannerId)
        {
            try
            {
                var scanner = await _context.ScannerDevices.FindAsync(scannerId);
                if (scanner == null || !scanner.IsActive || (scanner.DefaultRole != "Operator" && scanner.DefaultRole != null)) //|| !(scanner.AllowQuickAccess ?? false))
                {
                    return Json(new { success = false, message = "Quick operator access not available for this scanner." });
                }

                // Create anonymous operator session
                var securityStamp = Guid.NewGuid().ToString();
                var operatorUserId = $"Operator_{scanner.DeviceName ?? "Unknown"}_{DateTime.Now:HHmmss}";

                HttpContext.Session.SetString("ScannerSecurityStamp", securityStamp);
                HttpContext.Session.SetString("ScannerUserId", operatorUserId);
                HttpContext.Session.SetString("ScannerRole", "Operator");
                HttpContext.Session.SetString("ScannerDeviceId", scanner.Id.ToString());

                // Log quick access with NULL safety
                var accessLog = new ScannerAccessLog
                {
                    ScannerDeviceId = scanner.Id,
                    AccessPath = "/Scanner/QuickOperatorAccess",
                    AccessDateTime = DateTime.UtcNow,
                    UserAgent = Request.Headers["User-Agent"].ToString(),
                    UserClockNumber = operatorUserId,
                    UserRole = "Operator",
                    AccessType = "QuickAccess",
                    WasSuccessful = true,
                    Notes = $"Quick operator access from scanner {scanner.DeviceName ?? "Unknown"} at {scanner.Location ?? "Unknown"}"
                };

                _context.ScannerAccessLogs.Add(accessLog);
                scanner.LastAccessDate = DateTime.UtcNow;
                scanner.LastAccessUser = operatorUserId;
                scanner.AccessCount++;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Quick operator access granted from scanner {ScannerName}", scanner.DeviceName ?? "Unknown");

                return Json(new { success = true, redirectUrl = "/Operator/Index" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during quick operator access from scanner {ScannerId}", scannerId);
                return Json(new { success = false, message = "An error occurred. Please try manual authentication." });
            }
        }

        // GET: Scanner/Logout
        public async Task<IActionResult> Logout(int? scannerId)
        {
            var scannerDeviceId = HttpContext.Session.GetString("ScannerDeviceId");

            if (!string.IsNullOrEmpty(scannerDeviceId) && int.TryParse(scannerDeviceId, out int deviceId))
            {
                try
                {
                    var scanner = await _context.ScannerDevices.FindAsync(deviceId);
                    if (scanner != null)
                    {
                        // Log logout
                        var userId = HttpContext.Session.GetString("ScannerUserId");
                        var userRole = HttpContext.Session.GetString("ScannerRole");

                        var logoutLog = new ScannerAccessLog
                        {
                            ScannerDeviceId = scanner.Id,
                            AccessPath = "/Scanner/Logout",
                            AccessDateTime = DateTime.UtcNow,
                            UserAgent = Request.Headers["User-Agent"].ToString(),
                            UserClockNumber = userId,
                            UserRole = userRole,
                            AccessType = "Logout",
                            WasSuccessful = true,
                            Notes = $"Scanner session logout - User {userId} with role {userRole} from {scanner.DeviceName}"
                        };

                        _context.ScannerAccessLogs.Add(logoutLog);
                        await _context.SaveChangesAsync();

                        _logger.LogInformation("Scanner logout from {ScannerName}", scanner.DeviceName);

                        // Clear session
                        HttpContext.Session.Clear();

                        // Redirect back to scanner role selection
                        return RedirectToAction("RoleSelection", new { scannerId = scanner.Id });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during scanner logout");
                }
            }

            HttpContext.Session.Clear();
            return RedirectToAction("RoleSelection", new { scannerId = scannerId ?? 0 });
        }

        // Helper Methods
        private async Task<ScannerAuthenticationResult> AuthenticateUserManually(string clockNumber, string password)
        {
            try
            {
                // Check for null/empty inputs first
                if (string.IsNullOrWhiteSpace(clockNumber) || string.IsNullOrWhiteSpace(password))
                {
                    return new ScannerAuthenticationResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "Clock number and password are required"
                    };
                }

                // Find user with NULL-safe query
                var user = await _context.Users.FirstOrDefaultAsync(u =>
                    (u.UserName != null && u.UserName == clockNumber) ||
                    (u.ClockNumber != null && u.ClockNumber == clockNumber));

                if (user == null)
                {
                    _logger.LogWarning("User not found for clock number: {ClockNumber}", clockNumber);
                    return new ScannerAuthenticationResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "Invalid clock number"
                    };
                }

                // Use ASP.NET Core Identity to verify password
                var userManager = HttpContext.RequestServices.GetRequiredService<UserManager<User>>();

                var passwordCheck = await userManager.CheckPasswordAsync(user, password);
                if (!passwordCheck)
                {
                    _logger.LogWarning("Invalid password for user: {ClockNumber}", clockNumber);
                    return new ScannerAuthenticationResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "Invalid password"
                    };
                }

                // Get user roles with NULL safety
                var userRoles = await userManager.GetRolesAsync(user);

                return new ScannerAuthenticationResult
                {
                    IsSuccess = true,
                    UserName = user.UserName ?? clockNumber,
                    ClockNumber = clockNumber,
                    AvailableRoles = userRoles?.ToList() ?? new List<string> { "Operator" }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error authenticating user {ClockNumber}", clockNumber);
                return new ScannerAuthenticationResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Authentication system error. Please try again."
                };
            }
        }

        private async Task<string> DetermineUserRole(string clockNumber, string selectedRole = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(clockNumber))
                {
                    return "Operator"; // Default fallback
                }

                // Get user's actual role from your user management system with NULL safety
                var user = await _context.Users.FirstOrDefaultAsync(u =>
                    (u.UserName != null && u.UserName == clockNumber) ||
                    (u.ClockNumber != null && u.ClockNumber == clockNumber));

                if (user != null)
                {
                    // Get user's roles from your system with NULL safety
                    var userRoles = await _context.UserRoles
                        .Where(ur => ur.UserId == user.Id)
                        .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                        .Where(name => name != null) // Filter out NULL role names
                        .ToListAsync();

                    // If user selected a specific role and they have permission for it, use that
                    if (!string.IsNullOrEmpty(selectedRole) && userRoles.Contains(selectedRole))
                    {
                        return selectedRole;
                    }

                    // Otherwise, return the highest role they have with NULL safety
                    var roleHierarchy = new[] { "SeniorManager", "Manager", "GroupLeader", "TeamLeader", "Operator" };
                    foreach (var role in roleHierarchy)
                    {
                        if (userRoles.Contains(role))
                        {
                            return role;
                        }
                    }
                }

                // Default fallback
                return "Operator";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error determining user role for {ClockNumber}", clockNumber);
                return "Operator"; // Safe fallback
            }
        }

        private string GetRedirectPathForRole(string role)
        {
            return role switch
            {
                "Operator" => "/Operator/Index",
                "TeamLeader" or "GroupLeader" or "Manager" or "SeniorManager" => "/ScrapApproval/Dashboard",
                "Admin" => "/Analytics/Index",
                _ => "/Operator/Index"
            };
        }

        // Error view for scanner issues
        public IActionResult ScannerError(string message)
        {
            ViewBag.ErrorMessage = message;
            return View();
        }
    }
}