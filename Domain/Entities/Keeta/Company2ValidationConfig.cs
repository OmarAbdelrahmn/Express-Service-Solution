namespace Domain.Entities.Keeta;

/// <summary>
/// Singleton configuration record for Company 2 (Keta) monthly rider validation rules.
/// Only one row (Id = 1) should ever exist in this table.
/// </summary>
public class Company2ValidationConfig
{
    public int Id { get; set; } = 1;

    // ── Order / Hour Targets ──────────────────────────────────────────────
    /// <summary>Daily order target per rider (e.g. 12)</summary>
    public int TargetOrdersPerDay { get; set; } = 12;

    /// <summary>Daily working-hours target per rider (e.g. 10.5)</summary>
    public float TargetHoursPerDay { get; set; } = 10.5f;

    /// <summary>Minimum working hours for a day to count as a valid shift (e.g. 10)</summary>
    public float MinWorkingHoursPerDay { get; set; } = 10f;

    /// <summary>Full-month accepted-orders target (e.g. 300)</summary>
    public int FullMonthTargetOrders { get; set; } = 300;

    // ── Critical-Day Window ───────────────────────────────────────────────
    /// <summary>How many days at the start of the month are "critical" (e.g. 3)</summary>
    public int FirstCriticalDaysCount { get; set; } = 3;

    /// <summary>How many days at the end of the month are "critical" (e.g. 4)</summary>
    public int LastCriticalDaysCount { get; set; } = 4;

    /// <summary>
    /// Max start day-of-month an existing (continuing) rider may have before being
    /// considered absent from the beginning (e.g. 5 means days 1-5 must be covered).
    /// </summary>
    public int MaxStartDayForExistingRiders { get; set; } = 5;

    // ── Allowed Missing Days per Month Length ─────────────────────────────
    /// <summary>Max absence days allowed in a 28-day month (Feb non-leap)</summary>
    public int AllowedMissingDays28 { get; set; } = 3;

    /// <summary>Max absence days allowed in a 29-day month (Feb leap)</summary>
    public int AllowedMissingDays29 { get; set; } = 3;

    /// <summary>Max absence days allowed in a 30-day month</summary>
    public int AllowedMissingDays30 { get; set; } = 4;

    /// <summary>Max absence days allowed in a 31-day month</summary>
    public int AllowedMissingDays31 { get; set; } = 5;

    // ── Special / Off Days ────────────────────────────────────────────────
    /// <summary>Whether Sunday is a special (off / excluded) day</summary>
    public bool SundayIsSpecialDay { get; set; } = false;

    /// <summary>Whether Monday is a special (off / excluded) day</summary>
    public bool MondayIsSpecialDay { get; set; } = false;

    /// <summary>Whether Tuesday is a special (off / excluded) day</summary>
    public bool TuesdayIsSpecialDay { get; set; } = false;

    /// <summary>Whether Wednesday is a special (off / excluded) day</summary>
    public bool WednesdayIsSpecialDay { get; set; } = false;

    /// <summary>Whether Thursday is a special (off / excluded) day</summary>
    public bool ThursdayIsSpecialDay { get; set; } = true;

    /// <summary>Whether Friday is a special (off / excluded) day</summary>
    public bool FridayIsSpecialDay { get; set; } = true;

    /// <summary>Whether Saturday is a special (off / excluded) day</summary>
    public bool SaturdayIsSpecialDay { get; set; } = false;

    // ── Metadata ──────────────────────────────────────────────────────────
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string? UpdatedBy { get; set; }
}