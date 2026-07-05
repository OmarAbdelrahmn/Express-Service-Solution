using Application.Abstraction;
using Application.Contracts.OutageShiftPerformances;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.OutageShiftPerformances;

public class OutageShiftPerformanceService(ApplicationDbcontext db) : IOutageShiftPerformanceService
{
    public async Task<Result<OutageShiftPerformanceResponse>> CreateAsync(
        CreateOutageShiftPerformanceRequest request,
        DateOnly shiftDate,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request.SystemId, request.PhoneNumber, request.AcceptedOrders, request.RejectedOrders, request.WorkingHours);
        if (validation is not null)
            return Result.Failure<OutageShiftPerformanceResponse>(validation);

        try
        {
            var record = new OutageShiftPerformance
            {
                SystemId = request.SystemId.Trim(),
                PhoneNumber = request.PhoneNumber.Trim(),
                ShiftDate = shiftDate,
                AcceptedOrders = request.AcceptedOrders,
                RejectedOrders = request.RejectedOrders,
                WorkingHours = request.WorkingHours,
                UploadedAt = DateTime.UtcNow.AddHours(3),
                UploadedBy = createdBy
            };

            await db.OutageShiftPerformances.AddAsync(record, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(Map(record));
        }
        catch (Exception ex)
        {
            return Result.Failure<OutageShiftPerformanceResponse>(new Error(
                "OutageShiftPerformance.CreateFailed",
                ex.Message,
                500));
        }
    }

    public async Task<Result<OutageShiftPerformanceImportResponse>> ImportAsync(
        ImportOutageShiftPerformanceRequest request,
        string importedBy,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = new List<OutageShiftPerformance>();
            var warnings = new List<string>();
            var keysToDelete = new HashSet<(string systemId, DateOnly date)>();
            var now = DateTime.UtcNow.AddHours(3);

            foreach (var row in request.Rows)
            {
                var validation = Validate(row.SystemId, row.PhoneNumber, row.AcceptedOrders, row.RejectedOrders, row.WorkingHours);
                if (validation is not null)
                {
                    warnings.Add($"{row.SystemId}: {validation.Description}");
                    continue;
                }

                keysToDelete.Add((row.SystemId.Trim(), request.ShiftDate));
                records.Add(new OutageShiftPerformance
                {
                    SystemId = row.SystemId.Trim(),
                    PhoneNumber = row.PhoneNumber.Trim(),
                    ShiftDate = request.ShiftDate,
                    AcceptedOrders = row.AcceptedOrders,
                    RejectedOrders = row.RejectedOrders,
                    WorkingHours = row.WorkingHours,
                    UploadedAt = now,
                    UploadedBy = importedBy
                });
            }

            if (keysToDelete.Count > 0)
            {
                var systemIds = keysToDelete.Select(k => k.systemId).Distinct().ToList();
                var dates = keysToDelete.Select(k => k.date).Distinct().ToList();

                var stale = await db.OutageShiftPerformances
                    .Where(s => systemIds.Contains(s.SystemId) && dates.Contains(s.ShiftDate))
                    .ToListAsync(cancellationToken);

                db.OutageShiftPerformances.RemoveRange(
                    stale.Where(s => keysToDelete.Contains((s.SystemId, s.ShiftDate))));
            }

            await db.OutageShiftPerformances.AddRangeAsync(records, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(new OutageShiftPerformanceImportResponse(
                request.Rows.Count,
                records.Count,
                warnings));
        }
        catch (Exception ex)
        {
            return Result.Failure<OutageShiftPerformanceImportResponse>(new Error(
                "OutageShiftPerformance.ImportFailed",
                $"Import failed: {ex.Message}",
                500));
        }
    }

