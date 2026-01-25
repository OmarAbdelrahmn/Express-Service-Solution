//using Application.Service.Member;
//using Application.Service.Riders;
//using System;
//using System.Collections.Generic;
//using System.Text;

namespace Application.Contracts.ReportCo;

public record DailyCompanyShiftSummary(
    DateOnly ShiftDate,
    int CompanyId,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    int TotalShifts,
    int UniqueRiders
);
public record Company2StackedDeliveriesReport(
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalDays,
    string CompanyName,
    int TotalRiders,
    int TotalShifts,
    int TotalStackedDeliveries,
    int TotalAcceptedOrders,
    decimal StackedDeliveryRate,
    decimal AverageStackedPerRider,
    decimal AverageStackedPerShift,
    decimal AverageStackedPerDay,
    List<Company2RiderStackedDetail> RiderDetails,
    Company2StackedSummary Summary
);

public record Company2RiderStackedDetail(
    int RiderId,
    long IqamaNo,
    string RiderNameAR,
    string RiderNameEN,
    string WorkingId,
    string HousingName,
    int TotalShifts,
    int TotalStackedDeliveries,
    int TotalAcceptedOrders,
    int MaxStackedInDay,
    DateOnly? MaxStackedDate,
    decimal StackedPercentage,
    decimal AverageStackedPerShift,
    int Rank
);

public record Company2StackedSummary(
    int TopStackedDeliveries,
    string TopPerformerName,
    string TopPerformerWorkingId,
    decimal CompanyStackedRate,
    int TotalWorkingDays,
    List<HousingStackedBreakdown> HousingBreakdowns
);

public record HousingStackedBreakdown(
    string HousingName,
    int TotalRiders,
    int TotalStackedDeliveries,
    int TotalAcceptedOrders,
    decimal StackedRate,
    decimal AverageStackedPerRider
);
//internal class ReporteCont
//{
//}
//public record Company2DailySummaryReport(
//    DateOnly ReportDate,
//    DateOnly PeriodStart,
//    DateOnly PeriodEnd,
//    int TotalOrdersDelivered,
//    float AverageWorkingHours,
//    int TotalShifts,
//    int TotalRiders
//);

//public record Company2CumulativeRiderReport(
//    DateOnly PeriodStart,
//    DateOnly PeriodEnd,
//    int TotalExpectedDays,
//    List<Company2RiderCumulativeStats> RiderStats,
//    int TotalOrdersAllRiders
//);

//public record Company2RiderCumulativeStats(
//    int RiderId,
//    long IqamaNo,
//    string RiderNameAR,
//    string WorkingId,
//    int TotalOrders,
//    float AverageOrdersPerDay,
//    int DeficitOrSurplus,
//    string HousingGroup,
//    int Rank = 0
//);

//public record Company2DailyRiderDetailsReport(
//    DateOnly ReportDate,
//    List<Company2DailyRiderDetail> RiderDetails,
//    int TotalOrders,
//    int TotalRiders,
//    decimal AverageOrdersPerRider
//);

//public record Company2DailyRiderDetail(
//    int RiderId,
//    long IqamaNo,
//    string RiderNameAR,
//    string WorkingId,
//    int OrderCount,
//    float WorkingHours,
//    string HousingGroup,
//    DateTime DriverAppConnectionTime,
//    int Rank = 0
//);
//// Records for Validation Results
//public record MonthlyRiderValidationReport(
//int Year,
//int Month,
//DateOnly StartDate,
//DateOnly EndDate,
//bool IsCurrentMonth,
//int CurrentDay,
//int TotalExpectedDays,
//int TargetOrders,
//int TotalRiders,
//int ValidRiders,
//int InvalidRiders,
//List<RiderMonthlyValidation> RiderValidations
//);

//public record RiderMonthlyValidation(
//    string HousingName,
//    int RiderId,
//    long IqamaNo,
//    string RiderNameAR,
//    string RiderNameEN,
//    string WorkingId,
//    int TotalExpectedDays,
//    int TotalWorkingDays,
//    int GoodDays,
//    int MissingDays,
//    List<int> MissingDaysList,
//    List<int> DaysWithLessThan10Hours,
//    int TotalOrders,
//    int TargetOrders,
//    float TotalWorkingHours,
//    float AverageHoursPerDay,
//    bool IsValidForMonth,
//    List<string> ValidationErrors,
//    List<DailyValidationDetail> DailyDetails
//);

