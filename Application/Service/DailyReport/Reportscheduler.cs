using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Service.DailyReport;

/// <summary>
/// Registers all recurring Hangfire jobs.
/// Call RegisterAll() once at application startup (after Hangfire server is running).
/// </summary>
public class ReportScheduler(
    IRecurringJobManager recurring,
    IBackgroundJobClient hangfire,
    IOptions<DailyReportSettings> options,
    ILogger<ReportScheduler> logger)
{
    private readonly DailyReportSettings _settings = options.Value;

    // ── Company IDs ──────────────────────────────────────────────────────────
    // Add / remove company IDs here as needed.
    private static readonly int[] CompanyIds = [1, 2];

    // ── Cron helpers ─────────────────────────────────────────────────────────
    // "0 9 * * *"  = every day at 09:00 server-local time
    private const string DailyAt9Am = "0 9 * * *";

    public void RegisterAll()
    {
        if (!_settings.IsEnabled)
        {
            logger.LogInformation("ReportScheduler: reporting is disabled via settings. No jobs registered.");
            return;
        }

        foreach (var companyId in CompanyIds)
        {
            // ── 1. Absent / low-hours daily report ───────────────────────────
            var absentJobId = $"absent-report-company-{companyId}";
            recurring.AddOrUpdate<IDailyReportJob>(
                absentJobId,
                job => job.RunAsync(null, false),
                DailyAt9Am,
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time"),
                    MisfireHandling = MisfireHandlingMode.Ignorable
                });

            logger.LogInformation(
                "ReportScheduler: registered absent report job '{JobId}' at 09:00 AST.", absentJobId);

            // ── 2. Monthly progress / behind-target report ───────────────────
            var monthlyJobId = $"monthly-progress-report-company-{companyId}";
            recurring.AddOrUpdate<IMonthlyProgressReportJob>(
                monthlyJobId,
                job => job.RunAsync(companyId, null, 0),
                DailyAt9Am,
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time"),
                    MisfireHandling = MisfireHandlingMode.Ignorable
                });

            logger.LogInformation(
                "ReportScheduler: registered monthly progress report job '{JobId}' at 09:00 AST.", monthlyJobId);
        }
    }

    // ── Manual trigger (e.g. from a debug endpoint) ──────────────────────────
    public void TriggerNow(int companyId, ReportType type)
    {
        switch (type)
        {
            case ReportType.Absent:
                hangfire.Enqueue<IDailyReportJob>(job => job.RunAsync(null, false));
                logger.LogInformation(
                    "ReportScheduler: manually triggered absent report for company {CompanyId}.", companyId);
                break;

            case ReportType.MonthlyProgress:
                hangfire.Enqueue<IMonthlyProgressReportJob>(job => job.RunAsync(companyId, null, 0));
                logger.LogInformation(
                    "ReportScheduler: manually triggered monthly progress report for company {CompanyId}.", companyId);
                break;
        }
    }
}

public enum ReportType
{
    Absent,
    MonthlyProgress
}