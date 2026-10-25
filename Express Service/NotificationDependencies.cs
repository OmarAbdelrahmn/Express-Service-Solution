using Application.Service.Notifications;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace Express_Service;

public static class NotificationDependencies
{
    public static IServiceCollection AddNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<NotificationOptions>().Bind(configuration.GetSection("Notifications"))
            .ValidateDataAnnotations()
            .Validate(x => !x.Enabled || !string.IsNullOrWhiteSpace(x.DataProtectionKeyPath),
                "Notifications:DataProtectionKeyPath is required when sending is enabled; persist keys across deployments.")
            .Validate(x => x.StaffGrants.All(g => !string.IsNullOrWhiteSpace(g.UserId) && (g.CompanyId is null or > 0)) &&
                x.StaffGrants.Select(g => g.UserId).Distinct().Count() == x.StaffGrants.Count,
                "Notification staff grants require one nonblank user ID per grant and valid company IDs.")
            .ValidateOnStart();
        var keys = configuration["Notifications:DataProtectionKeyPath"];
        var protection = services.AddDataProtection().SetApplicationName("ExpressService");
        if (!string.IsNullOrWhiteSpace(keys))
        {
            protection.PersistKeysToFileSystem(new DirectoryInfo(keys));
            var certificate = configuration["Notifications:DataProtectionCertificateThumbprint"];
            if (!string.IsNullOrWhiteSpace(certificate)) protection.ProtectKeysWithCertificate(certificate);
            else if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }
        services.AddScoped<IPushDeviceService, PushDeviceService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationJob, NotificationJob>();
        services.AddScoped<IRiderMobileCoachingJob, RiderMobileCoachingJob>();
        services.AddSingleton<INotificationSender, FirebaseNotificationSender>();
        services.AddHealthChecks().AddCheck<NotificationHealthCheck>("notifications", tags: ["notifications"]);
        services.AddAuthorization(auth =>
        {
            foreach (var operation in new[] { "Send", "Broadcast", "Read" })
                auth.AddPolicy("Notifications." + operation, policy => policy
                    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser()
                    .RequireAssertion(context =>
                    {
                        if (context.User.IsInRole("Master")) return true;
                        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
                        return configuration.GetSection("Notifications:StaffGrants").Get<List<NotificationStaffGrant>>()?
                            .Any(x => x.UserId == id && (operation == "Send" ? x.Send : operation == "Broadcast" ? x.Broadcast : x.Send || x.Broadcast)) == true;
                    }));
        });
        services.AddRateLimiter(rate =>
        {
            rate.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rate.AddPolicy("notification-devices", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue("sid") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            rate.AddPolicy("notification-staff", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }
}

public class NotificationHealthCheck(INotificationSender sender, IOptions<NotificationOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) return HealthCheckResult.Healthy("Notification sending is disabled.");
        return await sender.IsReadyAsync(cancellationToken) ? HealthCheckResult.Healthy("Provider initialized; live delivery not verified.")
            : HealthCheckResult.Degraded("Notification credentials or project configuration are unavailable.");
    }
}
