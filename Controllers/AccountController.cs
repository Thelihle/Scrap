using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Data.SqlClient;
using Dapper;
using System;
using SCP_Control.Models;
using SCP_Control.ViewModels;
using SCP_Control.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using SCP_Control.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using SCP_Control.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using static SCP_Control.Controllers.ScrapApprovalController;
using SCP_Control.Models.Common;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http.Extensions;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Vml;

namespace SCP_Control.Controllers
{
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private readonly IActivityLogService _activityLogService;
        private readonly IAuthService _authService;
        private readonly IUserRegistrationService _userRegistrationService;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AccountController> _logger;
        private readonly IConfiguration _configuration;
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly IWebHostEnvironment _environment;
        private readonly ISessionService _sessionService;
        public AccountController(
            IActivityLogService activityLogService,
            IAuthService authService,
            IUserRegistrationService userRegistrationService,
            ApplicationDbContext context,
            ILogger<AccountController> logger,
            UserManager<User> userManager,
            SignInManager<User> signInManager,
            IConfiguration configuration,
              IWebHostEnvironment environment,
              ISessionService sessionService)
        {
            _activityLogService = activityLogService ?? throw new ArgumentNullException(nameof(activityLogService));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _userRegistrationService = userRegistrationService ?? throw new ArgumentNullException(nameof(userRegistrationService));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _userManager = userManager;
            _signInManager = signInManager;
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        }

