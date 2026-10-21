using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.RiderMobile;

public record RiderMobileLoginRequest(
    [Required, RegularExpression("^[0-9]{10}$")] string IqamaNo,
    string? Language = null);
public record RiderMobileLanguageRequest([Required] string Language);
public record RiderMobileProfile(int RiderId, string IqamaNo, string? WorkingId,
    string Name, string NameAR, string NameEN, string Company, string? VehicleNumber, string Language);
public record RiderMobileLoginResponse(string AccessToken, string TokenType,
    DateTime ExpiresAtUtc, string Language, RiderMobileProfile Rider);
public record RiderMobileDashboard(string Language, DateOnly Today, RiderMobileProfile Rider,
    RiderMobileMonth CurrentMonth, RiderMobileMonth PreviousMonth);
public record RiderMobileNote(string Code, string Severity, string Message, string Advice,
    decimal? Actual = null, decimal? Target = null, decimal? Gap = null, DateOnly? Date = null);
public record RiderMobilePerformance(int RecordCount, DateOnly? LastUpdatedDate, string? Rank,
    string RankSource, long GrossOrders, long CompletedOrders, long CompletedOrdersInTime,
    long FailedOrdersByRider, long VerificationRequests, long SuccessfulVerifications,
    decimal? OnTimeDeliveryPercentage, decimal? VerificationSuccessPercentage,
    decimal? CompletionPercentage, decimal? OnTimeDeliveryScorePercentage,
    decimal? DeliveryQualityPercentage);
public record RiderMobileTargets(decimal OnTimeDeliveryPercentage,
    decimal VerificationSuccessPercentage, decimal DeliveryQualityPercentage);
public record RiderMobileDay(DateOnly Date, string DayName, string Status, string StatusLabel,
    bool HasShiftData, decimal WorkingHours, long AcceptedOrders, long RejectedOrders,
    long RealRejectedOrders, long StackedDeliveries, bool MeetsHoursTarget,
    bool IsRecordedDayOff, bool IsBreakDateAllowed, IReadOnlyList<string> BreakRestrictionCodes,
    IReadOnlyList<string> BreakRestrictionMessages,
    IReadOnlyList<RiderMobileNote> Notes);
public record RiderMobileAttendance(int RequiredWorkingDays, decimal RequiredHoursPerDay,
    int WorkedDays, int QualifiedWorkingDays, int RemainingQualifiedDays,
    int ExpectedQualifiedDaysToDate, int QualifiedDaysDeficitToDate,
    int RemainingCalendarDays, bool CanReachMonthlyTarget, decimal TotalHours,
    long AcceptedOrders, long RejectedOrders, int ShortDays, int RecordedDaysOff,
    int MaximumBreakDays, int RemainingBreakDays, int ProhibitedRecordedDaysOff,
    int MissingCompletedDays, bool MonthlyTargetMet, bool? IsCompliant,
    IReadOnlyList<RiderMobileDay> Days);
public record RiderMobileMonth(int Year, int Month, string MonthName, string Language,
    DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly AsOfDate, bool IsCurrentMonth,
    RiderMobileTargets CoachingTargets, RiderMobilePerformance Performance,
    RiderMobileAttendance Attendance, IReadOnlyList<RiderMobileNote> Notes);
