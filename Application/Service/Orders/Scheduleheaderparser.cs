using System.Globalization;

namespace Application.Service.TransporterShifts;

/// <summary>
/// Converts Excel column-header strings such as
///   "Sun, 03/May"   →  DateOnly(currentYear, 5, 3)
///   "Sat, 09/May"   →  DateOnly(currentYear, 5, 9)
///   "Mon, 04/May"   →  DateOnly(currentYear, 5, 4)
///
/// The year is resolved as follows:
///   1. Use <paramref name="overrideYear"/> when supplied.
///   2. Otherwise use the current Saudi time year (UTC+3).
///
/// Edge case: if the parsed month is January and we are in December,
/// the year is bumped by 1 (schedule planning crosses year boundary).
/// </summary>
public static class ScheduleHeaderParser
{
    private static readonly Dictionary<string, int> MonthMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Jan"] = 1,
        ["Feb"] = 2,
        ["Mar"] = 3,
        ["Apr"] = 4,
        ["May"] = 5,
        ["Jun"] = 6,
        ["Jul"] = 7,
        ["Aug"] = 8,
        ["Sep"] = 9,
        ["Oct"] = 10,
        ["Nov"] = 11,
        ["Dec"] = 12
    };

    /// <summary>
    /// Parse a header like "Sun, 03/May" into a <see cref="DateOnly"/>.
    /// Returns null on failure.
    /// </summary>
    public static DateOnly? Parse(string header, int? overrideYear = null)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;

        // Strip day-of-week prefix: "Sun, 03/May" → "03/May"
        var datePart = header.Contains(',')
            ? header[(header.IndexOf(',') + 1)..].Trim()
            : header.Trim();

        // Split "03/May" → ["03", "May"]
        var segments = datePart.Split('/');
        if (segments.Length != 2) return null;

        if (!int.TryParse(segments[0].Trim(), out int day)) return null;
        if (!MonthMap.TryGetValue(segments[1].Trim(), out int month)) return null;

        var now = DateTime.UtcNow.AddHours(3);
        int year = overrideYear ?? now.Year;

        // Cross-year guard: schedule shows Jan but current month is Dec
        if (month == 1 && now.Month == 12 && overrideYear is null)
            year = now.Year + 1;

        try
        {
            return new DateOnly(year, month, day);
        }
        catch
        {
            return null;
        }
    }
}