using Domain;
using Domain.Entities;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Service.DailyReport;

public interface IDailyReportJob
{
    Task RunAsync(DateOnly? targetDate = null);
}

public class DailyReportJob(
    ApplicationDbcontext db,
    IDailyReportEmailSender emailSender,
    ILogger<DailyReportJob> logger,
    IBackgroundJobClient hangfire) : IDailyReportJob
{
    public async Task RunAsync(DateOnly? targetDate = null)
    {
        // ── 1. Determine which date to report on ────────────────────────
        var reportDate = targetDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3).AddDays(-1));

        logger.LogInformation("DailyReportJob starting for date {Date}", reportDate);

        // ── 2. Guard: already successfully sent? ────────────────────────
        var log = await db.DailyReportLogs
            .FirstOrDefaultAsync(x => x.ReportDate == reportDate);

        if (log?.IsSent == true)
        {
            logger.LogInformation("Report for {Date} already sent. Skipping.", reportDate);
            return;
        }

        // ── 3. Fetch yesterday's shifts with all needed nav properties ──
        var shifts = await db.RiderShifts
            .AsNoTracking()
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Include(s => s.Rider)
                .ThenInclude(r => r.Company)
            .Include(s => s.Housing)
            .Where(s => s.ShiftDate == reportDate)
            .ToListAsync();

        // ── 4. Empty data → schedule a retry in 1 hour ─────────────────
        if (shifts.Count == 0)
        {
            var retryCount = log?.RetryCount ?? 0;
            logger.LogWarning(
                "No shifts found for {Date}. Retry #{Count} scheduled in 1 hour.",
                reportDate, retryCount + 1);

            // Update / create the log entry with the retry count
            if (log is null)
            {
                db.DailyReportLogs.Add(new DailyReportLog
                {
                    ReportDate = reportDate,
                    IsSent = false,
                    RetryCount = 1,
                    ErrorMessage = "No shifts found — waiting for data"
                });
            }
            else
            {
                log.RetryCount++;
                log.ErrorMessage = $"No shifts after {log.RetryCount} attempt(s)";
            }

            await db.SaveChangesAsync();

            // Schedule the retry — pass the exact date so it doesn't shift by timezone
            hangfire.Schedule<IDailyReportJob>(
                job => job.RunAsync(reportDate),
                TimeSpan.FromHours(1));

            return;
        }

        // ── 5. Build the report payload ─────────────────────────────────
        try
        {
            var payload = BuildPayload(reportDate, shifts);

            // ── 6. Generate PDF ────────────────────────────────────────
            var pdfBytes = DailyReportPdfGenerator.Generate(payload);

            // ── 7. Send emails ─────────────────────────────────────────
            await emailSender.SendAsync(payload, pdfBytes);

            // ── 8. Mark as sent ────────────────────────────────────────
            if (log is null)
            {
                db.DailyReportLogs.Add(new DailyReportLog
                {
                    ReportDate = reportDate,
                    IsSent = true,
                    SentAt = DateTime.UtcNow.AddHours(3),
                    RetryCount = log?.RetryCount ?? 0
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
                "Daily report for {Date} sent successfully. Companies: {Count}, Shifts: {Total}",
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
            throw; // let Hangfire's own retry handle transient failures
        }
    }

    // ── Payload builder ──────────────────────────────────────────────────────
    private static DailyReportPayload BuildPayload(DateOnly date, List<RiderShift> shifts)
    {
        var companies = new List<CompanyReportBlock>();

        // Group by company
        var byCompany = shifts.GroupBy(s => new
        {
            s.CompanyId,
            CompanyName = s.Rider?.Company?.Name ?? $"Company {s.CompanyId}"
        });

        foreach (var companyGroup in byCompany.OrderBy(c => c.Key.CompanyName))
        {
            var ordered = companyGroup
                .OrderByDescending(s => s.AcceptedDailyOrders)
                .ToList();

            var top5 = ordered.Take(5).ToList();
            var bottom5 = ordered.TakeLast(5).ToList();

            var combined = top5
                .Select(s => MapToRow(s, "أعلى 5"))
                .Concat(bottom5.Select(s => MapToRow(s, "أدنى 5")))
                .ToList();

            // Sub-group by housing
            var byHousing = combined
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

    private static ShiftReportRow MapToRow(RiderShift s, string section) =>
        new(
            RiderNameAR: s.Rider?.Employee?.NameAR ?? "—",
            IqamaNo: s.Rider?.EmployeeIqamaNo ?? 0,
            HousingName: s.Housing?.Name ?? "No Housing",
            AcceptedOrders: s.AcceptedDailyOrders,
            WorkingHours: s.WorkingHours,
            Section: section
        );
}