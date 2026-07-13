using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities.Spare;

public enum InventoryItemType
{
    SparePart = 1,
    RiderAccessory = 2
}

public enum InventoryAuditAction
{
    Create = 1,
    Update = 2,
    Delete = 3
}

/// <summary>
/// Records every manual add/edit/delete made directly to a SparePart or
/// RiderAccessory record (quantity, price, name, location, etc.) — as opposed
/// to changes that happen automatically through bills or usage records.
/// Written in the same DbContext/transaction as the change itself so it is
/// never possible to have one without the other.
/// </summary>
public class InventoryAuditLog
{
    public int Id { get; set; }

    public InventoryItemType ItemType { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;

    public InventoryAuditAction Action { get; set; }

    // Location snapshot ("housing") the item belonged to before/after the change.
    // Matches the plain-string Location convention already used across the app
    // (SparePart.Location / RiderAccessory.Location / Housing.Name).
    public string? LocationBefore { get; set; }
    public string? LocationAfter { get; set; }

    public int? QuantityBefore { get; set; }
    public int? QuantityAfter { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? PriceBefore { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? PriceAfter { get; set; }

    // Who did it — the acting user's identifier (Iqama for housing members,
    // username for Admin/Master), taken straight from the auth claims.
    public string PerformedBy { get; set; } = string.Empty;

    public DateTime PerformedAt { get; set; } = DateTime.UtcNow.AddHours(3);

    public string? Notes { get; set; }
}
