using Application.Abstraction;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.HungerReports;

/// <summary>
/// Hunger (Company 1) monthly rider-validation report.
///
/// ┌─────────────────────────────────────────────────────────────────┐
/// │  VALIDATION RULES                                               │
/// ├─────────────────────────────────────────────────────────────────┤
/// │  Min shift hours      8 h  → below 8 h = missing day           │
/// │  Target work-days     26   (proportional, floor-based)          │
/// │  Monthly order target 450  (proportional, floor-based)          │
/// │  Critical weekdays    Thursday / Friday / Saturday              │
/// │  Critical window      last 5 calendar days of the month         │
/// │                                                                 │
/// │  A critical day where the rider is absent OR works < 8 h       │
/// │  is an automatic validation failure regardless of totals.       │
/// │                                                                 │
/// │  PROPORTIONAL FORMULA (floor — "allowed absences" model):       │
/// │    RequiredWorkingDays = floor(day / lastDay × 26)              │
/// │    RequiredOrders      = floor(day / lastDay × 450)             │
/// └─────────────────────────────────────────────────────────────────┘
/// </summary>
public class HungerReportService(ApplicationDbcontext dbcontext) : IHungerReportService
{
    // ── Constants ────────────────────────────────────────────────────────────
    private const int HUNGER_COMPANY_ID = 1;
    private const float MIN_HOURS_PER_DAY = 8f;
    private const int TARGET_WORKING_DAYS = 26;
    private const int MONTHLY_ORDER_TARGET = 450;
    private const int LAST_CRITICAL_DAYS_COUNT = 5;

    private const decimal DAILY_ORDER_RATE =
        (decimal)MONTHLY_ORDER_TARGET / TARGET_WORKING_DAYS; // ≈17.3

    // ═══════════════════════════════════════════════════════════════════════
    // PUBLIC ENTRY POINT
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<Result<HungerMonthlyValidationReport>> GetHungerMonthlyRiderValidationAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (month < 1 || month > 12)
            return Result.Failure<HungerMonthlyValidationReport>(
                new Error("Month must be between 1 and 12", "invalid_input", 400));

        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
            var yesterday = today.AddDays(-1);
            var monthStart = new DateOnly(year, month, 1);
            var lastDayOfMonth = monthStart.AddMonths(1).AddDays(-1).Day;

            var isCurrentMonth = year == today.Year && month == today.Month;

            // For the current month evaluate up to yesterday; for past months use the full month.
            var endDate = isCurrentMonth
                ? (yesterday < monthStart ? monthStart : yesterday)
                : new DateOnly(year, month, lastDayOfMonth);

            var currentDay = endDate.Day; // "how far into the month we are"

            // ── Targets ──────────────────────────────────────────────────────
            //
            // WORKING DAYS — flat-allowance model:
            //   allowedAbsences     = lastDayOfMonth − TARGET_WORKING_DAYS   (e.g. 30−26 = 4)
            //   requiredWorkingDays = max(0, currentDay − allowedAbsences)
            //
            //   Rationale: the rider gets the same 4 absent-day "budget" for the
            //   whole month regardless of when we evaluate. So on day 10 of a
            //   30-day month, required = max(0, 10−4) = 6. A rider with 7 valid
            //   days has only used 3 absences and is still on track → valid.
            //
            //   For a PAST (complete) month: currentDay = lastDayOfMonth, so
            //   required = lastDayOfMonth − allowedAbsences = TARGET_WORKING_DAYS = 26.
            //
            // ORDERS — proportional floor-based (no natural "absence budget"):
            //   requiredOrders = floor(currentDay / lastDayOfMonth × 450)
            //
            var allowedAbsences = lastDayOfMonth - TARGET_WORKING_DAYS;  // e.g. 4 for 30-day month
            var requiredWorkingDays = Math.Max(0, currentDay - allowedAbsences);

            var requiredOrders = (int)Math.Floor(
                (decimal)currentDay / lastDayOfMonth * MONTHLY_ORDER_TARGET);

            // Safety: always require at least 1 order if any days have been evaluated
            if (currentDay > 0)
                requiredOrders = Math.Max(1, requiredOrders);

