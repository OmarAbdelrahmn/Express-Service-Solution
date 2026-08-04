using Domain.Entities.Keeta;

namespace Application.Service.KeetaBreaks;

public sealed record SchedulerShift(string Key, int MinimumRiders, int MaximumRiders);
public sealed record SchedulerRider(string Identifier, string Name, string? HousingGroup, IReadOnlyList<string> Shifts);
public sealed record ExistingBreak(string RiderIdentifier, DateOnly Date);
public sealed record ScheduledBreak(string RiderIdentifier, DateOnly Date, IReadOnlyList<string> Shifts);
public sealed record RejectedRider(string RiderIdentifier, string Reason);
public sealed record ShiftCapacity(DateOnly Date, string Shift, int AssignedRiders, int ExistingBreaks, int PlannedBreaks, int Limit, int ActiveRiders);
public sealed record ScheduleResult(IReadOnlyList<ScheduledBreak> Assignments, IReadOnlyList<RejectedRider> Rejections, IReadOnlyList<ShiftCapacity> Capacities);

/// <summary>Deterministic whole-day scheduler. It deliberately has no EF or HTTP dependency.</summary>
public sealed class KeetaBreakScheduler
{
    public ScheduleResult Schedule(DateOnly start, DateOnly end, IReadOnlyList<SchedulerRider> riders, IReadOnlyList<SchedulerShift> definitions, decimal breakPercentage, KeetaBreakRoundingPolicy roundingPolicy, IReadOnlyList<ExistingBreak> existing)
    {
        var shifts = definitions.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var eligible = Dates(start, end).Where(IsEligible).ToArray();
        var existingByRiderMonth = existing.GroupBy(x => (x.RiderIdentifier, x.Date.Year, x.Date.Month)).ToDictionary(x => x.Key, x => x.Count());
        var existingByDateShift = new Dictionary<(DateOnly, string), int>();
        var assignments = new List<ScheduledBreak>();
        var rejections = new Dictionary<string, string>(StringComparer.Ordinal);
        var lastBreak = existing.GroupBy(x => x.RiderIdentifier).ToDictionary(x => x.Key, x => x.Max(y => y.Date), StringComparer.Ordinal);
        var riderById = riders.GroupBy(x => x.Identifier, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        foreach (var date in Dates(start, end))
        {
            var dateRiders = riders.Where(r => r.Shifts.Count > 0).ToArray();
            foreach (var shift in shifts.Values)
                existingByDateShift[(date, shift.Key)] = existing.Count(x => x.Date == date && riderById.TryGetValue(x.RiderIdentifier, out var rider) && rider.Shifts.Contains(shift.Key, StringComparer.Ordinal));

            if (!IsEligible(date))
            {
                foreach (var rider in dateRiders) rejections.TryAdd(rider.Identifier, "كل أيام الفترة محظورة");
                continue;
            }

            var assigned = dateRiders.GroupByManyShift();
            var candidates = dateRiders
                .Where(r => r.Shifts.All(shifts.ContainsKey))
                .Where(r => !existing.Any(x => x.RiderIdentifier == r.Identifier && x.Date == date))
                .Where(r => existingByRiderMonth.GetValueOrDefault((r.Identifier, date.Year, date.Month)) + assignments.Count(x => x.RiderIdentifier == r.Identifier && x.Date.Year == date.Year && x.Date.Month == date.Month) < 3)
                .OrderBy(r => existingByRiderMonth.GetValueOrDefault((r.Identifier, date.Year, date.Month)) + assignments.Count(x => x.RiderIdentifier == r.Identifier && x.Date.Year == date.Year && x.Date.Month == date.Month))
                .ThenBy(r => lastBreak.GetValueOrDefault(r.Identifier, DateOnly.MinValue))
                .ThenBy(r => assignments.Any(x => x.RiderIdentifier == r.Identifier && x.Date >= date.AddDays(-21)))
                .ThenBy(r => r.Shifts.Sum(s => assigned.GetValueOrDefault(s)))
                .ThenBy(r => r.Identifier, StringComparer.Ordinal)
                .ToArray();

            foreach (var rider in candidates)
            {
                var canAssign = rider.Shifts.All(shift =>
                {
                    var definition = shifts[shift];
                    var assignedCount = assigned.GetValueOrDefault(shift);
                    var percentage = Round(assignedCount * breakPercentage / 100m, roundingPolicy);
                    var limit = Math.Min(percentage, Math.Max(0, assignedCount - definition.MinimumRiders));
                    var used = existingByDateShift.GetValueOrDefault((date, shift)) + assignments.Count(x => x.Date == date && x.Shifts.Contains(shift, StringComparer.Ordinal));
                    return used < limit;
                });
                if (!canAssign) { rejections[rider.Identifier] = "لا توجد سعة في الشفت أو سيقل العدد عن الحد الأدنى"; continue; }
                assignments.Add(new ScheduledBreak(rider.Identifier, date, rider.Shifts));
                lastBreak[rider.Identifier] = date;
            }
        }

        foreach (var rider in riders)
        {
            if (assignments.All(x => x.RiderIdentifier != rider.Identifier) && !rejections.ContainsKey(rider.Identifier))
                rejections[rider.Identifier] = eligible.Length == 0 ? "كل أيام الفترة محظورة" : "لم يتم اختياره ضمن النسبة المتاحة";
        }
        var capacities = Dates(start, end).SelectMany(date => shifts.Values.Select(s =>
        {
            var count = riders.Count(r => r.Shifts.Contains(s.Key, StringComparer.Ordinal));
            var current = existingByDateShift.GetValueOrDefault((date, s.Key));
            var planned = assignments.Count(x => x.Date == date && x.Shifts.Contains(s.Key, StringComparer.Ordinal));
            var limit = Math.Min(Round(count * breakPercentage / 100m, roundingPolicy), Math.Max(0, count - s.MinimumRiders));
            return new ShiftCapacity(date, s.Key, count, current, planned, limit, count - current - planned);
        })).ToArray();
        return new ScheduleResult(assignments, rejections.Select(x => new RejectedRider(x.Key, x.Value)).ToArray(), capacities);
    }

    public static bool IsEligible(DateOnly date)
    {
        var last = DateTime.DaysInMonth(date.Year, date.Month);
        return date.Day > 3 && date.Day < last - 2 && date.DayOfWeek is not (DayOfWeek.Thursday or DayOfWeek.Friday or DayOfWeek.Saturday);
    }
    public static IEnumerable<DateOnly> Dates(DateOnly start, DateOnly end) { for (var d = start; d <= end; d = d.AddDays(1)) yield return d; }
    private static int Round(decimal value, KeetaBreakRoundingPolicy policy) => policy switch { KeetaBreakRoundingPolicy.Ceiling => (int)Math.Ceiling(value), KeetaBreakRoundingPolicy.Nearest => (int)Math.Round(value, MidpointRounding.AwayFromZero), _ => (int)Math.Floor(value) };
}

internal static class SchedulerExtensions
{
    public static Dictionary<string, int> GroupByManyShift(this IEnumerable<SchedulerRider> riders) => riders.SelectMany(x => x.Shifts).GroupBy(x => x, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
}
