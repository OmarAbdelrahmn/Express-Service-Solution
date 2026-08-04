using Domain.Entities.Keeta;

namespace Application.Contracts.KeetaBreaks;

public record KeetaBreakShiftDefinitionRequest(string ShiftKey, TimeOnly StartTime, TimeOnly EndTime, int MinimumRiders, int MaximumRiders);
public record KeetaBreakShiftPatternRequest(string Periods, int RiderCount);
public record CreateKeetaBreakConfigurationRequest(DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal BreakPercentage, KeetaBreakRoundingPolicy RoundingPolicy, List<KeetaBreakShiftDefinitionRequest> Shifts, List<KeetaBreakShiftPatternRequest> ShiftPatterns);
public record KeetaBreakShiftDefinitionResponse(string ShiftKey, TimeOnly StartTime, TimeOnly EndTime, int MinimumRiders, int MaximumRiders);
public record KeetaBreakShiftPatternResponse(int Id, string Periods, List<string> Shifts, int RiderCount);
public record KeetaBreakConfigurationResponse(Guid Id, int Version, DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal BreakPercentage, KeetaBreakRoundingPolicy RoundingPolicy, bool IsActive, List<KeetaBreakShiftDefinitionResponse> Shifts, List<KeetaBreakShiftPatternResponse> ShiftPatterns);

public record CreateKeetaBreakCapacityPlanRequest(DateOnly PeriodStart, DateOnly PeriodEnd, Guid? ConfigurationId = null);
public record KeetaBreakPatternCapacityResponse(int PatternId, string Periods, List<string> Shifts, int RiderCount, int MaximumBreakRiders, string Status, string? Reason);
public record KeetaBreakShiftTotalResponse(string Shift, int TotalRiders, int MinimumRiders, int MaximumRiders, int BreakLimitByPercentage, int BreakLimitByMinimumStaffing, int EffectiveBreakLimit, string Status);
public record KeetaBreakCapacityDateResponse(DateOnly Date, string DayName, bool IsEligible, string? ProhibitionReason, List<KeetaBreakPatternCapacityResponse> Patterns);
public record KeetaBreakCapacityPlanResponse(Guid ConfigurationId, int ConfigurationVersion, DateOnly PeriodStart, DateOnly PeriodEnd, decimal BreakPercentage, KeetaBreakRoundingPolicy RoundingPolicy, List<KeetaBreakShiftTotalResponse> ShiftTotals, List<KeetaBreakCapacityDateResponse> Dates);

public record KeetaBreakImportedRiderResponse(string RiderNumber, string RiderIdentifier, string RiderName, string? HousingGroup, List<string> Shifts, string? Notes, string? ValidationError);
public record KeetaBreakAssignmentResponse(Guid Id, string RiderIdentifier, DateOnly BreakDate, List<string> Shifts, KeetaBreakAssignmentStatus Status, string? Reason);
public record KeetaBreakRiderResultResponse(string RiderIdentifier, string RiderName, string? HousingGroup, List<string> AssignedShifts, List<DateOnly> BreakDates, int ConfirmedMonthlyBreaksBefore, int PlannedBreaks, int MonthlyTotalAfterConfirmation, string Status, string? Reason);
public record KeetaBreakShiftSummaryResponse(DateOnly Date, string Shift, int AssignedRiders, int MinimumRiders, int MaximumRiders, int ExistingConfirmedBreaks, int NewlyPlannedBreaks, int TotalBreaksAfterConfirmation, int EffectiveBreakLimit, int RemainingBreakSlots, int ActiveRiders, string Status);
public record KeetaBreakDateSummaryResponse(DateOnly Date, bool IsEligible, string? ProhibitionReason);
public record KeetaBreakBatchResponse(Guid Id, DateOnly PeriodStart, DateOnly PeriodEnd, KeetaBreakBatchStatus Status, Guid ConfigurationId, string SourceFileName, List<KeetaBreakDateSummaryResponse> Dates, List<KeetaBreakImportedRiderResponse> Riders, List<KeetaBreakAssignmentResponse> Assignments, List<KeetaBreakRiderResultResponse> Results, List<KeetaBreakShiftSummaryResponse> ShiftSummaries);

public record ConfirmKeetaBreakBatchRequest(byte[] RowVersion);
public record ManualKeetaBreakAssignmentRequest(string RiderIdentifier, DateOnly BreakDate);