            // ── Load shifts ──────────────────────────────────────────────────
            var shifts = await dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                        .ThenInclude(e => e.Housing)
                .Where(s => s.CompanyId == HUNGER_COMPANY_ID
                         && s.ShiftDate >= monthStart
                         && s.ShiftDate <= endDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
                return Result.Failure<HungerMonthlyValidationReport>(
                    new Error(
                        $"No shifts found for Hunger in {year}-{month:D2}",
                        "no_data", 404));

            // ── Validate each rider ──────────────────────────────────────────
            var riderGroups = shifts.GroupBy(s => s.RiderId);
            var validations = new List<HungerRiderMonthlyValidation>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var workedPreviousMonth = await DidRiderWorkPreviousMonthAsync(
                    rider.Id, monthStart, cancellationToken);

                var validation = ValidateRider(
                    rider,
                    group.OrderBy(s => s.ShiftDate).ToList(),
                    year, month,
                    monthStart, endDate,
                    currentDay, lastDayOfMonth,
                    requiredWorkingDays, requiredOrders,
                    workedPreviousMonth);

                validations.Add(validation);
            }

            // Sort: valid first, then by orders desc
            var sorted = validations
                .OrderByDescending(r => r.IsValidForMonth)
                .ThenByDescending(r => r.TotalOrders)
                .ThenBy(r => r.MissingDaysCount)
                .ToList();

