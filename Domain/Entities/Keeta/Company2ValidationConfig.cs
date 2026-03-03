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

    public bool IsThursdayCritical { get; set; } = false;  // ★ NEW


    // ── Critical Weekdays (must meet TargetOrdersPerDay) ─────────────────
    /// <summary>
    /// Whether Friday is a critical weekday. When true (and FridayIsSpecialDay is false),
    /// a rider who works on Friday but records fewer than TargetOrdersPerDay accepted orders
    /// has that day treated as invalid (counts toward missing days).
    /// </summary>
    public bool IsFridayCritical { get; set; } = false;

    /// <summary>
    /// Whether Saturday is a critical weekday. When true (and SaturdayIsSpecialDay is false),
    /// a rider who works on Saturday but records fewer than TargetOrdersPerDay accepted orders
    /// has that day treated as invalid (counts toward missing days).
    /// </summary>
    public bool IsSaturdayCritical { get; set; } = false;

    // ── Metadata ──────────────────────────────────────────────────────────
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string? UpdatedBy { get; set; }

    public string CriticalDaysOfMonth { get; set; } = string.Empty;  // ★ NEW

    /// <summary>Parses CriticalDaysOfMonth into a set for fast lookup.</summary>
    public HashSet<int> GetCriticalDaysOfMonthSet()                   // ★ NEW
    {
        if (string.IsNullOrWhiteSpace(CriticalDaysOfMonth)) return [];
        var result = new HashSet<int>();
        foreach (var part in CriticalDaysOfMonth.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(part.Trim(), out var day) && day >= 1 && day <= 31)
                result.Add(day);
        return result;
    }
}