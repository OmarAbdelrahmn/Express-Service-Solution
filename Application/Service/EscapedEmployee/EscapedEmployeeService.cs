using Application.Abstraction;
using Application.Contracts.Employees;
using Application.Service.EscapedEmployee;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static Application.Service.EscapedEmployee.IEscapedEmployeeService;

namespace Application.Service.Escaped;

public class EscapedEmployeeService(ApplicationDbcontext context) : IEscapedEmployeeService
{
    private readonly ApplicationDbcontext _context = context;

    public async Task<Result<IEnumerable<EscapedEmployeeSummaryResponse>>> GetAllEscapedAsync(
        CancellationToken ct = default)
    {
        var records = await _context.EscapedEmployeeDetails
            .Include(e => e.Employee)
                .ThenInclude(emp => emp.Housing)
            .AsNoTracking()
            .OrderBy(e => e.RemovalDeadline)
            .ToListAsync(ct);

        return Result.Success(records.Select(MapToSummary));
    }

    public async Task<Result> ForceDeleteEscapedEmployeeAsync(
    long iqamaNo, CancellationToken ct = default)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            var record = await _context.EscapedEmployeeDetails
                .FirstOrDefaultAsync(e => e.EmployeeIqamaNo == iqamaNo, ct);

            if (record == null)
                return Result.Failure(new Error("NotFound",
                    "No escaped employee record found", 404));

