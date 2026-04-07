using Application.Abstraction;
using Application.Contracts.Employees;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Service.EscapedEmployee;

public class EscapedEmployeeService(
    ApplicationDbcontext db,
    ILogger<EscapedEmployeeService> logger) : IEscapedEmployeeService
{
    private const int RemovalWindowDays = 60;
    private const int NotificationThresholdDays = 10;

    // ── Create ────────────────────────────────────────────────────────────────
    public async Task<Result<EscapedEmployeeResponse>> CreateAsync(
        CreateEscapedEmployeeRequest request,
        CancellationToken ct = default)
    {
        // Guard: employee must exist
        var employee = await db.Employees
            .Include(e => e.Housing)
            .Include(e => e.RiderDetails)
                .ThenInclude(rd => rd!.Company)
            .FirstOrDefaultAsync(e => e.IqamaNo == request.EmployeeIqamaNo, ct);

        if (employee is null)
            return Result.Failure<EscapedEmployeeResponse>(
                new Error("NotFound", "Employee not found.", 404));

        // Guard: no duplicate
        var existing = await db.EscapedEmployeeDetails
            .AnyAsync(e => e.EmployeeIqamaNo == request.EmployeeIqamaNo, ct);

        if (existing)
            return Result.Failure<EscapedEmployeeResponse>(
                new Error("Duplicate", "This employee already has an escaped record. " +
                    "Use the path-activation endpoints to update it.", 409));

        var record = new EscapedEmployeeDetails
        {
            EmployeeIqamaNo = request.EmployeeIqamaNo,
            EscapedAt = request.EscapedAt,
            ActivePath = EscapedPath.None,
            Notes = request.Notes,
            CreatedBy = request.CreatedBy,
            UpdatedBy = request.CreatedBy,
            CreatedAt = DateTime.UtcNow.AddHours(3),
            UpdatedAt = DateTime.UtcNow.AddHours(3)
        };

        db.EscapedEmployeeDetails.Add(record);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Escaped record created for IqamaNo {IqamaNo} by {CreatedBy}",
            request.EmployeeIqamaNo, request.CreatedBy);

        return Result.Success(MapToResponse(record, employee));
    }

    // ── Activate Reported Path ────────────────────────────────────────────────
    public async Task<Result<EscapedEmployeeResponse>> ActivateReportedPathAsync(
        ActivateReportedPathRequest request,
        CancellationToken ct = default)
    {
        var (record, employee, findError) = await FindRecordWithEmployee(request.EmployeeIqamaNo, ct);
        if (findError is not null)
            return Result.Failure<EscapedEmployeeResponse>(findError);

        // Clear outage path data if it was active
        if (record!.ActivePath == EscapedPath.Outage)
        {
            logger.LogInformation(
                "Clearing Outage path for IqamaNo {IqamaNo} before activating Reported path.",
                request.EmployeeIqamaNo);

            record.IsOutage = null;
            record.DateOfOutage = null;
            record.OutageVisaNumber = null;
        }

        // Set Reported path
        record.ActivePath = EscapedPath.Reported;
        record.IsReported = request.IsReported;
        record.ReportedAt = request.ReportedAt;
        record.RemovalDeadline = request.ReportedAt.AddDays(RemovalWindowDays);

        // Reset notification so it fires correctly at 10 days on the new deadline
        record.TenDayNotificationSent = false;
        record.TenDayNotificationSentAt = null;

        record.Notes = request.Notes ?? record.Notes;
        record.UpdatedBy = request.UpdatedBy;
        record.UpdatedAt = DateTime.UtcNow.AddHours(3);

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Reported path activated for IqamaNo {IqamaNo}. Deadline: {Deadline:dd/MM/yyyy}",
            request.EmployeeIqamaNo, record.RemovalDeadline);

        return Result.Success(MapToResponse(record, employee!));
    }

    // ── Activate Outage Path ──────────────────────────────────────────────────
    public async Task<Result<EscapedEmployeeResponse>> ActivateOutagePathAsync(
        ActivateOutagePathRequest request,
        CancellationToken ct = default)
    {
        var (record, employee, findError) = await FindRecordWithEmployee(request.EmployeeIqamaNo, ct);
        if (findError is not null)
            return Result.Failure<EscapedEmployeeResponse>(findError);

        if (string.IsNullOrWhiteSpace(request.OutageVisaNumber))
            return Result.Failure<EscapedEmployeeResponse>(
                new Error("Validation", "OutageVisaNumber is required for the Outage path.", 400));

        // Clear reported path data if it was active
        if (record!.ActivePath == EscapedPath.Reported)
        {
            logger.LogInformation(
                "Clearing Reported path for IqamaNo {IqamaNo} before activating Outage path.",
                request.EmployeeIqamaNo);

            record.IsReported = null;
            record.ReportedAt = null;
        }

        // Set Outage path
        record.ActivePath = EscapedPath.Outage;
        record.IsOutage = request.IsOutage;
        record.DateOfOutage = request.DateOfOutage;
        record.OutageVisaNumber = request.OutageVisaNumber;
        record.RemovalDeadline = request.DateOfOutage.AddDays(RemovalWindowDays);

        // Reset notification
        record.TenDayNotificationSent = false;
        record.TenDayNotificationSentAt = null;

        record.Notes = request.Notes ?? record.Notes;
        record.UpdatedBy = request.UpdatedBy;
        record.UpdatedAt = DateTime.UtcNow.AddHours(3);

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Outage path activated for IqamaNo {IqamaNo}. Deadline: {Deadline:dd/MM/yyyy}",
            request.EmployeeIqamaNo, record.RemovalDeadline);

        return Result.Success(MapToResponse(record, employee!));
    }

    // ── Clear Active Path ─────────────────────────────────────────────────────
    public async Task<Result> ClearActivePathAsync(
        long employeeIqamaNo,
        string updatedBy,
        CancellationToken ct = default)
    {
        var record = await db.EscapedEmployeeDetails
            .FirstOrDefaultAsync(e => e.EmployeeIqamaNo == employeeIqamaNo, ct);

        if (record is null)
            return Result.Failure(new Error("NotFound", "Escaped record not found.", 404));

        if (record.ActivePath == EscapedPath.None)
            return Result.Failure(new Error("NothingToClear", "No active path to clear.", 400));

        var previousPath = record.ActivePath;

        // Wipe both paths
        record.ActivePath = EscapedPath.None;
        record.IsReported = null;
        record.ReportedAt = null;
        record.IsOutage = null;
        record.DateOfOutage = null;
        record.OutageVisaNumber = null;
        record.RemovalDeadline = null;
        record.TenDayNotificationSent = false;
        record.TenDayNotificationSentAt = null;
        record.UpdatedBy = updatedBy;
        record.UpdatedAt = DateTime.UtcNow.AddHours(3);

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Path {Path} cleared for IqamaNo {IqamaNo} by {By}",
            previousPath, employeeIqamaNo, updatedBy);

        return Result.Success();
    }

    // ── Read ──────────────────────────────────────────────────────────────────
    public async Task<Result<EscapedEmployeeResponse>> GetByIqamaAsync(
        long iqamaNo,
        CancellationToken ct = default)
    {
        var record = await db.EscapedEmployeeDetails
            .Include(e => e.Employee)
                .ThenInclude(emp => emp.Housing)
            .Include(e => e.Employee)
                .ThenInclude(emp => emp.RiderDetails!)
                    .ThenInclude(rd => rd.Company)
            .FirstOrDefaultAsync(e => e.EmployeeIqamaNo == iqamaNo, ct);

        if (record is null)
            return Result.Failure<EscapedEmployeeResponse>(
                new Error("NotFound", "Escaped record not found.", 404));

        return Result.Success(MapToResponse(record, record.Employee));
    }

    public async Task<Result<IEnumerable<EscapedEmployeeSummaryResponse>>> GetAllAsync(
        CancellationToken ct = default)
    {
        var records = await db.EscapedEmployeeDetails
            .Include(e => e.Employee)
                .ThenInclude(emp => emp.Housing)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

        return Result.Success<IEnumerable<EscapedEmployeeSummaryResponse>>(
            records.Select(MapToSummary));
    }

    public async Task<Result<IEnumerable<EscapedEmployeeSummaryResponse>>> GetDueForRemovalAsync(
        int daysThreshold = NotificationThresholdDays,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.AddHours(3);
        var cutoff = now.AddDays(daysThreshold);

        var records = await db.EscapedEmployeeDetails
            .Include(e => e.Employee)
                .ThenInclude(emp => emp.Housing)
            .Where(e => e.RemovalDeadline != null && e.RemovalDeadline <= cutoff)
            .OrderBy(e => e.RemovalDeadline)
            .ToListAsync(ct);

        return Result.Success<IEnumerable<EscapedEmployeeSummaryResponse>>(
            records.Select(MapToSummary));
    }

    public async Task<Result<EscapedEmployeeStatsResponse>> GetStatsAsync(
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.AddHours(3);
        var records = await db.EscapedEmployeeDetails.ToListAsync(ct);

        var stats = new EscapedEmployeeStatsResponse(
            TotalEscaped: records.Count,
            WithReportedPath: records.Count(r => r.ActivePath == EscapedPath.Reported),
            WithOutagePath: records.Count(r => r.ActivePath == EscapedPath.Outage),
            WithNoPath: records.Count(r => r.ActivePath == EscapedPath.None),
            Overdue: records.Count(r => r.RemovalDeadline < now),
            DueWithin10Days: records.Count(r => r.RemovalDeadline != null
                                       && r.RemovalDeadline >= now
                                       && r.RemovalDeadline <= now.AddDays(10)),
            DueWithin30Days: records.Count(r => r.RemovalDeadline != null
                                       && r.RemovalDeadline >= now
                                       && r.RemovalDeadline <= now.AddDays(30)),
            NotificationPending: records.Count(r => !r.TenDayNotificationSent
                                       && r.RemovalDeadline != null
                                       && r.RemovalDeadline <= now.AddDays(NotificationThresholdDays))
        );

        return Result.Success(stats);
    }

    // ── Update ────────────────────────────────────────────────────────────────
    public async Task<Result<EscapedEmployeeResponse>> UpdateAsync(
        long iqamaNo,
        UpdateEscapedEmployeeRequest request,
        CancellationToken ct = default)
    {
        var (record, employee, findError) = await FindRecordWithEmployee(iqamaNo, ct);
        if (findError is not null)
            return Result.Failure<EscapedEmployeeResponse>(findError);

        if (request.EscapedAt.HasValue)
            record!.EscapedAt = request.EscapedAt.Value;

        if (request.Notes is not null)
            record!.Notes = request.Notes;

        record!.UpdatedBy = request.UpdatedBy;
        record.UpdatedAt = DateTime.UtcNow.AddHours(3);

        await db.SaveChangesAsync(ct);
        return Result.Success(MapToResponse(record, employee!));
    }

    // ── Delete ────────────────────────────────────────────────────────────────
    public async Task<Result> DeleteAsync(long iqamaNo, CancellationToken ct = default)
    {
        var record = await db.EscapedEmployeeDetails
            .FirstOrDefaultAsync(e => e.EmployeeIqamaNo == iqamaNo, ct);

        if (record is null)
            return Result.Failure(new Error("NotFound", "Escaped record not found.", 404));

        db.EscapedEmployeeDetails.Remove(record);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Escaped record deleted for IqamaNo {IqamaNo}", iqamaNo);
        return Result.Success();
    }

    // ── Notification helpers ──────────────────────────────────────────────────
    public async Task<List<EscapedNotificationItem>> GetPendingNotificationsAsync(
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.AddHours(3);
        var cutoff = now.AddDays(NotificationThresholdDays);

        return await db.EscapedEmployeeDetails
            .Include(e => e.Employee)
                .ThenInclude(emp => emp.Housing)
            .Where(e => !e.TenDayNotificationSent
                     && e.RemovalDeadline != null
                     && e.RemovalDeadline <= cutoff
                     && e.ActivePath != EscapedPath.None)
            .Select(e => new EscapedNotificationItem(
                e.EmployeeIqamaNo,
                e.Employee.NameAR,
                e.Employee.NameEN,
                e.Employee.Housing != null ? e.Employee.Housing.Name : null,
                e.EscapedAt,
                e.ActivePath.ToString(),
                e.RemovalDeadline!.Value,
                (int)(e.RemovalDeadline!.Value.Date - now.Date).TotalDays
            ))
            .ToListAsync(ct);
    }

    public async Task MarkNotificationSentAsync(
        IEnumerable<long> iqamaNos,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.AddHours(3);
        var iqamaList = iqamaNos.ToList();

        var records = await db.EscapedEmployeeDetails
            .Where(e => iqamaList.Contains(e.EmployeeIqamaNo))
            .ToListAsync(ct);

        foreach (var record in records)
        {
            record.TenDayNotificationSent = true;
            record.TenDayNotificationSentAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Private helpers ───────────────────────────────────────────────────────
    private async Task<(EscapedEmployeeDetails? Record, Employees? Employee, Error? Error)>
        FindRecordWithEmployee(long iqamaNo, CancellationToken ct)
    {
        var record = await db.EscapedEmployeeDetails
            .FirstOrDefaultAsync(e => e.EmployeeIqamaNo == iqamaNo, ct);

        if (record is null)
            return (null, null,
                new Error("NotFound", "Escaped record not found. Create it first.", 404));

        var employee = await db.Employees
            .Include(e => e.Housing)
            .Include(e => e.RiderDetails!)
                .ThenInclude(rd => rd.Company)
            .FirstOrDefaultAsync(e => e.IqamaNo == iqamaNo, ct);

        return (record, employee, null);
    }

    private static EscapedEmployeeResponse MapToResponse(
        EscapedEmployeeDetails r, Employees emp)
    {
        var now = DateTime.UtcNow.AddHours(3);
        int? remaining = r.RemovalDeadline.HasValue
            ? (int)(r.RemovalDeadline.Value.Date - now.Date).TotalDays
            : null;

        return new EscapedEmployeeResponse(
            Id: r.Id,
            EmployeeIqamaNo: r.EmployeeIqamaNo,
            EmployeeNameAR: emp.NameAR,
            EmployeeNameEN: emp.NameEN,
            HousingName: emp.Housing?.Name,
            CompanyName: emp.RiderDetails?.Company?.Name,
            EscapedAt: r.EscapedAt,
            ActivePath: r.ActivePath.ToString(),
            IsReported: r.IsReported,
            ReportedAt: r.ReportedAt,
            IsOutage: r.IsOutage,
            DateOfOutage: r.DateOfOutage,
            OutageVisaNumber: r.OutageVisaNumber,
            RemovalDeadline: r.RemovalDeadline,
            RemainingDaysToRemoval: remaining,
            IsOverdue: remaining.HasValue && remaining.Value < 0,
            TenDayNotificationSent: r.TenDayNotificationSent,
            TenDayNotificationSentAt: r.TenDayNotificationSentAt,
            CreatedAt: r.CreatedAt,
            UpdatedAt: r.UpdatedAt,
            CreatedBy: r.CreatedBy,
            UpdatedBy: r.UpdatedBy,
            Notes: r.Notes
        );
    }

    private static EscapedEmployeeSummaryResponse MapToSummary(EscapedEmployeeDetails r)
    {
        var now = DateTime.UtcNow.AddHours(3);
        int? remaining = r.RemovalDeadline.HasValue
            ? (int)(r.RemovalDeadline.Value.Date - now.Date).TotalDays
            : null;

        return new EscapedEmployeeSummaryResponse(
            Id: r.Id,
            EmployeeIqamaNo: r.EmployeeIqamaNo,
            EmployeeNameAR: r.Employee?.NameAR ?? string.Empty,
            EmployeeNameEN: r.Employee?.NameEN ?? string.Empty,
            EscapedAt: r.EscapedAt,
            ActivePath: r.ActivePath.ToString(),
            RemovalDeadline: r.RemovalDeadline,
            RemainingDaysToRemoval: remaining,
            IsOverdue: remaining.HasValue && remaining.Value < 0,
            TenDayNotificationSent: r.TenDayNotificationSent
        );
    }
}