        [HttpGet]
        public async Task<JsonResult> GetDepartments()
        {
            try
            {
                _logger.LogInformation("GetDepartments called");

                var departments = await _context.scpDepartment
                    .Where(d => d.IsActive)
                    .OrderBy(d => d.Plant)
                    .ThenBy(d => d.DepartmentName)
                    .Select(d => new
                    {
                        id = d.DepartmentId,
                        name = d.DepartmentName == "ENGINEERING"
                            ? $"ENGINEERING - {d.Plant}"  // ✅ Show "ENGINEERING - Plant A"
                            : d.Location                   // ✅ Show Location for other departments
                    })
                    .ToListAsync();

                _logger.LogInformation($"Retrieved {departments.Count} departments");

                return Json(new
                {
                    success = true,
                    departments = departments
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetDepartments");
                return Json(new
                {
                    success = false,
                    message = "Error loading departments",
                    error = ex.Message
                });
            }
        }

        [HttpGet]
        public IActionResult GetDepartmentsDirect()
        {
            var departments = _context.scpDepartment
                .Where(d => d.IsActive)
                .OrderBy(d => d.Category)
                .ThenBy(d => d.DepartmentName)
                .Select(d => new
                {
                    id = d.DepartmentId,
                    name = d.DepartmentName,
                    category = d.Category ?? "OTHER"
                })
                .ToList();

            return Json(new { success = true, departments });
        }

        private async Task<(string DepartmentName, string LineName)> GetLocationNames(UserRegistrationRequest request)
        {
            string departmentName = null;
            string lineName = null;

            // Try direct database lookup first as the most reliable method
            if (request.DepartmentId > 0)
            {
                // Use direct SQL to avoid EF Core tracking/navigation issues
                using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await connection.OpenAsync();

                string query = "SELECT DepartmentName FROM scpDepartment WHERE DepartmentId = @Id";
                departmentName = await connection.QueryFirstOrDefaultAsync<string>(query, new { Id = request.DepartmentId });
            }

            if (request.LineId.HasValue && request.LineId.Value > 0)
            {
                using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await connection.OpenAsync();

                string query = "SELECT LineName FROM scpLine WHERE LineId = @Id";
                lineName = await connection.QueryFirstOrDefaultAsync<string>(query, new { Id = request.LineId.Value });
            }

            // Fallback to defaults if needed
            departmentName = departmentName ?? "Not Specified";
            lineName = lineName ?? "Not Specified";

            return (departmentName, lineName);
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Register([FromForm] UserRegistrationRequest model)
        {
            var strategy = _context.Database.CreateExecutionStrategy();

            // Capture what we need AFTER the transaction completes
            int savedDepartmentId = 0;
            int? savedLineId = null;
            string savedShift = null;
            string savedUserType = null;
            bool registrationSaved = false;
            string errorMessage = null;

            await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // ✅ Dapper for ALL existence checks — never touch _context.Users
                    using var checkConn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                    await checkConn.OpenAsync();

                    var userExists = await checkConn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM scpUser WHERE ClockNumber = @ClockNumber",
                        new { model.ClockNumber }) > 0;

                    if (userExists)
                    {
                        errorMessage = "A user with this clock number already exists.";
                        return;
                    }

                    var requestExists = await checkConn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM scpRequest WHERE ClockNumber = @ClockNumber AND Status = 'Pending'",
                        new { model.ClockNumber }) > 0;

                    if (requestExists)
                    {
                        errorMessage = "A pending request already exists for this clock number.";
                        return;
                    }

                    // ✅ Skip ModelState.IsValid — it triggers EF/Identity pipeline
                    // Manually validate only what we need
                    var userTypeLower = model.UserType?.ToLower().Trim() ?? "";

                    if (string.IsNullOrWhiteSpace(model.ClockNumber) || !System.Text.RegularExpressions.Regex.IsMatch(model.ClockNumber, @"^[0-9]{1,6}$"))
                    {
                        errorMessage = "Clock Number must contain only numbers (1-6 digits).";
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(model.Name))
                    {
                        errorMessage = "Name is required.";
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(model.UserType))
                    {
                        errorMessage = "User Type is required.";
                        return;
                    }
                    if (model.DepartmentId == 0)
                    {
                        errorMessage = "Department is required.";
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(model.Shift))
                    {
                        errorMessage = "Shift is required.";
                        return;
                    }

                    var emailRequiredRoles = new[]
                    {
            "groupleader", "manager", "seniormanager", "admin",
            "engineering groupleader", "engineering manager",
            "engineering seniormanager", "engineering admin"
        };

                    var lineOptionalRoles = new[]
                    {
            "teamleader", "groupleader", "manager", "seniormanager", "admin",
            "engineering teamleader", "engineering groupleader",
            "engineering manager", "engineering seniormanager", "engineering admin"
        };

                    bool requiresEmail = emailRequiredRoles.Contains(userTypeLower);
                    bool isLineOptional = lineOptionalRoles.Contains(userTypeLower);

                    if (requiresEmail && !string.IsNullOrEmpty(model.Email))
                    {
                        var emailPattern = @"^[a-zA-Z]+(\.[a-zA-Z]+)?@hesto\.co\.za$";
                        if (!System.Text.RegularExpressions.Regex.IsMatch(model.Email, emailPattern))
                        {
                            errorMessage = "Invalid email format. Please use name@hesto.co.za or name.surname@hesto.co.za";
                            return;
                        }
                    }

                    if (isLineOptional && (model.LineId == null || model.LineId == 0))
                    {
                        model.LineId = null;
                    }
                    else if (!isLineOptional && (model.LineId == null || model.LineId == 0))
                    {
                        errorMessage = "Line selection is required for this user type.";
                        return;
                    }

                    // ✅ Insert directly via Dapper — never use _context.scpRequest.Add()
                    await checkConn.ExecuteAsync(@"
    INSERT INTO scpRequest 
        (ClockNumber, Name, Surname, Email, UserType, DepartmentId, LineId, 
         Shift, Status, RequestDate, MachineNumber, ZoneName)
    VALUES 
        (@ClockNumber, @Name, @Surname, @Email, @UserType, @DepartmentId, @LineId,
         @Shift, 'Pending', @RequestDate, @MachineNumber, @ZoneName)",
                        new
                        {
                            model.ClockNumber,
                            model.Name,
                            model.Surname,
                            model.Email,
                            model.UserType,
                            model.DepartmentId,
                            model.LineId,
                            model.Shift,
                            RequestDate = DateTime.UtcNow,
                            MachineNumber = model.MachineNumber ?? (object)DBNull.Value,
                            ZoneName = model.ZoneName ?? (object)DBNull.Value   // ✅ NEW
                        });

                    await transaction.CommitAsync();

                    savedDepartmentId = model.DepartmentId;
                    savedLineId = model.LineId;
                    savedShift = model.Shift;
                    savedUserType = model.UserType;
                    registrationSaved = true;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Error processing registration request");
                    errorMessage = "An error occurred during registration.";
                }
            });

            // ✅ All DB lookups happen HERE — completely outside the transaction


            if (!registrationSaved)
                return Json(new { success = false, message = errorMessage ?? "Registration failed." });

            string departmentName = "Not specified";
            string lineName = "Not specified";

            // ✅ Use Dapper for ALL lookups — no _context anywhere after this point
            using var lookupConn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await lookupConn.OpenAsync();

            if (savedDepartmentId > 0)
            {
                departmentName = await lookupConn.ExecuteScalarAsync<string>(
                    "SELECT DepartmentName FROM scpDepartment WHERE DepartmentId = @Id",
                    new { Id = savedDepartmentId }) ?? "Not specified";
            }

            if (savedLineId.HasValue && savedLineId.Value > 0)
            {
                lineName = await lookupConn.ExecuteScalarAsync<string>(
                    "SELECT LineName FROM scpLine WHERE LineId = @Id",
                    new { Id = savedLineId.Value }) ?? "Not specified";

                if (!string.IsNullOrEmpty(model.ZoneName))
                    lineName = $"{model.ZoneName} - {lineName}";   // ✅ "ZONE 2 - Komax"
            }
            else if (!string.IsNullOrEmpty(model.LineName))
            {
                lineName = !string.IsNullOrEmpty(model.ZoneName)
                    ? $"{model.ZoneName} - {model.LineName}"
                    : model.LineName;
            }
            else if (savedLineId.HasValue && savedLineId.Value > 0)
            {
                lineName = await lookupConn.ExecuteScalarAsync<string>(
                    "SELECT LineName FROM scpLine WHERE LineId = @Id",
                    new { Id = savedLineId.Value }) ?? "Not specified";
            }

            string shiftName = savedShift switch
            {
                "A" => "A Shift",
                "B" => "B Shift",
                "Straight" => "Straight Shift",
                _ => "Not specified"
            };

            bool isManagementRole = IsManagementRole(savedUserType);
            string username = isManagementRole
                ? (!string.IsNullOrWhiteSpace(model.Surname)
                    ? $"{model.Name.Trim().ToLower()}.{model.Surname.Trim().ToLower()}"
                    : model.Name.Trim().ToLower())
                : model.ClockNumber;

            return Json(new
            {
                success = true,
                message = "Registration request submitted successfully",
                username,
                department = departmentName,
                line = lineName,
                shift = shiftName
            });

        }

        [HttpGet]
        public async Task<JsonResult> GetPendingRegistrations()
        {
            try
            {
                var pendingRequests = await _context.scpRequest
                    .Where(r => r.Status == "Pending")
                    .Include(r => r.Department)
                    .Include(r => r.Line)
                    .OrderByDescending(r => r.RequestDate)
                    .Select(r => new
                    {
                        r.RequestId,
                        r.ClockNumber,
                        r.Name,
                        r.Surname,
                        r.UserType,
                        Department = r.Department.DepartmentName,
                        Line = r.Line.LineName,
                        r.Shift,
                        r.RequestDate
                    })
                    .ToListAsync();

                return Json(new { success = true, requests = pendingRequests });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving pending registration requests");
                return Json(new { success = false, message = "Error loading pending requests" });
            }
        }
        private static string GenerateSecurePassword()
        {
            const string chars = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$%^&*";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 12)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HandleRegistrationRequest([FromBody] RegistrationApprovalRequest request)
        {
            try
            {
                var registrationRequest = await _context.scpRequest
                 .Include(r => r.Department)
                 .Include(r => r.Line)
                 .FirstOrDefaultAsync(r => r.RequestId == request.RequestId && r.Status == "Pending");

                if (registrationRequest == null)
                    return Json(new { success = false, message = "Request not found or already processed" });


                // Get department and line names with fallback logic
                string departmentName = null;
                string lineName = null;

                // Try to get names from navigation properties first
                if (registrationRequest.Department != null)
                {
                    departmentName = registrationRequest.Department.DepartmentName;
                }
                // If navigation property failed, fetch directly using the ID
                else if (registrationRequest.DepartmentId > 0)
                {
                    var department = await _context.scpDepartment.FindAsync(registrationRequest.DepartmentId);
                    departmentName = department?.DepartmentName;
                }

                // Same for line name
                if (registrationRequest.Line != null)
                {
                    lineName = registrationRequest.Line.LineName;
                }
                else if (registrationRequest.LineId.HasValue && registrationRequest.LineId.Value > 0)
                {
                    var line = await _context.scpLine.FindAsync(registrationRequest.LineId.Value);
                    lineName = line?.LineName;
                }

                if (!string.IsNullOrEmpty(registrationRequest.ZoneName) && !string.IsNullOrEmpty(lineName))
                {
                    lineName = $"{registrationRequest.ZoneName} - {lineName}";   // ✅
                }


                if (request.IsApproved)
                {
                    // Generate a new password for the user
                    string tempPassword = GenerateSecurePassword();
                    string hashedPassword = BCrypt.Net.BCrypt.HashPassword(tempPassword);

                    // Store the actual clock number for all users
                    string clockNumber = registrationRequest.ClockNumber;

                    // Determine proper username based on user type
                    string properUsername;

                    // For management roles, use name.surname format
                    bool isManagementRole = IsManagementRole(registrationRequest.UserType);
                    if (isManagementRole)
                    {
                        _logger.LogInformation($"Creating management user for {registrationRequest.Name} {registrationRequest.Surname}");

                        // Check if surname is provided and not empty
                        if (!string.IsNullOrWhiteSpace(registrationRequest.Surname))
                        {
                            // Use name.surname format
                            properUsername = $"{registrationRequest.Name.Trim().ToLower()}.{registrationRequest.Surname.Trim().ToLower()}";
                        }
                        else
                        {
                            // Use just name if surname is not provided
                            properUsername = registrationRequest.Name.Trim().ToLower();
                        }
                    }
                    else
                    {
                        // For non-management roles (operator/teamleader), use clock number as username
                        properUsername = clockNumber;
                    }

                    _logger.LogInformation($"Setting Username={properUsername} and ClockNumber={clockNumber}");

                    // Create new user with distinct ClockNumber and UserName
                    var user = new User
                    {
                        ClockNumber = registrationRequest.ClockNumber,
                        UserName = properUsername,
                        Email = registrationRequest.Email,
                        UserType = registrationRequest.UserType,
                        Department = departmentName ?? "Not Specified",
                        Line = lineName ?? "Not Specified",
                        Shift = registrationRequest.Shift ?? "Not Specified",
                        IsActive = true,
                        PasswordResetRequired = true,
                        CreatedBy = User.Identity?.Name,
                        CreatedAt = DateTime.UtcNow,
                        PasswordHash = hashedPassword   // ✅ set this, not (or in addition to) Password
                    };

                    _context.Users.Add(user);

                    // Update request status
                    registrationRequest.Status = "Approved";
                    registrationRequest.ProcessedBy = User.Identity?.Name;
                    registrationRequest.ProcessedDate = DateTime.UtcNow;

                    await _context.SaveChangesAsync();

                    return Json(new
                    {
                        success = true,
                        message = "Request approved successfully.",
                        username = properUsername,
                        tempPassword = tempPassword
                    });
                }
                else
                {
                    // Handle rejection
                    registrationRequest.Status = "Rejected";
                    registrationRequest.ProcessedBy = User.Identity?.Name;
                    registrationRequest.ProcessedDate = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    return Json(new { success = true, message = "Request rejected" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing registration request");
                return Json(new { success = false, message = "Error processing request" });
            }
        }

        public class RegistrationApprovalRequest
        {
            public int RequestId { get; set; }
            public bool IsApproved { get; set; }
            public string ApproverPassword { get; set; }
            public string RejectReason { get; set; }
        }

        private string DetermineUsername(UserRegistrationRequest model)
        {
            // For operators and team leaders, use clock number
            if (model.UserType.ToLower() == "operator" || model.UserType.ToLower() == "teamleader")
            {
                return model.ClockNumber;
            }

            // For management roles, use name.surname
            return $"{model.Name}.{model.Surname}".ToLower();
        }

        [HttpGet]
        public IActionResult SetLocation()
        {
            // This should actually redirect to the correct dashboard based on user role
            if (User.Identity?.IsAuthenticated == true)
            {
                var userRole = User.Claims
                    .FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value?.ToLower();

                if (IsManagementRole(userRole))
                {
                    return RedirectToAction("Dashboard", "ScrapApproval");
                }
                else if (userRole == "operator")
                {
                    return RedirectToAction("Index", "Operator");
                }
            }

            return RedirectToAction("Login");
        }

        private bool IsManagementRole(string? role)
        {
            if (string.IsNullOrEmpty(role)) return false;
            var r = role.ToLower().Trim();
            return r switch
            {
                "teamleader" or "groupleader" or "manager" or "seniormanager" or "admin"
                or "engineering teamleader" or "engineering groupleader"
                or "engineering manager" or "engineering seniormanager" or "engineering admin" => true,
                _ => false
            };
        }

        private bool IsValidCompanyEmail(string email) =>
            !string.IsNullOrEmpty(email) &&
            email.EndsWith("@hesto.co.za") &&
            System.Text.RegularExpressions.Regex.IsMatch(email, @"^[a-zA-Z.]+@hesto\.co\.za$");

        private static string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(12));
        }

      

        // Add this method to verify the current user's role
        private bool IsInRole(string role)
        {
            return User?.IsInRole(role) ?? false;
        }


        private IActionResult RedirectToRoleBasedDashboard(string? userRole)
        {
            return userRole?.ToLower() switch
            {
                "operator" => RedirectToAction("Index", "Operator"),
                var type when new[] { "teamleader", "groupleader", "manager", "seniormanager", "admin" }
                    .Contains(type) => RedirectToAction("Dashboard", "ScrapApproval"),
                _ => RedirectToAction("Login", "Account")
            };
        }

        [HttpGet]
        public async Task<JsonResult> GetLinesByDepartment(int id, string userType, string lineType = null, string parentZone = null)
        {
            try
            {
                var query = _context.scpLine
                    .Where(l => l.DepartmentId == id && l.IsActive);

                if (!string.IsNullOrEmpty(lineType))
                    query = query.Where(l => l.LineType == lineType);

                if (!string.IsNullOrEmpty(parentZone))
                {
                    // Areas belonging specifically to this zone (e.g. Battery's own areas)
                    query = query.Where(l => l.Parent != null && l.Parent.LineName == parentZone);
                }
                else if (lineType == "Area")
                {
                    // Default/shared area pool — only areas with NO specific parent zone
                    query = query.Where(l => l.ParentLineId == null);
                }

                var lines = await query
                    .OrderBy(l => l.LineName)
                    .Select(l => new { id = l.LineId, text = l.LineName })
                    .Distinct()
                    .ToListAsync();

                return Json(new { success = true, results = lines });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting lines for department {DepartmentId}", id);
                return Json(new { success = false, message = $"Failed to load lines: {ex.Message}" });
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ResetAccountLock(string clockNumber)
        {
            var success = await _authService.ResetAccountLockout(clockNumber);
            return Json(new { success, message = success ? "Account unlocked successfully" : "Failed to unlock account" });
        }

        [HttpGet]
        public IActionResult UserRegistrationRequest()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> PendingApprovals()
        {
            var requests = await _context.scpRequest
                .Include(r => r.Department)
                .Include(r => r.Line)
                .Where(r => r.Status == "Pending")
                .OrderByDescending(r => r.RequestDate)
                .ToListAsync();

            return View(requests);
        }


        private string DetermineRedirectPath(string? userType)
        {
            return userType?.ToLower() switch
            {
                "operator" => "/Operator/Index",
                var type when new[] { "teamleader", "groupleader", "manager", "seniormanager", "admin" }
                    .Contains(type) => "/ScrapApproval/Dashboard",
                _ => "/Account/Login"
            };
        }

        private class UserDto
        {
            public int UserId { get; set; }
            public string? ClockNumber { get; set; }
            public string? Password { get; set; }
            public string? UserType { get; set; } // Added UserType property
            public bool IsActive { get; set; }
            public string? Department { get; set; }
            public string? Line { get; set; }
            public UserDto() { }
        }

        private bool VerifyPassword(User user, string password)
        {
            try
            {
                // Remove the check for temporary password in registration request
                // Only check permanent password
                if (!string.IsNullOrEmpty(user.PasswordHash))
                {
                    _logger.LogInformation($"Verifying permanent password for user: {user.ClockNumber}");
                    try
                    {
                        return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error verifying password hash");
                    }
                }
                else if (!string.IsNullOrEmpty(user.Password))
                {
                    // Try Password field if PasswordHash is empty
                    _logger.LogInformation($"Verifying password from Password field for user: {user.ClockNumber}");
                    try
                    {
                        return BCrypt.Net.BCrypt.Verify(password, user.Password);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error verifying password from Password field");
                    }
                }
                else
                {
                    _logger.LogWarning($"User {user.ClockNumber} has no password hash");
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unhandled error in VerifyPassword for user {user.ClockNumber}");
                return false;
            }
        }

        private string GetRedirectUrlForUser(User user, string returnUrl)
        {
            // Validate return URL
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                _logger.LogInformation($"Using validated return URL: {returnUrl}");
                return returnUrl;
            }

            // Determine redirect based on user type
            return user.UserType?.ToLower() switch
            {
                "operator" => "/Operator/Index",
                var type when new[] { "teamleader", "groupleader", "manager", "seniormanager", "admin" }
                    .Contains(type) => "/ScrapApproval/Dashboard",
                _ => "/Account/Login"
            };
        }
        private bool NeedsPasswordChange(User user)
        {
            try
            {
                // First check if user has PasswordResetRequired flag
                if (user.PasswordResetRequired == true)
                {
                    _logger.LogInformation($"User {user.ClockNumber} has PasswordResetRequired flag set to true");
                    return true;
                }

                // Remove the temporary password check since we no longer store it in UserRegistrationRequests
                // Just check if it's a recently approved registration
                var regRequest = _context.scpRequest
                    .FirstOrDefault(r => r.ClockNumber == user.ClockNumber &&
                                       r.Status == "Approved" &&
                                       r.ProcessedDate > DateTime.UtcNow.AddDays(-1)); // Approved within the last day

                if (regRequest != null)
                {
                    _logger.LogInformation($"User {user.ClockNumber} has a recently approved registration request");
                    return true;
                }

                _logger.LogInformation($"User {user.ClockNumber} does not need password change");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in NeedsPasswordChange for user {user.ClockNumber}");
                // Default to false to avoid blocking login on error
                return false;
            }
        }
        private List<Claim> CreateUserClaims(User user)
        {
            string userKey = IsManagementRole(user.UserType)
                ? user.UserName
                : user.ClockNumber;

            return new List<Claim>
    {
        new Claim(ClaimTypes.Name, userKey),
        new Claim(ClaimTypes.Role, (user.UserType ?? "User").ToLower()),
        new Claim("Department", user.Department ?? ""),
        new Claim("Line", user.Line ?? ""),
        new Claim("Shift", user.Shift ?? "")
    };
        }


        private async Task SignInUser(List<Claim> claims, bool rememberMe = false)
        {
            var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                IdentityConstants.ApplicationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = rememberMe,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
                }
            );
        }

        private async Task<User> AuthenticateUser(LoginViewModel model)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u =>
                    (u.ClockNumber == model.ClockNumber || u.UserName == model.ClockNumber)
                    && u.IsActive);

            if (user == null)
            {
                _logger.LogWarning($"Login attempt for non-existent user: {model.ClockNumber}");
                return null;
            }

            bool isValidPassword = VerifyPassword(user, model.Password);
            if (!isValidPassword)
            {
                _logger.LogWarning($"Failed login attempt for user: {model.ClockNumber}");
                return null;
            }

            _logger.LogInformation($"Successful login for user: {user.ClockNumber}, Type: {user.UserType}");
            return user;
        }


        // Add these action methods to your AccountController

        [HttpGet]
        public IActionResult ChangePassword()
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return RedirectToAction("Login");
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HandlePasswordChange([FromBody] NewPasswordRequest request)
        {
            try
            {
                // Get the logged-in username (could be clock number or name.surname)
                var loginIdentifier = User.Identity?.Name;
                if (string.IsNullOrEmpty(loginIdentifier))
                {
                    return Json(new { success = false, message = "Not authenticated" });
                }

                _logger.LogInformation($"Processing password change for user identifier: {loginIdentifier}");

                using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // First find the user to get their actual ClockNumber and UserType
                    const string findUserQuery = @"
SELECT UserId, ClockNumber, UserName, UserType 
FROM [IntegrationCompanyB].[dbo].[scpUser] 
WHERE ClockNumber = @Identifier OR UserName = @Identifier";

                    var user = await connection.QueryFirstOrDefaultAsync(findUserQuery,
                        new { Identifier = loginIdentifier }, transaction);

                    if (user == null)
                    {
                        _logger.LogWarning($"User not found with identifier: {loginIdentifier}");
                        await transaction.RollbackAsync();
                        return Json(new { success = false, message = "User not found" });
                    }

                    _logger.LogInformation($"Found user - ClockNumber: {user.ClockNumber}, UserName: {user.UserName}, UserType: {user.UserType}");

                    // Hash new password
                    string newPasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

                    // Update user password - now search by both fields, ONLY use PasswordHash column
                    string updateQuery = @"
UPDATE [IntegrationCompanyB].[dbo].[scpUser]
SET PasswordHash = @NewPasswordHash,
    PasswordResetRequired = 0,
    LastPasswordChange = @LastPasswordChange,
    LastModifiedAt = @LastModifiedAt,
    LastModifiedBy = @ModifiedBy
WHERE ClockNumber = @Identifier OR UserName = @Identifier";

                    int rowsAffected = await connection.ExecuteAsync(updateQuery, new
                    {
                        NewPasswordHash = newPasswordHash,
                        LastPasswordChange = DateTime.UtcNow,
                        LastModifiedAt = DateTime.UtcNow,
                        ModifiedBy = loginIdentifier,
                        Identifier = loginIdentifier
                    }, transaction);

                    if (rowsAffected == 0)
                    {
                        _logger.LogWarning($"No rows affected when updating password for {loginIdentifier}");
                        await transaction.RollbackAsync();
                        return Json(new { success = false, message = "Failed to update password" });
                    }

                    _logger.LogInformation($"Password updated for {loginIdentifier}, {rowsAffected} rows affected");

                    await transaction.CommitAsync();

                    // Log the activity
                    await _activityLogService.LogActivityAsync(
                        user.ClockNumber, // Use actual ClockNumber for logging
                        "Password Change",
                        "Password successfully changed",
                        "Success");


                    _logger.LogInformation($"Password change transaction committed for {loginIdentifier}");

                    // In HandlePasswordChange, replace the switch with:
                    string redirectUrl = DetermineRedirectUrl(new User { UserType = user.UserType, PasswordResetRequired = false });

                    _logger.LogInformation($"Generated redirect URL: {redirectUrl}");

                    return Json(new
                    {
                        success = true,
                        message = "Password changed successfully",
                        redirectUrl = redirectUrl
                    });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Transaction error changing password for user {User}", loginIdentifier);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password");
                return Json(new { success = false, message = "An unexpected error occurred while changing your password" });
            }
        }

        public class NewPasswordRequest
        {
            [Required]
            public string NewPassword { get; set; }
        }



        private bool IsValidPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return false;

            // Password requirements
            var hasMinLength = password.Length >= 8;
            var hasUppercase = password.Any(char.IsUpper);
            var hasLowercase = password.Any(char.IsLower);
            var hasDigit = password.Any(char.IsDigit);
            var hasSpecialChar = password.Any(ch => !char.IsLetterOrDigit(ch));

            return hasMinLength && hasUppercase && hasLowercase && hasDigit && hasSpecialChar;
        }

        public class ChangePasswordRequest
        {
            [Required]
            public string CurrentPassword { get; set; }

            [Required]
            public string NewPassword { get; set; }
        }


        private bool IsLocalUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            // Ensure the URL is a local relative path
            if (url.StartsWith("/") && !url.StartsWith("//") && !url.StartsWith("/\\"))
            {
                return true;
            }

            try
            {
                var outUri = new Uri(url, UriKind.Relative);
                return true;
            }
            catch
            {
                return false;
            }
        }


       

       
        public Task<string> GetRedirectUrl(User user)
        {
            // Add logging to verify redirect logic
            _logger.LogInformation($"Determining redirect for user type: {user.UserType}");

            return Task.FromResult(user.UserType?.ToLower() switch
            {
                "operator" => "/Operator/Index",
                var type when new[] { "teamleader", "groupleader", "manager", "seniormanager", "admin" }
                    .Contains(type) => "/ScrapApproval/Dashboard",
                _ => "/Account/Login"
            });
        }

        private IActionResult RedirectToUserDashboard()
        {
            if (User.Identity?.IsAuthenticated != true)
                return RedirectToAction("Login", "Account");

            // Always redirect to /Account/Login first
            if (HttpContext.Request.Path == "/")
                return RedirectToAction("Login", "Account");

            return RedirectToAction("Index", "Operator");

        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GrantApprovalAccess([FromBody] AccessGrantRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Username) ||
                    string.IsNullOrEmpty(request.AccessLevel) ||
                    string.IsNullOrEmpty(request.VerificationPassword))
                {
                    return Json(new { success = false, message = "Invalid request parameters" });
                }

                var granterUsername = User.Identity?.Name;
                if (string.IsNullOrEmpty(granterUsername))
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                // Verify granter's password
                var isValidPassword = await _authService.VerifyPassword(granterUsername, request.VerificationPassword);
                if (!isValidPassword)
                {
                    return Json(new { success = false, message = "Invalid verification password" });
                }

                // Check if granter has permission to grant this access level
                var granterRole = User.FindFirst(ClaimTypes.Role)?.Value;
                if (!CanGrantAccessToRole(granterRole, request.AccessLevel))
                {
                    return Json(new { success = false, message = "Insufficient privileges to grant this access level" });
                }

                // Get the user registration request
                var pendingRequest = await _context.scpRequest
                    .Include(r => r.Department)
                    .Include(r => r.Line)
                    .FirstOrDefaultAsync(r => r.ClockNumber == request.Username && r.Status == "Pending");

                if (pendingRequest != null)
                {
                    // Create temporary password
                    var tempPassword = GenerateSecurePassword();
                    var hashedPassword = BCrypt.Net.BCrypt.HashPassword(tempPassword);

                    // Create user from pending request - updated for the new database schema
                    var user = new User
                    {
                        ClockNumber = pendingRequest.ClockNumber,
                        UserName = pendingRequest.ClockNumber,
                        Email = pendingRequest.Email,
                        UserType = request.AccessLevel,
                        Department = pendingRequest.Department?.DepartmentName,
                        Line = pendingRequest.Line?.LineName,
                        Shift = pendingRequest.Shift,
                        IsActive = true,
                        PasswordResetRequired = true,
                        IsTemporary = false,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = granterUsername,
                        Password = hashedPassword

                       
                    };

                    // Add user to database
                    _context.Users.Add(user);

                    // Update request status
                    pendingRequest.Status = "Approved";
                    pendingRequest.ProcessedBy = granterUsername;
                    pendingRequest.ProcessedDate = DateTime.UtcNow;

                    await _context.SaveChangesAsync();

                    // Create activity log
                    var activityLog = new ActivityLog
                    {
                        ClockNumber = granterUsername,
                        ActionType = "User Approval",
                        ActionDetails = $"Approved access for user {pendingRequest.ClockNumber} with role {request.AccessLevel}",
                        Status = "Success",
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.ActivityLogs.Add(activityLog);
                    await _context.SaveChangesAsync();

                    return Json(new
                    {
                        success = true,
                        message = $"Access granted successfully. Temporary password: {tempPassword}"
                    });
                }

                return Json(new { success = false, message = "User request not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error granting approval access");

                // Log the error
                var errorLog = new ErrorLog
                {
                    ClockNumber = User.Identity?.Name,
                    ErrorMessage = ex.Message,
                    StackTrace = ex.StackTrace,
                    ErrorSource = "GrantApprovalAccess",
                    CreatedAt = DateTime.UtcNow
                };
                _context.ErrorLogs.Add(errorLog);
                await _context.SaveChangesAsync();

                return Json(new { success = false, message = "An error occurred while granting access" });
            }
        }

        private bool CanGrantAccessToRole(string? granterRole, string requestedRole)
        {
            // Define role hierarchy from lowest to highest
            var roleHierarchy = new Dictionary<string, int>
    {
        { "Operator", 1 },
        { "TeamLeader", 2 },
        { "GroupLeader", 3 },
        { "Manager", 4 },
        { "SeniorManager", 5 },
        { "Admin", 6 }
    };

            // Check if roles exist in hierarchy
            if (!roleHierarchy.TryGetValue(granterRole ?? "", out int granterLevel) ||
                !roleHierarchy.TryGetValue(requestedRole, out int requestedLevel))
            {
                return false;
            }

            // Granter must be exactly one level above or be an admin
            return granterRole == "Admin" || granterLevel == requestedLevel + 1;
        }

        [AllowAnonymous]
        public IActionResult AccessDenied(string returnUrl)
        {
            _logger.LogWarning("Access denied to {ReturnUrl} for user {User}",
                returnUrl,
                User.Identity?.Name ?? "unknown");

            var userRole = User.Claims
                .FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value?.ToLower();

            // Determine correct redirect based on user role
            string redirectUrl = userRole switch
            {
                "operator" => "/Operator/Index",
                var type when new[] { "teamleader", "groupleader", "manager", "seniormanager", "admin" }
                    .Contains(type) => "/ScrapApproval/Dashboard",
                _ => "/Account/Login"
            };

            // If user is authenticated but accessing wrong area, redirect to their proper dashboard
            if (User.Identity?.IsAuthenticated == true)
            {
                _logger.LogInformation("Redirecting authenticated user {User} to {Redirect}",
                    User.Identity.Name,
                    redirectUrl);
                return Redirect(redirectUrl);
            }

            // Otherwise show access denied view
            return View();
        }



        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> TestDirectSessionCreation()
        {
            try
            {
                var securityStamp = Guid.NewGuid().ToString();
                var clockNumber = "direct-test-user";
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "test-ip";
                var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();
                var createdAt = DateTime.UtcNow;

                // Try direct SQL insertion using ADO.NET
                using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    await connection.OpenAsync();

                    // First try to clean up any existing test sessions
                    using (var command = new SqlCommand("DELETE FROM [dbo].[scpUserSession] WHERE ClockNumber = @ClockNumber", connection))
                    {
                        command.Parameters.AddWithValue("@ClockNumber", clockNumber);
                        int deleted = await command.ExecuteNonQueryAsync();
                        _logger.LogInformation($"Deleted {deleted} existing test sessions");
                    }

                    // Get current time in South Africa (UTC+2)
                    DateTime saTime;
                    try
                    {
                        var saTimeZone = TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time");
                        saTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, saTimeZone);
                    }
                    catch
                    {
                        // Fallback to manual offset if timezone not available
                        saTime = DateTime.UtcNow.AddHours(2);
                    }

                    // Then create a new session
                    string insertSql = @"
                INSERT INTO [dbo].[scpUserSession] 
                    (SecurityStamp, ClockNumber, SecurityStamp, CreatedAt, LastActivity, IpAddress, UserAgent)
                VALUES 
                    (@SecurityStamp, @ClockNumber, @SecurityStamp, @CreatedAt, @LastActivity, @IpAddress, @UserAgent)";

                    using (var command = new SqlCommand(insertSql, connection))
                    {
                        command.Parameters.AddWithValue("@SecurityStamp", securityStamp);
                        command.Parameters.AddWithValue("@ClockNumber", clockNumber);
           
                        command.Parameters.AddWithValue("@CreatedAt", saTime);
                        command.Parameters.AddWithValue("@LastActivity", saTime);
                        command.Parameters.AddWithValue("@IpAddress", ipAddress);
                        command.Parameters.AddWithValue("@UserAgent", userAgent);

                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        _logger.LogInformation($"Inserted {rowsAffected} rows with SA time: {saTime}");

                        // Verify the insert
                        using (var verifyCommand = new SqlCommand("SELECT COUNT(1) FROM [dbo].[scpUserSession] WHERE SecurityStamp = @SecurityStamp", connection))
                        {
                            verifyCommand.Parameters.AddWithValue("@SecurityStamp", securityStamp);
                            int count = (int)await verifyCommand.ExecuteScalarAsync();

                            return Json(new
                            {
                                success = true,
                                message = $"Session created via direct ADO.NET with SA time {saTime}. Rows affected: {rowsAffected}, Verification count: {count}",
                                securityStamp = securityStamp,
                                connectionString = GetSanitizedConnectionString(_configuration.GetConnectionString("DefaultConnection"))
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Failed to create session",
                    error = ex.Message,
                    stack = ex.StackTrace
                });
            }
        }

        // Helper to sanitize connection string for logging
        private string GetSanitizedConnectionString(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return "NULL";

            // Show only the server and database, not credentials
            var parts = connectionString.Split(';');
            var sanitized = new List<string>();

            foreach (var part in parts)
            {
                if (part.StartsWith("Server=", StringComparison.OrdinalIgnoreCase) ||
                    part.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase) ||
                    part.StartsWith("Initial Catalog=", StringComparison.OrdinalIgnoreCase) ||
                    part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
                {
                    sanitized.Add(part);
                }
            }

            return string.Join(";", sanitized);
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> TestSessionCreation()
        {
            try
            {
                // Try to create a test session
                var testSecurityStamp = Guid.NewGuid();
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "test-ip";
                var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();

                // First try the session service
                try
                {
                    var session = await _sessionService.CreateSessionAsync(
                        "test-user",
                        testSecurityStamp.ToString(),
                        ipAddress,
                        userAgent
                    );

                    return Json(new
                    {
                        success = true,
                        message = "Session created successfully via service",
                        securityStamp = testSecurityStamp,
                        session = session
                    });
                }
                catch (Exception ex)
                {
                    // Log the error and try direct SQL
                    _logger.LogError(ex, "Error creating test session via service");

                    // Try direct SQL insertion
                    using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                    await connection.OpenAsync();

                    string insertQuery = @"
                INSERT INTO [dbo].[scpUserSession] 
                    (SecurityStamp, ClockNumber, SecurityStamp, CreatedAt, LastActivity, IpAddress, UserAgent)
                VALUES 
                    (@SecurityStamp, @ClockNumber, @SecurityStamp, @CreatedAt, @LastActivity, @IpAddress, @UserAgent)";

                    await connection.ExecuteAsync(insertQuery, new
                    {
                        SecurityStamp = testSecurityStamp,
                        ClockNumber = "test-user",
                    
                        CreatedAt = DateTime.UtcNow,
                        LastActivity = DateTime.UtcNow,
                        IpAddress = ipAddress,
                        UserAgent = userAgent
                    });

                    return Json(new
                    {
                        success = true,
                        message = "Session created successfully via direct SQL",
                        securityStamp = testSecurityStamp
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TestSessionCreation");
                return Json(new
                {
                    success = false,
                    message = "Failed to create test session",
                    error = ex.Message,
                    stack = ex.StackTrace
                });
            }
        }

      

        // Add this helper method to your AccountController to debug the issue
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> DebugUserQuery(string clockNumber)
        {
            try
            {
                var result = new Dictionary<string, object>();

                // First check if the user exists at all
                var userExists = await _context.Users.AnyAsync(u => u.ClockNumber == clockNumber);
                result["userExists"] = userExists;

                if (userExists)
                {
                    // Try a minimal query with just one field
                    var userClockNumber = await _context.Users
                        .Where(u => u.ClockNumber == clockNumber)
                        .Select(u => u.ClockNumber)
                        .FirstOrDefaultAsync();
                    result["userClockNumber"] = userClockNumber;

                    // Try individual properties one by one
                    var selectProps = await _context.Users
                        .Where(u => u.ClockNumber == clockNumber)
                        .Select(u => new {
                            u.UserName,
                            u.UserType,
                            HasPasswordHash = u.PasswordHash != null,
                            u.IsActive,
                            HasSecurityStamp = u.SecurityStamp != null,
                            HasDepartment = u.Department != null,
                            HasLine = u.Line != null,
                     
                        })
                        .FirstOrDefaultAsync();

                    if (selectProps != null)
                    {
                        result["properties"] = new
                        {
                            selectProps.UserName,
                            selectProps.UserType,
                            selectProps.HasPasswordHash,
                            selectProps.IsActive,
                            selectProps.HasSecurityStamp,
                            selectProps.HasDepartment,
                            selectProps.HasLine,
                            
                        };
                    }

                    // Check registration requests
                    var regRequest = await _context.scpRequest
                        .Where(r => r.ClockNumber == clockNumber && r.Status == "Approved")
                        .Select(r => new {
                            r.RequestId,
                            r.ClockNumber,                          
                        })
                        .FirstOrDefaultAsync();

                    result["hasRegistrationRequest"] = regRequest != null;
                    if (regRequest != null)
                    {
                        result["registrationRequest"] = regRequest;
                    }
                }

                return Json(result);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = ex.Message,
                    stackTrace = ex.StackTrace,
                    innerError = ex.InnerException?.Message
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ValidateSession()
        {
            try
            {
                var username = User.Identity?.Name;
                if (string.IsNullOrEmpty(username))
                {
                    return Json(new { valid = false });
                }

                // Get SecurityStamp from claims
                var securityStamp = User.FindFirst("SecurityStamp")?.Value;
                if (string.IsNullOrEmpty(securityStamp))
                {
                    return Json(new { valid = false });
                }

                // Check if this is the most recent session
                var isLatestSession = await _sessionService.IsLatestSessionAsync(username, securityStamp);
                if (!isLatestSession)
                {
                    return Json(new
                    {
                        valid = false,
                        message = "Your session has been invalidated by a login from another device"
                    });
                }

                // Check if the session is valid
                var isValid = await _sessionService.ValidateSessionAsync(username, securityStamp);
                if (!isValid)
                {
                    return Json(new { valid = false });
                }

                // Update last activity
                await _sessionService.UpdateSessionActivityAsync(securityStamp);

                return Json(new { valid = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating session");
                return Json(new { valid = false });
            }
        }




        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> DirectForceLogin(string ClockNumber, string Password, bool RememberMe = false)
        {
            try
            {
                _logger.LogInformation($"DirectForceLogin started for user: {ClockNumber}");

                await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                await Task.Delay(100);

                var user = await _context.Users
                    .AsNoTracking()
                    .Where(u => (u.ClockNumber == ClockNumber || u.UserName == ClockNumber) && u.IsActive)
                    .Select(u => new {
                        u.ClockNumber,
                        u.UserName,
                        u.PasswordHash,
                        u.Password,
                        u.UserType,
                        u.Department,
                        u.Line,
                        u.Shift,
                        u.PasswordResetRequired
                    })
                    .FirstOrDefaultAsync();

                if (user == null)
                {
                    _logger.LogWarning($"DirectForceLogin - User not found: {ClockNumber}");
                    return Json(new { success = false, message = "Invalid credentials", redirectUrl = "/Account/Login" });
                }

                bool isValidPassword = false;
                if (!string.IsNullOrEmpty(user.PasswordHash))
                    isValidPassword = BCrypt.Net.BCrypt.Verify(Password, user.PasswordHash);
                else if (!string.IsNullOrEmpty(user.Password))
                    isValidPassword = BCrypt.Net.BCrypt.Verify(Password, user.Password);

                if (!isValidPassword)
                {
                    _logger.LogWarning($"DirectForceLogin - Invalid password for: {ClockNumber}");
                    return Json(new { success = false, message = "Invalid credentials", redirectUrl = "/Account/Login" });
                }

                // UserName is the session key - same rule as LoginPost
                string userKey = user.UserName ?? user.ClockNumber;

                try
                {
                    await _sessionService.InvalidateSessionAsync(userKey);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"DirectForceLogin - Error clearing sessions for: {userKey}");
                }

                var securityStamp = Guid.NewGuid().ToString();

                var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, userKey),
            new Claim("ClockNumber", user.ClockNumber ?? userKey),
            new Claim(ClaimTypes.Role, (user.UserType ?? "User").ToLower()),
            new Claim("SecurityStamp", securityStamp),
            new Claim("Department", user.Department ?? "Not Set"),
            new Claim("Line", user.Line ?? "Not Set"),
            new Claim("Shift", user.Shift ?? "Not Set")
        };

                var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync(
                    IdentityConstants.ApplicationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = RememberMe,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
                    }
                );

                try
                {
                    string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    string userAgent = HttpContext.Request.Headers["User-Agent"].ToString();

                    await _sessionService.CreateSessionAsync(userKey, securityStamp, ipAddress, userAgent);
                    _logger.LogInformation($"DirectForceLogin - Created session record: {securityStamp}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"DirectForceLogin - Error creating session record for {ClockNumber}");
                }

                try
                {
                    await _activityLogService.LogActivityAsync(
                        user.ClockNumber,
                        "Force Login",
                        "User force-logged in successfully",
                        "Success",
                        HttpContext.Connection.RemoteIpAddress?.ToString(),
                        HttpContext.Request.Headers["User-Agent"].ToString(),
                        securityStamp
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"DirectForceLogin - Error logging activity for {ClockNumber}");
                }

                string redirectUrl = user.PasswordResetRequired == true
                    ? "/Account/ChangePassword"
                    : (user.UserType ?? "").ToLower().Trim() switch
                    {
                        "operator" or "engineer" => "/Operator/Index",
                        "teamleader" or "groupleader" or "manager" or "seniormanager" or "admin"
                        or "engineering teamleader" or "engineering groupleader"
                        or "engineering manager" or "engineering seniormanager" or "engineering admin"
                            => "/ScrapApproval/Dashboard",
                        _ => "/Account/Login"
                    };

                _logger.LogInformation($"DirectForceLogin - Redirect: {redirectUrl} for user type: {user.UserType}");

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success = true, message = "Force login successful", redirectUrl = redirectUrl });

                return Redirect(redirectUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"DirectForceLogin - Unhandled error: {ex.Message}");

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success = false, message = "An error occurred during login", redirectUrl = "/Account/Login" });

                TempData["ErrorMessage"] = "An unexpected error occurred. Please try again.";
                return RedirectToAction("Login");
            }
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> DirectLogin(string clockNumber, string password)
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrEmpty(clockNumber) || string.IsNullOrEmpty(password))
                {
                    TempData["ErrorMessage"] = "Both clock number and password are required";
                    return RedirectToAction("Login");
                }

                _logger.LogInformation($"DirectLogin attempt for: {clockNumber}");

                // First try to invalidate any existing sessions for this user
                await _sessionService.InvalidateSessionAsync(clockNumber);
                _logger.LogInformation($"Existing sessions invalidated for: {clockNumber}");

                // Create a login model
                var model = new LoginViewModel
                {
                    ClockNumber = clockNumber,
                    Password = password,
                    ForceLogin = true  // This ensures we force login
                };

                // Find user
                var user = await _userManager.FindByNameAsync(clockNumber) ??
                           await _context.Users.FirstOrDefaultAsync(u => u.ClockNumber == clockNumber && u.IsActive);

                if (user == null)
                {
                    _logger.LogWarning($"DirectLogin - User not found: {clockNumber}");
                    TempData["ErrorMessage"] = "Invalid credentials";
                    return RedirectToAction("Login");
                }

                _logger.LogInformation($"DirectLogin - User found: {clockNumber}");

                // Verify password
                bool isValidPassword = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
                if (!isValidPassword)
                {
                    _logger.LogWarning($"DirectLogin - Invalid password for: {clockNumber}");
                    TempData["ErrorMessage"] = "Invalid credentials";
                    return RedirectToAction("Login");
                }

                _logger.LogInformation($"DirectLogin - Password verified for: {clockNumber}");

                // Create session ID and retrieve location data
                var securityStamp = Guid.NewGuid().ToString();

                // Get location data from user object
                string department = user.Department;
                string line = user.Line;
                string shift = user.Shift;

                // Log location data to help debug
                _logger.LogInformation($"User location data: Department={department}, Line={line}, Shift={shift}");

                // Create claims with location data
                var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name,
    model.LoginType == "admin"
        ? user.UserName
        : user.ClockNumber
),

            new Claim(ClaimTypes.Role, user.UserType ?? "User"),
            new Claim("SecurityStamp", securityStamp)
        };

                // Add location claims with proper null-checking
                claims.Add(new Claim("Department", !string.IsNullOrEmpty(department) ? department : "Not Set"));
                claims.Add(new Claim("Line", !string.IsNullOrEmpty(line) ? line : "Not Set"));
                claims.Add(new Claim("Shift", !string.IsNullOrEmpty(shift) ? shift : "Not Set"));

                // Create identity and principal
                var identity = new ClaimsIdentity(claims,   IdentityConstants.ApplicationScheme);
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync(
                    IdentityConstants.ApplicationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = model.RememberMe,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
                    }
                );

                _logger.LogInformation($"DirectLogin - User signed in: {clockNumber}");

                // Create session record
                try
                {
                    string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    string userAgent = HttpContext.Request.Headers["User-Agent"].ToString();

                    string userKey = IsManagementRole(user.UserType)
    ? user.UserName
    : user.ClockNumber;


                    await _sessionService.CreateSessionAsync(
    userKey,
    securityStamp,
    ipAddress,
    userAgent
);


                    _logger.LogInformation($"DirectLogin - Session record created: {securityStamp}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"DirectLogin - Error creating session record: {ex.Message}");
                    // Continue even if session creation fails
                }

                string redirectUrl = user.UserType?.ToLower().Trim() switch
                {
                    "operator" or "engineer" => "/Operator/Index",
                    "teamleader" or "groupleader" or "manager" or "seniormanager" or "admin"
                    or "engineering teamleader" or "engineering groupleader"
                    or "engineering manager" or "engineering seniormanager" or "engineering admin"
                        => "/ScrapApproval/Dashboard",
                    _ => "/Account/Login"
                };

                _logger.LogInformation($"DirectLogin - Redirecting to: {redirectUrl}");

                return Redirect(redirectUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"DirectLogin - Unhandled error: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred during login";
                return RedirectToAction("Login");
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult DirectAccess()
        {
            return Content(@"
    <html>
    <head>
        <title>Direct Dashboard Access</title>
        <style>
            body { 
                font-family: Arial; 
                background: #000; 
                color: #00fffc; 
                text-align: center; 
                padding: 50px; 
                margin: 0;
            }
            .container { 
                max-width: 400px; 
                margin: 0 auto; 
                background: rgba(0, 21, 20, 0.9);
                padding: 30px; 
                border: 1px solid #00fffc; 
                box-shadow: 0 0 20px #00fffc;
            }
            h1 { 
                color: #00fffc; 
                font-size: 24px;
                margin-bottom: 30px;
            }
            .button-group {
                display: flex;
                flex-direction: column;
                gap: 15px;
            }
            .btn { 
                display: block;
                background: rgba(0, 15, 14, 0.95); 
                border: 1px solid #00a4a2; 
                color: #00fffc; 
                padding: 15px 20px; 
                text-decoration: none;
                font-size: 16px;
            }
            .btn:hover { 
                background: #00a4a2;
                border-color: #00fffc;
            }
            .warning {
                color: #ff6666;
                font-size: 14px;
                margin: 20px 0;
                padding: 10px;
                border: 1px dashed #ff6666;
            }
            .return {
                margin-top: 30px;
                font-size: 14px;
            }
            .return a {
                color: #999;
                text-decoration: none;
            }
            .return a:hover {
                color: #00fffc;
            }
        </style>
    </head>
    <body>
        <div class='container'>
            <h1>Direct Dashboard Access</h1>
            
            <div class='warning'>
                ⚠️ Note: This bypasses normal authentication.<br>
                Use only when login isn't working properly.
            </div>
            
            <div class='button-group'>
                <a class='btn' href='/Operator/Index'>Operator Dashboard</a>
                <a class='btn' href='/ScrapApproval/Dashboard'>Management Dashboard</a>
            </div>
            
            <div class='return'>
                <a href='/Account/Login'>← Return to normal login</a>
            </div>
        </div>
    </body>
    </html>
    ", "text/html");
        }



        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> ForceLoginAjax([FromBody] LoginViewModel model)
        {
            try
            {
                _logger.LogInformation($"ForceLoginAjax called for user: {model.ClockNumber}");

                // Step 1: Find user
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => (u.ClockNumber == model.ClockNumber ||
                                             u.UserName == model.ClockNumber) &&
                                             u.IsActive);

                if (user == null)
                {
                    _logger.LogWarning($"ForceLoginAjax - User not found: {model.ClockNumber}");
                    return Json(new { success = false, message = "Invalid username or password" });
                }

                // Step 2: Verify password using BCrypt
                bool isValidPassword = !string.IsNullOrEmpty(user.PasswordHash) &&
                                       BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash);

                if (!isValidPassword)
                {
                    _logger.LogWarning($"ForceLoginAjax - Invalid password for: {model.ClockNumber}");
                    return Json(new { success = false, message = "Invalid username or password" });
                }

                _logger.LogInformation($"ForceLoginAjax - Password verified for: {model.ClockNumber}");

                // ✅ FIX: Declare userKey BEFORE using it
                string userKey = IsManagementRole(user.UserType)
                    ? user.UserName
                    : user.ClockNumber;

                _logger.LogInformation($"ForceLoginAjax - Using userKey: {userKey} for user type: {user.UserType}");

                // Step 3: Invalidate existing sessions
                try
                {
                    await _sessionService.InvalidateSessionAsync(userKey);
                    _logger.LogInformation($"ForceLoginAjax - Invalidated sessions for: {userKey}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"ForceLoginAjax - Error invalidating sessions: {ex.Message}");
                    // Continue even if this fails
                }

                // Step 4: Set up authentication
                var securityStamp = Guid.NewGuid().ToString();

                // ✅ Create claims with userKey
                var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, userKey),
            new Claim(ClaimTypes.Role, user.UserType ?? "User"),
            new Claim("SecurityStamp", securityStamp)
        };

                if (!string.IsNullOrEmpty(user.Department))
                    claims.Add(new Claim("Department", user.Department));

                if (!string.IsNullOrEmpty(user.Line))
                    claims.Add(new Claim("Line", user.Line));

                if (!string.IsNullOrEmpty(user.Shift))
                    claims.Add(new Claim("Shift", user.Shift));

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync(
                    IdentityConstants.ApplicationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = model.RememberMe,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
                    }
                );

                _logger.LogInformation($"ForceLoginAjax - User signed in: {model.ClockNumber}");

                // Step 5: Create session record
                try
                {
                    string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    string userAgent = HttpContext.Request.Headers["User-Agent"].ToString();

                    await _sessionService.CreateSessionAsync(
                        userKey,
                        securityStamp,
                        ipAddress,
                        userAgent
                    );

                    _logger.LogInformation($"ForceLoginAjax - Session created for: {model.ClockNumber}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"ForceLoginAjax - Error creating session: {ex.Message}");
                    // Continue even if this fails
                }

                // Determine redirect URL based on user type and password reset status
                string redirectUrl = user.PasswordResetRequired == true
                    ? "/Account/ChangePassword"
                    : user.UserType?.ToLower() switch
                    {
                        "operator" => "/Operator/Index",
                        var type when new[] { "teamleader", "groupleader", "manager", "seniormanager", "admin" }.Contains(type)
                            => "/ScrapApproval/Dashboard",
                        _ => "/Account/Login"
                    };

                // Step 6: Log the activity
                try
                {
                    await _activityLogService.LogActivityAsync(
                        user.ClockNumber,  // Use ClockNumber for activity log consistency
                        "Force Login (AJAX)",
                        "User force-logged in via AJAX",
                        "Success"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"ForceLoginAjax - Error logging activity: {ex.Message}");
                }

                // Return success with redirectUrl in addition to userType
                return Json(new
                {
                    success = true,
                    userType = user.UserType,
                    message = "Authentication successful",
                    redirectUrl = redirectUrl
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ForceLoginAjax - Unhandled error: {ex.Message}");
                return Json(new { success = false, message = "An unexpected error occurred" });
            }
        }

        private async Task LogLoginActivity(string clockNumber, string securityStamp, bool isForced, string status = "Success")
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.ClockNumber == clockNumber);

            await _activityLogService.LogActivityAsync(
                clockNumber,
                "Login",
                isForced ? "User forced login successfully" : "User logged in successfully",
                status,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.Request.Headers["User-Agent"].ToString(),
                securityStamp,
                user?.Department,
                user?.Line
            );
        }

        // Helper method to fix duplicate user records
        private async Task FixDuplicateUserRecords(SqlConnection connection, string clockNumber)
        {
            _logger.LogInformation($"Fixing duplicate records for {clockNumber}");

            // Begin a transaction
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Find all records for this user
                const string usersQuery = @"
            SELECT *
            FROM [IntegrationCompanyB].[dbo].[scpUser]
            WHERE ClockNumber = @ClockNumber OR UserName = @ClockNumber
            ORDER BY IsActive DESC, LastModifiedAt DESC";

                var users = await connection.QueryAsync(usersQuery, new { ClockNumber = clockNumber }, transaction);
                var userList = users.ToList();

                if (userList.Count <= 1)
                {
                    await transaction.CommitAsync();
                    return; // No duplicates
                }

                // Keep the first record, which should be the most recent active one based on our ORDER BY
                var primaryRecord = userList.First();

                // Get UserId column name that actually exists in your database (might be Id, UserId, etc.)
                // For this example, I'll use "UserId" but change it to match your actual schema
                var primaryId = primaryRecord.UserId;

                // Merge data from all records into the primary one
                string bestPasswordHash = userList.FirstOrDefault(u => !string.IsNullOrEmpty(u.PasswordHash))?.PasswordHash
                    ?? primaryRecord.PasswordHash;

                // Update the primary record with the best data
                const string updateQuery = @"
            UPDATE [IntegrationCompanyB].[dbo].[scpUser]
            SET PasswordHash = @PasswordHash,
                IsActive = 1
            WHERE UserId = @PrimaryId";

                await connection.ExecuteAsync(updateQuery,
                    new { PasswordHash = bestPasswordHash, PrimaryId = primaryId },
                    transaction);

                // Delete all duplicates
                const string deleteQuery = @"
            DELETE FROM [IntegrationCompanyB].[dbo].[scpUser]
            WHERE ClockNumber = @ClockNumber
            AND UserId <> @PrimaryId";

                int deleted = await connection.ExecuteAsync(deleteQuery,
                    new { ClockNumber = clockNumber, PrimaryId = primaryId },
                    transaction);

                _logger.LogInformation($"Deleted {deleted} duplicate records for {clockNumber}");

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error fixing duplicate records for {clockNumber}");
            }
        }

        // Helper method to fix duplicate users
        private async Task FixDuplicateUsers(string clockNumber)
        {
            try
            {
                using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    // Find all user records for this clock number
                    const string findQuery = @"
                SELECT * FROM [IntegrationCompanyB].[dbo].[scpUser]
                WHERE ClockNumber = @ClockNumber
                ORDER BY IsActive DESC, LastModifiedAt DESC";

                    var users = await connection.QueryAsync(findQuery, new { ClockNumber = clockNumber }, transaction);
                    var userList = users.ToList();

                    if (userList.Count <= 1)
                    {
                        // No duplicates, nothing to fix
                        await transaction.CommitAsync();
                        return;
                    }

                    _logger.LogInformation($"Found {userList.Count} duplicate records for {clockNumber}, merging...");

                    // Keep the first record (most recently active/modified)
                    var primaryUser = userList.First();
                    var otherUsers = userList.Skip(1);

                    // Get the best values from all records
                    string passwordHash = userList.FirstOrDefault(u => !string.IsNullOrEmpty(u.PasswordHash))?.PasswordHash
                        ?? primaryUser.PasswordHash;

                    string securityStamp = userList.FirstOrDefault(u => !string.IsNullOrEmpty(u.SecurityStamp))?.SecurityStamp
                        ?? primaryUser.SecurityStamp;

                    // Update the primary record with the best data
                    const string updateQuery = @"
                UPDATE [IntegrationCompanyB].[dbo].[scpUser]
                SET PasswordHash = @PasswordHash,
                    SecurityStamp = @SecurityStamp,
                    IsActive = 1
                WHERE ClockNumber = @ClockNumber
                AND Id = @PrimaryId";

                    await connection.ExecuteAsync(updateQuery, new
                    {
                        PasswordHash = passwordHash,
                        SecurityStamp = securityStamp,
                        ClockNumber = clockNumber,
                        PrimaryId = primaryUser.Id
                    }, transaction);

                    // Delete the duplicate records
                    const string deleteQuery = @"
                DELETE FROM [IntegrationCompanyB].[dbo].[scpUser]
                WHERE ClockNumber = @ClockNumber
                AND Id <> @PrimaryId";

                    int deleted = await connection.ExecuteAsync(deleteQuery, new
                    {
                        ClockNumber = clockNumber,
                        PrimaryId = primaryUser.Id
                    }, transaction);

                    _logger.LogInformation($"Deleted {deleted} duplicate records for user {clockNumber}");

                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, $"Error fixing duplicate users for {clockNumber}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to fix duplicate users for {clockNumber}");
                throw;
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordViewModel model)
        {
            try
            {
                _logger.LogInformation("Processing password change request for identifier: {Identifier}", model.ClockNumber);

                if (!ModelState.IsValid)
                {
                    return Json(new { success = false, message = "Please fill all required fields" });
                }

                if (model.NewPassword != model.ConfirmPassword)
                {
                    return Json(new { success = false, message = "Passwords do not match" });
                }

                // Generate hash using BCrypt
                string hashedPassword = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);

                // Use Dapper directly to avoid Entity Framework and transaction issues
                using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    await connection.OpenAsync();

                    // First find the user to get their details
                    const string findUserQuery = @"
            SELECT UserId, ClockNumber, UserName, UserType 
            FROM [IntegrationCompanyB].[dbo].[scpUser] 
            WHERE ClockNumber = @Identifier OR UserName = @Identifier";

                    var user = await connection.QueryFirstOrDefaultAsync(findUserQuery,
                        new { Identifier = model.ClockNumber });

                    if (user == null)
                    {
                        _logger.LogWarning($"User not found with identifier: {model.ClockNumber}");
                        return Json(new { success = false, message = "User not found" });
                    }

                    _logger.LogInformation($"Found user - ClockNumber: {user.ClockNumber}, UserName: {user.UserName}, UserType: {user.UserType}");

                    // Update user password - now search by both fields
                    string updateQuery = @"
                UPDATE [IntegrationCompanyB].[dbo].[scpUser]
                SET 
                    PasswordHash = @NewPasswordHash,
                    PasswordResetRequired = 0,
                    LastPasswordChange = @LastPasswordChange,
                    LastModifiedAt = @LastModifiedAt,
                    LastModifiedBy = @LastModifiedBy
                WHERE ClockNumber = @Identifier OR UserName = @Identifier";

                    int rowsAffected = await connection.ExecuteAsync(updateQuery, new
                    {
                        NewPasswordHash = hashedPassword,
                        LastPasswordChange = DateTime.UtcNow,
                        LastModifiedAt = DateTime.UtcNow,
                        LastModifiedBy = model.ClockNumber,
                        Identifier = model.ClockNumber
                    });

                    if (rowsAffected == 0)
                    {
                        _logger.LogWarning("No rows affected when updating password for user {Identifier}", model.ClockNumber);
                        return Json(new { success = false, message = "Failed to update password" });
                    }

                    _logger.LogInformation("Password updated for user {Identifier}", model.ClockNumber);

                    // Log the activity
                    await _activityLogService.LogActivityAsync(
                        user.ClockNumber, // Use actual ClockNumber for activity logging
                        "Password Change",
                        "User changed their password",
                        "Success"
                    );

                    // Determine redirect URL based on user type
                    string redirectUrl = DetermineRedirectUrl(new User { UserType = user.UserType });

                    return Json(new
                    {
                        success = true,
                        message = "Password changed successfully",
                        redirectUrl = redirectUrl
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password for user {Identifier}", model.ClockNumber);
                return Json(new { success = false, message = "An unexpected error occurred while changing your password" });
            }
        }

        // Update ViewModel to remove CurrentPassword
        public class ChangePasswordViewModel
        {
            [Required]
            public string ClockNumber { get; set; }

            [Required]
            [StringLength(100, MinimumLength = 8)]
            [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$",
                ErrorMessage = "Password must be at least 8 characters and contain uppercase, lowercase, number and special character")]
            public string NewPassword { get; set; }

            [Required]
            [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; }
        }


        [HttpPost]
        public async Task<IActionResult> ApproveRequest(int requestId, string password)
        {
            try
            {
                var approverClockNumber = User.Identity?.Name;
                if (string.IsNullOrEmpty(approverClockNumber))
                {
                    return Json(new { success = false, message = "Approver not authenticated" });
                }

                var request = await _context.scpRequest
                    .Include(r => r.Department)
                    .Include(r => r.Line)
                    .FirstOrDefaultAsync(r => r.RequestId == requestId);

                if (request == null)
                {
                    return Json(new { success = false, message = "Request not found" });
                }

                string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

                string departmentName = request.Department?.DepartmentName;
                string lineName = request.Line?.LineName;

                if (string.IsNullOrEmpty(departmentName) && request.DepartmentId > 0)
                {
                    var department = await _context.scpDepartment.FindAsync(request.DepartmentId);
                    departmentName = department?.DepartmentName;
                }

                if (string.IsNullOrEmpty(lineName) && request.LineId > 0)
                {
                    var line = await _context.scpLine.FindAsync(request.LineId);
                    lineName = line?.LineName;
                }

                if (!string.IsNullOrEmpty(request.ZoneName) && !string.IsNullOrEmpty(lineName))
                {
                    lineName = $"{request.ZoneName} - {lineName}";
                }

                // ✅ compute proper username — same rule as HandleRegistrationRequest
                bool isManagementRole = IsManagementRole(request.UserType);
                string properUsername = isManagementRole
                    ? (!string.IsNullOrWhiteSpace(request.Surname)
                        ? $"{request.Name.Trim().ToLower()}.{request.Surname.Trim().ToLower()}"
                        : request.Name.Trim().ToLower())
                    : request.ClockNumber;

                var user = new User
                {
                    ClockNumber = request.ClockNumber,
                    UserName = properUsername,
                    Email = request.Email,
                    UserType = request.UserType,
                    Department = departmentName ?? "Not Specified",
                    Line = lineName ?? "Not Specified",
                    Shift = request.Shift ?? "Not Specified",
                    IsActive = true,
                    PasswordResetRequired = true,
                    CreatedBy = User.Identity?.Name ?? "System",
                    CreatedAt = DateTime.UtcNow,
                    IsTemporary = false,
                    PasswordHash = hashedPassword   // ✅ correct field
                };

                request.Status = "Approved";
                request.ProcessedBy = User.Identity?.Name;
                request.ProcessedDate = DateTime.UtcNow;

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                var activityLog = new ActivityLog
                {
                    ClockNumber = User.Identity?.Name,
                    ActionType = "User Request",
                    ActionDetails = $"Approved user request for {request.ClockNumber}",
                    Status = "Success",
                    CreatedAt = DateTime.UtcNow
                };
                _context.ActivityLogs.Add(activityLog);

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"User approved successfully. Department: {departmentName}, Line: {lineName}, Shift: {request.Shift}",
                    tempPassword = password
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving user request");
                return Json(new { success = false, message = "An error occurred while approving the user" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> RejectRequest(int requestId)
        {
            try
            {
                var request = await _context.scpRequest.FindAsync(requestId);

                if (request == null)
                {
                    return Json(new { success = false, message = "Request not found" });
                }

                request.Status = "Rejected";
                request.ProcessedBy = User.Identity?.Name;
                request.ProcessedDate = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                // Create activity log instance
                var activityLog = new ActivityLog
                {
                    ClockNumber = User.Identity?.Name,
                    ActionType = "User Request",
                    ActionDetails = $"Rejected user request for {request.ClockNumber}",
                    Status = "Success",
                    CreatedAt = DateTime.UtcNow
                };

                _context.ActivityLogs.Add(activityLog);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Request rejected successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting user request");

                // Log error
                var errorLog = new ErrorLog
                {
                    ClockNumber = User.Identity?.Name,
                    ErrorMessage = ex.Message,
                    StackTrace = ex.StackTrace,
                    ErrorSource = "RejectRequest",
                    CreatedAt = DateTime.UtcNow
                };

                _context.ErrorLogs.Add(errorLog);
                await _context.SaveChangesAsync();

                return Json(new { success = false, message = "An error occurred while rejecting the request" });
            }
        }

        private string GenerateJwtToken(List<Claim> claims)
        {
            var keyBytes = new byte[32];
            System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes); // Modern API

            var key = new SymmetricSecurityKey(keyBytes);
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: "SCP_Control",
                audience: "SCP_Control_Client",
                claims: claims,
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        public async Task<Result> ResetUserPassword(string identifier, string newPassword)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.ClockNumber == identifier);

                if (user == null)
                {
                    _logger.LogWarning("User not found during password reset: {Identifier}", identifier);
                    return Result.CreateFailure("User not found");
                }

                // Hash the temporary password using BCrypt
                string hashedPassword = BCrypt.Net.BCrypt.HashPassword(newPassword, BCrypt.Net.BCrypt.GenerateSalt(12));

                // Store the BCrypt hashed password
                user.PasswordHash = hashedPassword;
                user.PasswordHash = null; // Clear any existing password hash
                user.PasswordResetRequired = true;
                user.LastPasswordChange = DateTime.UtcNow;
                user.SecurityStamp = Guid.NewGuid().ToString();
                user.LastModifiedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Password successfully reset for user {Identifier}", identifier);
                return Result.CreateSuccess("Password reset successful");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting password for user {Identifier}", identifier);
                return Result.CreateFailure("An error occurred while resetting the password");
            }
        }


        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> DebugUserData(string clockNumber)
        {
            // Only enable this in development environment
            if (!_environment.IsDevelopment())
            {
                return NotFound();
            }

            try
            {
                var result = new Dictionary<string, object>();

                using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    await connection.OpenAsync();

                    // First check if the user exists
                    string checkQuery = "SELECT COUNT(1) FROM scpUser WHERE ClockNumber = @ClockNumber OR UserName = @ClockNumber";
                    using (var command = new SqlCommand(checkQuery, connection))
                    {
                        command.Parameters.AddWithValue("@ClockNumber", clockNumber);
                        int count = (int)await command.ExecuteScalarAsync();
                        result["userExists"] = count > 0;

                        if (count > 0)
                        {
                            // Get detailed user info
                            string userQuery = @"
                        SELECT 
                            UserId,
                            ClockNumber,
                            UserName,
                            UserType,
                            Department,
                            Line,
                            Shift,
                            IsActive,
                            PasswordResetRequired,
                            CASE WHEN PasswordHash IS NULL THEN 'NULL' ELSE 'NOT NULL' END AS PasswordHashStatus,
                            Email,                                                           
                            CreatedAt,
                            LastModifiedAt
                        FROM scpUser
                        WHERE ClockNumber = @ClockNumber OR UserName = @ClockNumber";

                            using (var userCommand = new SqlCommand(userQuery, connection))
                            {
                                userCommand.Parameters.AddWithValue("@ClockNumber", clockNumber);
                                using (var reader = await userCommand.ExecuteReaderAsync())
                                {
                                    if (await reader.ReadAsync())
                                    {
                                        var userData = new Dictionary<string, object>();

                                        // Read all fields
                                        for (int i = 0; i < reader.FieldCount; i++)
                                        {
                                            string columnName = reader.GetName(i);
                                            object value = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i);
                                            userData[columnName] = value;
                                        }

                                        result["userData"] = userData;
                                    }
                                }
                            }

                            // Check for registration requests
                            string regQuery = @"
SELECT 
    RequestId, 
    ClockNumber,
    Status,
    RequestDate,
    ProcessedDate
FROM Request
WHERE ClockNumber = @ClockNumber
ORDER BY RequestDate DESC";

                            using (var regCommand = new SqlCommand(regQuery, connection))
                            {
                                regCommand.Parameters.AddWithValue("@ClockNumber", clockNumber);
                                using (var reader = await regCommand.ExecuteReaderAsync())
                                {
                                    var requests = new List<Dictionary<string, object>>();

                                    while (await reader.ReadAsync())
                                    {
                                        var requestData = new Dictionary<string, object>();

                                        // Read all fields
                                        for (int i = 0; i < reader.FieldCount; i++)
                                        {
                                            string columnName = reader.GetName(i);
                                            object value = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i);
                                            requestData[columnName] = value;
                                        }

                                        requests.Add(requestData);
                                    }

                                    result["registrationRequests"] = requests;
                                }
                            }

                            // Check for active sessions
                            string sessionQuery = @"
                        SELECT 
                            SecurityStamp,
                            CreatedAt,
                            LastActivity,
                            IsActive,
                            IpAddress,
                            UserAgent
                        FROM scpUserSession
                        WHERE ClockNumber = @ClockNumber
                        ORDER BY CreatedAt DESC";

                            using (var sessionCommand = new SqlCommand(sessionQuery, connection))
                            {
                                sessionCommand.Parameters.AddWithValue("@ClockNumber", clockNumber);
                                using (var reader = await sessionCommand.ExecuteReaderAsync())
                                {
                                    var sessions = new List<Dictionary<string, object>>();

                                    while (await reader.ReadAsync())
                                    {
                                        var sessionData = new Dictionary<string, object>();

                                        // Read all fields
                                        for (int i = 0; i < reader.FieldCount; i++)
                                        {
                                            string columnName = reader.GetName(i);
                                            object value = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i);
                                            sessionData[columnName] = value;
                                        }

                                        sessions.Add(sessionData);
                                    }

                                    result["sessions"] = sessions;
                                }
                            }
                        }
                    }
                }

                return Json(result);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }


       

        // Helper method to get dashboard URL based on user type
        private string GetDashboardUrl(string userType)
        {
            return userType?.ToLower() switch
            {
                "operator" => "/Operator/Index",
                var type when new[] { "teamleader", "groupleader", "manager", "seniormanager", "admin" }
                    .Contains(type) => "/ScrapApproval/Dashboard",
                _ => "/Account/Login"
            };
        }

        [HttpPost]
        public async Task<IActionResult> UpdatePasswordHash([FromBody] UpdatePasswordHashModel model)
        {
            try
            {
                var user = await _userManager.Users.FirstOrDefaultAsync(u => u.ClockNumber == model.ClockNumber);
                if (user == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                // Use the custom PasswordHasher
                var passwordHasher = new CustomPasswordHasher();
                var newPasswordHash = passwordHasher.HashPassword(user, model.NewPassword);

                // Update the PasswordHash in the database
                user.PasswordHash = newPasswordHash;
                var updateResult = await _userManager.UpdateAsync(user);

                if (updateResult.Succeeded)
                {
                    return Json(new { success = true, message = "Password hash updated successfully" });
                }
                else
                {
                    return Json(new
                    {
                        success = false,
                        message = "Failed to update password hash: " + string.Join(", ", updateResult.Errors.Select(e => e.Description))
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating password hash for user {ClockNumber}", model.ClockNumber);
                return Json(new { success = false, message = "An error occurred while updating the password hash" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> CheckDuplicateRegistration(string clockNumber)
        {
            try
            {
                if (string.IsNullOrEmpty(clockNumber))
                    return Json(new { isDuplicate = false });

                using var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await conn.OpenAsync();

                var userCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM scpUser WHERE ClockNumber = @ClockNumber",
                    new { ClockNumber = clockNumber });

                var requestCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM scpRequest WHERE ClockNumber = @ClockNumber AND Status = 'Pending'",
                    new { ClockNumber = clockNumber });

                return Json(new { isDuplicate = userCount > 0 || requestCount > 0 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for duplicate registration");
                return Json(new { isDuplicate = false });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DebugLoginAttempt([FromBody] LoginViewModel model)
        {
            var debugInfo = await _authService.DebugLogin(model.ClockNumber, model.Password);
            return Json(new { debug = debugInfo });
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Login(string returnUrl = null, bool sessionExpired = false)
        {
            try
            {
                _logger.LogInformation("GET Login called");

                // Clear any existing authentication
                if (User?.Identity?.IsAuthenticated == true)
                {
                    await _signInManager.SignOutAsync();
                }

                // SAFE header setting - won't throw if already exists
                Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
                Response.Headers["Pragma"] = "no-cache";
                Response.Headers["Expires"] = "0";

                ViewData["SessionExpired"] = sessionExpired;
                ViewData["Message"] = sessionExpired ? "Your session has expired. Please log in again." : null;
                ViewData["ReturnUrl"] = returnUrl;

                var model = new LoginViewModel { ReturnUrl = returnUrl };
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Login GET method");
                return View(new LoginViewModel());
            }
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginPost([FromForm] LoginViewModel model)
        {
            try
            {
                _logger.LogInformation($"LoginPost called for user: {model.ClockNumber}");

                if (!ModelState.IsValid)
                    return await HandleLoginFailure("Please fill in all required fields.", model);

                await _signInManager.SignOutAsync();
                await Task.Delay(100);

                // Find user by UserName OR ClockNumber (both should work)
                var user = await _context.Users
                    .Where(u => (u.UserName == model.ClockNumber ||
                                 u.ClockNumber == model.ClockNumber) && u.IsActive)
                    .FirstOrDefaultAsync();

                if (user == null)
                {
                    _logger.LogWarning($"User not found: {model.ClockNumber}");
                    return await HandleLoginFailure("Invalid username or password.", model);
                }

                if (string.IsNullOrEmpty(user.PasswordHash) ||
                    !BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
                {
                    _logger.LogWarning($"Invalid password for user: {model.ClockNumber}");
                    return await HandleLoginFailure("Invalid username or password.", model);
                }

                // UserName is the single source of truth for identity
                // Operators/TL: UserName = ClockNumber
                // GL to Admin:  UserName = name.surname
                string userKey = user.UserName ?? user.ClockNumber;

                _logger.LogInformation($"Using userKey: {userKey} for user type: {user.UserType}");

                var securityStamp = Guid.NewGuid().ToString();
                bool hadExistingSession = false;

                try
                {
                    var existingSession = await _sessionService.GetActiveSessionAsync(userKey);
                    if (existingSession != null)
                    {
                        hadExistingSession = true;
                        _logger.LogInformation($"Found existing session, invalidating...");
                    }
                    await _sessionService.InvalidateSessionAsync(userKey);
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error managing sessions");
                    return await HandleLoginFailure("Unable to manage sessions.", model);
                }

                // Claims use UserName as the identity name - consistent with session key
                var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, userKey),
            new Claim("ClockNumber", user.ClockNumber ?? userKey),
            new Claim(ClaimTypes.Role, (user.UserType ?? "User").ToLower()),
            new Claim("SecurityStamp", securityStamp),
            new Claim("Department", user.Department ?? "Not Set"),
            new Claim("Line", user.Line ?? "Not Set"),
            new Claim("Shift", user.Shift ?? "Not Set")
        };

                var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync(
                    IdentityConstants.ApplicationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = model.RememberMe,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
                    }
                );

                // Create session using userKey - matches User.Identity.Name
                try
                {
                    string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    string userAgent = HttpContext.Request.Headers["User-Agent"].ToString();

                    await _sessionService.CreateSessionAsync(userKey, securityStamp, ipAddress, userAgent);

                    var savedSession = await _sessionService.GetActiveSessionAsync(userKey);
                    if (savedSession == null || savedSession.SecurityStamp != securityStamp)
                    {
                        _logger.LogError("Session verification failed!");
                        return await HandleLoginFailure("Session creation failed.", model);
                    }
                    _logger.LogInformation("Session verified in database");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating session");
                    return await HandleLoginFailure("Unable to create session.", model);
                }

                // Log using ClockNumber for activity consistency
                try
                {
                    await _activityLogService.LogActivityAsync(
                        user.ClockNumber,
                        "Login",
                        hadExistingSession ? "Login successful - previous session terminated" : "Login successful",
                        "Success"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error logging activity");
                }

                string redirectUrl = DetermineRedirectUrl(user);

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new
                    {
                        success = true,
                        redirectUrl = redirectUrl,
                        message = hadExistingSession
                            ? "Login successful. Previous session terminated."
                            : "Login successful"
                    });
                }

                return Redirect(redirectUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in LoginPost");
                return await HandleLoginFailure("An error occurred during login.", model);
            }
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> ClearUserSessions([FromForm] string clockNumber)
        {
            try
            {
                _logger.LogInformation($"ClearUserSessions called for: {clockNumber}");

                if (string.IsNullOrEmpty(clockNumber))
                {
                    _logger.LogWarning("ClearUserSessions called with empty clock number");
                    return Json(new { success = false, message = "Clock number is required" });
                }

                using var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                await conn.OpenAsync();
                var userExists = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM scpUser WHERE (ClockNumber = @cn OR UserName = @cn) AND IsActive = 1",
                    new { cn = clockNumber }) > 0;

                if (!userExists)
                {
                    _logger.LogWarning($"User not found: {clockNumber}");
                    return Json(new { success = false, message = "User not found" });
                }

                // IMPORTANT: Sign out first to clear cookies
                await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                await Task.Delay(100); // Small delay to ensure signout completes

                // Clear all sessions in database
                await _sessionService.InvalidateSessionAsync(clockNumber);
                _logger.LogInformation($"✅ All sessions cleared for user: {clockNumber}");

                // Log the activity
                try
                {
                    await _activityLogService.LogActivityAsync(
                        clockNumber,
                        "Sessions Cleared",
                        "User manually cleared all sessions from login page",
                        "Success"
                    );
                }
                catch (Exception logEx)
                {
                    _logger.LogError(logEx, "Error logging session clear activity");
                }

                return Json(new
                {
                    success = true,
                    message = "All sessions cleared. You can now log in."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Error clearing sessions for user: {clockNumber}");
                return Json(new
                {
                    success = false,
                    message = "Error clearing sessions. Please contact support."
                });
            }
        }


        [HttpGet]
        [AllowAnonymous]
        public IActionResult DebugClaimsSimple()
        {
            if (User?.Identity?.IsAuthenticated != true)
            {
                return Json(new
                {
                    IsAuthenticated = false,
                    Message = "User is not authenticated"
                });
            }

            var claimInfo = new Dictionary<string, object>
            {
                ["IsAuthenticated"] = User.Identity.IsAuthenticated,
                ["Username"] = User.Identity.Name ?? "Not set",
                ["ClaimsCount"] = User.Claims.Count(),
                ["UserType"] = User.FindFirst(ClaimTypes.Role)?.Value ?? "No role claim found"
            };

            // Add individual claims
            var claimsList = new List<string>();
            foreach (var claim in User.Claims)
            {
                claimsList.Add($"{claim.Type}: '{claim.Value}'");
            }
            claimInfo["AllClaims"] = claimsList;

            // Check specific role claims
            claimInfo["HasOperatorClaim"] = User.HasClaim(ClaimTypes.Role, "operator");
            claimInfo["HasOperatorClaimCapital"] = User.HasClaim(ClaimTypes.Role, "Operator");

            // Find all role claims
            var roleClaims = User.Claims.Where(c => c.Type == ClaimTypes.Role).ToList();
            claimInfo["RoleClaimsFound"] = roleClaims.Select(c => $"'{c.Value}'").ToList();

            return Json(claimInfo);
        }



        private string DetermineRedirectUrl(User user)
        {
            if (user.PasswordResetRequired == true)
                return "/Account/ChangePassword";

            var userType = (user.UserType ?? "").ToLower().Trim();
            return userType switch
            {
                "operator" or "engineer" => "/Operator/Index",
                "teamleader" or "groupleader" or "manager" or "seniormanager" or "admin"
                or "engineering teamleader" or "engineering groupleader"
                or "engineering manager" or "engineering seniormanager" or "engineering admin"
                    => "/ScrapApproval/Dashboard",
                _ => "/Account/Login"
            };
        }

        private async Task<IActionResult> HandleLoginFailure(string message, LoginViewModel model)
        {
            try
            {
                await _activityLogService.LogActivityAsync(
                    model.ClockNumber ?? "unknown",
                    "Login Failed",
                    message,
                    "Failed"
                );
            }
            catch
            {
                // Ignore logging errors
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = false, message = message });
            }

            TempData["ErrorMessage"] = message;
            return View("Login", model);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> ForceLogin([FromBody] LoginViewModel model)
        {
            try
            {
                _logger.LogInformation($"ForceLogin called for user: {model.ClockNumber}");

                // Set force login flag and reuse LoginPost logic
                model.ForceLogin = true;

                // Call the main login method
                var result = await LoginPost(model);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in ForceLogin: {ex.Message}");
                return Json(new { success = false, message = "An error occurred during force login." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            try
            {
                var userKey = User.Identity?.Name;
                var securityStamp = User.FindFirst("SecurityStamp")?.Value;

                _logger.LogInformation($"Logout initiated for user {userKey}");

                // Invalidate session in database
                if (!string.IsNullOrEmpty(userKey))
                {
                    try
                    {
                        await _sessionService.InvalidateSessionAsync(userKey);

                        // Log the activity
                        await _activityLogService.LogActivityAsync(
                            userKey,
                            "Logout",
                            "User logged out successfully",
                            "Success"
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error during session cleanup for {userKey}");
                    }
                }

                // Sign out using Identity
                await _signInManager.SignOutAsync();

                // Clear session
                HttpContext.Session.Clear();

                // For AJAX requests
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new
                    {
                        success = true,
                        redirectUrl = "/Account/Login"
                    });
                }

                return RedirectToAction("Login", "Account");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                return RedirectToAction("Login", "Account");
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult ForceRefreshLogin()
        {
            // Add cache-busting headers
            Response.Headers.Add("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
            Response.Headers.Add("Pragma", "no-cache");
            Response.Headers.Add("Expires", "0");

            // Use TempData to signal a fresh login page
            TempData["FreshLogin"] = true;

            // Redirect to login with a unique parameter to prevent caching

            return RedirectToAction("Login", new { fresh = true, _ = DateTime.Now.Ticks });
        }
    }
}