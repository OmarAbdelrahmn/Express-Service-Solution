using Application.Abstraction;
using Application.Contracts.OutRiderInfos;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.OutRiderInfos;

public class OutRiderInfoService(ApplicationDbcontext db) : IOutRiderInfoService
{
    public async Task<Result<OutRiderInfoResponse>> CreateAsync(
        CreateOutRiderInfoRequest request,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request.RiderId, request.PhoneNumber);
        if (validation is not null)
            return Result.Failure<OutRiderInfoResponse>(validation);

        var riderId = request.RiderId.Trim();
        var phoneNumber = request.PhoneNumber.Trim();

        if (await db.OutRiderInfos.AnyAsync(r => r.RiderId == riderId, cancellationToken))
            return Result.Failure<OutRiderInfoResponse>(Duplicate(riderId));

        var record = new OutRiderInfo
        {
            RiderId = riderId,
            PhoneNumber = phoneNumber,
            CreatedAt = DateTime.UtcNow.AddHours(3),
            CreatedBy = createdBy
        };

        await db.OutRiderInfos.AddAsync(record, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(Map(record));
    }

    public async Task<Result<OutRiderInfoResponse>> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var record = await db.OutRiderInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        return record is null
            ? Result.Failure<OutRiderInfoResponse>(NotFound())
            : Result.Success(Map(record));
    }

    public async Task<Result<List<OutRiderInfoResponse>>> GetAsync(
        string? riderId,
        string? phoneNumber,
        CancellationToken cancellationToken = default)
    {
        var query = db.OutRiderInfos.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(riderId))
            query = query.Where(r => r.RiderId == riderId.Trim());

        if (!string.IsNullOrWhiteSpace(phoneNumber))
            query = query.Where(r => r.PhoneNumber == phoneNumber.Trim());

        var rows = await query
            .OrderBy(r => r.RiderId)
            .Select(r => new OutRiderInfoResponse(
                r.Id,
                r.RiderId,
                r.PhoneNumber,
                r.CreatedAt,
                r.CreatedBy))
            .ToListAsync(cancellationToken);

        return Result.Success(rows);
    }

    public async Task<Result<OutRiderInfoResponse>> UpdateAsync(
        int id,
        UpdateOutRiderInfoRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request.RiderId, request.PhoneNumber);
        if (validation is not null)
            return Result.Failure<OutRiderInfoResponse>(validation);

        var record = await db.OutRiderInfos
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (record is null)
            return Result.Failure<OutRiderInfoResponse>(NotFound());

        var riderId = request.RiderId.Trim();
        if (await db.OutRiderInfos.AnyAsync(r => r.Id != id && r.RiderId == riderId, cancellationToken))
            return Result.Failure<OutRiderInfoResponse>(Duplicate(riderId));

        record.RiderId = riderId;
        record.PhoneNumber = request.PhoneNumber.Trim();

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(Map(record));
    }

    public async Task<Result> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var record = await db.OutRiderInfos
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (record is null)
            return Result.Failure(NotFound());

        var hasPerformance = await db.OutageShiftPerformances
            .AnyAsync(p => p.OutRiderInfoId == id, cancellationToken);

        if (hasPerformance)
        {
            return Result.Failure(new Error(
                "OutRiderInfo.InUse",
                "Cannot delete rider info while outage shift performance records reference it.",
                409));
        }

        db.OutRiderInfos.Remove(record);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Error? Validate(string riderId, string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(riderId))
            return new Error("OutRiderInfo.RiderIdRequired", "Rider ID is required.", 400);

        if (string.IsNullOrWhiteSpace(phoneNumber))
            return new Error("OutRiderInfo.PhoneNumberRequired", "Phone number is required.", 400);

        return null;
    }

    private static Error Duplicate(string riderId) =>
        new("OutRiderInfo.DuplicateRiderId", $"Rider ID '{riderId}' already exists.", 409);

    private static Error NotFound() =>
        new("OutRiderInfo.NotFound", "Out rider info record was not found.", 404);

    private static OutRiderInfoResponse Map(OutRiderInfo r) =>
        new(r.Id, r.RiderId, r.PhoneNumber, r.CreatedAt, r.CreatedBy);
}
