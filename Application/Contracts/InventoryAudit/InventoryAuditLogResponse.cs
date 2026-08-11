using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Contracts.InventoryAudit;


public enum InventoryItemType
{
    SparePart = 1,
    RiderAccessory = 2
}

public record InventoryAuditLogResponse(
    long Id,
    string ItemType,        // "SparePart" | "RiderAccessory"
    int ItemId,
    string ItemName,
    string Action,          // "Create" | "Update" | "Delete"
    string? LocationBefore,
    string? LocationAfter,
    int? QuantityBefore,
    int? QuantityAfter,
    decimal? PriceBefore,
    decimal? PriceAfter,
    string PerformedBy,
    DateTime PerformedAt,
    string? Notes
);

public record InventoryAuditLogPageResponse(
    int TotalCount,
    int Page,
    int PageSize,
    IEnumerable<InventoryAuditLogResponse> Items
);