    public async Task<Result<OutageShiftPerformanceResponse>> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await db.OutageShiftPerformances
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            return record is null
                ? Result.Failure<OutageShiftPerformanceResponse>(NotFound())
                : Result.Success(Map(record));
        }
        catch (Exception ex)
        {
            return Result.Failure<OutageShiftPerformanceResponse>(new Error(
                "OutageShiftPerformance.QueryFailed",
                ex.Message,
                500));
        }
    }

    public async Task<Result<List<OutageShiftPerformanceResponse>>> GetAsync(
        string? systemId,
        string? phoneNumber,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = db.OutageShiftPerformances.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(systemId))
                query = query.Where(s => s.SystemId == systemId.Trim());

            if (!string.IsNullOrWhiteSpace(phoneNumber))
                query = query.Where(s => s.PhoneNumber == phoneNumber.Trim());

            if (from.HasValue)
                query = query.Where(s => s.ShiftDate >= from.Value);

            if (to.HasValue)
                query = query.Where(s => s.ShiftDate <= to.Value);

            var rows = await query
                .OrderByDescending(s => s.ShiftDate)
                .ThenBy(s => s.SystemId)
                .Select(s => new OutageShiftPerformanceResponse(
                    s.Id,
                    s.SystemId,
                    s.PhoneNumber,
                    s.ShiftDate,
                    s.AcceptedOrders,
                    s.RejectedOrders,
                    s.WorkingHours,
                    s.UploadedAt,
                    s.UploadedBy))
                .ToListAsync(cancellationToken);

            return Result.Success(rows);
        }
        catch (Exception ex)
        {
            return Result.Failure<List<OutageShiftPerformanceResponse>>(new Error(
                "OutageShiftPerformance.QueryFailed",
                ex.Message,
                500));
        }
    }

    public async Task<Result<OutageShiftPerformanceResponse>> UpdateAsync(
        int id,
        UpdateOutageShiftPerformanceRequest request,
        DateOnly shiftDate,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request.SystemId, request.PhoneNumber, request.AcceptedOrders, request.RejectedOrders, request.WorkingHours);
        if (validation is not null)
            return Result.Failure<OutageShiftPerformanceResponse>(validation);

        try
        {
            var record = await db.OutageShiftPerformances
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            if (record is null)
                return Result.Failure<OutageShiftPerformanceResponse>(NotFound());

            record.SystemId = request.SystemId.Trim();
            record.PhoneNumber = request.PhoneNumber.Trim();
            record.ShiftDate = shiftDate;
            record.AcceptedOrders = request.AcceptedOrders;
            record.RejectedOrders = request.RejectedOrders;
            record.WorkingHours = request.WorkingHours;

            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(record));
        }
        catch (Exception ex)
        {
            return Result.Failure<OutageShiftPerformanceResponse>(new Error(
                "OutageShiftPerformance.UpdateFailed",
                ex.Message,
                500));
        }
    }

    public async Task<Result> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await db.OutageShiftPerformances
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            if (record is null)
                return Result.Failure(NotFound());

            db.OutageShiftPerformances.Remove(record);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error(
                "OutageShiftPerformance.DeleteFailed",
                ex.Message,
                500));
        }
    }

    private static Error? Validate(
        string systemId,
        string phoneNumber,
        int acceptedOrders,
        int rejectedOrders,
        float workingHours)
    {
        if (string.IsNullOrWhiteSpace(systemId))
            return new Error("OutageShiftPerformance.SystemIdRequired", "System ID is required.", 400);

        if (string.IsNullOrWhiteSpace(phoneNumber))
            return new Error("OutageShiftPerformance.PhoneNumberRequired", "Phone number is required.", 400);

        if (acceptedOrders < 0)
            return new Error("OutageShiftPerformance.InvalidAcceptedOrders", "Accepted orders must be >= 0.", 400);

        if (rejectedOrders < 0)
            return new Error("OutageShiftPerformance.InvalidRejectedOrders", "Rejected orders must be >= 0.", 400);

        if (workingHours is < 0 or > 24)
            return new Error("OutageShiftPerformance.InvalidWorkingHours", "Working hours must be between 0 and 24.", 400);

        return null;
    }

    private static Error NotFound() =>
        new("OutageShiftPerformance.NotFound", "Outage shift performance record was not found.", 404);

    private static OutageShiftPerformanceResponse Map(OutageShiftPerformance s) =>
        new(
            s.Id,
            s.SystemId,
            s.PhoneNumber,
            s.ShiftDate,
            s.AcceptedOrders,
            s.RejectedOrders,
            s.WorkingHours,
            s.UploadedAt,
            s.UploadedBy);
}
