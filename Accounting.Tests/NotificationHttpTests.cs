using Application.Authentication;
using Application.Contracts.Notifications;
using Application.Contracts.RiderMobile;
using Application.Service.Notifications;
using Domain;
using Domain.Entities;
using Express_Service;
using Express_Service.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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

public class NotificationHttpTests
{
    [Fact]
    public async Task Routes_SeparateMobileAndStaffAuth_EnforcePermissionsAndReturnDurableStatus()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:Enabled"] = "false",
            ["Jwt:Issuer"] = RiderMobileServiceTests.Jwt.Issuer,
            ["Jwt:Audience"] = RiderMobileServiceTests.Jwt.Audience,
            ["Jwt:Key"] = RiderMobileServiceTests.Jwt.Key,
            ["Jwt:ExpiryIn"] = "60"
        });
        var database = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<ApplicationDbcontext>(o => o.UseInMemoryDatabase(database));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IJwtAccountValidator, JwtAccountValidator>();
        builder.Services.AddAuth(builder.Configuration);
        builder.Services.AddNotifications(builder.Configuration);
        builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("login", l =>
        { l.PermitLimit = 10; l.Window = TimeSpan.FromMinutes(1); l.QueueLimit = 0; }));
        builder.Services.AddControllers().AddApplicationPart(typeof(NotificationsController).Assembly);
        await using var app = builder.Build();
        app.UseDeveloperExceptionPage();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapControllers();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbcontext>();
            await RiderMobileServiceTests.Seed(db);
            db.Users.Add(new ApplicationUser { Id = "staff", UserName = "staff", SecurityStamp = "stamp" });
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Urls.Single()) };
        var installation = Guid.NewGuid();
        var deviceUrl = $"/api/rider-mobile/devices/{installation:D}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(deviceUrl,
            new RegisterPushDeviceRequest("fid", "android", "authorized", null))).StatusCode);
        var login = await client.PostAsJsonAsync("/api/rider-mobile/login", new { iqamaNo = "2100000001", language = "en" });
        var mobile = (await login.Content.ReadFromJsonAsync<RiderMobileLoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", mobile.AccessToken);
        var registered = await client.PutAsJsonAsync(deviceUrl, new RegisterPushDeviceRequest("fid", "android", "authorized", null));
        Assert.True(registered.StatusCode == HttpStatusCode.NoContent, await registered.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/admin/riders/1/notifications", NotificationTemplates.DailyReminder)).StatusCode);
        var staff = new ApplicationUser { Id = "staff", UserName = "staff", SecurityStamp = "stamp" };
        var tokens = new JwtProvider(Options.Create(RiderMobileServiceTests.Jwt));
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.GenerateToken(staff, ["Admin"]).Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/admin/riders/1/notifications", NotificationTemplates.DailyReminder)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.GenerateToken(staff, ["Master"]).Token);
        var queued = await client.PostAsJsonAsync("/api/admin/riders/1/notifications", NotificationTemplates.DailyReminder);
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        Assert.NotNull(queued.Headers.Location);
        var status = await client.GetAsync(queued.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.True(status.Headers.CacheControl?.NoStore);
        Assert.Equal("queued", (await status.Content.ReadFromJsonAsync<NotificationStatusResponse>())!.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync(deviceUrl)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", mobile.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/rider-mobile/logout", null)).StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
            Assert.False((await scope.ServiceProvider.GetRequiredService<ApplicationDbcontext>().RiderPushDevices.SingleAsync()).IsActive);
        await app.StopAsync();
    }
}