//public record DailyValidationDetail(
//    int Day,
//    DateOnly Date,
//    bool HasShift,
//    float WorkingHours,
//    int AcceptedOrders,
//    bool IsValid,
//    string Reason
//);

////public record VehicleStatistics(
////       int TotalVehicles,
////       int AssignedVehicles,
////       int UnassignedVehicles,
////       int ExpiredLicenses,
////       int ExpiringIn30Days,
////       int ExpiringIn90Days,
////       double AverageVehicleAge,
////       int WithCompleteDocumentation,
////       int RecentRegistrations,
////       List<VehicleTypeCount> ByType,
////       List<ManufacturerCount> ByManufacturer,
////       List<LocationCount> ByLocation
////   );

//public record VehicleTypeCount(string Type, int Count);
//public record ManufacturerCount(string Manufacturer, int Count);
//public record LocationCount(string Location, int Count);

//public record HousingDetailedDailyPerformanceReport(
//    DateOnly StartDate,
//    DateOnly EndDate,
//    int TotalExpectedDays,
//    List<HousingPerformanceDetail> HousingDetails,
//    ReportSummary Summary
//);

//public record HousingPerformanceDetail(
//    int HousingId,
//    string HousingName,
//    List<RiderDailyPerformanceDetail> Riders,
//    HousingSummaryMetrics HousingSummary
//);

//public record RiderDailyPerformanceDetail(
//    int RiderId,
//    long IqamaNo,
//    string RiderNameAR,
//    string RiderNameEN,
//    string WorkingId,
//    List<DailyPerformanceEntry> DailyEntries,
//    RiderPeriodSummary PeriodSummary
//);

//public record DailyPerformanceEntry(
//    DateOnly Date,
//    bool IsPresent,
//    float WorkingHours,
//    float TargetHours,
//    float HoursDifference,
//    int AcceptedOrders,
//    int RejectedOrders,
//    int TargetOrders,
//    int OrdersDifference,
//    string ShiftStatus,
//    string PerformanceLevel  // Excellent, Good, Average, Poor, Absent
//);

//public record RiderPeriodSummary(
//    int TotalWorkingDays,
//    int TotalAbsentDays,
//    float TotalWorkingHours,
//    float TotalTargetHours,
//    float TotalHoursDifference,
//    int TotalAcceptedOrders,
//    int TotalRejectedOrders,
//    int TotalTargetOrders,
//    int TotalOrdersDifference,
//    float AverageHoursPerDay,
//    decimal AverageOrdersPerDay,
//    decimal AttendanceRate,
//    decimal HoursCompletionRate,
//    decimal OrdersCompletionRate,
//    decimal OverallPerformanceScore
//);

//public record HousingSummaryMetrics(
//    int TotalRiders,
//    int TotalWorkingDays,
//    int TotalAbsentDays,
//    float TotalWorkingHours,
//    float TotalTargetHours,
//    float TotalHoursDifference,
//    int TotalAcceptedOrders,
//    int TotalTargetOrders,
//    int TotalOrdersDifference,
//    decimal AverageAttendanceRate,
//    decimal AverageHoursCompletionRate,
//    decimal AverageOrdersCompletionRate,
//    decimal OverallHousingScore
//);

//public record ReportSummary(
//    int TotalHousings,
//    int TotalRiders,
//    int TotalWorkingDays,
//    int TotalAbsentDays,
//    float GrandTotalHours,
//    float GrandTotalTargetHours,
//    int GrandTotalOrders,
//    int GrandTotalTargetOrders,
//    decimal CompanyWideAttendanceRate,
//    decimal CompanyWideHoursCompletionRate,
//    decimal CompanyWideOrdersCompletionRate
//);
//public record PeriodOrdersComparison(
//    DateOnly Period1Start,
//    DateOnly Period1End,
//    DateOnly Period2Start,
//    DateOnly Period2End,
//    int Period1TotalOrders,
//    int Period2TotalOrders,
//    int OrdersDifference,
//    decimal ChangePercentage,
//    string TrendDescription
//);

//// 2. Housing Daily Summary Report
//public record HousingDailySummaryReport(
//    DateOnly ReportDate,
//    List<HousingDailySummary> HousingSummaries,
//    int TotalOrders,
//    int TotalRiders,
//    decimal AverageOrdersPerRider
//);

