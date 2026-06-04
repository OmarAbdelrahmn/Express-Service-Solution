namespace Domain.Entities.Keeta;

/// <summary>
/// One row from a Keeta platform daily driver-report Excel export.
/// One record per driver (PlatformDriverId) per day.
/// Links back to the internal RiderDetails via WorkingId / RiderId.
/// </summary>
public class KeetaDriverShift
{
    public int Id { get; set; }

    // ── Date ──────────────────────────────────────────────────────────────
    /// <summary>The operational date this row belongs to (YYYYMMDD → DateOnly).</summary>
    public DateOnly ReportDate { get; set; }

    // ── Platform identity ─────────────────────────────────────────────────
    /// <summary>
    /// The identifier that came from the Keeta Excel column "معرّف السائق".
    /// This is the value used as a key when searching RiderDetails.
    /// </summary>
    public string PlatformDriverId { get; set; } = string.Empty;

    // ── Internal rider link (nullable when not matched) ───────────────────
    /// <summary>Resolved WorkingId from RiderDetails or WorkingIdHistory. Null if not found.</summary>
    public string? WorkingId { get; set; }

    /// <summary>FK → RiderDetails.Id. Null when no match was found in import.</summary>
    public int? RiderId { get; set; }
    public RiderDetails? Rider { get; set; }

    // ── Supervisor ────────────────────────────────────────────────────────
    public string? Supervisor { get; set; }

    // ── Shift status ──────────────────────────────────────────────────────
    /// <summary>
    /// True when the platform column "هل أنت في الوردية؟" was "Yes".
    /// False (No / zero-time records) drivers are still stored for completeness.
    /// </summary>
    public bool IsInShift { get; set; }

    // ── Connection time ───────────────────────────────────────────────────
    /// <summary>Raw Arabic string from the Excel, e.g. "18 س 3 د".</summary>
    public string? TotalConnectionTimeRaw { get; set; }

    /// <summary>Connection time converted to minutes for easy queries.</summary>
    public int TotalConnectionMinutes { get; set; }

    // ── Tasks ─────────────────────────────────────────────────────────────
    public int TasksDelivered { get; set; }

    // ── Raw payload (preserved for re-parsing / audit) ────────────────────
    /// <summary>
    /// The complete pipe-separated slot string from the Excel
    /// "فترة الوردية_ملخص الاتصال" column, e.g.
    /// "00:00-03:00,Off-Shift,0 ث|08:00-12:00,On-Shift,3 س 52 د,qualified|…"
    /// </summary>
    public string? RawShiftSummary { get; set; }

    // ── Derived ───────────────────────────────────────────────────────────
    /// <summary>How many qualified slots were stored (0–3).</summary>
    public int QualifiedSlotsCount { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────
    /// <summary>
    /// Up to 3 qualified On-Shift slots, chosen by descending duration,
    /// then re-ordered chronologically.
    /// </summary>
    public ICollection<KeetaShiftSlot> ShiftSlots { get; set; } = [];

    // ── Audit ─────────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public DateTime? UpdatedAt { get; set; }
    public string? ImportedBy { get; set; }
}