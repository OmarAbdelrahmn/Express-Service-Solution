using Application.Abstraction;
using Application.Contracts.RiderMobile;
using Application.Service.KeetaBreaks;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.RiderMobile;

public partial class RiderMobileService
{
    private const int RequiredDays = 26;
    private const decimal DailyHours = 8m;

    public async Task<Result<RiderMobileDashboard>> GetDashboardAsync(int riderId, string language, CancellationToken ct = default)
    {
        var rider = await FindRiderAsync(riderId, ct);
        if (rider is null)
            return Result.Failure<RiderMobileDashboard>(Error("InvalidSession", language, 401));
        var today = Today;
        var start = new DateOnly(today.Year, today.Month, 1);
        var current = await BuildMonthAsync(riderId, language, start, today, ct);
        var previous = await BuildMonthAsync(riderId, language, start.AddMonths(-1), today, ct);
        return Result.Success(new RiderMobileDashboard(language, today, await ProfileAsync(rider, language, today, ct), current, previous));
    }

    public async Task<Result<RiderMobileMonth>> GetMonthAsync(int riderId, string language, int year, int month, CancellationToken ct = default)
    {
        var today = Today;
        if (year < 2000 || year > today.Year || month is < 1 or > 12 || (year == today.Year && month > today.Month))
            return Result.Failure<RiderMobileMonth>(Error("InvalidMonth", language, 400));
        if (!await ActiveRiders.AsNoTracking().AnyAsync(x => x.Id == riderId, ct))
            return Result.Failure<RiderMobileMonth>(Error("InvalidSession", language, 401));
        return Result.Success(await BuildMonthAsync(riderId, language, new DateOnly(year, month, 1), today, ct));
    }

    private async Task<RiderMobileMonth> BuildMonthAsync(int riderId, string language, DateOnly start, DateOnly today, CancellationToken ct)
    {
        var end = start.AddMonths(1).AddDays(-1);
        var asOf = end < today ? end : today;
        var shifts = await db.RiderShifts.AsNoTracking()
            .Where(x => x.RiderId == riderId && x.ShiftDate >= start && x.ShiftDate <= asOf)
            .ToListAsync(ct);
        var scores = await db.RiderScorePerformances.AsNoTracking()
            .Where(x => x.RiderId == riderId && x.PerformanceDate >= start && x.PerformanceDate <= asOf)
            .OrderBy(x => x.PerformanceDate).ToListAsync(ct);

        var attendance = Attendance(shifts, start, end, today, language);
        var performance = Performance(scores);
        var notes = PerformanceNotes(performance, language);
        if (attendance.RemainingQualifiedDays > 0)
            notes.Add(Note(language, "WorkingDays", end < today ? "problem" : "progress",
                attendance.QualifiedWorkingDays, RequiredDays));
        if (attendance.MissingCompletedDays > 0)
            notes.Add(Note(language, "MissingData", "info", attendance.MissingCompletedDays));
        if (!attendance.CanReachMonthlyTarget)
            notes.Add(Note(language, "Unreachable", "problem"));
        if (attendance.RecordedDaysOff > attendance.MaximumBreakDays)
            notes.Add(Note(language, "BreakLimit", "problem", attendance.RecordedDaysOff, attendance.MaximumBreakDays));
        // Date-specific notes retain their date in the monthly improvement list.
        notes.AddRange(attendance.Days.SelectMany(x => x.Notes));

        return new RiderMobileMonth(start.Year, start.Month,
            RiderMobileText.Culture(language).DateTimeFormat.GetMonthName(start.Month), language,
            start, end, asOf, start.Year == today.Year && start.Month == today.Month,
            new RiderMobileTargets(settings.OnTimeDeliveryTargetPercentage,
                settings.VerificationTargetPercentage, settings.DeliveryQualityTargetPercentage),
            performance, attendance, notes);
    }

    private static RiderMobilePerformance Performance(IReadOnlyList<Domain.Entities.RiderScorePerformance> scores)
    {
        var latest = scores.LastOrDefault();
        var gross = scores.Sum(x => (long)x.GrossOrders);
        var completed = scores.Sum(x => (long)x.CompletedOrders);
        var inTime = scores.Sum(x => (long)x.CompletedOrdersInTime);
        var requests = scores.Sum(x => (long)x.TotalVerificationRequests);
        var verified = scores.Sum(x => (long)x.SuccessfulVerificationRequests);
        // Imports save daily counter deltas, but scores and rank are cumulative platform snapshots.
        return new RiderMobilePerformance(scores.Count, latest?.PerformanceDate, latest?.Segment,
            "latest_imported_segment", gross, completed, inTime,
            scores.Sum(x => (long)x.FailedOrdersByRider), requests, verified,
            Percentage(inTime, completed), Percentage(verified, requests), Percentage(completed, gross),
            latest is null ? null : decimal.Round(latest.OnTimeDeliveryScore * 100, 2),
            latest is null ? null : decimal.Round(latest.FinalDeliveryQualityScore * 100, 2));
    }

