using Application.Abstraction;
using Application.Contracts.SystemIdPhoneStatuses;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.SystemIdPhoneStatuses;

public class SystemIdPhoneStatusService(ApplicationDbcontext db) : ISystemIdPhoneStatusService
{
    public async Task<Result<SystemIdPhoneStatusResponse>> CreateAsync(
        CreateSystemIdPhoneStatusRequest request,
        DateOnly statusDate,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request.SystemId, request.PhoneNumber);
        if (validation is not null)
            return Result.Failure<SystemIdPhoneStatusResponse>(validation);

        try
        {
            var record = new SystemIdPhoneStatus
            {
                SystemId = request.SystemId.Trim(),
                PhoneNumber = request.PhoneNumber.Trim(),
                StatusDate = statusDate,
                Status = NormalizeStatus(request.Status),
                RawStatus = request.Status?.Trim(),
                UploadedAt = DateTime.UtcNow.AddHours(3),
                UploadedBy = createdBy
            };

            await db.SystemIdPhoneStatuses.AddAsync(record, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(Map(record));
        }
        catch (Exception ex)
        {
            return Result.Failure<SystemIdPhoneStatusResponse>(new Error(
                "SystemIdPhoneStatus.CreateFailed",
                ex.Message,
                500));
        }
    }

    public async Task<Result<SystemIdPhoneStatusImportResponse>> ImportAsync(
        ImportSystemIdPhoneStatusRequest request,
        string importedBy,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = new List<SystemIdPhoneStatus>();
            var warnings = new List<string>();
            var keysToDelete = new HashSet<(string systemId, DateOnly date)>();
            var blankCellsSkipped = 0;
            var now = DateTime.UtcNow.AddHours(3);

            foreach (var cell in request.Cells)
            {
                if (string.IsNullOrWhiteSpace(cell.SystemId))
                {
                    warnings.Add("A cell was skipped because system ID is missing.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(cell.PhoneNumber))
                {
                    warnings.Add($"System ID '{cell.SystemId}' skipped because phone number is missing.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(cell.Status))
                {
                    blankCellsSkipped++;
                    keysToDelete.Add((cell.SystemId.Trim(), request.StatusDate));
                    continue;
                }

                keysToDelete.Add((cell.SystemId.Trim(), request.StatusDate));
                records.Add(new SystemIdPhoneStatus
                {
                    SystemId = cell.SystemId.Trim(),
                    PhoneNumber = cell.PhoneNumber.Trim(),
                    StatusDate = request.StatusDate,
                    Status = NormalizeStatus(cell.Status),
                    RawStatus = cell.Status.Trim(),
                    UploadedAt = now,
                    UploadedBy = importedBy
                });
            }

            if (keysToDelete.Count > 0)
            {
                var systemIds = keysToDelete.Select(k => k.systemId).Distinct().ToList();
                var dates = keysToDelete.Select(k => k.date).Distinct().ToList();

                var stale = await db.SystemIdPhoneStatuses
                    .Where(s => systemIds.Contains(s.SystemId) && dates.Contains(s.StatusDate))
                    .ToListAsync(cancellationToken);

                db.SystemIdPhoneStatuses.RemoveRange(
                    stale.Where(s => keysToDelete.Contains((s.SystemId, s.StatusDate))));
            }

            await db.SystemIdPhoneStatuses.AddRangeAsync(records, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(new SystemIdPhoneStatusImportResponse(
                TotalCellsProcessed: request.Cells.Count,
                RecordsCreated: records.Count,
                BlankCellsSkipped: blankCellsSkipped,
                Warnings: warnings));
        }
        catch (Exception ex)
        {
            return Result.Failure<SystemIdPhoneStatusImportResponse>(new Error(
                "SystemIdPhoneStatus.ImportFailed",
                $"Import failed: {ex.Message}",
                500));
        }
    }

    public async Task<Result<SystemIdPhoneStatusResponse>> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await db.SystemIdPhoneStatuses
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            return record is null
                ? Result.Failure<SystemIdPhoneStatusResponse>(NotFound())
                : Result.Success(Map(record));
        }
        catch (Exception ex)
        {
            return Result.Failure<SystemIdPhoneStatusResponse>(new Error(
                "SystemIdPhoneStatus.QueryFailed",
                ex.Message,
                500));
        }
    }

    public async Task<Result<List<SystemIdPhoneStatusResponse>>> GetAsync(
        string? systemId,
        string? phoneNumber,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = db.SystemIdPhoneStatuses.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(systemId))
                query = query.Where(s => s.SystemId == systemId.Trim());

            if (!string.IsNullOrWhiteSpace(phoneNumber))
                query = query.Where(s => s.PhoneNumber == phoneNumber.Trim());

            if (from.HasValue)
                query = query.Where(s => s.StatusDate >= from.Value);

            if (to.HasValue)
                query = query.Where(s => s.StatusDate <= to.Value);

            var rows = await query
                .OrderByDescending(s => s.StatusDate)
                .ThenBy(s => s.SystemId)
                .Select(s => new SystemIdPhoneStatusResponse(
                    s.Id,
                    s.SystemId,
                    s.PhoneNumber,
                    s.StatusDate,
                    s.Status,
                    s.RawStatus,
                    s.UploadedAt,
                    s.UploadedBy))
                .ToListAsync(cancellationToken);

            return Result.Success(rows);
        }
        catch (Exception ex)
        {
            return Result.Failure<List<SystemIdPhoneStatusResponse>>(new Error(
                "SystemIdPhoneStatus.QueryFailed",
                ex.Message,
                500));
        }
    }

