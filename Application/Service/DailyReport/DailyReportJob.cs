using Domain;
using Domain.Entities;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Service.DailyReport;

public interface IDailyReportJob
{
    Task RunAsync(DateOnly? targetDate = null, bool forcedResend = false);
}

public class DailyReportJob(
    ApplicationDbcontext db,
    IDailyReportEmailSender emailSender,
    ILogger<DailyReportJob> logger,
    IBackgroundJobClient hangfire,
    IWebHostEnvironment env,
    IOptions<DailyReportSettings> options) : IDailyReportJob
{
    private readonly IWebHostEnvironment _env = env;
    private readonly DailyReportSettings _settings = options.Value;

    public async Task RunAsync(DateOnly? targetDate = null, bool forceResend = false)
    {
        // ── Master on/off switch ─────────────────────────────────────────────

        _settings.IsEnabled = false; // TEMPORARY: disable the job until further notice

        if (!_settings.IsEnabled)
        {
            logger.LogInformation("DailyReportJob is disabled via settings. Skipping.");
            return;
        }

        var reportDate = targetDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3).AddDays(-1));

        logger.LogInformation("DailyReportJob starting for {Date}", reportDate);

        // ── Fetch log entry if exists (for retry count tracking only) ────────
        var log = await db.DailyReportLogs
            .FirstOrDefaultAsync(x => x.ReportDate == reportDate);

        // ── Fetch shifts ─────────────────────────────────────────────────────
        var shifts = await db.RiderShifts
            .AsNoTracking()
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Include(s => s.Company)
            .Include(s => s.Housing)
            .Where(s => s.ShiftDate == reportDate)
            .ToListAsync();

        // ── Load company logo ─────────────────────────────────────────────────
        var logoPath = Path.Combine(_env.WebRootPath, "images", "company-logo.png");
        byte[]? logoBytes = File.Exists(logoPath)
            ? await File.ReadAllBytesAsync(logoPath, cancellationToken: default)
            : null;

        // ── Guard: data must exist for company 1 AND company 2 ───────────────
        var companyIds = shifts.Select(s => s.CompanyId).ToHashSet();

        var missing = new List<string>();
        if (shifts.Count == 0 || !companyIds.Contains(1)) missing.Add("الشركة الأولى (ID: 1)");
        if (shifts.Count == 0 || !companyIds.Contains(2)) missing.Add("الشركة الثانية (ID: 2)");

        if (missing.Count > 0)
        {
            var missingNames = string.Join(" و ", missing);
            var retryCount = log?.RetryCount ?? 0;

            logger.LogWarning(
                "No shifts found for {Missing} on {Date}. Retry #{Count} scheduled in 5 minutes.",
                missingNames, reportDate, retryCount + 1);

            if (log is null)
            {
                db.DailyReportLogs.Add(new DailyReportLog
                {
                    ReportDate = reportDate,
                    IsSent = false,
                    RetryCount = 1,
                    ErrorMessage = $"لا توجد ورديات لـ {missingNames}"
                });
            }
            else
            {
                log.RetryCount++;
                log.ErrorMessage = $"لا توجد ورديات لـ {missingNames} — بعد {log.RetryCount} محاولة/محاولات";
            }

            await db.SaveChangesAsync();

            hangfire.Schedule<IDailyReportJob>(
                job => job.RunAsync(reportDate, false),
                TimeSpan.FromMinutes(5));

            return;
        }

        // ── Build → PDF → Send → Log ──────────────────────────────────────────
        try
        {
            var payload = BuildPayload(reportDate, shifts);

            var pdfBytes = DailyReportPdfGenerator.Generate(payload, logoBytes);
            await emailSender.SendAsync(payload, pdfBytes, logoBytes);

            // Always upsert the log — never block future sends
            if (log is null)
            {
                db.DailyReportLogs.Add(new DailyReportLog
                {
                    ReportDate = reportDate,
                    IsSent = true,
                    SentAt = DateTime.UtcNow.AddHours(3),
                    RetryCount = 0
                });
            }
            else
            {
                log.IsSent = true;
                log.SentAt = DateTime.UtcNow.AddHours(3);
                log.ErrorMessage = null;
            }

            await db.SaveChangesAsync();

            logger.LogInformation(
                "Daily report for {Date} sent. Companies: {C}, Shifts: {S}",
                reportDate, payload.Companies.Count, payload.GrandTotalShifts);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send daily report for {Date}", reportDate);

            if (log is null)
                db.DailyReportLogs.Add(new DailyReportLog
                {
                    ReportDate = reportDate,
                    IsSent = false,
                    RetryCount = 1,
                    ErrorMessage = ex.Message
                });
            else
            {
                log.RetryCount++;
                log.ErrorMessage = ex.Message;
            }

            await db.SaveChangesAsync();
            throw;
        }
    }

    // ── Payload builder ───────────────────────────────────────────────────────
    private static DailyReportPayload BuildPayload(DateOnly date, List<RiderShift> shifts)
    {
        var companies = new List<CompanyReportBlock>();

        var byCompany = shifts.GroupBy(s => new
        {
            s.CompanyId,
            CompanyName = s.Company?.Name ?? $"شركة {s.CompanyId}"
        });

        foreach (var companyGroup in byCompany.OrderBy(c => c.Key.CompanyName))
        {
            var allRows = companyGroup
                .OrderByDescending(s => s.AcceptedDailyOrders)
                .Select(MapToRow)
                .ToList();

            var byHousing = allRows
                .GroupBy(r => r.HousingName)
                .OrderBy(g => g.Key)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToList());

            companies.Add(new CompanyReportBlock(
                CompanyName: companyGroup.Key.CompanyName,
                RowsByHousing: byHousing,
                TotalShifts: companyGroup.Count()));
        }

        return new DailyReportPayload(
            ReportDate: date,
            Companies: companies,
            GrandTotalShifts: shifts.Count);
    }

    private static ShiftReportRow MapToRow(RiderShift s) =>
        new(
            RiderNameAR: s.Rider?.Employee?.NameAR ?? "—",
            IqamaNo: s.Rider?.EmployeeIqamaNo ?? 0,
            HousingName: s.Housing?.Name ?? "بدون سكن",
            AcceptedOrders: s.AcceptedDailyOrders,
            WorkingHours: s.WorkingHours
        );
}