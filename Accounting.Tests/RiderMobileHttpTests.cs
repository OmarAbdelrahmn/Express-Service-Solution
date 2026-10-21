using Application.Authentication;
using Application.Contracts.RiderMobile;
using Application.Service.RiderMobile;
using Domain;
using Domain.Entities;
using Express_Service;
using Express_Service.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Accounting.Tests;

public class RiderMobileHttpTests
{
    [Fact]
    public async Task MobileApi_EnforcesIdentitySchemeOwnershipLanguageAndRevocation()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = RiderMobileServiceTests.Jwt.Issuer,
            ["Jwt:Audience"] = RiderMobileServiceTests.Jwt.Audience,
            ["Jwt:Key"] = RiderMobileServiceTests.Jwt.Key,
            ["Jwt:ExpiryIn"] = "60"
        });
        var databaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<ApplicationDbcontext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IJwtAccountValidator, JwtAccountValidator>();
        builder.Services.AddAuth(builder.Configuration);
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("login", limiter =>
            {
                limiter.PermitLimit = 10;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(RiderMobileController).Assembly);
        await using var app = builder.Build();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbcontext>();
            await RiderMobileServiceTests.Seed(db);
            await RiderMobileServiceTests.Seed(db, 2);
            db.Users.Add(new ApplicationUser
            {
                Id = "staff-user", UserName = "staff-user", SecurityStamp = "staff-stamp"
            });
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/rider-mobile/me")).StatusCode);
        var invalidLogin = await client.PostAsJsonAsync("/api/rider-mobile/login", new { iqamaNo = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidLogin.StatusCode);
        var login = await client.PostAsJsonAsync("/api/rider-mobile/login", new { iqamaNo = "2100000001", language = "en" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(login.Headers.CacheControl?.NoStore);
        var session = (await login.Content.ReadFromJsonAsync<RiderMobileLoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var me = await client.GetAsync("/api/rider-mobile/me?riderId=2");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var profile = (await me.Content.ReadFromJsonAsync<RiderMobileProfile>())!;
        Assert.Equal(1, profile.RiderId); // Extra query input cannot select another rider.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/rider-mobile/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/RiderScorePerformance")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/rider-mobile/performance?year=2026&month=13")).StatusCode);

        var changed = await client.PutAsJsonAsync("/api/rider-mobile/language", new { language = "bn" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var replacement = (await changed.Content.ReadFromJsonAsync<RiderMobileLoginResponse>())!;
        Assert.Equal("bn", replacement.Language);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/rider-mobile/me")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", replacement.AccessToken);
        var updated = (await client.GetFromJsonAsync<RiderMobileProfile>("/api/rider-mobile/me"))!;
        Assert.Equal("bn", updated.Language);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/rider-mobile/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/rider-mobile/me")).StatusCode);

        var staffToken = new JwtProvider(Options.Create(RiderMobileServiceTests.Jwt)).GenerateToken(new ApplicationUser
        {
            Id = "staff-user", UserName = "staff-user", SecurityStamp = "staff-stamp"
        }, ["Admin"]).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/rider-mobile/me")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        for (var i = 0; i < 8; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/rider-mobile/login", new { iqamaNo = "2100000099" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/rider-mobile/login", new { iqamaNo = "2100000099" })).StatusCode);
        await app.StopAsync();
    }
}
