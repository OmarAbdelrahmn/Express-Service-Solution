using Application.Abstraction;
using Application.Contracts.InventoryAudit;
using Domain;
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

        var query = _dbcontext.SystemAuditEvents
            .AsNoTracking()
            .Where(a => a.EntityType == InventoryAuditProjection.SparePartEntityType ||
                        a.EntityType == InventoryAuditProjection.RiderAccessoryEntityType)
            .AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(a => a.OccurredAtUtc >= ToUtc(fromDate.Value));

        if (toDate.HasValue)
            query = query.Where(a => a.OccurredAtUtc <= ToUtc(toDate.Value));

        if (itemType.HasValue)
            query = itemType.Value == InventoryItemType.SparePart
                ? query.Where(a => a.EntityType == InventoryAuditProjection.SparePartEntityType)
                : query.Where(a => a.EntityType == InventoryAuditProjection.RiderAccessoryEntityType);

        if (!string.IsNullOrWhiteSpace(location))
            query = query.Where(a => a.ScopeBefore == location || a.ScopeAfter == location);

        if (!string.IsNullOrWhiteSpace(performedBy))
            query = query.Where(a => a.ActorName == performedBy);

        query = query.OrderByDescending(a => a.OccurredAtUtc);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var response = new InventoryAuditLogPageResponse(
            totalCount,
            page,
            pageSize,
            items.Select(InventoryAuditProjection.ToResponse));

        return Result.Success(response);
    }

    private static DateTimeOffset ToUtc(DateTime value) => new(value.ToUniversalTime());
}
