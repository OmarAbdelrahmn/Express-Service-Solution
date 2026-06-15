using Application.EmailWarmup;
using Application.Service.DailyReport;
using Application.Service.VehiclePermission;
using Express_Service;
using Hangfire;

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

var app = builder.Build();

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
//}

using (var scope = app.Services.CreateScope())
{
    var scheduler = scope.ServiceProvider.GetRequiredService<ReportScheduler>();
    scheduler.RegisterAll();
}

app.UseHangfireDashboard("/job", new DashboardOptions
{
    // Remove this line in production or add auth filter
    Authorization = []
});

RecurringJob.AddOrUpdate<IDailyReportJob>(
    "daily-rider-report",
    x => x.RunAsync(null),
    "0 12 * * *",
    new RecurringJobOptions
    {
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time")
    });

RecurringJob.AddOrUpdate<IVehiclePermissionRenewalJob>(
    "vehicle-permission-renewal",
    x => x.RunAsync(CancellationToken.None),
    "0 12 * * *",                          // same time: noon daily
    new RecurringJobOptions
    {
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time")
    });

RecurringJob.AddOrUpdate<IEmailWarmupJob>(
    "email-warmup",
    x => x.RunAsync(CancellationToken.None),
    "*/20 9-17 * * *",   // Every 20 min, between 9:00 and 17:59
    new RecurringJobOptions
    {
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time")
    });

// 3 PM KSA = 12:00 UTC
RecurringJob.AddOrUpdate<IAbsentReportJob>(
    "absent-report-company-1",
    job => job.RunAsync(1, null),
    "0 7 * * *");

RecurringJob.AddOrUpdate<IAbsentReportJob>(
    "absent-report-company-2",
    job => job.RunAsync(2, null),
    "0 7 * * *");


app.UseStaticFiles();

app.UseResponseCaching();

app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("health");

app.Run();
