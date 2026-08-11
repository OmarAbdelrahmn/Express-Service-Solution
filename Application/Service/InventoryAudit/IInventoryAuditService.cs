using Application.Abstraction;
using Application.Contracts.InventoryAudit;

namespace Application.Service.InventoryAudit;

public interface IInventoryAuditService
{
    /// <summary>
    /// Main-service view: every manual add/edit/delete made to any spare part
    /// or rider accessory, across every housing and the main company stock.
    /// </summary>
    Task<Result<InventoryAuditLogPageResponse>> GetAllAsync(
        DateTime? fromDate = null,
        DateTime? toDate = null,
        InventoryItemType? itemType = null,
        string? location = null,
        string? performedBy = null,
        int page = 1,
        int pageSize = 50);
}
