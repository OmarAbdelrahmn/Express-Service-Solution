using Application.Authentication;
using Application.Service.RiderMobile;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Security.Claims;

namespace Express_Service;

public static class RiderMobileDependencies
{
    public static IServiceCollection AddRiderMobile(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RiderMobileOptions>().Bind(configuration.GetSection(RiderMobileOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddScoped<IRiderMobileTokenProvider, RiderMobileTokenProvider>();
        services.AddScoped<IRiderMobileService, RiderMobileService>();
        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>()
            ?? throw new InvalidOperationException("JWT configuration is missing.");
        services.AddAuthentication().AddJwtBearer(RiderMobileTokenProvider.Scheme, options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = RiderMobileTokenProvider.ValidationParameters(jwt);
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var user = context.Principal;
                    if (user?.FindFirstValue(RiderMobileTokenProvider.PurposeClaim) != RiderMobileTokenProvider.Scheme ||
                        !int.TryParse(user.FindFirstValue(RiderMobileTokenProvider.RiderIdClaim), out var riderId) || riderId <= 0 ||
                        !Guid.TryParse(user.FindFirstValue(RiderMobileTokenProvider.SessionClaim), out var sessionId) ||
                        user.FindFirstValue("sub") != riderId.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                        user.FindFirstValue(RiderMobileTokenProvider.LanguageClaim) is not ("ar" or "en" or "bn"))
                    {
                        context.Fail("Invalid rider session claims.");
                        return;
                    }
                    var service = context.HttpContext.RequestServices.GetRequiredService<IRiderMobileService>();
                    if (!await service.IsSessionValidAsync(sessionId, riderId,
                        user.FindFirstValue(RiderMobileTokenProvider.LanguageClaim)!, context.HttpContext.RequestAborted))
                        context.Fail("The rider session is no longer active.");
                }
            };
        });
        return services;
    }
}
