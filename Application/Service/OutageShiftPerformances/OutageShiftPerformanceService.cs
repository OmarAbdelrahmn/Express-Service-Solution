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
        var validation = Validate(request.OutRiderInfoId, request.AcceptedOrders, request.RejectedOrders, request.WorkingHours);
        if (validation is not null)
            return Result.Failure<OutageShiftPerformanceResponse>(validation);

        var outRiderInfo = await db.OutRiderInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.OutRiderInfoId, cancellationToken);

        if (outRiderInfo is null)
            return Result.Failure<OutageShiftPerformanceResponse>(OutRiderInfoNotFound());

        var exists = await db.OutageShiftPerformances
            .AnyAsync(p => p.OutRiderInfoId == request.OutRiderInfoId && p.ShiftDate == shiftDate, cancellationToken);

        if (exists)
        {
            return Result.Failure<OutageShiftPerformanceResponse>(new Error(
                "OutageShiftPerformance.Duplicate",
                "Outage shift performance already exists for this rider and date.",
                409));
        }

        var record = new OutageShiftPerformance
        {
            OutRiderInfoId = request.OutRiderInfoId,
            ShiftDate = shiftDate,
            AcceptedOrders = request.AcceptedOrders,
            RejectedOrders = request.RejectedOrders,
            WorkingHours = request.WorkingHours,
            UploadedAt = DateTime.UtcNow.AddHours(3),
            UploadedBy = createdBy
        };

        await db.OutageShiftPerformances.AddAsync(record, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(Map(record, outRiderInfo.RiderId, outRiderInfo.Name));
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
            var riderIds = request.Rows
                .Select(r => r.RiderId.Trim())
                .Where(r => r.Length > 0)
                .Distinct()
                .ToList();

            var outRiders = await db.OutRiderInfos
                .Where(r => riderIds.Contains(r.RiderId))
                .ToDictionaryAsync(r => r.RiderId, cancellationToken);

            var seenKeys = new HashSet<(int outRiderInfoId, DateOnly date)>();
            var now = DateTime.UtcNow.AddHours(3);

            foreach (var row in request.Rows)
            {
                if (string.IsNullOrWhiteSpace(row.RiderId))
                {
                    warnings.Add("Missing rider ID. Row skipped.");
                    continue;
                }

                var validation = ValidateMetrics(row.AcceptedOrders, row.RejectedOrders, row.WorkingHours);
                if (validation is not null)
                {
                    warnings.Add($"{row.RiderId}: {validation.Description}");
                    continue;
                }

                var riderId = row.RiderId.Trim();
                if (!outRiders.TryGetValue(riderId, out var outRiderInfo))
                {
                    warnings.Add($"{riderId}: rider ID does not exist in OutRiderInfo. Row skipped.");
                    continue;
                }

                var key = (outRiderInfo.Id, request.ShiftDate);
                if (!seenKeys.Add(key))
                {
                    warnings.Add($"{riderId}: duplicate rider/date in Excel file. Row skipped.");
                    continue;
                }

                records.Add(new OutageShiftPerformance
                {
                    OutRiderInfoId = outRiderInfo.Id,
                    ShiftDate = request.ShiftDate,
                    AcceptedOrders = row.AcceptedOrders,
                    RejectedOrders = row.RejectedOrders,
                    WorkingHours = row.WorkingHours,
                    UploadedAt = now,
                    UploadedBy = importedBy
                });
            }

            if (seenKeys.Count > 0)
            {
                var outRiderInfoIds = seenKeys.Select(k => k.outRiderInfoId).Distinct().ToList();
                var dates = seenKeys.Select(k => k.date).Distinct().ToList();

                var stale = await db.OutageShiftPerformances
                    .Where(s => outRiderInfoIds.Contains(s.OutRiderInfoId) && dates.Contains(s.ShiftDate))
                    .ToListAsync(cancellationToken);

                db.OutageShiftPerformances.RemoveRange(
                    stale.Where(s => seenKeys.Contains((s.OutRiderInfoId, s.ShiftDate))));
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
                .Include(s => s.OutRiderInfo)
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
        string? riderId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = db.OutageShiftPerformances
                .Include(s => s.OutRiderInfo)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(riderId))
                query = query.Where(s => s.OutRiderInfo.RiderId == riderId.Trim());

            if (from.HasValue)
                query = query.Where(s => s.ShiftDate >= from.Value);

            if (to.HasValue)
                query = query.Where(s => s.ShiftDate <= to.Value);

            var rows = await query
                .OrderByDescending(s => s.ShiftDate)
                .ThenBy(s => s.OutRiderInfo.RiderId)
                .Select(s => new OutageShiftPerformanceResponse(
                    s.Id,
                    s.OutRiderInfoId,
                    s.OutRiderInfo.RiderId,
                    s.OutRiderInfo.Name,
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
        var validation = Validate(request.OutRiderInfoId, request.AcceptedOrders, request.RejectedOrders, request.WorkingHours);
        if (validation is not null)
            return Result.Failure<OutageShiftPerformanceResponse>(validation);

        try
        {
            var outRiderInfo = await db.OutRiderInfos
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == request.OutRiderInfoId, cancellationToken);

            if (outRiderInfo is null)
                return Result.Failure<OutageShiftPerformanceResponse>(OutRiderInfoNotFound());

            var record = await db.OutageShiftPerformances
                .Include(s => s.OutRiderInfo)
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            if (record is null)
                return Result.Failure<OutageShiftPerformanceResponse>(NotFound());

            var duplicate = await db.OutageShiftPerformances
                .AnyAsync(p => p.Id != id && p.OutRiderInfoId == request.OutRiderInfoId && p.ShiftDate == shiftDate, cancellationToken);

            if (duplicate)
            {
                return Result.Failure<OutageShiftPerformanceResponse>(new Error(
                    "OutageShiftPerformance.Duplicate",
                    "Outage shift performance already exists for this rider and date.",
                    409));
            }

            record.OutRiderInfoId = request.OutRiderInfoId;
            record.ShiftDate = shiftDate;
            record.AcceptedOrders = request.AcceptedOrders;
            record.RejectedOrders = request.RejectedOrders;
            record.WorkingHours = request.WorkingHours;

            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(record, outRiderInfo.RiderId, outRiderInfo.Name));
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
        int outRiderInfoId,
        int acceptedOrders,
        int rejectedOrders,
        float workingHours)
    {
        if (outRiderInfoId <= 0)
            return new Error("OutageShiftPerformance.OutRiderInfoRequired", "Out rider info ID is required.", 400);

        return ValidateMetrics(acceptedOrders, rejectedOrders, workingHours);
    }

    private static Error? ValidateMetrics(
        int acceptedOrders,
        int rejectedOrders,
        float workingHours)
    {
        if (acceptedOrders < 0)
            return new Error("OutageShiftPerformance.InvalidAcceptedOrders", "Accepted orders must be >= 0.", 400);

        if (rejectedOrders < 0)
            return new Error("OutageShiftPerformance.InvalidRejectedOrders", "Rejected orders must be >= 0.", 400);

        if (workingHours is < 0 or > 24)
            return new Error("OutageShiftPerformance.InvalidWorkingHours", "Working hours must be between 0 and 24.", 400);

        return null;
    }

    private static Error OutRiderInfoNotFound() =>
        new("OutageShiftPerformance.OutRiderInfoNotFound", "Out rider info record was not found.", 404);

    private static Error NotFound() =>
        new("OutageShiftPerformance.NotFound", "Outage shift performance record was not found.", 404);

    private static OutageShiftPerformanceResponse Map(OutageShiftPerformance s) =>
        new(
            s.Id,
            s.OutRiderInfoId,
            s.OutRiderInfo.RiderId,
            s.OutRiderInfo.Name,
            s.ShiftDate,
            s.AcceptedOrders,
            s.RejectedOrders,
            s.WorkingHours,
            s.UploadedAt,
            s.UploadedBy);

    private static OutageShiftPerformanceResponse Map(
        OutageShiftPerformance s,
        string riderId,
        string? name) =>
        new(
            s.Id,
            s.OutRiderInfoId,
            riderId,
            name,
            s.ShiftDate,
            s.AcceptedOrders,
            s.RejectedOrders,
            s.WorkingHours,
            s.UploadedAt,
            s.UploadedBy);
}