//public record HousingDailySummary(
//    int HousingId,
//    string HousingName,
//    int TotalOrders,
//    int ActiveRiders,
//    decimal AverageOrdersPerRider,
//    decimal PercentageOfTotalOrders
//);

//// 3. Housing Daily Detailed Report
//public record HousingDailyDetailedReport(
//    DateOnly ReportDate,
//    List<HousingDailyDetails> HousingDetails,
//    int GrandTotalOrders,
//    int GrandTotalRiders
//);

//public record HousingDailyDetails(
//    int HousingId,
//    string HousingName,
//    List<RiderDailyPerformance> Riders,
//    int HousingTotalOrders,
//    int HousingRiderCount,
//    decimal PercentageOfCompanyTotal
//);

//public record RiderDailyPerformance(
//    int RiderId,
//    string RiderName,
//    string RiderNameE,
//    string PhoneNumber,
//    string WorkingId,
//    int AcceptedOrders,
//    DateOnly ShiftDate
//);
//public record RiderPeriodComparison(
//    int RiderId,
//    string RiderName,
//    string WorkingId,
//    PeriodSummary Period1,
//    PeriodSummary Period2,
//    ComparisonMetrics Comparison,
//    PeriodPerformanceVerdict Verdict,
//    List<string> KeyInsights,
//    List<string> Recommendations
//);

//public record PeriodSummary(
//    DateOnly StartDate,
//    DateOnly EndDate,
//    int TotalDays,
//    int WorkingDays,
//    int CompletedShifts,
//    int IncompleteShifts,
//    int FailedShifts,
//    int AbsentShifts,
//    int TotalAcceptedOrders,
//    int TotalRejectedOrders,
//    int TotalRealRejectedOrders,
//    int TotalStackedDeliveries, // ADD THIS

//    float TotalWorkingHours,
//    int ProblematicShiftsCount,
//    decimal TotalPenaltyAmount,
//    decimal AverageStackedPerDay, // ADD THIS

//    decimal AverageOrdersPerDay,
//    decimal CompletionRate,
//    decimal PerformanceScore,
//    List<CompanyPeriodBreakdown> CompanyBreakdowns
//);
//public record PreviousDayCompanySummary(
//    DateOnly ReportDate,
//    CompanyDaySummary Hunger,
//    CompanyDaySummary Keta,
//    int TotalDayOrders,
//    int TotalDayShifts,
//    CompanyMonthToDateSummary HungerMonthToDate,
//    CompanyMonthToDateSummary KetaMonthToDate,
//    int TotalMonthOrders,
//    int TotalMonthShifts,
//    DateOnly MonthStartDate
//);

//public record CompanyDaySummary(
//    string CompanyName,
//    int TotalOrders,
//    int TotalShifts,
//    int AcceptedOrders,
//    int RejectedOrders,
//    int CompletedShifts,
//    int IncompleteShifts,
//    int FailedShifts
//);

//public record CompanyMonthToDateSummary(
//    string CompanyName,
//    int TotalOrders,
//    int TotalShifts,
//    int AcceptedOrders,
//    int RejectedOrders,
//    int CompletedShifts,
//    int IncompleteShifts,
//    int FailedShifts,
//    int TotalDays
//);
//public record ComparisonMetrics(
//    int WorkingDaysDifference,
//    decimal WorkingDaysChangePercent,
//    int OrdersDifference,
//    decimal OrdersChangePercent,
//    decimal AverageOrdersPerDayDifference,
//    decimal AverageOrdersPerDayChangePercent,
//    decimal CompletionRateDifference,
//    decimal CompletionRateChangePercent,
//    decimal PerformanceScoreDifference,
//    decimal PerformanceScoreChangePercent,
//    float WorkingHoursDifference,
//    decimal WorkingHoursChangePercent,
//    decimal PenaltyDifference,
//    decimal PenaltyChangePercent,
//    int ProblematicShiftsDifference,
//    decimal ProblematicShiftsChangePercent,
//    decimal RejectionRateDifference,
//    decimal RejectionRateChangePercent
//);

//public record PeriodPerformanceVerdict(
//    ComparisonResult OverallResult,
//    string Summary,
//    decimal ImprovementScore,
//    List<MetricChange> TopImprovements,
//    List<MetricChange> TopDeclines
//);