            _context.EscapedEmployeeDetails.Remove(record);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            return Result.Failure(new Error("ForceDeleteError",
                $"Failed to force delete escaped employee record: {ex.Message}", 500));
        }
    }

    public async Task<Result> DeactivateEscapedEmployeeAsync(
    long iqamaNo, string deactivatedBy, CancellationToken ct = default)
    {
        var record = await _context.EscapedEmployeeDetails
            .FirstOrDefaultAsync(e => e.EmployeeIqamaNo == iqamaNo, ct);

        if (record == null)
            return Result.Failure(new Error("NotFound",
                "No escaped employee record found", 404));

        if (!record.IsActive)
            return Result.Failure(new Error("AlreadyInactive",
                "Escaped employee record is already inactive", 400));

        record.IsActive = false;
        record.DeactivatedAt = DateTime.UtcNow.AddHours(3);
        record.DeactivatedBy = deactivatedBy;
        record.UpdatedAt = DateTime.UtcNow.AddHours(3);
        record.UpdatedBy = deactivatedBy;

        await _context.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<BackfillResult>> BackfillFleeingEmployeesAsync(
    string createdBy, CancellationToken ct = default)
    {
        // Get all fleeing employees that don't already have an escaped record
        var fleeingEmployees = await _context.Employees
            .Where(e => !e.IsDeleted &&
                        e.Status.ToLower() == "fleeing" &&
                        !_context.EscapedEmployeeDetails
                            .Any(esc => esc.EmployeeIqamaNo == e.IqamaNo))
            .ToListAsync(ct);

        if (!fleeingEmployees.Any())
            return Result.Success(new BackfillResult(0, []));

        var now = DateTime.UtcNow.AddHours(3);
        var records = fleeingEmployees.Select(e => new EscapedEmployeeDetails
        {
            EmployeeIqamaNo = e.IqamaNo,
            EscapedAt = e.DeletedAt.HasValue
                ? DateOnly.FromDateTime(e.DeletedAt.Value)
                : DateOnly.FromDateTime(now),
            ActivePath = EscapedPath.None,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Notes = "Auto-migrated from fleeing status"
        }).ToList();

        await _context.EscapedEmployeeDetails.AddRangeAsync(records, ct);
        await _context.SaveChangesAsync(ct);

        var createdIqamaNos = records.Select(r => r.EmployeeIqamaNo).ToList();
        return Result.Success(new BackfillResult(records.Count, createdIqamaNos));
    }

    //public async Task<Result<EscapedEmployeeDetailResponse>> GetByIqamaAsync(
    //    long iqamaNo, CancellationToken ct = default)
    //{
    //    var record = await _context.EscapedEmployeeDetails
    //        .Include(e => e.Employee)
    //            .ThenInclude(emp => emp.Housing)
    //        .AsNoTracking()
    //        .FirstOrDefaultAsync(e => e.EmployeeIqamaNo == iqamaNo, ct);

    //    if (record == null)
    //        return Result.Failure<EscapedEmployeeDetailResponse>(
    //            new Error("NotFound", "No escaped employee record found", 404));

    //    return Result.Success(MapToDetail(record));
    //}

    public async Task<Result<IEnumerable<EscapedEmployeeSummaryResponse>>> GetByPathAsync(
        EscapedPath path, CancellationToken ct = default)
    {
        var records = await _context.EscapedEmployeeDetails
            .Include(e => e.Employee)
                .ThenInclude(emp => emp.Housing)
            .Where(e => e.ActivePath == path)
            .AsNoTracking()
            .OrderBy(e => e.RemovalDeadline)
            .ToListAsync(ct);

        return Result.Success(records.Select(MapToSummary));
    }

    public async Task<Result<IEnumerable<EscapedEmployeeSummaryResponse>>> GetOverdueAsync(
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.AddHours(3);

        var records = await _context.EscapedEmployeeDetails
            .Include(e => e.Employee)
                .ThenInclude(emp => emp.Housing)
            .Where(e => e.RemovalDeadline.HasValue && e.RemovalDeadline.Value < now)
            .AsNoTracking()
            .OrderBy(e => e.RemovalDeadline)
            .ToListAsync(ct);

        return Result.Success(records.Select(MapToSummary));
    }

    public async Task<Result> SetReportedPathAsync(
        long iqamaNo, SetReportedPathRequest request, CancellationToken ct = default)
    {
        var record = await _context.EscapedEmployeeDetails
            .FirstOrDefaultAsync(e => e.EmployeeIqamaNo == iqamaNo, ct);

        if (record == null)
            return Result.Failure(new Error("NotFound", "No escaped employee record found", 404));

        // Clear outage path data
        record.IsOutage = null;
        record.DateOfOutage = null;
        record.OutageVisaNumber = null;

        // Set reported path
        record.IsReported = true;
        record.ReportedAt = request.ReportedAt;
        record.ActivePath = EscapedPath.Reported;
        record.RemovalDeadline = request.ReportedAt.AddDays(60);
        record.TenDayNotificationSent = false;
        record.TenDayNotificationSentAt = null;
        record.UpdatedAt = DateTime.UtcNow.AddHours(3);
        record.UpdatedBy = request.UpdatedBy;

        if (!string.IsNullOrWhiteSpace(request.Notes))
            record.Notes = request.Notes;

        await _context.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetOutagePathAsync(
        long iqamaNo, SetOutagePathRequest request, CancellationToken ct = default)
    {
        var record = await _context.EscapedEmployeeDetails
            .FirstOrDefaultAsync(e => e.EmployeeIqamaNo == iqamaNo, ct);

        if (record == null)
            return Result.Failure(new Error("NotFound", "No escaped employee record found", 404));

        // Clear reported path data
        record.IsReported = null;
        record.ReportedAt = null;

        // Set outage path
        record.IsOutage = true;
        record.DateOfOutage = request.DateOfOutage;
        record.OutageVisaNumber = request.VisaNumber;
        record.ActivePath = EscapedPath.Outage;
        record.RemovalDeadline = request.DateOfOutage.AddDays(60);
        record.TenDayNotificationSent = false;
        record.TenDayNotificationSentAt = null;
        record.UpdatedAt = DateTime.UtcNow.AddHours(3);
        record.UpdatedBy = request.UpdatedBy;

        if (!string.IsNullOrWhiteSpace(request.Notes))
            record.Notes = request.Notes;

        await _context.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SwitchPathAsync(
        long iqamaNo, SwitchPathRequest request, CancellationToken ct = default)
    {
        if (request.NewPath == EscapedPath.None)
            return Result.Failure(new Error("InvalidPath",
                "Cannot switch to None path. Use SetReported or SetOutage.", 400));

        if (request.NewPath == EscapedPath.Reported)
        {
            if (!request.ReportedAt.HasValue)
                return Result.Failure(new Error("MissingData",
                    "ReportedAt is required when switching to Reported path", 400));

            return await SetReportedPathAsync(iqamaNo, new SetReportedPathRequest(
                request.ReportedAt.Value,
                request.UpdatedBy,
                request.Notes), ct);
        }

        if (request.NewPath == EscapedPath.Outage)
        {
            if (!request.DateOfOutage.HasValue || string.IsNullOrWhiteSpace(request.VisaNumber))
                return Result.Failure(new Error("MissingData",
                    "DateOfOutage and VisaNumber are required when switching to Outage path", 400));

            return await SetOutagePathAsync(iqamaNo, new SetOutagePathRequest(
                request.DateOfOutage.Value,
                request.VisaNumber,
                request.UpdatedBy,
                request.Notes), ct);
        }

        return Result.Failure(new Error("InvalidPath", "Unknown path", 400));
    }

    public async Task<Result> UpdateNotesAsync(
        long iqamaNo, string notes, CancellationToken ct = default)
    {
        var record = await _context.EscapedEmployeeDetails
            .FirstOrDefaultAsync(e => e.EmployeeIqamaNo == iqamaNo, ct);

        if (record == null)
            return Result.Failure(new Error("NotFound", "No escaped employee record found", 404));

        record.Notes = notes;
        record.UpdatedAt = DateTime.UtcNow.AddHours(3);
        await _context.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RemoveEscapedEmployeeAsync(
        long iqamaNo, string removedBy, CancellationToken ct = default)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            var record = await _context.EscapedEmployeeDetails
                .Include(e => e.Employee)
                .FirstOrDefaultAsync(e => e.EmployeeIqamaNo == iqamaNo, ct);

            if (record == null)
                return Result.Failure(new Error("NotFound",
                    "No escaped employee record found", 404));

            // Remove escaped record
            _context.EscapedEmployeeDetails.Remove(record);

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            return Result.Failure(new Error("RemoveError",
                $"Failed to remove escaped employee: {ex.Message}", 500));
        }
    }

    public async Task<Result<EscapedEmployeeStatsResponse>> GetStatsAsync(
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.AddHours(3);
        var tenDaysFromNow = now.AddDays(10);

        var all = await _context.EscapedEmployeeDetails.AsNoTracking().ToListAsync(ct);

        var stats = new EscapedEmployeeStatsResponse(
            TotalEscaped: all.Count,
            NonePathCount: all.Count(e => e.ActivePath == EscapedPath.None),
            ReportedPathCount: all.Count(e => e.ActivePath == EscapedPath.Reported),
            OutagePathCount: all.Count(e => e.ActivePath == EscapedPath.Outage),
            OverdueCount: all.Count(e =>
                e.RemovalDeadline.HasValue && e.RemovalDeadline.Value < now),
            DueWithin10DaysCount: all.Count(e =>
                e.RemovalDeadline.HasValue &&
                e.RemovalDeadline.Value >= now &&
                e.RemovalDeadline.Value <= tenDaysFromNow),
            NotificationsSentCount: all.Count(e => e.TenDayNotificationSent)
        );

        return Result.Success(stats);
    }

    // ── Mapping Helpers ──────────────────────────────────────────────────────

    private static EscapedEmployeeSummaryResponse MapToSummary(EscapedEmployeeDetails e)
    {
        var now = DateTime.UtcNow.AddHours(3);
        return new EscapedEmployeeSummaryResponse(
            e.EmployeeIqamaNo,
            e.Employee.NameAR,
            e.Employee.NameEN,
            e.Employee.JobTitle,
            e.Employee.Housing?.Name,
            e.EscapedAt,
            e.ActivePath,
            e.RemovalDeadline,
            e.RemainingDaysToRemoval,
            IsOverdue: e.RemovalDeadline.HasValue && e.RemovalDeadline.Value < now,
            e.TenDayNotificationSent
        );
    }

    private static EscapedEmployeeDetailResponse MapToDetail(EscapedEmployeeDetails e)
    {
        var now = DateTime.UtcNow.AddHours(3);
        return new EscapedEmployeeDetailResponse(
            e.EmployeeIqamaNo,
            e.Employee.NameAR,
            e.Employee.NameEN,
            e.Employee.JobTitle,
            e.Employee.Country,
            e.Employee.Phone,
            e.Employee.Housing?.Name,
            e.Employee.Sponsor,
            e.EscapedAt,
            e.ActivePath,
            e.IsReported,
            e.ReportedAt,
            e.IsOutage,
            e.DateOfOutage,
            e.OutageVisaNumber,
            e.RemovalDeadline,
            e.RemainingDaysToRemoval,
            IsOverdue: e.RemovalDeadline.HasValue && e.RemovalDeadline.Value < now,
            e.TenDayNotificationSent,
            e.TenDayNotificationSentAt,
            e.CreatedAt,
            e.CreatedBy,
            e.UpdatedAt,
            e.UpdatedBy,
            e.Notes
        );
    }
}