    public async Task<Result<SystemIdPhoneStatusResponse>> UpdateAsync(
        int id,
        UpdateSystemIdPhoneStatusRequest request,
        DateOnly statusDate,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request.SystemId, request.PhoneNumber);
        if (validation is not null)
            return Result.Failure<SystemIdPhoneStatusResponse>(validation);

        try
        {
            var record = await db.SystemIdPhoneStatuses
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            if (record is null)
                return Result.Failure<SystemIdPhoneStatusResponse>(NotFound());

            record.SystemId = request.SystemId.Trim();
            record.PhoneNumber = request.PhoneNumber.Trim();
            record.StatusDate = statusDate;
            record.Status = NormalizeStatus(request.Status);
            record.RawStatus = request.Status?.Trim();

            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(record));
        }
        catch (Exception ex)
        {
            return Result.Failure<SystemIdPhoneStatusResponse>(new Error(
                "SystemIdPhoneStatus.UpdateFailed",
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
            var record = await db.SystemIdPhoneStatuses
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            if (record is null)
                return Result.Failure(NotFound());

            db.SystemIdPhoneStatuses.Remove(record);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error(
                "SystemIdPhoneStatus.DeleteFailed",
                ex.Message,
                500));
        }
    }

    private static Error? Validate(string systemId, string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(systemId))
            return new Error("SystemIdPhoneStatus.SystemIdRequired", "System ID is required.", 400);

        if (string.IsNullOrWhiteSpace(phoneNumber))
            return new Error("SystemIdPhoneStatus.PhoneNumberRequired", "Phone number is required.", 400);

        return null;
    }

    private static Error NotFound() =>
        new("SystemIdPhoneStatus.NotFound", "System ID phone status record was not found.", 404);

    private static SystemIdPhoneStatusResponse Map(SystemIdPhoneStatus s) =>
        new(
            s.Id,
            s.SystemId,
            s.PhoneNumber,
            s.StatusDate,
            s.Status,
            s.RawStatus,
            s.UploadedAt,
            s.UploadedBy);

    private static string? NormalizeStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();
        if (text.Equals("Accepted", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Accept", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            return "Accepted";

        if (text.Equals("Rejected", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Reject", StringComparison.OrdinalIgnoreCase))
            return "Rejected";

        return text;
    }
}
