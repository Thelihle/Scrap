using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SCP_Control.DbContext;
using SCP_Control.Models;
using System.Security.Cryptography;
using System.Text;

[ApiController]
[Route("api/[controller]")]
public class DeviceController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DeviceController> _logger;

    public DeviceController(ApplicationDbContext context, ILogger<DeviceController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterDevice([FromBody] DeviceRegistrationRequest request)
    {
        try
        {
            // Generate device fingerprint
            var deviceFingerprint = GenerateDeviceFingerprint(request);

            // Check if device already exists
            var existingDevice = await _context.RegisteredDevices
                .FirstOrDefaultAsync(d => d.MacAddress == request.MacAddress ||
                                         d.DeviceFingerprint == deviceFingerprint);

            if (existingDevice != null)
            {
                // Update existing device
                existingDevice.IPAddress = request.IPAddress;
                existingDevice.LastSeen = DateTime.Now;
                existingDevice.UserAgent = request.UserAgent;
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    deviceId = existingDevice.DeviceId,
                    deviceName = existingDevice.DeviceName,
                    message = "Device updated successfully",
                    isNewDevice = false
                });
            }

            // Register new device
            var newDevice = new RegisteredDevice
            {
                DeviceName = request.DeviceName ?? $"TC21-{DateTime.Now:MMddHHmm}",
                DeviceModel = request.DeviceModel ?? "Unknown",
                SerialNumber = request.SerialNumber,
                MacAddress = request.MacAddress,
                IPAddress = request.IPAddress,
                DeviceFingerprint = deviceFingerprint,
                AndroidVersion = request.AndroidVersion,
                BuildFingerprint = request.BuildFingerprint,
                UserAgent = request.UserAgent,
                IsActive = true,
                LastSeen = DateTime.Now
            };

            _context.RegisteredDevices.Add(newDevice);
            await _context.SaveChangesAsync();

            // Generate auto-login token
            var token = GenerateSecureToken();
            var deviceToken = new DeviceToken
            {
                DeviceId = newDevice.DeviceId,
                Token = token,
                ExpiresAt = DateTime.Now.AddDays(30), // Token valid for 30 days
                IsActive = true
            };

            _context.DeviceTokens.Add(deviceToken);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"New device registered: {newDevice.DeviceName} ({newDevice.MacAddress})");

            return Ok(new
            {
                success = true,
                deviceId = newDevice.DeviceId,
                deviceName = newDevice.DeviceName,
                token = token,
                message = "Device registered successfully",
                isNewDevice = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering device");
            return StatusCode(500, new { success = false, message = "Error registering device" });
        }
    }

    [HttpPost("identify")]
    public async Task<IActionResult> IdentifyDevice([FromBody] DeviceIdentificationRequest request)
    {
        try
        {
            var deviceFingerprint = GenerateDeviceFingerprint(request);

            var device = await _context.RegisteredDevices
                .FirstOrDefaultAsync(d => d.MacAddress == request.MacAddress ||
                                         d.DeviceFingerprint == deviceFingerprint);

            if (device == null)
            {
                return Ok(new
                {
                    success = false,
                    isRegistered = false,
                    message = "Device not registered"
                });
            }

            // Update last seen
            device.LastSeen = DateTime.Now;
            device.IPAddress = request.IPAddress;
            await _context.SaveChangesAsync();

            // Check for valid auto-login token
            var validToken = await _context.DeviceTokens
                .FirstOrDefaultAsync(t => t.DeviceId == device.DeviceId &&
                                         t.IsActive &&
                                         t.ExpiresAt > DateTime.Now);

            return Ok(new
            {
                success = true,
                isRegistered = true,
                deviceId = device.DeviceId,
                deviceName = device.DeviceName,
                assignedUser = device.AssignedUser,
                department = device.Department,
                hasAutoLoginToken = validToken != null,
                autoLoginToken = validToken?.Token,
                message = "Device identified successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error identifying device");
            return StatusCode(500, new { success = false, message = "Error identifying device" });
        }
    }

    [HttpPost("auto-login")]
    public async Task<IActionResult> AutoLogin([FromBody] AutoLoginRequest request)
    {
        try
        {
            var deviceToken = await _context.DeviceTokens
                .Include(t => t.Device)
                .FirstOrDefaultAsync(t => t.Token == request.Token &&
                                         t.IsActive &&
                                         t.ExpiresAt > DateTime.Now);

            if (deviceToken == null)
            {
                return Ok(new { success = false, message = "Invalid or expired token" });
            }

            var device = deviceToken.Device;

            // Check if device has an assigned user
            if (string.IsNullOrEmpty(device.AssignedUser))
            {
                return Ok(new
                {
                    success = false,
                    requiresUserSelection = true,
                    deviceName = device.DeviceName,
                    message = "Device not assigned to a user"
                });
            }

            // Find the assigned user
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.ClockNumber == device.AssignedUser && u.IsActive);

            if (user == null)
            {
                return Ok(new
                {
                    success = false,
                    message = "Assigned user not found or inactive"
                });
            }

            // Create device session
            var session = new DeviceSession
            {
                DeviceId = device.DeviceId,
                UserClockNumber = user.ClockNumber,
                IPAddress = request.IPAddress,
                UserAgent = request.UserAgent,
                SessionStatus = "Active",
                LastActivity = DateTime.Now
            };

            _context.DeviceSessions.Add(session);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Auto-login successful: Device {device.DeviceName} -> User {user.ClockNumber}");

            return Ok(new
            {
                success = true,
                securityStamp = session.SecurityStamp,
                userId = user.ClockNumber,
                userName = user.UserName,
                userType = user.UserType,
                department = user.Department,
                line = user.Line,
                shift = user.Shift,
                message = "Auto-login successful"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during auto-login");
            return StatusCode(500, new { success = false, message = "Error during auto-login" });
        }
    }

    [HttpGet("list")]
    public async Task<IActionResult> GetRegisteredDevices()
    {
        try
        {
            var devices = await _context.RegisteredDevices
                .Where(d => d.IsActive)
                .Select(d => new {
                    d.DeviceId,
                    d.DeviceName,
                    d.DeviceModel,
                    d.MacAddress,
                    d.IPAddress,
                    d.Department,
                    d.AssignedUser,
                    d.LastSeen,
                    d.AndroidVersion,
                    IsOnline = d.LastSeen > DateTime.Now.AddMinutes(-10) // Consider online if seen in last 10 minutes
                })
                .OrderByDescending(d => d.LastSeen)
                .ToListAsync();

            return Ok(new { success = true, devices });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving device list");
            return StatusCode(500, new { success = false, message = "Error retrieving devices" });
        }
    }

    [HttpPost("assign-user")]
    public async Task<IActionResult> AssignUserToDevice([FromBody] AssignUserRequest request)
    {
        try
        {
            var device = await _context.RegisteredDevices
                .FindAsync(request.DeviceId);

            if (device == null)
            {
                return NotFound(new { success = false, message = "Device not found" });
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.ClockNumber == request.UserClockNumber && u.IsActive);

            if (user == null)
            {
                return NotFound(new { success = false, message = "User not found or inactive" });
            }

            device.AssignedUser = user.ClockNumber;
            device.Department = request.Department ?? user.Department;
            device.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Device {device.DeviceName} assigned to user {user.ClockNumber}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning user to device");
            return StatusCode(500, new { success = false, message = "Error assigning user" });
        }
    }

    [HttpDelete("{deviceId}")]
    public async Task<IActionResult> RemoveDevice(int deviceId)
    {
        try
        {
            var device = await _context.RegisteredDevices
                .FindAsync(deviceId);

            if (device == null)
            {
                return NotFound(new { success = false, message = "Device not found" });
            }

            // Deactivate device instead of deleting (safer approach)
            device.IsActive = false;
            device.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            _logger.LogInformation($"Device deactivated: {device.DeviceName} (ID: {deviceId})");

            return Ok(new
            {
                success = true,
                message = $"Device {device.DeviceName} has been deactivated"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing device");
            return StatusCode(500, new { success = false, message = "Error removing device" });
        }
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> DeviceHeartbeat([FromBody] HeartbeatRequest request)
    {
        try
        {
            // Find device by IP or other identifier
            var device = await _context.RegisteredDevices
                .FirstOrDefaultAsync(d => d.IPAddress == request.IPAddress && d.IsActive);

            if (device != null)
            {
                device.LastSeen = DateTime.Now;
                await _context.SaveChangesAsync();
            }

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing heartbeat");
            return StatusCode(500, new { success = false });
        }
    }

    private string GenerateDeviceFingerprint(dynamic deviceInfo)
    {
        var fingerprint = $"{deviceInfo.DeviceModel}-{deviceInfo.MacAddress}-{deviceInfo.AndroidVersion}-{deviceInfo.BuildFingerprint}";
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(fingerprint));
        return Convert.ToBase64String(hash).Substring(0, 32);
    }

    private string GenerateSecureToken()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }

 
}

// Request models
public class DeviceRegistrationRequest
{
    public string? DeviceName { get; set; }
    public string? DeviceModel { get; set; }
    public string? SerialNumber { get; set; }
    public string MacAddress { get; set; } = "";
    public string? IPAddress { get; set; }
    public string? AndroidVersion { get; set; }
    public string? BuildFingerprint { get; set; }
    public string? UserAgent { get; set; }
}

public class DeviceIdentificationRequest : DeviceRegistrationRequest { }

public class AutoLoginRequest
{
    public string Token { get; set; } = "";
    public string? IPAddress { get; set; }
    public string? UserAgent { get; set; }
}

public class AssignUserRequest
{
    public int DeviceId { get; set; }
    public string UserClockNumber { get; set; } = "";
    public string? Department { get; set; }
}

public class HeartbeatRequest
{
    public string? IPAddress { get; set; }
    public string? Timestamp { get; set; }
}