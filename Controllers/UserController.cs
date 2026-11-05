using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SCP_Control.DbContext;
using SCP_Control.Models;
using SCP_Control.ViewModels;
using System.Security.Claims;

namespace SCP_Control.Controllers
{
    public class UserController : Controller
    {
        private readonly UserManager<User> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<UserController> _logger;

        public UserController(
            UserManager<User> userManager,
            ApplicationDbContext context,
            ILogger<UserController> logger)
        {
            _userManager = userManager;
            _context = context;
            _logger = logger;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateLocation([FromBody] LocationUpdateRequest request)
        {
            try
            {
                var username = User.Identity?.Name;
                if (string.IsNullOrEmpty(username))
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                var user = await _context.Users.FirstOrDefaultAsync(u => u.ClockNumber == username);
                if (user == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                user.Department = request.Department;
                user.Line = request.Line;
                user.Shift = request.Shift;
                user.LastModifiedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Location updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user location");
                return Json(new { success = false, message = "Failed to update location" });
            }
        }

        private async Task UpdateLocationClaims(User user, LocationUpdateModel model)
        {
            var claims = await _userManager.GetClaimsAsync(user);

            // Remove existing location claims
            var locationClaims = claims.Where(c =>
                c.Type == "Department" || c.Type == "Line" || c.Type == "Shift");

            foreach (var claim in locationClaims)
                await _userManager.RemoveClaimAsync(user, claim);

            // Add new claims
            await _userManager.AddClaimAsync(user, new Claim("Department", model.Department));

            var isManagementRole = IsManagementRole(user.UserType);
            if (!isManagementRole)
            {
                if (!string.IsNullOrEmpty(model.Line))
                    await _userManager.AddClaimAsync(user, new Claim("Line", model.Line));
                if (!string.IsNullOrEmpty(model.Shift))
                    await _userManager.AddClaimAsync(user, new Claim("Shift", model.Shift));
            }
        }

        private bool IsManagementRole(string userType)
        {
            return userType == "GroupLeader" || userType == "Manager" ||
                   userType == "SeniorManager" || userType == "Admin";
        }
    }

    public class LocationUpdateModel
    {
        public string Department { get; set; }
        public string? Line { get; set; }
        public string? Shift { get; set; }
    }
}
