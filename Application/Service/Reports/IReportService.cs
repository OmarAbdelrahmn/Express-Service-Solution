using Application.Abstraction;
using Application.Service.Riders;

namespace Application.Service.Reports;


public interface IReportService
{

    Task<Result<MonthlyRiderReport>> GetMonthlyReportByWorkingIdAsync(
        int workingId,
        int year,
        int month,
        CancellationToken cancellationToken = default);

   
    Task<Result<IEnumerable<MonthlyRiderReport>>> GetAllRidersMonthlyReportAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default);

   
    Task<Result<YearlyRiderReport>> GetYearlyReportByWorkingIdAsync(
        int workingId,
        int year,
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<YearlyRiderReport>>> GetAllRidersYearlyReportAsync(
        int year,
        CancellationToken cancellationToken = default);

  
    Task<Result<DateRangeReport>> GetCustomDateRangeReportByWorkingIdAsync(
        int workingId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<DateRangeReport>>> GetAllRidersCustomDateRangeReportAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);


    Task<Result<CompanyPerformanceReport>> GetCompanyPerformanceReportAsync(
        string companyName,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);


    Task<Result<CompanyPeriodComparison>> CompareCompanyPeriodsAsync(
        string companyName,
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default);


    Task<Result<IEnumerable<ProblemShiftDetail>>> GetProblematicShiftsAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

   
    Task<Result<RiderPeriodComparison>> CompareRiderPeriodsAsync(
        int workingId,
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<RiderPeriodComparison>>> CompareAllRidersPeriodsAsync(
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default);

  
    Task<Result<RiderPeriodComparison>> CompareRiderMonthsAsync(
        int workingId,
        int year1,
        int month1,
        int year2,
        int month2,
        CancellationToken cancellationToken = default);

    Task<Result<RiderPeriodComparison>> CompareRiderYearsAsync(
        int workingId,
        int year1,
        int year2,
        CancellationToken cancellationToken = default);

    Task<Result<List<HousingPeriodComparison>>> CompareHousingPeriodsAsync(
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default);


    Task<Result<PeriodHousingAnalysis>> GetHousingAnalysisForPeriodAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

 
    Task<Result<HousingPeriodComparison>> CompareSpecificHousingAsync(
        int housingId,
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default);

   
    Task<Result<List<RiderHousingAssignment>>> GetRidersForHousingAsync(
        int housingId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);
}

public record RiderPeriodComparison(
    int RiderId,
    string RiderName,
    int WorkingId,
    PeriodSummary Period1,
    PeriodSummary Period2,
    ComparisonMetrics Comparison,
    PeriodPerformanceVerdict Verdict,
    List<string> KeyInsights,
    List<string> Recommendations
);

public record PeriodSummary(
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalDays,
    int WorkingDays,
    int CompletedShifts,
    int IncompleteShifts,
    int FailedShifts,
    int AbsentShifts,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    int TotalRealRejectedOrders,
    float TotalWorkingHours,
    int ProblematicShiftsCount,
    decimal TotalPenaltyAmount,
    decimal AverageOrdersPerDay,
    decimal CompletionRate,
    decimal PerformanceScore,
    List<CompanyPeriodBreakdown> CompanyBreakdowns
);

public record ComparisonMetrics(
    int WorkingDaysDifference,
    decimal WorkingDaysChangePercent,
    int OrdersDifference,
    decimal OrdersChangePercent,
    decimal AverageOrdersPerDayDifference,
    decimal AverageOrdersPerDayChangePercent,
    decimal CompletionRateDifference,
    decimal CompletionRateChangePercent,
    decimal PerformanceScoreDifference,
    decimal PerformanceScoreChangePercent,
    float WorkingHoursDifference,
    decimal WorkingHoursChangePercent,
    decimal PenaltyDifference,
    decimal PenaltyChangePercent,
    int ProblematicShiftsDifference,
    decimal ProblematicShiftsChangePercent,
    decimal RejectionRateDifference,
    decimal RejectionRateChangePercent
);

public record PeriodPerformanceVerdict(
    ComparisonResult OverallResult,
    string Summary,
    decimal ImprovementScore,
    List<MetricChange> TopImprovements,
    List<MetricChange> TopDeclines
);

public record MetricChange(
    string MetricName,
    string OldValue,
    string NewValue,
    decimal ChangePercent,
    TrendDirection Direction
);

public enum ComparisonResult
{
    Better,
    Worse,
    Same,
    Mixed
}

public enum TrendDirection
{
    Up,
    Down,
    Stable
}


public record CompanyPeriodComparison(
    string CompanyName,
    PeriodSummary Period1,
    PeriodSummary Period2,
    ComparisonMetrics Comparison,
    List<RiderPeriodComparison> TopImprovedRiders,
    List<RiderPeriodComparison> TopDeclinedRiders,
    string OverallTrend
);



public record HousingPeriodBreakdown(
    int HousingId,
    string HousingName,
    int DailyOrdersCount,
    int CompletedOrdersCount,
    int RejectedOrdersCount,
    decimal CompletionRate,
    int RiderCount,
    List<RiderHousingAssignment> RiderAssignments,
    decimal HousingContribution,
    int ProblematicOrdersCount,
    decimal AverageOrdersPerRider
);

public record RiderHousingAssignment(
    int RiderId,
    string RiderName,
    int WorkingId,
    int ShiftsCount,
    int OrdersCompleted,
    int OrdersRejected,
    decimal CompletionRate,
    float TotalWorkingHours
);

public record HousingPeriodComparison(
    int HousingId,
    string HousingName,
    HousingPeriodBreakdown Period1Breakdown,
    HousingPeriodBreakdown Period2Breakdown,
    HousingComparisonMetrics Comparison,
    List<string> Insights
);

public record HousingComparisonMetrics(
    int DailyOrdersDifference,
    decimal DailyOrdersChangePercent,
    int CompletedOrdersDifference,
    decimal CompletedOrdersChangePercent,
    decimal CompletionRateDifference,
    decimal CompletionRateChangePercent,
    int RiderCountDifference,
    decimal RiderCountChangePercent,
    int RejectedOrdersDifference,
    decimal RejectionRateChangePercent,
    decimal HousingContributionDifference
);

public record PeriodHousingAnalysis(
    DateOnly StartDate,
    DateOnly EndDate,
    List<HousingPeriodBreakdown> HousingBreakdowns,
    int TotalOrders,
    int TotalRiders,
    HousingPerformanceRanking TopPerformingHousing,
    HousingPerformanceRanking LowestPerformingHousing
);

public record HousingPerformanceRanking(
    int HousingId,
    string HousingName,
    decimal CompletionRate,
    int OrdersCount,
    int RiderCount
);


public record CompanyPerformanceReport(
    string CompanyName,
    DateOnly StartDate,
    DateOnly EndDate,
    int DailyOrderTarget,
    int TotalWorkingDays,
    int ExpectedOrders,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    int CompletedShifts,
    int IncompleteShifts,
    int FailedShifts,
    decimal OverallPerformanceScore,
    decimal TotalPenaltyAmount,
    List<RiderCompanyPerformance> RiderPerformances
);

public record RiderCompanyPerformance(
    int RiderId,
    string RiderName,
    int WorkingId,
    int TotalShifts,
    int CompletedShifts,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    decimal PerformanceScore
);




public record MonthlyCompanyData(
    int Month,
    int WorkingDays,
    int AcceptedOrders,
    int RejectedOrders
);
public record YearlyCompanyBreakdown(
    string CompanyName,
    int DailyOrderTarget,
    int TotalWorkingDays,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    decimal AveragePerformanceScore,
    List<MonthlyCompanyData> MonthlyDetails
);
