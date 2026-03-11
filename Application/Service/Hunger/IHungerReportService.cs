using Application.Abstraction;

namespace Application.Service.HungerReports;

/// <summary>
/// Hunger (Company 1) rider-shift reporting service.
/// Validation rules:
///   - Min shift hours  : 8 h  (below → day treated as missing)
///   - Target work-days : 26 per full month  (proportional for partial month)
///   - Monthly orders   : 450  (proportional for partial month)
///   - Critical weekdays: Thursday, Friday, Saturday
///   - Critical window  : last 5 calendar days of the month
///   Critical days must NOT be absent and must NOT have &lt; 8 working hours.
///
/// Proportional formula — flat-allowance "absence budget" model:
///   allowedAbsences     = lastDayOfMonth − 26          (e.g. 4 for a 30-day month)
///   RequiredWorkingDays = max(0, currentDay − allowedAbsences)
///   RequiredOrders      = floor(currentDay / lastDayOfMonth × 450)
///
///   Rationale: the rider's 4-day absence budget is fixed for the whole month.
///   On day 10 of a 30-day month: required = max(0,10−4) = 6 working days.
///   A rider with 7 valid days has used only 3 absences → still on track → valid.
///   On the last day of the month: required = 30−4 = 26 = TARGET_WORKING_DAYS. ✓
/// </summary>
public interface IHungerReportService
{
    Task<Result<HungerMonthlyValidationReport>> GetHungerMonthlyRiderValidationAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default);
}

// ═══════════════════════════════════════════════════════════════════════
// TOP-LEVEL REPORT
// ═══════════════════════════════════════════════════════════════════════

/// <summary>Full monthly validation report for all Hunger riders.</summary>
public record HungerMonthlyValidationReport(
    int Year,
    int Month,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsCurrentMonth,

    /// <summary>
    /// For the current month: yesterday's day number (last fully-evaluated day).
    /// For past months: LastDayOfMonth.
    /// </summary>
    int CurrentDay,

    int LastDayOfMonth,

    /// <summary>Calendar days evaluated (= CurrentDay).</summary>
    int TotalCalendarDays,

    /// <summary>
    /// Global required working days for a rider active from day 1.
    /// Formula: max(0, CurrentDay − (LastDayOfMonth − 26)).
    /// E.g. day 10 of a 30-day month → max(0, 10−4) = 6.
    /// NOTE: individual riders may have a different adjusted target — always
    /// display HungerRiderMonthlyValidation.RequiredWorkingDays in the UI.
    /// </summary>
    int RequiredWorkingDays,

    /// <summary>
    /// Global proportional order target.
    /// Formula: floor(CurrentDay / LastDayOfMonth × 450).
    /// </summary>
    int RequiredOrders,

    int TotalRiders,
    int ValidRiders,
    int InvalidRiders,
    List<HungerRiderMonthlyValidation> RiderValidations
);

// ═══════════════════════════════════════════════════════════════════════
// PER-RIDER RECORD
// ═══════════════════════════════════════════════════════════════════════

public record HungerRiderMonthlyValidation(
    string HousingName,
    int RiderId,
    long IqamaNo,
    string RiderNameAR,
    string RiderNameEN,
    string WorkingId,

    /// <summary>Calendar days from the rider's effective start date to EndDate (inclusive).</summary>
    int TotalExpectedDays,

    /// <summary>Days where the rider had a shift with ≥ 8 working hours.</summary>
    int TotalValidWorkingDays,

    /// <summary>
    /// The actual working-days target applied to THIS rider during validation.
    /// May differ from the global RequiredWorkingDays for new riders.
    /// Always use this value in the UI — not the global report-level target.
    /// </summary>
    int RequiredWorkingDays,

    /// <summary>Days with no shift OR a shift with &lt; 8 hours (both count as missing).</summary>
    int MissingDaysCount,
    List<int> MissingDaysList,

    /// <summary>Days where a shift existed but hours were below the 8-hour minimum.</summary>
    List<int> DaysWithLessThan8Hours,

    /// <summary>Critical days (Thu / Fri / Sat / last-5) that were absent or under-hours.</summary>
    List<int> ViolatedCriticalDays,

    int TotalOrders,

    /// <summary>
    /// The actual order target applied to THIS rider during validation.
    /// May differ from the global RequiredOrders for new riders.
    /// </summary>
    int RequiredOrders,

    int OrdersDeficit,

    float TotalWorkingHours,
    float AverageHoursPerValidDay,

    /// <summary>How many valid days are still needed to reach RequiredWorkingDays. 0 if already met.</summary>
    int WorkingDaysDeficit,

    bool IsValidForMonth,

    /// <summary>
    /// Arabic status label derived from IsValidForMonth.
    /// "صالح" when the rider meets all targets; "غير صالح" otherwise.
    /// Use this instead of hardcoding labels in the frontend.
    /// </summary>
    string StatusLabel,

    bool IsNewRider,
    DateOnly EffectiveStartDate,
    List<string> ValidationErrors,
    List<HungerDailyValidationDetail> DailyDetails,

    /// <summary>
    /// Working-days completion percentage, capped at 100%.
    /// Formula: min(100, TotalValidWorkingDays / RequiredWorkingDays × 100)
    /// Example: 8 valid / 8 required → 100 %
    /// </summary>
    decimal DaysPercentage,

    /// <summary>
    /// Orders completion percentage, capped at 100%.
    /// Formula: min(100, TotalOrders / RequiredOrders × 100)
    /// Example: 146 orders / 175 required → 83.43 %
    /// </summary>
    decimal OrdersPercentage,

    /// <summary>
    /// Overall performance: average of DaysPercentage and OrdersPercentage, capped at 100%.
    /// Example: days 100 % + orders 83 % → performance 91.5 %
    /// </summary>
    decimal PerformancePercentage
);

// ═══════════════════════════════════════════════════════════════════════
// PER-DAY RECORD
// ═══════════════════════════════════════════════════════════════════════

public record HungerDailyValidationDetail(
    int Day,
    DateOnly Date,
    bool HasShift,
    float WorkingHours,
    int AcceptedOrders,

    /// <summary>True when the day counts as a valid working day (shift + ≥ 8 h).</summary>
    bool IsValidWorkingDay,

    /// <summary>True when this day is Thursday, Friday, Saturday, or within the last-5 window.</summary>
    bool IsCriticalDay,

    /// <summary>Human-readable label of why this day is critical, e.g. "خميس" or "آخر 5 أيام".</summary>
    string CriticalDayReason,

    /// <summary>Short explanation of the day's outcome.</summary>
    string Reason
);