            var report = new HungerMonthlyValidationReport(
                Year: year,
                Month: month,
                StartDate: monthStart,
                EndDate: endDate,
                IsCurrentMonth: isCurrentMonth,
                CurrentDay: currentDay,
                LastDayOfMonth: lastDayOfMonth,
                TotalCalendarDays: currentDay,
                RequiredWorkingDays: requiredWorkingDays,
                RequiredOrders: requiredOrders,
                TotalRiders: sorted.Count,
                ValidRiders: sorted.Count(r => r.IsValidForMonth),
                InvalidRiders: sorted.Count(r => !r.IsValidForMonth),
                RiderValidations: sorted
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<HungerMonthlyValidationReport>(
                new Error(
                    $"Error generating Hunger monthly validation report: {ex.Message}",
                    "server_error", 500));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CORE PER-RIDER VALIDATION
    // ═══════════════════════════════════════════════════════════════════════

    private HungerRiderMonthlyValidation ValidateRider(
        RiderDetails rider,
        List<RiderShift> riderShifts,
        int year,
        int month,
        DateOnly monthStart,
        DateOnly endDate,
        int currentDay,
        int lastDayOfMonth,
        int requiredWorkingDays,
        int requiredOrders,
        bool workedPreviousMonth)
    {
        // ── Determine effective start ────────────────────────────────────────
        var (effectiveStart, isNewRider) = DetermineEffectiveStart(
            riderShifts, monthStart, workedPreviousMonth);

        // New riders who started after day 1 get scaled-down targets.
        int adjustedRequiredWorkingDays;
        int adjustedRequiredOrders;

        if (isNewRider && effectiveStart > monthStart)
        {
            var daysForRider = endDate.DayNumber - effectiveStart.DayNumber + 1;
            var daysInEvaluated = endDate.DayNumber - monthStart.DayNumber + 1;

            // For new riders apply the same flat-allowance logic scoped to their
            // active window: allowedAbsences scales proportionally with how many
            // days of the month they are responsible for.
            var allowedAbsencesForRider = daysInEvaluated > 0
                ? (int)Math.Floor((decimal)daysForRider / daysInEvaluated * (lastDayOfMonth - TARGET_WORKING_DAYS))
                : (lastDayOfMonth - TARGET_WORKING_DAYS);

            adjustedRequiredWorkingDays = Math.Max(0, daysForRider - allowedAbsencesForRider);

            adjustedRequiredOrders = daysInEvaluated > 0
                ? Math.Max(1, (int)Math.Floor((decimal)daysForRider / daysInEvaluated * requiredOrders))
                : requiredOrders;
        }
        else
        {
            // Continuing rider – held to the full proportional target.
            adjustedRequiredWorkingDays = requiredWorkingDays;
            adjustedRequiredOrders = requiredOrders;
        }

        var totalExpectedDays = endDate.DayNumber - effectiveStart.DayNumber + 1;

        // ── Day-by-day loop ─────────────────────────────────────────────────
        var shiftByDate = riderShifts.ToDictionary(s => s.ShiftDate);
        var missingDays = new List<int>(); // all absent days (including low-hour)
        var lowHoursDays = new List<int>(); // shift existed but hours < 8
        var violatedCriticalDays = new List<int>(); // critical days missed or under-hours
        var dailyDetails = new List<HungerDailyValidationDetail>();
        var validWorkingDays = 0;

        for (var date = effectiveStart; date <= endDate; date = date.AddDays(1))
        {
            var dayNum = date.Day;
            var isCritical = IsCriticalDay(date, lastDayOfMonth);
            var critReason = GetCriticalDayReason(date, lastDayOfMonth);

            if (shiftByDate.TryGetValue(date, out var shift))
            {
                if (shift.WorkingHours < MIN_HOURS_PER_DAY)
                {
                    // Shift exists but hours are insufficient → treated as missing
                    lowHoursDays.Add(dayNum);
                    missingDays.Add(dayNum);
                    if (isCritical) violatedCriticalDays.Add(dayNum);

                    dailyDetails.Add(new HungerDailyValidationDetail(
                        Day: dayNum,
                        Date: date,
                        HasShift: true,
                        WorkingHours: shift.WorkingHours,
                        AcceptedOrders: shift.AcceptedDailyOrders,
                        IsValidWorkingDay: false,
                        IsCriticalDay: isCritical,
                        CriticalDayReason: critReason,
                        Reason: isCritical
                            ? $"❌ {critReason} (حرج) – ساعات ({shift.WorkingHours:F1}h) أقل من {MIN_HOURS_PER_DAY}h"
                            : $"⚠️ ساعات ({shift.WorkingHours:F1}h) أقل من {MIN_HOURS_PER_DAY}h – يُحتسب غياباً"
                    ));
                }
                else
                {
                    // Valid working day
                    validWorkingDays++;

                    dailyDetails.Add(new HungerDailyValidationDetail(
                        Day: dayNum,
                        Date: date,
                        HasShift: true,
                        WorkingHours: shift.WorkingHours,
                        AcceptedOrders: shift.AcceptedDailyOrders,
                        IsValidWorkingDay: true,
                        IsCriticalDay: isCritical,
                        CriticalDayReason: critReason,
                        Reason: isCritical ? $"✅ صالح ({critReason})" : "✅ صالح"
                    ));
                }
            }
            else
            {
                // No shift at all → missing
                missingDays.Add(dayNum);
                if (isCritical) violatedCriticalDays.Add(dayNum);

                dailyDetails.Add(new HungerDailyValidationDetail(
                    Day: dayNum,
                    Date: date,
                    HasShift: false,
                    WorkingHours: 0,
                    AcceptedOrders: 0,
                    IsValidWorkingDay: false,
                    IsCriticalDay: isCritical,
                    CriticalDayReason: critReason,
                    Reason: isCritical
                        ? $"❌ غياب في يوم حرج ({critReason})"
                        : "لا يوجد دوام"
                ));
            }
        }

        var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders);
        var totalHours = riderShifts.Sum(s => s.WorkingHours);
        var avgHours = validWorkingDays > 0 ? totalHours / validWorkingDays : 0f;
        var workingDaysDeficit = Math.Max(0, adjustedRequiredWorkingDays - validWorkingDays);
        var ordersDeficit = totalOrders - adjustedRequiredOrders;

        // ── Percentages ─────────────────────────────────────────────────────
        //
        //  DaysPercentage  = min(100, validWorkingDays / adjustedRequiredWorkingDays × 100)
        //  OrdersPercentage = min(100, totalOrders / adjustedRequiredOrders × 100)
        //  PerformancePercentage = average of both (still ≤ 100 since each component is)
        //
        var daysPercentage = adjustedRequiredWorkingDays > 0
            ? Math.Round(Math.Min(100m, (decimal)validWorkingDays / adjustedRequiredWorkingDays * 100m), 2)
            : 100m;

        var ordersPercentage = adjustedRequiredOrders > 0
            ? Math.Round(Math.Min(100m, (decimal)totalOrders / adjustedRequiredOrders * 100m), 2)
            : 100m;

        var performancePercentage = Math.Round((daysPercentage + ordersPercentage) / 2m, 2);

        // ── Apply validation rules ───────────────────────────────────────────
        var (isValid, errors) = ApplyValidationRules(
            validWorkingDays: validWorkingDays,
            missingDays: missingDays,
            lowHoursDays: lowHoursDays,
            violatedCriticalDays: violatedCriticalDays,
            totalOrders: totalOrders,
            adjustedRequiredWorkingDays: adjustedRequiredWorkingDays,
            adjustedRequiredOrders: adjustedRequiredOrders,
            isNewRider: isNewRider,
            effectiveStartDay: effectiveStart.Day,
            workedPreviousMonth: workedPreviousMonth,
            totalExpectedDays: totalExpectedDays,
            currentDay: currentDay,
            lastDayOfMonth: lastDayOfMonth);

        return new HungerRiderMonthlyValidation(
            HousingName: rider.Employee.Housing?.Name ?? "غير محدد",
            RiderId: rider.Id,
            IqamaNo: rider.EmployeeIqamaNo,
            RiderNameAR: rider.Employee.NameAR,
            RiderNameEN: rider.Employee.NameEN,
            WorkingId: rider.WorkingId ?? "0",
            TotalExpectedDays: totalExpectedDays,
            TotalValidWorkingDays: validWorkingDays,
            RequiredWorkingDays: adjustedRequiredWorkingDays,   // ← rider-specific target
            MissingDaysCount: missingDays.Count,
            MissingDaysList: missingDays,
            DaysWithLessThan8Hours: lowHoursDays,
            ViolatedCriticalDays: violatedCriticalDays,
            TotalOrders: totalOrders,
            RequiredOrders: adjustedRequiredOrders,
            OrdersDeficit: ordersDeficit,
            TotalWorkingHours: totalHours,
            AverageHoursPerValidDay: avgHours,
            WorkingDaysDeficit: workingDaysDeficit,
            IsValidForMonth: isValid,
            StatusLabel: isValid ? "صالح" : "غير صالح",  // ← replaces "غير مستحق"
            IsNewRider: isNewRider,
            EffectiveStartDate: effectiveStart,
            ValidationErrors: errors,
            DailyDetails: dailyDetails,
            DaysPercentage: daysPercentage,
            OrdersPercentage: ordersPercentage,
            PerformancePercentage: performancePercentage
        );
    }

