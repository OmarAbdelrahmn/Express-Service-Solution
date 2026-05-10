using Domain.Entities;

namespace Application.Service.TransporterShifts;

/// <summary>
/// Parses the raw text content of a single Excel schedule cell into one or more
/// <see cref="TransporterShift"/> domain objects.
///
/// Supported cell formats (separator is bullet "•"):
///   "Driver • 6 PM • 5h"      → start 18:00, duration 5 h, end 23:00
///   "Driver • 12 PM • 5h"     → start 12:00, duration 5 h, end 17:00
///   "Driver • 1 AM • 3h"      → start 01:00, duration 3 h, end 04:00
///   "Driver • 2 PM • 10h"     → start 14:00, duration 10 h, end 00:00 (next day)
///   "Driver • -- • 4h"        → no fixed start time, duration 4 h (floating)
///   "Driver • –– • 4h"        → same (em-dash variant)
///   Empty / whitespace cell   → break day
///   Two lines in one cell     → two separate shift blocks (ShiftIndex 1 and 2)
/// </summary>
public static class ShiftCellParser
{
    private const char BulletSeparator = '•';

    /// <summary>
    /// Parse a raw cell string into partial <see cref="TransporterShift"/> objects.
    /// Caller is responsible for setting RiderId, WorkingId, ShiftDate, audit fields.
    /// Returns an empty-list when the cell represents a break day.
    /// </summary>
    public static ParsedCellResult Parse(string? rawCell)
    {
        if (string.IsNullOrWhiteSpace(rawCell))
            return ParsedCellResult.BreakDay();

        // Split multi-shift cells on newlines
        var lines = rawCell
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count == 0)
            return ParsedCellResult.BreakDay();

        var shifts = new List<ParsedShiftBlock>();
        int index = 1;

        foreach (var line in lines)
        {
            var block = ParseSingleLine(line, index);
            if (block is not null)
            {
                shifts.Add(block);
                index++;
            }
        }

        return shifts.Count == 0
            ? ParsedCellResult.BreakDay()
            : new ParsedCellResult(IsBreakDay: false, Shifts: shifts);
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    private static ParsedShiftBlock? ParseSingleLine(string line, int shiftIndex)
    {
        var parts = line.Split(BulletSeparator);
        if (parts.Length < 3)
            return null;

        // parts[0] = "Driver" (or any role label – we accept anything)
        var timePart = parts[1].Trim();
        var durationPart = parts[2].Trim();

        float duration = ParseDuration(durationPart);
        if (duration <= 0) return null;

        bool isFloating = IsFloatingTime(timePart);
        TimeOnly? start = isFloating ? null : ParseTime(timePart);
        TimeOnly? end = (start is not null) ? AddHours(start.Value, duration) : null;

        return new ParsedShiftBlock(
            ShiftIndex: shiftIndex,
            StartTime: start,
            EndTime: end,
            DurationHours: duration,
            IsFloating: isFloating,
            RawEntry: line
        );
    }

    /// <summary>Parses "5h", "10h", "3h" → 5f, 10f, 3f.</summary>
    private static float ParseDuration(string raw)
    {
        var cleaned = raw.Replace("h", "").Replace("H", "").Trim();
        return float.TryParse(cleaned, out var val) ? val : 0f;
    }

    /// <summary>Returns true when the time token is "--", "–", "––", "—", or similar.</summary>
    private static bool IsFloatingTime(string raw)
        => string.IsNullOrWhiteSpace(raw)
           || raw.Replace("-", "").Replace("–", "").Replace("—", "").Trim().Length == 0;

    /// <summary>
    /// Parses "6 PM", "12 PM", "1 AM", "7 AM", "2 PM" etc. to <see cref="TimeOnly"/>.
    /// </summary>
    private static TimeOnly? ParseTime(string raw)
    {
        raw = raw.Trim();
        var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return null;

        if (!int.TryParse(tokens[0], out int hour)) return null;
        bool isPm = tokens[1].Equals("PM", StringComparison.OrdinalIgnoreCase);
        bool isAm = tokens[1].Equals("AM", StringComparison.OrdinalIgnoreCase);

        if (!isPm && !isAm) return null;

        // Convert 12-hour → 24-hour
        if (isPm && hour != 12) hour += 12;
        if (isAm && hour == 12) hour = 0;

        return new TimeOnly(hour, 0);
    }

    /// <summary>Adds fractional hours to a TimeOnly, wrapping at midnight.</summary>
    private static TimeOnly AddHours(TimeOnly start, float hours)
    {
        var totalMinutes = (int)(hours * 60);
        return start.AddMinutes(totalMinutes);
    }
}

// ── Result types ──────────────────────────────────────────────────────────────

public record ParsedCellResult(bool IsBreakDay, List<ParsedShiftBlock> Shifts)
{
    public static ParsedCellResult BreakDay()
        => new(IsBreakDay: true, Shifts: []);
}

public record ParsedShiftBlock(
    int ShiftIndex,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    float DurationHours,
    bool IsFloating,       // true when raw time was "--"
    string RawEntry
);