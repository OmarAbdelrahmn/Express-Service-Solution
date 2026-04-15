namespace Domain.Entities.Petrol;

/// <summary>
/// Represents the raw petrol cost record uploaded from Excel for a specific vehicle on a specific date.
/// One row per vehicle per upload batch. Attribution to riders is handled separately in RiderPetrolCost.
/// </summary>
public class VehiclePetrolCost
{
    public int Id { get; set; }

    // ── Vehicle identity (from Excel) ─────────────────────────────────────
    /// <summary>
    /// English plate number as it appears in the Excel file.
    /// Used to resolve the Vehicle record on import.
    /// </summary>
    public string PlateNumberE { get; set; } = string.Empty;

    /// <summary>
    /// Resolved vehicle number from the Vehicle table (VehicleNumber is the PK-like key).
    /// Null if the plate could not be matched to any known vehicle.
    /// </summary>
    public string? VehicleNumber { get; set; }

    // ── Cost ──────────────────────────────────────────────────────────────
    public decimal Cost { get; set; }

    // ── Date this report covers (usually yesterday) ───────────────────────
    /// <summary>
    /// The operational date the cost belongs to.
    /// Supplied via the request querystring at upload time.
    /// </summary>
    public DateOnly Date { get; set; }

    // ── Upload metadata ───────────────────────────────────────────────────
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string UploadedBy { get; set; } = string.Empty;

    /// <summary>
    /// True once the attribution engine has run for this record and written
    /// the corresponding RiderPetrolCost rows.
    /// </summary>
    public bool IsAttributed { get; set; } = false;

    /// <summary>
    /// Set when the plate number could not be matched to a vehicle in the database.
    /// The record is still stored so it can be manually resolved later.
    /// </summary>
    public bool HasResolutionError { get; set; } = false;
    public string? ResolutionErrorMessage { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────
    public Vehicle? Vehicle { get; set; }

    /// <summary>
    /// All rider-level attributions derived from this vehicle cost.
    /// A single vehicle cost may fan out to more than one rider when
    /// a shift change occurred during that day.
    /// </summary>
    public ICollection<RiderPetrolCost> RiderPetrolCosts { get; set; } = [];
}