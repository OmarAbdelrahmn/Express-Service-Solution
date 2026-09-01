using Application.Authentication;
using Application.Service.AccountingOutbox;
using Application.Service.VehiclePermission;
using Application.Service.Vacation;
using Domain.Auditing;
using Express_Service;
using Hangfire;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy.WithOrigins(
                      "http://localhost:3000",
                      "https://expserco.com",
                      "https://forsenex.com"
                  )
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
});



builder.Services.AddDependencies(builder.Configuration);

builder.Services.AddSingleton<IWebHostEnvironment>(builder.Environment);

builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var configuredProxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(configuredProxy, out var proxyAddress))
            options.KnownProxies.Add(proxyAddress);
    }
});

var app = builder.Build();

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    var suppliedCorrelationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    var correlationId = !string.IsNullOrWhiteSpace(suppliedCorrelationId) && suppliedCorrelationId.Length <= 128
        ? suppliedCorrelationId
        : Guid.NewGuid().ToString("N");

    context.TraceIdentifier = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;

    using (app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
    {
        await next();
    }
});

app.UseExceptionHandler();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{

QuestPDF.Drawing.FontManager.RegisterFont(
    File.OpenRead(
        Path.Combine(builder.Environment.WebRootPath, "Font", "ScheherazadeNew-Medium.ttf")
    )
);


app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
});

using (var scope = app.Services.CreateScope())
{
    var identityBootstrapper = scope.ServiceProvider.GetRequiredService<IIdentityBootstrapper>();
    await identityBootstrapper.InitializeAsync();

}

app.UseHangfireDashboard("/job");

// Remove legacy recurring jobs that send email. Hangfire persists recurring
// jobs, so removing their registration alone would leave old schedules active.
foreach (var emailJobId in new[]
{
    "email-warmup",
    "daily-rider-report",
    "absent-report-company-1",
    "absent-report-company-2",
    "monthly-progress-report-company-1",
    "monthly-progress-report-company-2"
})
{
    RecurringJob.RemoveIfExists(emailJobId);
}

RecurringJob.AddOrUpdate<IAccountingOutboxJob>(
    "accounting-outbox",
    job => job.ProcessAsync(CancellationToken.None),
    "* * * * *",
    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

RecurringJob.AddOrUpdate<IVehiclePermissionRenewalJob>(
    "vehicle-permission-renewal",
    x => x.RunAsync(CancellationToken.None),
    "0 12 * * *",                          // same time: noon daily
    new RecurringJobOptions
    {
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time")
    });

RecurringJob.AddOrUpdate<IVacationLifecycleJob>(
    "vacation-lifecycle",
    job => job.RunAsync(CancellationToken.None),
    "5 0 * * *",
    new RecurringJobOptions
    {
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time")
    });


app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/vacation-documents", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/financial-operations", StringComparison.OrdinalIgnoreCase) &&
        !app.Configuration.GetValue<bool>("Accounting:LegacyFinancialOperationsEnabled"))
    {
        context.Response.StatusCode = StatusCodes.Status410Gone;
        await context.Response.WriteAsJsonAsync(new { error = "The legacy financial write API is disabled. Use /api/accounting resources." });
        return;
    }
    await next();
});

app.UseStaticFiles();

app.UseResponseCaching();

app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

app.UseRateLimiter();

app.UseAuthentication();

app.Use(async (context, next) =>
{
    var actorUserId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? context.User.FindFirst("sub")?.Value;
    var actorName = context.User.Identity?.Name ?? actorUserId ?? "Anonymous";
    var auditContext = context.RequestServices.GetRequiredService<IAuditContextAccessor>();
    auditContext.Set(new AuditContext(
        Guid.NewGuid(),
        string.IsNullOrWhiteSpace(actorUserId) ? AuditActorType.System : AuditActorType.User,
        actorUserId,
        actorName,
        "Http",
        $"{context.Request.Method} {context.Request.Path}",
        context.TraceIdentifier,
        context.Request.Method,
        context.Request.Path,
        context.Connection.RemoteIpAddress?.ToString()));

    await next();
});

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("health").AllowAnonymous();

app.Run();
