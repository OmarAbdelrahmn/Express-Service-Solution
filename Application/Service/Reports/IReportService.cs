using Application.Abstraction;
using Application.Service.Riders;

namespace Application.Service.Reports;


public interface IReportService
{
    Task<Result<ComprehensiveDashboard>> GetComprehensiveDashboardAsync(
       DateOnly? startDate = null,
       DateOnly? endDate = null,
       CancellationToken cancellationToken = default);



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
        string housingName,
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default);

   
    Task<Result<List<RiderHousingAssignment>>> GetRidersForHousingAsync(
        string housingName,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    Task<Result<TopRidersReport>> GetTopRidersInPeriodAsync(
        TopRidersRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<TopRidersReport>> GetTopRidersForMonthAsync(
        int year,
        int month,
        int topCount = 10,
        string? companyFilter = null,
        CancellationToken cancellationToken = default);

    Task<Result<TopRidersReport>> GetTopRidersForYearAsync(
        int year,
        int topCount = 10,
        string? companyFilter = null,
        CancellationToken cancellationToken = default);


    Task<Result<Dictionary<string, List<TopRiderDetail>>>> GetTopRidersPerCompanyAsync(
        DateOnly startDate,
        DateOnly endDate,
        int topCountPerCompany = 100,
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

public record TopRidersReport(
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalRiders,
    int TotalShifts,
    int TotalOrders,
    List<TopRiderDetail> TopRiders,
    CompanyBreakdownSummary CompanyBreakdown
);

// Detailed information about each top rider
public record TopRiderDetail(
    int RiderId,
    int WorkingId,
    string RiderNameEN,
    string RiderNameAR,
    string CompanyName,
    int TotalShifts,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    int TotalRealRejectedOrders,
    float TotalWorkingHours,
    int CompletedShifts,
    int IncompleteShifts,
    int FailedShifts,
    decimal CompletionRate,
    decimal AverageOrdersPerShift,
    decimal RejectionRate,
    decimal PerformanceScore,
    decimal TotalPenalty,
    int ProblematicShiftsCount,
    int Rank,
    string PerformanceGrade,
    List<string> Achievements,
    bool IsSubstitutionActive,
    int? OriginalWorkingId
);

// Company-wise breakdown in the period
public record CompanyBreakdownSummary(
    List<CompanyTopRiders> CompaniesSummary
);

public record CompanyTopRiders(
    string CompanyName,
    int DailyOrderTarget,
    int TotalRiders,
    int TotalShifts,
    int TotalOrders,
    decimal CompanyPerformanceScore,
    TopRiderDetail TopPerformer,
    int TopPerformersCount
);

// Request parameters
public record TopRidersRequest(
    DateOnly StartDate,
    DateOnly EndDate,
    int TopCount = 10,
    string? CompanyFilter = null,
    TopRidersSortBy SortBy = TopRidersSortBy.TotalOrders,
    bool IncludeAllCompanies = true,
    decimal MinimumShifts = 0
);

// Sorting options
public enum TopRidersSortBy
{
    TotalOrders,
    CompletionRate,
    PerformanceScore,
    AverageOrdersPerShift,
    TotalShifts,
    WorkingHours
}

// Performance grade calculation
public enum PerformanceGrade
{
    Exceptional,  // 95%+
    Excellent,    // 85-94%
    Good,         // 75-84%
    Average,      // 65-74%
    BelowAverage, // 50-64%
    Poor          // <50%
}

public record ComprehensiveDashboard(
    DateTime GeneratedAt,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    CompaniesStatistics Companies,
    RidersStatistics Riders,
    ShiftsStatistics Shifts,
    OrdersStatistics Orders,
    PerformanceMetrics Performance,
    HousingStatistics Housing,
    TrendsAnalysis Trends
);

public record CompaniesStatistics(
    int TotalCompanies,
    int ActiveCompanies,
    List<CompanyDetail> CompanyDetails,
    string? TopPerformingCompany,
    string? LowestPerformingCompany,
    decimal AverageCompanyPerformance
);

public record CompanyDetail(
    int CompanyId,
    string CompanyName,
    int DailyOrderTarget,
    int TotalShifts,
    int ActiveRiders,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    int CompletedShifts,
    int IncompleteShifts,
    int FailedShifts,
    decimal PerformanceScore,
    float TotalWorkingHours
);

public record RidersStatistics(
    int TotalRiders,
    int ActiveRiders,
    int InactiveRiders,
    int RidersWithWorkingId,
    int RidersWithSubstitution,
    decimal AverageShiftsPerRider,
    float TotalWorkingHours
);

public record ShiftsStatistics(
    int TotalShifts,
    int CompletedShifts,
    int IncompleteShifts,
    int FailedShifts,
    decimal CompletionRate,
    float AverageWorkingHoursPerShift,
    float TotalWorkingHours,
    List<DailyShiftBreakdown> DailyBreakdown
);

public record DailyShiftBreakdown(
    DateOnly Date,
    int TotalShifts,
    int CompletedShifts,
    int TotalOrders,
    int AcceptedOrders,
    int RejectedOrders
);

public record OrdersStatistics(
    int TotalOrders,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    int TotalRealRejectedOrders,
    decimal AcceptanceRate,
    decimal RejectionRate,
    decimal AverageOrdersPerShift,
    int ProblematicShiftsCount,
    decimal TotalPenaltyAmount
);

public record PerformanceMetrics(
    decimal OverallPerformanceScore,
    List<TopPerformer> TopPerformers,
    decimal AverageCompletionRate,
    decimal AverageOrdersPerDay
);

public record TopPerformer(
    int RiderId,
    string RiderName,
    int WorkingId,
    int TotalOrders,
    decimal PerformanceScore,
    decimal CompletionRate
);

public record HousingStatistics(
    int TotalHousings,
    int ActiveHousings,
    List<HousingDetail> HousingDetails,
    string? TopPerformingHousing,
    double AverageRidersPerHousing
);

public record HousingDetail(
    int HousingId,
    string HousingName,
    int TotalRiders,
    int TotalShifts,
    int TotalOrders,
    int AcceptedOrders,
    decimal CompletionRate
);

public record TrendsAnalysis(
    List<WeeklyTrend> WeeklyTrends,
    decimal OrdersGrowthRate,
    decimal ShiftsGrowthRate,
    string PerformanceTrend
);

public record WeeklyTrend(
    int WeekNumber,
    int TotalShifts,
    int TotalOrders,
    decimal AveragePerformance
);