    // ═══════════════════════════════════════════════════════════════════════
    // VALIDATION RULE ENGINE
    // ═══════════════════════════════════════════════════════════════════════

    private static (bool isValid, List<string> errors) ApplyValidationRules(
        int validWorkingDays,
        List<int> missingDays,
        List<int> lowHoursDays,
        List<int> violatedCriticalDays,
        int totalOrders,
        int adjustedRequiredWorkingDays,
        int adjustedRequiredOrders,
        bool isNewRider,
        int effectiveStartDay,
        bool workedPreviousMonth,
        int totalExpectedDays,
        int currentDay,
        int lastDayOfMonth)
    {
        var isValid = true;
        var errors = new List<string>();

        // ── Rider-type info line ─────────────────────────────────────────────
        if (!isNewRider || workedPreviousMonth)
            errors.Add("ℹ️ موظف مستمر (متوقع العمل من اليوم الأول)");
        else if (effectiveStartDay > 1)
            errors.Add($"ℹ️ موظف جديد – بدأ يوم {effectiveStartDay} " +
                       $"(الأيام المتوقعة: {totalExpectedDays}، " +
                       $"هدف أيام العمل المعدّل: {adjustedRequiredWorkingDays}، " +
                       $"هدف الطلبات المعدّل: {adjustedRequiredOrders})");

        // ── Rule 1: Violated critical days ──────────────────────────────────
        if (violatedCriticalDays.Any())
        {
            isValid = false;
            errors.Add(
                $"❌ غياب أو ساعات ناقصة في أيام حرجة (خميس / جمعة / سبت / آخر 5 أيام): " +
                $"الأيام {string.Join(", ", violatedCriticalDays)}");
        }

        // ── Rule 2: Insufficient valid working days ──────────────────────────
        if (validWorkingDays < adjustedRequiredWorkingDays)
        {
            isValid = false;
            errors.Add(
                $"❌ أيام العمل الصالحة غير كافية: {validWorkingDays} " +
                $"(المطلوب: {adjustedRequiredWorkingDays}، " +
                $"النقص: {adjustedRequiredWorkingDays - validWorkingDays})");
        }

        // ── Rule 3: Insufficient total orders ───────────────────────────────
        if (totalOrders < adjustedRequiredOrders)
        {
            isValid = false;
            errors.Add(
                $"❌ عدد الطلبات غير كافٍ: {totalOrders} " +
                $"(المطلوب: {adjustedRequiredOrders}، " +
                $"النقص: {adjustedRequiredOrders - totalOrders}، " +
                $"المعدل اليومي المستهدف: ~{DAILY_ORDER_RATE:F1} طلب/يوم)");
        }

        // ── Informational: days with low hours ───────────────────────────────
        if (lowHoursDays.Any())
            errors.Add(
                $"⚠️ أيام عمل بأقل من {MIN_HOURS_PER_DAY}h (تُحتسب غياباً): " +
                $"الأيام {string.Join(", ", lowHoursDays)}");

        // ── Informational: regular missing days (no shift, non-critical) ─────
        var regularMissing = missingDays
            .Except(lowHoursDays)
            .Except(violatedCriticalDays)
            .OrderBy(d => d)
            .ToList();

        if (regularMissing.Any())
            errors.Add($"⚠️ أيام بدون دوام: {string.Join(", ", regularMissing)}");

        // ── All rules passed ─────────────────────────────────────────────────
        var infoOnly = errors.All(e => e.StartsWith("ℹ️"));
        if (isValid && infoOnly)
            errors.Add("✅ جميع شروط التحقق مستوفاة");

        return (isValid, errors);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns true when the given calendar date is a "critical" day for Hunger:
    ///   • Thursday, Friday, or Saturday
    ///   • One of the last 5 calendar days of the month
    /// </summary>
    private static bool IsCriticalDay(DateOnly date, int lastDayOfMonth)
    {
        if (date.DayOfWeek is DayOfWeek.Thursday or DayOfWeek.Friday or DayOfWeek.Saturday)
            return true;

        var lastCriticalStart = lastDayOfMonth - LAST_CRITICAL_DAYS_COUNT + 1;
        return date.Day >= lastCriticalStart && date.Day <= lastDayOfMonth;
    }

    /// <summary>Human-readable Arabic label for why a day is critical.</summary>
    private static string GetCriticalDayReason(DateOnly date, int lastDayOfMonth)
    {
        var reasons = new List<string>();

        if (date.DayOfWeek == DayOfWeek.Thursday) reasons.Add("خميس");
        if (date.DayOfWeek == DayOfWeek.Friday) reasons.Add("جمعة");
        if (date.DayOfWeek == DayOfWeek.Saturday) reasons.Add("سبت");

        var lastCriticalStart = lastDayOfMonth - LAST_CRITICAL_DAYS_COUNT + 1;
        if (date.Day >= lastCriticalStart) reasons.Add("آخر 5 أيام");

        return reasons.Any() ? string.Join(" / ", reasons) : string.Empty;
    }

    /// <summary>
    /// Determines the date from which the rider is expected to work this month
    /// and whether they are a "new" rider.
    ///
    /// Continuing rider (worked previous month or had a shift on/before day 1):
    ///   → accountable from day 1 of the current month.
    /// New rider (first month): → accountable from their first shift date onward.
    /// </summary>
    private static (DateOnly effectiveStart, bool isNewRider) DetermineEffectiveStart(
        List<RiderShift> riderShifts,
        DateOnly monthStart,
        bool workedPreviousMonth)
    {
        if (!riderShifts.Any())
            return (monthStart, !workedPreviousMonth);

        var firstShiftDate = riderShifts.Min(s => s.ShiftDate);

        if (workedPreviousMonth)
            return (monthStart, false);

        if (firstShiftDate <= monthStart)
            return (monthStart, false);

        return (firstShiftDate, true);
    }

    /// <summary>
    /// Returns true when the rider had at least one shift for Hunger in the
    /// calendar month immediately before <paramref name="currentMonthStart"/>.
    /// </summary>
    private async Task<bool> DidRiderWorkPreviousMonthAsync(
        int riderId,
        DateOnly currentMonthStart,
        CancellationToken cancellationToken)
    {
        var prevStart = currentMonthStart.AddMonths(-1);
        var prevEnd = currentMonthStart.AddDays(-1);

        return await dbcontext.RiderShifts.AnyAsync(
            s => s.RiderId == riderId
              && s.CompanyId == HUNGER_COMPANY_ID
              && s.ShiftDate >= prevStart
              && s.ShiftDate <= prevEnd,
            cancellationToken);
    }
}