//public record MetricChange(
//    string MetricName,
//    string OldValue,
//    string NewValue,
//    decimal ChangePercent,
//    TrendDirection Direction
//);

//public enum ComparisonResult
//{
//    Better,
//    Worse,
//    Same,
//    Mixed
//}

//public enum TrendDirection
//{
//    Up,
//    Down,
//    Stable
//}


//public record CompanyPeriodComparison(
//    string CompanyName,
//    PeriodSummary Period1,
//    PeriodSummary Period2,
//    ComparisonMetrics Comparison,
//    List<RiderPeriodComparison> TopImprovedRiders,
//    List<RiderPeriodComparison> TopDeclinedRiders,
//    string OverallTrend
//);



//public record HousingPeriodBreakdown(
//    int HousingId,
//    string HousingName,
//    int DailyOrdersCount,
//    int CompletedOrdersCount,
//    int RejectedOrdersCount,
//    decimal CompletionRate,
//    int RiderCount,
//    List<RiderHousingAssignment> RiderAssignments,
//    decimal HousingContribution,
//    int ProblematicOrdersCount,
//    decimal AverageOrdersPerRider
//);

//public record RiderHousingAssignment(
//    int RiderId,
//    string RiderName,
//    string WorkingId,
//    int ShiftsCount,
//    int OrdersCompleted,
//    int OrdersRejected,
//    decimal CompletionRate,
//    float TotalWorkingHours
//);

//public record HousingPeriodComparison(
//    string HousingName,
//    HousingPeriodBreakdown Period1Breakdown,
//    HousingPeriodBreakdown Period2Breakdown,
//    HousingComparisonMetrics Comparison,
//    List<string> Insights
//);

//public record HousingComparisonMetrics(
//    int DailyOrdersDifference,
//    decimal DailyOrdersChangePercent,
//    int CompletedOrdersDifference,
//    decimal CompletedOrdersChangePercent,
//    decimal CompletionRateDifference,
//    decimal CompletionRateChangePercent,
//    int RiderCountDifference,
//    decimal RiderCountChangePercent,
//    int RejectedOrdersDifference,
//    decimal RejectionRateChangePercent,
//    decimal HousingContributionDifference
//);

//public record PeriodHousingAnalysis(
//    DateOnly StartDate,
//    DateOnly EndDate,
//    List<HousingPeriodBreakdown> HousingBreakdowns,
//    int TotalOrders,
//    int TotalRiders,
//    HousingPerformanceRanking TopPerformingHousing,
//    HousingPerformanceRanking LowestPerformingHousing
//);

//public record HousingPerformanceRanking(
//    int HousingId,
//    string HousingName,
//    decimal CompletionRate,
//    int OrdersCount,
//    int RiderCount
//);


//public record AcceptedOrdersResponse(
//       int RiderId,
//       string WorkingId,
//       string RiderName,
//       string CompanyName,
//       DateOnly ShiftDate,
//       int AcceptedDailyOrders,
//       int RejectedDailyOrders,
//       int RealRejectedDailyOrders,
//       int StackedDeliveries,
//       float WorkingHours,
//       string ShiftStatus,
//       bool HasRejectionProblem,
//       decimal PenaltyAmount,
//       DateTime CreatedAt
//   );

//public record CreateRiderShiftRequest(
//    string WorkingId,
//    DateOnly ShiftDate,
//    int AcceptedDailyOrders,
//    int RejectedDailyOrders,
//    int StackedDeliveries,
//    int RealRejectedDailyOrders,
//    float WorkingHours
//);

//public record UpdateRiderShiftRequest(
//    string WorkingId,
//    DateOnly ShiftDate,
//    int? AcceptedDailyOrders,
//    int? RejectedDailyOrders,
//    int? StackedDeliveries,
//    int? RealRejectedDailyOrders,
//    float? WorkingHours
//);

//public record RiderShiftResponse(
//    int RiderId,
//    string WorkingId,
//    DateOnly ShiftDate,
//    int AcceptedDailyOrders,
//    int RejectedDailyOrders,
//    int RealRejectedDailyOrders,
//    int StackedDeliveries,
//    float WorkingHours,
//    int CompanyId,
//    string CompanyName,
//    string RiderName,
//    string ShiftStatus,
//    bool HasRejectionProblem,
//    decimal PenaltyAmount,
//    DateTime CreatedAt,
//    bool IsSubstitution,
//    string? OriginalWorkingId,
//    int? HousingId
//);




