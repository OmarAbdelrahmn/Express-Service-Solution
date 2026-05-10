namespace Domain.Entities;

/// <summary>
/// Represents a single parsed shift block for a transporter rider on a given date.
/// One rider may have up to 2 shifts per day (e.g. "Driver • 6 PM • 5h" + "Driver • 12 PM • 5h").
/// A null StartTime / EndTime with IsBreakDay = false and RawEntry "–" indicates an unscheduled / unknown-time block.
/// </summary>
public class TransporterShift
{
    public int Id { get; set; }

    // ── Rider linkage ─────────────────────────────────────────────────────
    public int RiderId { get; set; }
    public RiderDetails Rider { get; set; } = default!;

    /// <summary>Denormalized copy of WorkingId for fast filtering.</summary>
    public string WorkingId { get; set; } = string.Empty;

    // ── Date & time ───────────────────────────────────────────────────────
    public DateOnly ShiftDate { get; set; }

    /// <summary>
    /// Position of this block within the day (1 = first entry, 2 = second entry).
    /// Cells with two lines produce two rows with ShiftIndex 1 and 2.
    /// </summary>
    public int ShiftIndex { get; set; } = 1;

    /// <summary>Parsed start time (null when the raw entry contains "–" / "--").</summary>
    public TimeOnly? StartTime { get; set; }

    /// <summary>Parsed end time = StartTime + DurationHours (null when StartTime is null).</summary>
    public TimeOnly? EndTime { get; set; }

    /// <summary>Duration in hours as read from the raw entry (e.g. 5 for "5h").</summary>
    public float DurationHours { get; set; }

    /// <summary>True when this day is a scheduled break (no shift entry exists at all).</summary>
    public bool IsBreakDay { get; set; } = false;

    // ── Source ────────────────────────────────────────────────────────────
    /// <summary>Verbatim string from the Excel cell, e.g. "Driver • 6 PM • 5h".</summary>
    public string? RawEntry { get; set; }

    // ── Edit tracking ─────────────────────────────────────────────────────
    public bool IsManuallyEdited { get; set; } = false;
    public string? Notes { get; set; }

    // ── Audit ─────────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}