    private List<RiderMobileNote> PerformanceNotes(RiderMobilePerformance performance, string language)
    {
        List<RiderMobileNote> notes = [];
        if (performance.RecordCount == 0)
        {
            notes.Add(Note(language, "NoPerformance", "info"));
            return notes;
        }
        AddMetric("OnTime", performance.OnTimeDeliveryPercentage, settings.OnTimeDeliveryTargetPercentage);
        AddMetric("Verification", performance.VerificationSuccessPercentage, settings.VerificationTargetPercentage);
        AddMetric("Quality", performance.DeliveryQualityPercentage, settings.DeliveryQualityTargetPercentage);
        if (performance.FailedOrdersByRider > 0)
            notes.Add(Note(language, "FailedOrders", "problem", performance.FailedOrdersByRider, 0));
        var rank = performance.Rank?.Trim().ToUpperInvariant();
        if (rank == "A") notes.Add(Note(language, "MaintainA", "info"));
        else if (rank is "B" or "C") notes.Add(Note(language, "RankBelowA", "problem"));
        return notes;

        void AddMetric(string code, decimal? actual, decimal target)
        {
            if (actual.HasValue && actual < target)
                notes.Add(Note(language, code, "problem", actual, target));
        }
    }

    private static RiderMobileAttendance Attendance(IReadOnlyList<RiderShift> shifts,
        DateOnly start, DateOnly end, DateOnly today, string language)
    {
        var byDate = shifts.GroupBy(x => x.ShiftDate).ToDictionary(x => x.Key, x => x.ToList());
        List<RiderMobileDay> days = [];
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var records = byDate.GetValueOrDefault(date) ?? [];
            var hasData = records.Count > 0;
            // Sum across working-ID changes and count each calendar day once.
            var recordedHours = records.Sum(x => (decimal)(double)x.WorkingHours);
            var hours = decimal.Round(recordedHours, 4);
            var accepted = records.Sum(x => (long)x.AcceptedDailyOrders);
            var rejected = records.Sum(x => (long)x.RejectedDailyOrders);
            var realRejected = records.Sum(x => (long)x.RealRejectedDailyOrders);
            var stacked = records.Sum(x => (long)x.StackedDeliveries);
            var hasActivity = hours > 0 || accepted > 0 || rejected > 0 || realRejected > 0 || stacked > 0;
            var isOff = hasData && !hasActivity;
            var qualified = recordedHours >= DailyHours;
            var restrictions = BreakRestrictions(date);
            var status = date > today ? "Future" : date == today && !qualified ? "InProgress" :
                !hasData ? "Missing" : isOff ? "DayOff" : qualified ? "Qualified" : "ShortHours";
            List<RiderMobileNote> notes = [];
            if (date < today && hasActivity && !qualified)
                notes.Add(Note(language, "DailyHours", "problem", hours, DailyHours, date));
            if (date < today && isOff && !KeetaBreakScheduler.IsEligible(date))
                notes.Add(Note(language, "ProhibitedBreak", "problem", date: date));
            days.Add(new RiderMobileDay(date, RiderMobileText.Culture(language).DateTimeFormat.GetDayName(date.DayOfWeek),
                status, RiderMobileText.Get(language, status), hasData, hours, accepted, rejected, realRejected,
                stacked, qualified, isOff, KeetaBreakScheduler.IsEligible(date), restrictions,
                restrictions.Select(code => RiderMobileText.Get(language, code)).ToList(), notes));
        }

        var qualifiedDays = days.Count(x => x.MeetsHoursTarget);
        var workedDays = days.Count(x => x.HasShiftData && !x.IsRecordedDayOff);
        var missing = days.Count(x => x.Date < today && !x.HasShiftData);
        var shortDays = days.Count(x => x.Date < today && x.HasShiftData && !x.IsRecordedDayOff && !x.MeetsHoursTarget);
        var offDays = days.Count(x => x.Date < today && x.IsRecordedDayOff);
        var prohibited = days.Count(x => x.Date < today && x.IsRecordedDayOff && !x.IsBreakDateAllowed);
        var remaining = days.Count(x => x.Date >= today && !x.MeetsHoursTarget);
        var completedDays = days.Count(x => x.Date < today);
        var expected = Math.Max(0, completedDays - (days.Count - RequiredDays));
        var maximumBreaks = Math.Min(3, days.Count - RequiredDays);
        var met = qualifiedDays >= RequiredDays;
        bool? compliant = end >= today || missing > 0 ? null :
            met && shortDays == 0 && prohibited == 0 && offDays <= maximumBreaks;
        return new RiderMobileAttendance(RequiredDays, DailyHours, workedDays, qualifiedDays,
            Math.Max(0, RequiredDays - qualifiedDays), expected, Math.Max(0, expected - qualifiedDays),
            remaining, qualifiedDays + remaining >= RequiredDays, days.Sum(x => x.WorkingHours),
            days.Sum(x => x.AcceptedOrders), days.Sum(x => x.RejectedOrders), shortDays, offDays,
            maximumBreaks, Math.Max(0, maximumBreaks - offDays), prohibited, missing, met, compliant, days);
    }

    private static IReadOnlyList<string> BreakRestrictions(DateOnly date)
    {
        List<string> reasons = [];
        if (date.DayOfWeek is DayOfWeek.Thursday or DayOfWeek.Friday or DayOfWeek.Saturday)
            reasons.Add("RestrictedWeekday");
        if (date.Day <= 3) reasons.Add("FirstThreeDays");
        if (date.Day > DateTime.DaysInMonth(date.Year, date.Month) - 3) reasons.Add("LastThreeDays");
        return reasons;
    }

    private static decimal? Percentage(long numerator, long denominator) => denominator == 0
        ? null : decimal.Round(100m * numerator / denominator, 2);

    private static RiderMobileNote Note(string language, string code, string severity,
        decimal? actual = null, decimal? target = null, DateOnly? date = null) => new(
            code, severity, RiderMobileText.Get(language, code, actual ?? 0, target ?? 0),
            RiderMobileText.Get(language, code + "Advice"), actual, target,
            actual.HasValue && target.HasValue ? Math.Abs(target.Value - actual.Value) : null, date);
}
