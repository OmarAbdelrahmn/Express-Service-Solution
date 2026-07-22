using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Contracts.InventoryAudit;


public record InventoryAuditLogResponse(
    int Id,
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