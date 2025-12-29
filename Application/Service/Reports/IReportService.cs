using Application.Abstraction;
using Application.Service.Riders;
using static Application.Service.Reports.ReportService;

namespace Application.Service.Reports;


public interface IReportService
{
    Task<Result<ComprehensiveDashboard>> GetComprehensiveDashboardAsync(
       DateOnly? startDate = null,
       DateOnly? endDate = null,
       CancellationToken cancellationToken = default);



    Task<Result<MonthlyRiderReport>> GetMonthlyReportByWorkingIdAsync(
        string WorkingId,
        int year,
        int month,
        CancellationToken cancellationToken = default);

   
    Task<Result<IEnumerable<MonthlyRiderReport>>> GetAllRidersMonthlyReportAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default);

   
    Task<Result<YearlyRiderReport>> GetYearlyReportByWorkingIdAsync(
        string WorkingId,
        int year,
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<YearlyRiderReport>>> GetAllRidersYearlyReportAsync(
        int year,
        CancellationToken cancellationToken = default);

  
    Task<Result<DateRangeReport>> GetCustomDateRangeReportByWorkingIdAsync(
        string WorkingId,
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
        string WorkingId,
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
        string WorkingId,
        int year1,
        int month1,
        int year2,
        int month2,
        CancellationToken cancellationToken = default);

    Task<Result<RiderPeriodComparison>> CompareRiderYearsAsync(
        string WorkingId,
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

    Task<Result<MonthlyStackedDeliveriesReport>> GetMonthlyStackedDeliveriesByWorkingIdAsync(
       string WorkingId,
       int year,
       int month,
       CancellationToken cancellationToken = default);

    Task<Result<AllRidersStackedDeliveriesReport>> GetAllRidersStackedDeliveriesAsync(
        DateOnly startDate,
        DateOnly endDate,
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

    // Add these methods to the IReportService interface

    /// <summary>
    /// Compare orders between two time periods
    /// Period 1 is automatically calculated as the previous month of Period 2
    /// </summary>
    Task<Result<PeriodOrdersComparison>> ComparePeriodOrdersAsync(
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get daily summary report grouped by housing
    /// </summary>
    Task<Result<HousingDailySummaryReport>> GetHousingDailySummaryAsync(
        DateOnly reportDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get detailed daily report with individual riders grouped by housing
    /// </summary>
    Task<Result<HousingDailyDetailedReport>> GetHousingDailyDetailedReportAsync(
        DateOnly reportDate,
        CancellationToken cancellationToken = default);
    Task<Result<PreviousDayCompanySummary>> GetPreviousDayCompanySummaryAsync(
    CancellationToken cancellationToken = default);

    Task<Result<PreviousDayCompanySummary>> GetHousingPreviousDayCompanySummaryAsync(
        long managerIqamaNo,
    CancellationToken cancellationToken = default);
}

public record PeriodOrdersComparison(
    DateOnly Period1Start,
    DateOnly Period1End,
    DateOnly Period2Start,
    DateOnly Period2End,
    int Period1TotalOrders,
    int Period2TotalOrders,
    int OrdersDifference,
    decimal ChangePercentage,
    string TrendDescription
);

// 2. Housing Daily Summary Report
public record HousingDailySummaryReport(
    DateOnly ReportDate,
    List<HousingDailySummary> HousingSummaries,
    int TotalOrders,
    int TotalRiders,
    decimal AverageOrdersPerRider
);

public record HousingDailySummary(
    int HousingId,
    string HousingName,
    int TotalOrders,
    int ActiveRiders,
    decimal AverageOrdersPerRider,
    decimal PercentageOfTotalOrders
);

// 3. Housing Daily Detailed Report
public record HousingDailyDetailedReport(
    DateOnly ReportDate,
    List<HousingDailyDetails> HousingDetails,
    int GrandTotalOrders,
    int GrandTotalRiders
);

public record HousingDailyDetails(
    int HousingId,
    string HousingName,
    List<RiderDailyPerformance> Riders,
    int HousingTotalOrders,
    int HousingRiderCount,
    decimal PercentageOfCompanyTotal
);

public record RiderDailyPerformance(
    int RiderId,
    string RiderName,
    string WorkingId,
    int AcceptedOrders,
    DateOnly ShiftDate
);
public record RiderPeriodComparison(
    int RiderId,
    string RiderName,
    string WorkingId,
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
    int TotalStackedDeliveries, // ADD THIS

    float TotalWorkingHours,
    int ProblematicShiftsCount,
    decimal TotalPenaltyAmount,
    decimal AverageStackedPerDay, // ADD THIS

    decimal AverageOrdersPerDay,
    decimal CompletionRate,
    decimal PerformanceScore,
    List<CompanyPeriodBreakdown> CompanyBreakdowns
);
public record PreviousDayCompanySummary(
    DateOnly ReportDate,
    CompanyDaySummary Hunger,
    CompanyDaySummary Keta,
    int TotalDayOrders,
    int TotalDayShifts,
    CompanyMonthToDateSummary HungerMonthToDate,
    CompanyMonthToDateSummary KetaMonthToDate,
    int TotalMonthOrders,
    int TotalMonthShifts,
    DateOnly MonthStartDate
);

public record CompanyDaySummary(
    string CompanyName,
    int TotalOrders,
    int TotalShifts,
    int AcceptedOrders,
    int RejectedOrders,
    int CompletedShifts,
    int IncompleteShifts,
    int FailedShifts
);

public record CompanyMonthToDateSummary(
    string CompanyName,
    int TotalOrders,
    int TotalShifts,
    int AcceptedOrders,
    int RejectedOrders,
    int CompletedShifts,
    int IncompleteShifts,
    int FailedShifts,
    int TotalDays
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
    string WorkingId,
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
    string WorkingId,
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
    string WorkingId,
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
    int TotalStackedDeliveries, // ADD THIS
    decimal AverageStackedPerShift, // ADD THIS
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
    string? OriginalWorkingId
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
    TrendsAnalysis Trends,
    VehicleStatistics Vehicle
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
    int RejectedOrders,
    int StackedDeliveries // ADD THIS

);
public record MonthlyStackedDeliveriesReport(
    int RiderId,
    string RiderName,
    string WorkingId,
    int Year,
    int Month,
    int TotalStackedDeliveries,
    int TotalShifts,
    decimal AverageStackedPerShift,
    int MaxStackedInDay,
    DateOnly? MaxStackedDate,
    List<DailyStackedBreakdown> DailyBreakdown
);


public record DailyStackedBreakdown(
    DateOnly Date,
    int StackedDeliveries,
    int AcceptedOrders,
    decimal StackedPercentage
);
public record OrdersStatistics(
    int TotalOrders,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    int TotalRealRejectedOrders,
    int TotalStackedDeliveries, // ADD THIS
    decimal AcceptanceRate,
    decimal RejectionRate,
    decimal StackedDeliveryRate, // ADD THIS
    decimal AverageOrdersPerShift,
    decimal AverageStackedPerShift, // ADD THIS
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
    string WorkingId,
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

public record AllRidersStackedDeliveriesReport(
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalRiders,
    int TotalStackedDeliveries,
    int TotalShifts,
    decimal AverageStackedPerRider,
    List<RiderStackedSummary> RiderSummaries
);

public record RiderStackedSummary(
    int RiderId,
    string RiderName,
    string WorkingId,
    int TotalStackedDeliveries,
    int TotalShifts,
    decimal AverageStackedPerShift,
    int MaxStackedInDay,
    DateOnly? MaxStackedDate,
    decimal TotalStackedPercentage
);

public record WeeklyTrend(
    int WeekNumber,
    int TotalShifts,
    int TotalOrders,
    decimal AveragePerformance
);