//public record BulkDeleteResult(
//    int TotalDeleted,
//    List<string> DeletedShiftDetails
//);

//public record MonthlyRiderReport(
//    int RiderId,
//    string RiderName,
//    string WorkingId,
//    int Year,
//    int Month,

//    int TotalWorkingDays,
//    int CompletedShifts,
//    int IncompleteShifts,
//    int FailedShifts,
//    int TotalAcceptedOrders,
//    int TotalRejectedOrders,
//    int TotalRealRejectedOrders,
//    float TotalWorkingHours,
//    int ProblematicShiftsCount,
//    decimal TotalPenaltyAmount,
//    decimal OverallPerformanceScore,

//    List<CompanyPeriodBreakdown> CompanyBreakdowns,

//    List<ProblemShiftDetail> ProblematicShifts,

//    List<WorkingIdPeriod> WorkingIdHistory
//);

////public record CompanyPeriodBreakdown(
////    string CompanyName,
////    int DailyOrderTarget,
////    int WorkingDays,
////    int CompletedShifts,
////    int IncompleteShifts,
////    int FailedShifts,
////    int TotalAcceptedOrders,
////    int TotalRejectedOrders,
////    int TotalRealRejectedOrders,
////    int TotalStackedDeliveries, // ADD THIS
////    decimal AverageStackedPerShift,
////    float TotalWorkingHours,
////    int ProblematicShiftsCount,
////    decimal PenaltyAmount,
////    decimal PerformanceScore,
////    int ExpectedOrders
////);

//public record YearlyRiderReport(
//    int RiderId,
//    string RiderName,
//    string WorkingId,
//    int Year,

//    int TotalWorkingDays,
//    int CompletedShifts,
//    int IncompleteShifts,
//    int FailedShifts,
//    int TotalAcceptedOrders,
//    int TotalRejectedOrders,
//    int TotalRealRejectedOrders,
//    float TotalWorkingHours,
//    int ProblematicShiftsCount,
//    decimal TotalPenaltyAmount,
//    decimal AveragePerformanceScore,

//    List<YearlyCompanyBreakdown> YearlyCompanyBreakdowns,

//    List<MonthlyBreakdown> MonthlyBreakdowns,

//    List<WorkingIdPeriod> WorkingIdHistory
//);

//public record YearlyCompanyBreakdowns(
//    string CompanyName,
//    int DailyOrderTarget,
//    int TotalWorkingDays,
//    int CompletedShifts,
//    int IncompleteShifts,
//    int FailedShifts,
//    int TotalAcceptedOrders,
//    int TotalRejectedOrders,
//    int TotalRealRejectedOrders,
//    float TotalWorkingHours,
//    int ProblematicShiftsCount,
//    decimal TotalPenaltyAmount,
//    decimal AveragePerformanceScore,
//    int ExpectedOrders,
//    List<MonthlyCompanyData> MonthlyDetails
//);

//public record MonthlyCompanyData(
//    int Month,
//    int WorkingDays,
//    int AcceptedOrders,
//    int RejectedOrders
//);

//public record MonthlyBreakdown(
//    int Month,
//    int WorkingDays,
//    int CompletedShifts,
//    int TotalAcceptedOrders,
//    int TotalRejectedOrders,
//    decimal PerformanceScore,
//    List<CompanyPeriodBreakdown> CompanyBreakdowns
//);
//public record DateRangeReport(
//    int RiderId,
//    long IqamaNo,
//    string RiderName,
//    string WorkingId,
//    DateOnly StartDate,
//    DateOnly EndDate,

//    int TotalWorkingDays,
//    int CompletedShifts,
//    int IncompleteShifts,
//    int FailedShifts,
//    int TotalAcceptedOrders,
//    int TotalRejectedOrders,
//    int TotalRealRejectedOrders,
//    float TotalWorkingHours,
//    int ProblematicShiftsCount,
//    decimal TotalPenaltyAmount,
//    decimal OverallPerformanceScore,

//    List<CompanyPeriodBreakdown> CompanyBreakdowns,

//    List<ProblemShiftDetail> ProblematicShifts,

//    List<WorkingIdPeriod> WorkingIdHistory
//);

