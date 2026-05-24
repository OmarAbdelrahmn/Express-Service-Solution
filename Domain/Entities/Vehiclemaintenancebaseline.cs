namespace Domain.Entities;

/// <summary>
/// Stores the "last maintenance done" date for a specific vehicle (SparePart interval)
/// or rider (Accessory interval) when no SparePartUsage / RiderAccessoryUsage record exists yet
/// — i.e. before the system started tracking, or as a manual admin override.
///
/// Priority rule used by ReminderService:
///   effective last-done = MAX(latest usage record, latest baseline record)
/// </summary>
public class VehicleMaintenanceBaseline
{
    public int Id { get; set; }

    public int MaintenanceIntervalId { get; set; }
    public MaintenanceInterval MaintenanceInterval { get; set; } = default!;

    // ── Target: vehicle (SparePart intervals) ─────────────────────────────
    public string? VehicleNumber { get; set; }
    public Vehicle? Vehicle { get; set; }

    // ── Target: rider (Accessory intervals) ───────────────────────────────
    public int? RiderId { get; set; }
    public RiderDetails? Rider { get; set; }

    /// <summary>When this maintenance was last performed (manual baseline date).</summary>
    public DateTime LastDoneAt { get; set; }

    // ── Audit ─────────────────────────────────────────────────────────────
    public string SetBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public string? Notes { get; set; }
}