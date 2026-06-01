using Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Service.DailyReport;

public interface IAbsentReportJob
{
    Task RunAsync(int companyId, DateOnly? targetDate = null);
}

public class AbsentReportJob(
    ApplicationDbcontext db,
    IAbsentReportEmailSender emailSender,
    ILogger<AbsentReportJob> logger,
    IWebHostEnvironment env,
    IOptions<DailyReportSettings> options) : IAbsentReportJob
{
    private readonly DailyReportSettings _settings = options.Value;


    private static readonly HashSet<long> Company2Exclusions =
    [

    ];

    public async Task RunAsync(int companyId, DateOnly? targetDate = null)
    {
        // ── Master on/off switch ─────────────────────────────────────────────
        if (!_settings.IsEnabled)
        {
            logger.LogInformation(
                "AbsentReportJob is disabled via settings. Skipping company {CompanyId}.", companyId);
            return;
        }

        var reportDate = targetDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3).AddDays(-1));

        logger.LogInformation(
            "AbsentReportJob starting for company {CompanyId} on {Date}", companyId, reportDate);

        // ── Gate: only run if shift data was uploaded for this company ────────
        var anyShifts = await db.RiderShifts
            .AnyAsync(s => s.ShiftDate == reportDate && s.CompanyId == companyId);

        if (!anyShifts)
        {
            logger.LogInformation(
                "No shifts uploaded for company {CompanyId} on {Date}. Skipping absent report.",
                companyId, reportDate);
            return;
        }

        try
        {
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
                    "No active non-employee riders found for company {CompanyId}. Skipping.", companyId);
                return;
            }

            // ── Load yesterday's shifts for this company ──────────────────────
            var shifts = await db.RiderShifts
                .AsNoTracking()
                .Where(s => s.ShiftDate == reportDate && s.CompanyId == companyId)
                .ToListAsync();

            var shiftByRiderId = shifts.ToDictionary(s => s.RiderId);

            // ── Build absent / low-hours list ─────────────────────────────────
            var absentRows = new List<AbsentRiderRow>();

            foreach (var rider in riders)
            {
                if (companyId == 2 && Company2Exclusions.Contains(rider.EmployeeIqamaNo))
                    continue;


                if (shiftByRiderId.TryGetValue(rider.Id, out var shift))
                {
                    // Rider has a shift but worked less than 8 hours
                    if (shift.WorkingHours < 8f)
                    {
                        absentRows.Add(new AbsentRiderRow(
                            RiderNameAR: rider.Employee?.NameAR ?? "—",
                            IqamaNo: rider.EmployeeIqamaNo,
                            HousingName: rider.Employee?.Housing?.Name ?? "بدون سكن",
                            WorkingId: rider.WorkingId ?? "—",
                            HadShiftButLowHours: true,
                            WorkingHours: shift.WorkingHours));
                    }
                    // else: worked ≥ 8 hours — not included in report
                }
                else
                {
                    // Rider has no shift at all yesterday
                    absentRows.Add(new AbsentRiderRow(
                        RiderNameAR: rider.Employee?.NameAR ?? "—",
                        IqamaNo: rider.EmployeeIqamaNo,
                        HousingName: rider.Employee?.Housing?.Name ?? "بدون سكن",
                        WorkingId: rider.WorkingId ?? "—",
                        HadShiftButLowHours: false,
                        WorkingHours: null));
                }
            }

            // ── Nothing to report ─────────────────────────────────────────────
            if (absentRows.Count == 0)
            {
                logger.LogInformation(
                    "All riders worked ≥ 8h for company {CompanyId} on {Date}. No absent report sent.",
                    companyId, reportDate);
                return;
            }

            // Sort: absent (no shift) first, then low-hours sorted by hours ascending
            absentRows = absentRows
                .OrderBy(r => r.HadShiftButLowHours)           // false (absent) before true (low)
                .ThenBy(r => r.WorkingHours ?? -1)              // lowest hours first within low-hours group
                .ThenBy(r => r.HousingName)
                .ToList();

            // ── Load company name ─────────────────────────────────────────────
            var company = await db.Companies.FindAsync(companyId);
            var companyName = company?.Name ?? $"شركة {companyId}";

            // ── Load logo ─────────────────────────────────────────────────────
            var logoPath = Path.Combine(env.WebRootPath, "images", "company-logo.png");
            byte[]? logoBytes = File.Exists(logoPath)
                ? await File.ReadAllBytesAsync(logoPath)
                : null;

            // ── Build payload and send ────────────────────────────────────────
            var payload = new AbsentReportPayload(
                ReportDate: reportDate,
                CompanyId: companyId,
                CompanyName: companyName,
                Riders: absentRows);

            await emailSender.SendAsync(payload, logoBytes);

            logger.LogInformation(
                "Absent report for company {CompanyId} ({CompanyName}) on {Date} sent. " +
                "Absent: {Absent}, LowHours: {LowHours}",
                companyId,
                companyName,
                reportDate,
                absentRows.Count(r => !r.HadShiftButLowHours),
                absentRows.Count(r => r.HadShiftButLowHours));
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to send absent report for company {CompanyId} on {Date}",
                companyId, reportDate);
            throw;
        }
    }
}