//public record CompanyPerformanceReport(
//    int CompanyId,
//    string CompanyName,
//    DateOnly StartDate,
//    DateOnly EndDate,
//    int TotalRiders,
//    int TotalShifts,
//    int CompletedShifts,
//    int IncompleteShifts,
//    int FailedShifts,
//    int TotalAcceptedOrders,
//    int TotalRejectedOrders,
//    int TotalRealRejectedOrders,
//    float TotalWorkingHours,
//    int ProblematicShiftsCount,
//    decimal TotalPenaltyAmount,
//    decimal CompanyPerformanceScore,
//    List<RiderPerformanceSummary> TopPerformers,
//    List<RiderPerformanceSummary> LowPerformers
//);

//public record ProblemShiftDetail(
//    int RiderId,
//    string RiderName,
//    string WorkingId,
//    DateOnly ShiftDate,
//    string CompanyName,
//    int AcceptedOrders,
//    int RejectedOrders,
//    int RealRejectedOrders,
//    string Status,
//    decimal PenaltyAmount,
//    string ProblemDescription
//);


//public record RiderPerformanceSummary(
//    int RiderId,
//    string RiderName,
//    string WorkingId,
//    int TotalShifts,
//    int CompletedShifts,
//    int TotalAcceptedOrders,
//    int ProblematicShiftsCount,
//    decimal TotalPenaltyAmount,
//    decimal PerformanceScore
//);

//public record WorkingIdPeriod(
//    string WorkingId,
//    DateOnly StartDate,
//    DateOnly EndDate,
//    int ShiftCount
//);

//public enum ShiftStatus
//{
//    Completed = 1,
//    Incomplete = 2,
//    Failed = 3,
//    Absent = 4,
//    Average = 5
//}

//public record CompanyPerformanceReportt(
//    string CompanyName,
//    DateOnly StartDate,
//    DateOnly EndDate,
//    int DailyOrderTarget,
//    int TotalWorkingDays,
//    int ExpectedOrders,
//    int TotalAcceptedOrders,
//    int TotalRejectedOrders,
//    int CompletedShifts,
//    int IncompleteShifts,
//    int FailedShifts,
//    decimal OverallPerformanceScore,
//    decimal TotalPenaltyAmount,
//    List<RiderCompanyPerformance> RiderPerformances
//);

//public record RiderCompanyPerformance(
//    int RiderId,
//    string RiderName,
//    string WorkingId,
//    int TotalShifts,
//    int CompletedShifts,
//    int TotalAcceptedOrders,
//    int TotalRejectedOrders,
//    decimal PerformanceScore
//);


//public record RiderWorkHistorySummary(
//    long IqamaNo,
//    string RiderName,
//    string WorkingId,
//    int TotalMonthsWorked,
//    int TotalShifts,
//    int TotalOrders,
//    decimal AverageOrdersPerMonth,
//    DateOnly FirstWorkDate,
//    DateOnly LastWorkDate,
//    List<MonthlyShiftSummary> ActiveMonths
//);

//public record MonthlyCompanyDatat(
//    int Month,
//    int WorkingDays,
//    int AcceptedOrders,
//    int RejectedOrders
//);
//public record YearlyCompanyBreakdown(
//    string CompanyName,
//    int DailyOrderTarget,
//    int TotalWorkingDays,
//    int TotalAcceptedOrders,
//    int TotalRejectedOrders,
//    decimal AveragePerformanceScore,
//    List<MonthlyCompanyData> MonthlyDetails
//);

//public record TopRidersReport(
//    DateOnly StartDate,
//    DateOnly EndDate,
//    int TotalRiders,
//    int TotalShifts,
//    int TotalOrders,
//    List<TopRiderDetail> TopRiders,
//    CompanyBreakdownSummary CompanyBreakdown
//);

//// Detailed information about each top rider
//public record TopRiderDetail(
//    int RiderId,
//    string WorkingId,
//    string RiderNameEN,
//    string RiderNameAR,
//    string CompanyName,
//    int TotalShifts,
//    int TotalAcceptedOrders,
//    int TotalRejectedOrders,
//    int TotalRealRejectedOrders,
//    float TotalWorkingHours,
//    int CompletedShifts,
//    int IncompleteShifts,
//    int FailedShifts,
//    int TotalStackedDeliveries, // ADD THIS
//    decimal AverageStackedPerShift, // ADD THIS
//    decimal CompletionRate,
//    decimal AverageOrdersPerShift,
//    decimal RejectionRate,
//    decimal PerformanceScore,
//    decimal TotalPenalty,
//    int ProblematicShiftsCount,
//    int Rank,
//    string PerformanceGrade,
//    List<string> Achievements,
//    bool IsSubstitutionActive,
//    string? OriginalWorkingId
//);

