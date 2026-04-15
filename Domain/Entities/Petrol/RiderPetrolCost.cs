namespace Domain.Entities.Petrol;

/// <summary>
/// A single petrol cost attribution row that links one rider to one vehicle for one day.
///
/// Key scenarios handled:
///   • Rider had ONE vehicle that day  → one RiderPetrolCost row.
///   • Rider had TWO vehicles that day → two RiderPetrolCost rows (one per vehicle).
///   • Vehicle had TWO riders that day → two RiderPetrolCost rows sharing the same VehiclePetrolCostId.
///   • No rider found for the vehicle  → one row with RiderIqamaNo = null and Source = Unattributed.
/// </summary>
public class RiderPetrolCost
{
    public int Id { get; set; }

    // ── Source vehicle cost record ────────────────────────────────────────
    public int VehiclePetrolCostId { get; set; }
    public VehiclePetrolCost VehiclePetrolCost { get; set; } = default!;

    // ── Vehicle ───────────────────────────────────────────────────────────
    public string VehicleNumber { get; set; } = string.Empty;
    public Vehicle? Vehicle { get; set; }

    // ── Rider (nullable when no rider could be resolved) ──────────────────
    public long? RiderIqamaNo { get; set; }
    public Employees? Rider { get; set; }

    // ── Date this cost belongs to ─────────────────────────────────────────
    public DateOnly Date { get; set; }

    // ── Cost ─────────────────────────────────────────────────────────────
    public decimal Cost { get; set; }

    // ── Attribution detail ────────────────────────────────────────────────
    /// <summary>How the system resolved the rider for this vehicle on this date.</summary>
    public PetrolAttributionSource AttributionSource { get; set; }

    /// <summary>
    /// The RiderVehicleStatus.Id that was used to resolve this attribution.
    /// Stored for full audit / debugging traceability.
    /// </summary>
    public int? ResolvedFromStatusId { get; set; }

    /// <summary>
    /// Extra context: e.g. "Vehicle switched mid-day; cost attributed to rider active during the day.",
    /// or "No active rider found for this vehicle on this date."
    /// </summary>
    public string? Notes { get; set; }

    // ── Audit ─────────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
}

/// <summary>Describes how the system determined which rider to attribute the cost to.</summary>
public enum PetrolAttributionSource
{
    /// <summary>
    /// Attributed via an explicit Permission window (PermissionStartDate ≤ date ≤ PermissionEndDate)
    /// on a RiderVehicleStatus record.
    /// </summary>
    Permission = 1,

    /// <summary>
    /// Attributed by following the Taken/Returned status timeline:
    /// the rider's "Taken" Timestamp was on or before the report date
    /// and no "Returned" event existed before end-of-day.
    /// </summary>
    VehicleStatusTimeline = 2,

    /// <summary>No rider could be resolved; manual intervention required.</summary>
    Unattributed = 3,

    /// <summary>Manually assigned by an admin after the fact.</summary>
    ManualOverride = 4,
}