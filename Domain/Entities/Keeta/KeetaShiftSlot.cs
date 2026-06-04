namespace Domain.Entities.Keeta;

/// <summary>
/// A single qualified On-Shift time slot persisted for a KeetaDriverShift record.
///
/// Selection rule: from all slots in the raw summary that are both On-Shift AND qualified,
/// keep the top-3 by DurationMinutes (most time worked). If two slots tie, the earlier
/// one (smaller StartTime) wins. The saved slots are stored in chronological order.
/// </summary>
public class KeetaShiftSlot
{
    public int Id { get; set; }

    // ── Owner ─────────────────────────────────────────────────────────────
    public int KeetaDriverShiftId { get; set; }
    public KeetaDriverShift DriverShift { get; set; } = default!;

    // ── Slot identity ─────────────────────────────────────────────────────
    /// <summary>Human-readable key, e.g. "08:00-12:00".</summary>
    public string SlotKey { get; set; } = string.Empty;

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    // ── Flags ─────────────────────────────────────────────────────────────
    public bool IsOnShift { get; set; }
    public bool IsQualified { get; set; }

    // ── Duration ──────────────────────────────────────────────────────────
    /// <summary>Raw Arabic duration string from the slot, e.g. "3 س 52 د".</summary>
    public string DurationRaw { get; set; } = string.Empty;

    /// <summary>Duration rounded to the nearest minute (seconds dropped).</summary>
    public int DurationMinutes { get; set; }

    // ── Ordering ──────────────────────────────────────────────────────────
    /// <summary>
    /// 1-based position of this slot in the original 6-slot daily breakdown.
    /// 1 = 00:00–03:00 … 6 = 20:00–24:00.
    /// Preserved so the original ordering can always be reconstructed.
    /// </summary>
    public int SlotOrder { get; set; }
}