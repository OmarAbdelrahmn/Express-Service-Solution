using Domain;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Service.DailyReport;

public interface IMonthlyProgressReportJob
{
    Task RunAsync(int companyId, DateOnly? today = null, int retryCount = 0);
}

public class MonthlyProgressReportJob(
    ApplicationDbcontext db,
    IMonthlyProgressReportEmailSender emailSender,
    ILogger<MonthlyProgressReportJob> logger,
    IBackgroundJobClient hangfire,
    IWebHostEnvironment env,
    IOptions<DailyReportSettings> options) : IMonthlyProgressReportJob
{
    private const int MonthlyTarget = 450;
    private const int MaxRetries = 30;   // 30 × 5 min = 2.5 hours
    private readonly DailyReportSettings _settings = options.Value;

    public async Task RunAsync(int companyId, DateOnly? today = null, int retryCount = 0)
    {
        // ── Master on/off switch ─────────────────────────────────────────────
        if (!_settings.IsEnabled)
        {
            logger.LogInformation(
                "MonthlyProgressReportJob disabled via settings. Skipping company {CompanyId}.", companyId);
            return;
        }

        var reportingDate = today ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        // Day 1 of the month means there is no "yesterday" in the current month yet
        if (reportingDate.Day == 1)
        {
            logger.LogInformation(
                "MonthlyProgressReport: day 1 of month — no prior data. Skipping company {CompanyId}.", companyId);
            return;
        }

        var yesterday = reportingDate.AddDays(-1);

        logger.LogInformation(
            "MonthlyProgressReport starting for company {CompanyId}, reporting date {Date} (retry #{Retry})",
            companyId, reportingDate, retryCount);

        // ── Gate: yesterday's shift data must be uploaded ────────────────────
        var hasData = await db.RiderShifts
            .AnyAsync(s => s.ShiftDate == yesterday && s.CompanyId == companyId);

        if (!hasData)
        {
            if (retryCount >= MaxRetries)
            {
                logger.LogWarning(
                    "MonthlyProgressReport: max retries ({Max}) reached for company {CompanyId} on {Date}. Giving up.",
                    MaxRetries, companyId, reportingDate);
                return;
            }

            logger.LogInformation(
                "MonthlyProgressReport: no shifts yet for company {CompanyId} on {Date}. " +
                "Retry #{Next} scheduled in 5 minutes.",
                companyId, yesterday, retryCount + 1);

            hangfire.Schedule<IMonthlyProgressReportJob>(
                job => job.RunAsync(companyId, reportingDate, retryCount + 1),
                TimeSpan.FromMinutes(5));

            return;
        }

        try
        {
            // ── Progress math ────────────────────────────────────────────────
            // yesterday.Day = number of days elapsed in the month with data
            var daysElapsed = yesterday.Day;
            var daysInMonth = DateTime.DaysInMonth(yesterday.Year, yesterday.Month);
            var proportional = (int)Math.Round((double)daysElapsed / daysInMonth * MonthlyTarget);

            // ── Load all active, non-employee riders for this company ─────────
            var riders = await db.RiderDetails
                .AsNoTracking()
                .Include(r => r.Employee)
                    .ThenInclude(e => e.Housing)
                .Where(r =>
                    r.CompanyId == companyId &&
                    r.Employee.Status == "enable" &&
                    r.Employee.IsEmployee == false &&
                    r.Employee.IsDeleted == false)
                .ToListAsync();

            if (riders.Count == 0)
            {
                logger.LogInformation(
                    "MonthlyProgressReport: no active riders for company {CompanyId}. Skipping.", companyId);
                return;
            }

            // ── Load all shifts for this month up to yesterday ────────────────
            var monthStart = new DateOnly(yesterday.Year, yesterday.Month, 1);

            var shiftTotals = await db.RiderShifts
                .AsNoTracking()
                .Where(s =>
                    s.ShiftDate >= monthStart &&
                    s.ShiftDate <= yesterday &&
                    s.CompanyId == companyId)
                .GroupBy(s => s.RiderId)
                .Select(g => new { RiderId = g.Key, Total = g.Sum(s => s.AcceptedDailyOrders) })
                .ToDictionaryAsync(x => x.RiderId, x => x.Total);

            // ── Build behind-target list ──────────────────────────────────────
            var behindRows = new List<MonthlyProgressRiderRow>();

            foreach (var rider in riders)
            {
                var orders = shiftTotals.GetValueOrDefault(rider.Id, 0);

                if (orders < proportional)
                {
                    behindRows.Add(new MonthlyProgressRiderRow(
                        RiderNameAR: rider.Employee?.NameAR ?? "—",
                        IqamaNo: rider.EmployeeIqamaNo,
                        HousingName: rider.Employee?.Housing?.Name ?? "بدون سكن",
                        WorkingId: rider.WorkingId ?? "—",
                        OrdersSoFar: orders,
                        ProportionalTarget: proportional,
                        RemainingToFullTarget: Math.Max(0, MonthlyTarget - orders),
                        Shortfall: proportional - orders));
                }
            }

            if (behindRows.Count == 0)
            {
                logger.LogInformation(
                    "MonthlyProgressReport: all riders on-track for company {CompanyId} on {Date}. No email sent.",
                    companyId, reportingDate);
                return;
            }

            // Sort: worst shortfall first, then by housing
            behindRows = behindRows
                .OrderByDescending(r => r.Shortfall)
                .ThenBy(r => r.HousingName)
                .ThenBy(r => r.RiderNameAR)
                .ToList();

            // ── Load company name & logo ──────────────────────────────────────
            var company = await db.Companies.FindAsync(companyId);
            var companyName = company?.Name ?? $"شركة {companyId}";

            var logoPath = Path.Combine(env.WebRootPath, "images", "company-logo.png");
            byte[]? logoBytes = File.Exists(logoPath)
                ? await File.ReadAllBytesAsync(logoPath)
                : null;

            // ── Build payload and send ────────────────────────────────────────
            var payload = new MonthlyProgressPayload(
                ReportDate: yesterday,
                CompanyId: companyId,
                CompanyName: companyName,
                DaysElapsed: daysElapsed,
                DaysInMonth: daysInMonth,
                MonthlyTarget: MonthlyTarget,
                ProportionalTarget: proportional,
                Riders: behindRows);

            await emailSender.SendAsync(payload, logoBytes);

            logger.LogInformation(
                "MonthlyProgressReport for company {CompanyId} ({Name}) sent. " +
                "Behind-target riders: {Count} / {Total}. Day {Day}/{DaysInMonth}.",
                companyId, companyName,
                behindRows.Count, riders.Count,
                daysElapsed, daysInMonth);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to send monthly progress report for company {CompanyId} on {Date}",
                companyId, reportingDate);
            throw;
        }
    }
}