//// Company-wise breakdown in the period
//public record CompanyBreakdownSummary(
//    List<CompanyTopRiders> CompaniesSummary
//);

//public record CompanyTopRiders(
//    string CompanyName,
//    int DailyOrderTarget,
//    int TotalRiders,
//    int TotalShifts,
//    int TotalOrders,
//    decimal CompanyPerformanceScore,
//    TopRiderDetail TopPerformer,
//    int TopPerformersCount
//);

//// Request parameters
//public record TopRidersRequest(
//    DateOnly StartDate,
//    DateOnly EndDate,
//    int TopCount = 10,
//    string? CompanyFilter = null,
//    TopRidersSortBy SortBy = TopRidersSortBy.TotalOrders,
//    bool IncludeAllCompanies = true,
//    decimal MinimumShifts = 0
//);

//// Sorting options
//public enum TopRidersSortBy
//{
//    TotalOrders,
//    CompletionRate,
//    PerformanceScore,
//    AverageOrdersPerShift,
//    TotalShifts,
//    WorkingHours
//}

//// Performance grade calculation
//public enum PerformanceGrade
//{
//    Exceptional,  // 95%+
//    Excellent,    // 85-94%
//    Good,         // 75-84%
//    Average,      // 65-74%
//    BelowAverage, // 50-64%
//    Poor          // <50%
//}

//public record ComprehensiveDashboard(
//    DateTime GeneratedAt,
//    DateOnly PeriodStart,
//    DateOnly PeriodEnd,
//    CompaniesStatistics Companies,
//    RidersStatistics Riders,
//    ShiftsStatistics Shifts,
//    OrdersStatistics Orders,
//    PerformanceMetrics Performance,
//    HousingStatistics Housing,
//    TrendsAnalysis Trends,
//    VehicleStatistics Vehicle
//);

//public record CompaniesStatistics(
//    int TotalCompanies,
//    int ActiveCompanies,
//    List<CompanyDetail> CompanyDetails,
//    string? TopPerformingCompany,
//    string? LowestPerformingCompany,
//    decimal AverageCompanyPerformance
//);

//public record CompanyDetail(
//    int CompanyId,
//    string CompanyName,
//    int DailyOrderTarget,
//    int TotalShifts,
//    int ActiveRiders,
//    int TotalAcceptedOrders,
//    int TotalRejectedOrders,
//    int CompletedShifts,
//    int IncompleteShifts,
//    int FailedShifts,
//    decimal PerformanceScore,
//    float TotalWorkingHours
//);

//public record RidersStatistics(
//    int TotalRiders,
//    int ActiveRiders,
//    int InactiveRiders,
//    int RidersWithWorkingId,
//    int RidersWithSubstitution,
//    decimal AverageShiftsPerRider,
//    float TotalWorkingHours
//);

//public record ShiftsStatistics(
//    int TotalShifts,
//    int CompletedShifts,
//    int IncompleteShifts,
//    int FailedShifts,
//    decimal CompletionRate,
//    float AverageWorkingHoursPerShift,
//    float TotalWorkingHours,
//    List<DailyShiftBreakdown> DailyBreakdown
//);

//public record DailyShiftBreakdown(
//    DateOnly Date,
//    int TotalShifts,
//    int CompletedShifts,
//    int TotalOrders,
//    int AcceptedOrders,
//    int RejectedOrders,
//    int StackedDeliveries // ADD THIS

//);
//public record MonthlyStackedDeliveriesReport(
//    int RiderId,
//    string RiderName,
//    string WorkingId,
//    int Year,
//    int Month,
//    int TotalStackedDeliveries,
//    int TotalShifts,
//    decimal AverageStackedPerShift,
//    int MaxStackedInDay,
//    DateOnly? MaxStackedDate,
//    List<DailyStackedBreakdown> DailyBreakdown
//);


