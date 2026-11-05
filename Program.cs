using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SCP_Control.DbContext;
using SCP_Control.Extensions;
using SCP_Control.Filters;
using SCP_Control.Models;
using SCP_Control.Services.Interfaces;
using SCP_Control.Services;
using System.Security.Claims;
using System.Threading.RateLimiting;
using SCP_Control.Middleware;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Serilog;
using Serilog.Events;

namespace SCP_Control;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ============================================================================
        // ENHANCED LOGGING CONFIGURATION
        // ============================================================================

        var logsPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
        var appLogsPath = Path.Combine(logsPath, "Application");
        var errorLogsPath = Path.Combine(logsPath, "Errors");
        var perfLogsPath = Path.Combine(logsPath, "Performance");

        Directory.CreateDirectory(appLogsPath);
        Directory.CreateDirectory(errorLogsPath);
        Directory.CreateDirectory(perfLogsPath);

        // Configure Serilog - Enhanced version with better error handling
        Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Authentication", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithMachineName()
    .Enrich.WithProcessName()
    .Enrich.WithThreadId()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine(appLogsPath, "app-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 50 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        shared: true, // ADD THIS - allows multiple processes to write
        flushToDiskInterval: TimeSpan.FromSeconds(1), // ADD THIS - ensures writes are flushed
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine(errorLogsPath, "errors-.log"),
        restrictedToMinimumLevel: LogEventLevel.Error,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 90,
        fileSizeLimitBytes: 100 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        shared: true, // ADD THIS
        flushToDiskInterval: TimeSpan.FromSeconds(1), // ADD THIS
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine(perfLogsPath, "performance-.log"),
        restrictedToMinimumLevel: LogEventLevel.Warning,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        fileSizeLimitBytes: 25 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        shared: true, // ADD THIS
        flushToDiskInterval: TimeSpan.FromSeconds(1), // ADD THIS
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();

        builder.Host.UseSerilog();

        // ============================================================================
        // WEB HOST CONFIGURATION
        // ============================================================================

        var kestrelSection = builder.Configuration.GetSection("Kestrel:Endpoints:Http:Url");
        if (!string.IsNullOrEmpty(kestrelSection.Value))
        {
            builder.WebHost.UseUrls(kestrelSection.Value);
        }
        else
        {
            builder.WebHost.UseUrls("http://0.0.0.0:9000");
        }

        // ============================================================================
        // DATABASE CONFIGURATION
        // ============================================================================

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString));

        // ============================================================================
        // SESSION CONFIGURATION
        // ============================================================================

        builder.Services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromHours(12);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        // ============================================================================
        // AUTHORIZATION CONFIGURATION
        // ============================================================================

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("OperatorOnly", policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim(ClaimTypes.Role, "operator"));

            options.AddPolicy("ApproverOnly", policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim(ClaimTypes.Role, "teamleader", "groupleader", "manager", "seniormanager"));

            options.AddPolicy("AdminOnly", policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim(ClaimTypes.Role, "admin"));

            options.AddPolicy("Level1Approver", policy =>
                policy.RequireAssertion(context =>
                    context.User.HasClaim(c => c.Type == ClaimTypes.Role &&
                        c.Value.Equals("teamleader", StringComparison.OrdinalIgnoreCase))));

            options.AddPolicy("Level2Approver", policy =>
                policy.RequireAssertion(context =>
                    context.User.HasClaim(c => c.Type == ClaimTypes.Role &&
                        c.Value.Equals("groupleader", StringComparison.OrdinalIgnoreCase))));

            options.AddPolicy("Level3Approver", policy =>
                policy.RequireAssertion(context =>
                    context.User.HasClaim(c => c.Type == ClaimTypes.Role &&
                        c.Value.Equals("manager", StringComparison.OrdinalIgnoreCase))));

            options.AddPolicy("Level4Approver", policy =>
                policy.RequireAssertion(context =>
                    context.User.HasClaim(c => c.Type == ClaimTypes.Role &&
                        c.Value.Equals("seniormanager", StringComparison.OrdinalIgnoreCase))));

            options.AddPolicy("AnyApprover", policy =>
                policy.RequireAssertion(context =>
                    context.User.HasClaim(c => c.Type == ClaimTypes.Role &&
                        (c.Value.Equals("teamleader", StringComparison.OrdinalIgnoreCase) ||
                         c.Value.Equals("groupleader", StringComparison.OrdinalIgnoreCase) ||
                         c.Value.Equals("manager", StringComparison.OrdinalIgnoreCase) ||
                         c.Value.Equals("seniormanager", StringComparison.OrdinalIgnoreCase)))));

            options.AddPolicy("ManagementOnly", policy =>
                policy.RequireAssertion(context =>
                    context.User.HasClaim(c => c.Type == ClaimTypes.Role &&
                        (c.Value.Equals("teamleader", StringComparison.OrdinalIgnoreCase) ||
                         c.Value.Equals("groupleader", StringComparison.OrdinalIgnoreCase) ||
                         c.Value.Equals("manager", StringComparison.OrdinalIgnoreCase) ||
                         c.Value.Equals("seniormanager", StringComparison.OrdinalIgnoreCase) ||
                         c.Value.Equals("admin", StringComparison.OrdinalIgnoreCase)))));

            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        // ============================================================================
        // IDENTITY CONFIGURATION
        // ============================================================================

        builder.Services.AddIdentity<User, IdentityRole<int>>(options =>
        {
            options.SignIn.RequireConfirmedAccount = false;
            options.SignIn.RequireConfirmedEmail = false;
            options.SignIn.RequireConfirmedPhoneNumber = false;

            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;

            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        // ============================================================================
        // APPLICATION COOKIE CONFIGURATION
        // ============================================================================

        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
            options.SlidingExpiration = true;
            options.Cookie.Name = ".AspNetCore.Identity.Application";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Lax;

            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/Account/Login"))
                    {
                        return Task.CompletedTask;
                    }

                    if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        context.Response.StatusCode = 401;
                        context.Response.ContentType = "application/json";
                        return context.Response.WriteAsync("{\"success\":false,\"message\":\"Session expired\",\"redirectUrl\":\"/Account/Login?sessionExpired=true\"}");
                    }

                    var redirectUri = $"{options.LoginPath}?sessionExpired=true";
                    context.Response.Redirect(redirectUri);
                    return Task.CompletedTask;
                }
            };
        });

        // ============================================================================
        // SERVICE REGISTRATION
        // ============================================================================

        builder.Services.AddScoped<IAuthService, SecureAuthService>();
        builder.Services.AddScoped<IAuditService, AuditService>();
        builder.Services.AddScoped<IEmailService, EmailService>();
        builder.Services.AddScoped<IInventoryService, InventoryService>();
        builder.Services.AddScoped<IOperatorService, OperatorService>();
        builder.Services.AddScoped<IScrapApprovalService, ScrapApprovalService>();
        builder.Services.AddScoped<IDepartmentLineService, DepartmentLineService>();
        builder.Services.AddScoped<IUserRegistrationService, UserRegistrationService>();
        builder.Services.AddScoped<IActivityLogService, ActivityLogService>();
        builder.Services.AddScoped<IScannerService, ScannerService>();
        builder.Services.AddScoped<IUserService, UserService>();

        builder.Services.AddSingleton<ISessionService, SessionService>();
        builder.Services.AddHostedService<SessionCleanupService>();

        builder.Services.AddSingleton(typeof(DinkToPdf.Contracts.IConverter), new DinkToPdf.SynchronizedConverter(new DinkToPdf.PdfTools()));

        builder.Services.AddScoped<IBatchIdService, BatchIdService>();

        // Register BOM Processing Service
        builder.Services.AddScoped<IBomProcessingService, BomProcessingService>();
        // ============================================================================
        // CORS AND JSON CONFIGURATION
        // ============================================================================

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.WriteIndented = true;
        });

        // ============================================================================
        // ANTIFORGERY AND MVC CONFIGURATION
        // ============================================================================

        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "RequestVerificationToken";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.SuppressXFrameOptionsHeader = false;
        });

        builder.Services.AddControllersWithViews()
             .AddRazorRuntimeCompilation();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddDistributedMemoryCache();

        var app = builder.Build();

        // ============================================================================
        // MIDDLEWARE PIPELINE CONFIGURATION
        // ============================================================================

        Log.Information("SCP Control Application Starting Up");
        Log.Information("Environment: {Environment}", app.Environment.EnvironmentName);
        Log.Information("Logs Directory: {LogsPath}", logsPath);
        Log.Information("Content Root: {ContentRoot}", app.Environment.ContentRootPath);
        Log.Information("Base URL: {BaseUrl}", builder.Configuration["BaseUrl"]);

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            Log.Information("Development mode: Developer exception page enabled");
        }
        else
        {
            app.UseExceptionHandler(appBuilder =>
            {
                appBuilder.Run(async context =>
                {
                    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                    var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();

                    if (exceptionFeature != null)
                    {
                        logger.LogError(exceptionFeature.Error, "Unhandled exception occurred");

                        var isAjaxRequest = context.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                                          context.Request.Headers["Content-Type"].ToString().Contains("application/json");

                        if (isAjaxRequest)
                        {
                            context.Response.StatusCode = 500;
                            context.Response.ContentType = "application/json";

                            var response = new
                            {
                                success = false,
                                message = "An error occurred while processing your request. Please try again."
                            };

                            await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
                        }
                        else
                        {
                            context.Response.Redirect("/Account/Login?error=true");
                        }
                    }
                });
            });
            app.UseHsts();
            Log.Information("Production mode: Exception handler and HSTS enabled");
        }

        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception ex)
            {
                var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "Unhandled exception in middleware pipeline");

                var isAjaxRequest = context.Request.Headers["X-Requested-With"] == "XMLHttpRequest";

                if (isAjaxRequest && !context.Response.HasStarted)
                {
                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "application/json";

                    var response = new
                    {
                        success = false,
                        message = "An error occurred while processing your request. Please try again."
                    };

                    await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
                }
                else
                {
                    throw;
                }
            }
        });

        if (builder.Configuration.GetValue<bool>("UseHttpsRedirection", false))
        {
            app.UseHttpsRedirection();
            Log.Information("HTTPS redirection enabled");
        }

        app.UseStaticFiles();

        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
            options.GetLevel = (httpContext, elapsed, ex) => ex != null
                ? LogEventLevel.Error
                : elapsed > 5000
                    ? LogEventLevel.Warning
                    : httpContext.Response.StatusCode > 499
                        ? LogEventLevel.Error
                        : LogEventLevel.Information;

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
                diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers["User-Agent"].FirstOrDefault());

                if (httpContext.User.Identity?.IsAuthenticated == true)
                {
                    diagnosticContext.Set("UserName", httpContext.User.Identity.Name);
                }
            };
        });

        app.UseRouting();
        app.UseCors();
        app.UseSession();
        app.UseAuthentication();

        // *** CRITICAL: SESSION VALIDATION MIDDLEWARE ***
        app.UseMiddleware<SessionValidationMiddleware>();
        Log.Information("Session validation middleware registered - single session enforcement active");

        app.UseAuthorization();

        app.MapControllerRoute(
            name: "scanner",
            pattern: "Scanner/{action=RoleSelection}/{id?}",
            defaults: new { controller = "Scanner" }
        );

        app.MapControllerRoute(
            name: "scannerDevice",
            pattern: "ScannerDevice/{action=Index}/{id?}",
            defaults: new { controller = "ScannerDevice" }
        );

        app.MapControllerRoute(
            name: "operator",
            pattern: "Operator/{action=Index}/{id?}",
            defaults: new { controller = "Operator" }
        );

        app.MapControllerRoute(
            name: "scrapApproval",
            pattern: "ScrapApproval/{action=Dashboard}/{id?}",
            defaults: new { controller = "ScrapApproval" }
        );

        app.MapControllerRoute(
            name: "analytics",
            pattern: "Analytics/{action=Index}/{id?}",
            defaults: new { controller = "Analytics" }
        );

        app.MapControllerRoute(
            name: "export",
            pattern: "Export/{action=ExportToExcel}/{id?}",
            defaults: new { controller = "Export" }
        );

        app.MapControllerRoute(
            name: "account",
            pattern: "Account/{action=Login}/{id?}",
            defaults: new { controller = "Account" }
        );

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Account}/{action=Login}/{id?}"
        );

        app.MapGet("/", async context =>
        {
            context.Response.Redirect("/Account/Login");
        });

        try
        {
            Log.Information("SCP Control Application Started Successfully on {Environment}", app.Environment.EnvironmentName);
            Log.Information("Application running on: {BaseUrl}", builder.Configuration["BaseUrl"]);
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.Information("SCP Control Application Shutting Down");
            Log.CloseAndFlush();
        }
    }
}