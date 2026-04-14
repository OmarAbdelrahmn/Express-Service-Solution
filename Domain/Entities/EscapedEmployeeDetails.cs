namespace Domain.Entities;

/// <summary>
/// Tracks an employee who has escaped/fled. Two mutually exclusive paths:
///   • Reported  — officially reported to authorities; 60-day removal window from ReportedAt.
///   • Outage    — country-exit / system outage; 60-day removal window from DateOfOutage.
/// Only one path may be active at a time. Switching paths clears the previous path's data.
/// </summary>
public class EscapedEmployeeDetails
{
    public int Id { get; set; }

    // ── One-to-one with Employees (nullable on Employees side) ───────────
    public long EmployeeIqamaNo { get; set; }
    public Employees Employee { get; set; } = default!;

    /// <summary>The date the employee was confirmed to have escaped.</summary>
    public DateOnly EscapedAt { get; set; }

    /// <summary>Which path is currently active. None means freshly created (no path yet).</summary>
    public EscapedPath ActivePath { get; set; } = EscapedPath.None;

    // ── Path 1: Reported ─────────────────────────────────────────────────
    /// <summary>True once the report to authorities has been filed.</summary>
    public bool? IsReported { get; set; }

    /// <summary>When the employee was officially reported.</summary>
    public DateTime? ReportedAt { get; set; }

    // ── Path 2: Outage ───────────────────────────────────────────────────
    /// <summary>True when the employee left the country / system went dark.</summary>
    public bool? IsOutage { get; set; }

    /// <summary>Date the outage / departure was recorded.</summary>
    public DateTime? DateOfOutage { get; set; }

    /// <summary>Visa number associated with the outage event.</summary>
    public string? OutageVisaNumber { get; set; }

    // ── Shared: 60-day removal window ────────────────────────────────────
    /// <summary>
    /// The deadline date by which the employee record must be removed.
    /// Computed as 60 days from the active path's trigger date.
    /// </summary>
    public DateTime? RemovalDeadline { get; set; }

    /// <summary>Remaining calendar days until RemovalDeadline (can be negative if overdue).</summary>
    public int? RemainingDaysToRemoval =>
        RemovalDeadline.HasValue
            ? (int)(RemovalDeadline.Value.Date - DateTime.UtcNow.AddHours(3).Date).TotalDays
            : null;

    // ── Notification tracking ─────────────────────────────────────────────
    /// <summary>True after the 10-day warning e-mail has been dispatched.</summary>
    public bool TenDayNotificationSent { get; set; } = false;
    public DateTime? TenDayNotificationSentAt { get; set; }

    // ── Audit ─────────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // ── Notes ─────────────────────────────────────────────────────────────
    public string? Notes { get; set; }

    // ── Soft Delete ───────────────────────────────────────────────────────────
    public bool IsActive { get; set; } = true;
    public DateTime? DeactivatedAt { get; set; }
    public string? DeactivatedBy { get; set; }
}

public enum EscapedPath
{
    /// <summary>No path chosen yet.</summary>
    None = 0,

    /// <summary>Reported to authorities — 60 days from ReportedAt.</summary>
    Reported = 1,

    /// <summary>Country exit / system outage — 60 days from DateOfOutage.</summary>
    Outage = 2
}