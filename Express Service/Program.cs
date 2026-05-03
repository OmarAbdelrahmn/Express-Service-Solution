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
                      "https://fastexp.netlify.app",
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


app.UseStaticFiles();

app.UseResponseCaching();

app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("health");

app.Run();