//public record DailyStackedBreakdown(
//    DateOnly Date,
//    int StackedDeliveries,
//    int AcceptedOrders,
//    decimal StackedPercentage
//);
//public record OrdersStatistics(
//    int TotalOrders,
//    int TotalAcceptedOrders,
//    int TotalRejectedOrders,
//    int TotalRealRejectedOrders,
//    int TotalStackedDeliveries, // ADD THIS
//    decimal AcceptanceRate,
//    decimal RejectionRate,
//    decimal StackedDeliveryRate, // ADD THIS
//    decimal AverageOrdersPerShift,
//    decimal AverageStackedPerShift, // ADD THIS
//    int ProblematicShiftsCount,
//    decimal TotalPenaltyAmount
//);

//public record PerformanceMetrics(
//    decimal OverallPerformanceScore,
//    List<TopPerformer> TopPerformers,
//    decimal AverageCompletionRate,
//    decimal AverageOrdersPerDay
//);

//public record TopPerformer(
//    int RiderId,
//    string RiderName,
//    string WorkingId,
//    int TotalOrders,
//    decimal PerformanceScore,
//    decimal CompletionRate
//);

//public record HousingStatistics(
//    int TotalHousings,
//    int ActiveHousings,
//    List<HousingDetail> HousingDetails,
//    string? TopPerformingHousing,
//    double AverageRidersPerHousing
//);

//public record HousingDetail(
//    int HousingId,
//    string HousingName,
//    int TotalRiders,
//    int TotalShifts,
//    int TotalOrders,
//    int AcceptedOrders,
//    decimal CompletionRate
//);

//public record TrendsAnalysis(
//    List<WeeklyTrend> WeeklyTrends,
//    decimal OrdersGrowthRate,
//    decimal ShiftsGrowthRate,
//    string PerformanceTrend
//);

//public record AllRidersStackedDeliveriesReport(
//    DateOnly StartDate,
//    DateOnly EndDate,
//    int TotalRiders,
//    int TotalStackedDeliveries,
//    int TotalShifts,
//    decimal AverageStackedPerRider,
//    List<RiderStackedSummary> RiderSummaries
//);

//public record RiderStackedSummary(
//    int RiderId,
//    string RiderName,
//    string WorkingId,
//    int TotalStackedDeliveries,
//    int TotalShifts,
//    decimal AverageStackedPerShift,
//    int MaxStackedInDay,
//    DateOnly? MaxStackedDate,
//    decimal TotalStackedPercentage
//);

//public record WeeklyTrend(
//    int WeekNumber,
//    int TotalShifts,
//    int TotalOrders,
//    decimal AveragePerformance
//);

//// Company 2 Monthly Performance Distribution Records
//public record Company2MonthlyPerformanceDistribution(
//    int Year,
//    int Month,
//    DateOnly StartDate,
//    DateOnly CurrentDate,
//    int TotalExpectedDays,
//    int CurrentDayOfMonth,
//    int TargetOrdersToDate,
//    CompanyPerformanceSummary CompanySummary,
//    List<HousingPerformanceDistribution> HousingDistributions,
//    List<RiderPerformanceDetail> RiderDetails
//);

//public record CompanyPerformanceSummary(
//    int TotalRiders,
//    int TotalOrders,
//    PerformanceTierDistribution TierDistribution
//);

//public record HousingPerformanceDistribution(
//    int HousingId,
//    string HousingName,
//    int TotalRiders,
//    int TotalOrders,
//    PerformanceTierDistribution TierDistribution,
//    List<RiderPerformanceDetail> Riders
//);

//public record PerformanceTierDistribution(
//    int ExcellentCount,
//    decimal ExcellentPercentage,
//    int GoodCount,
//    decimal GoodPercentage,
//    int PoorCount,
//    decimal PoorPercentage,
//    string Summary
//);

//public record RiderPerformanceDetail(
//    int RiderId,
//    long IqamaNo,
//    string RiderNameAR,
//    string RiderNameEN,
//    string WorkingId,
//    string HousingName,
//    int TotalOrders,
//    int TargetOrders,
//    int OrdersDifference,
//    decimal AverageOrdersPerDay,
//    int TotalWorkingDays,
//    PerformanceTier Tier,
//    string TierDescription
//);

//public enum PerformanceTier
//{
//    Excellent = 1,  // 400+ orders (14+ per day average)
//    Good = 2,       // 301-400 orders (10-13 per day average)
//    Poor = 3        // 1-300 orders (1-9 per day average)
//}