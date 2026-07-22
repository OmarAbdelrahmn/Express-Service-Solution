using Application.Abstraction;
using Application.Contracts.InventoryAudit;
using Domain;
using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.InventoryAudit;

public class InventoryAuditService(ApplicationDbcontext dbcontext) : IInventoryAuditService
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;

    public async Task<Result<InventoryAuditLogPageResponse>> GetAllAsync(
        DateTime? fromDate = null,
        DateTime? toDate = null,
        InventoryItemType? itemType = null,
        string? location = null,
        string? performedBy = null,
        int page = 1,
        int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 500) pageSize = 50;

        var query = _dbcontext.InventoryAuditLogs.AsNoTracking().AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(a => a.PerformedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(a => a.PerformedAt <= toDate.Value);

        if (itemType.HasValue)
            query = query.Where(a => a.ItemType == itemType.Value);

        if (!string.IsNullOrWhiteSpace(location))
            query = query.Where(a => a.LocationBefore == location || a.LocationAfter == location);

        if (!string.IsNullOrWhiteSpace(performedBy))
            query = query.Where(a => a.PerformedBy == performedBy);

        query = query.OrderByDescending(a => a.PerformedAt);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var response = new InventoryAuditLogPageResponse(
            totalCount,
            page,
            pageSize,
            items.Select(MapToResponse));

        return Result.Success(response);
    }

    private static InventoryAuditLogResponse MapToResponse(InventoryAuditLog log) => new(
        log.Id,
        log.ItemType.ToString(),
        log.ItemId,
        log.ItemName,
        log.Action.ToString(),
        log.LocationBefore,
        log.LocationAfter,
        log.QuantityBefore,
        log.QuantityAfter,
        log.PriceBefore,
        log.PriceAfter,
        log.PerformedBy,
        log.PerformedAt,
        log.Notes);
}
