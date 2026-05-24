using Domain.Entities.Spare;

namespace Domain.Entities;

/// <summary>
/// Admin-configured rule: "Change [ItemName] every [IntervalDays] days."
/// Applies to a SparePart (tracked per vehicle) or a RiderAccessory (tracked per rider).
/// </summary>
public class MaintenanceInterval
{
    public int Id { get; set; }

    // ── Item reference (exactly one must be set) ──────────────────────────
    public int? SparePartId { get; set; }
    public SparePart? SparePart { get; set; }

    public int? AccessoryId { get; set; }
    public RiderAccessory? Accessory { get; set; }

    public MaintenanceItemType ItemType { get; set; }

    /// <summary>
    /// Denormalised display name copied from SparePart.Name or Accessory.Name
    /// so reminders can be rendered even if the item is later renamed.
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    // ── Timing ────────────────────────────────────────────────────────────
    /// <summary>Maintenance must be performed every this many days.</summary>
    public int IntervalDays { get; set; }

    /// <summary>
    /// How many days before the due date the reminder starts appearing.
    /// 0  = show only on the exact due day.
    /// 2  = show 2 days early (upcoming warning).
    /// </summary>
    public int AlertDaysBeforeDue { get; set; } = 0;

    // ── Scope (optional) ──────────────────────────────────────────────────
    /// <summary>
    /// When null this interval applies to ALL locations.
    /// When set it only applies to vehicles/riders whose housing name matches.
    /// </summary>
    public string? Location { get; set; }

    // ── State ─────────────────────────────────────────────────────────────
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    // ── Audit ─────────────────────────────────────────────────────────────
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────
    public ICollection<VehicleMaintenanceBaseline> Baselines { get; set; } = [];
}

public enum MaintenanceItemType
{
    SparePart = 1,
    Accessory = 2
}