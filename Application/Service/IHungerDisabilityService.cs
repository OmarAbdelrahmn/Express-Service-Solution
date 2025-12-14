using Application.Abstraction;
using Application.Service.Riders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service;

public interface IHungerDisabilityService
{
    Task<Result<HungerDisabilityImportResult>> ImportFromExcelAsync(
           Stream excelStream,
           DateOnly shiftDate,
           CancellationToken cancellationToken = default);

    /// <summary>
    /// Get aggregated reports for a date range (sums all days)
    /// </summary>
    Task<Result<IEnumerable<HungerDisabilityAggregatedResponse>>> GetReportsByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get aggregated reports for a specific month
    /// </summary>
    Task<Result<IEnumerable<HungerDisabilityAggregatedResponse>>> GetReportsByMonthAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get aggregated reports for an entire year
    /// </summary>
    Task<Result<IEnumerable<HungerDisabilityAggregatedResponse>>> GetReportsByYearAsync(
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get aggregated report for a specific rider in a date range
    /// </summary>
    Task<Result<HungerDisabilityAggregatedResponse>> GetReportByRiderAndDateRangeAsync(
        string actualWorkingId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get overall summary with top/bottom performers
    /// </summary>
    Task<Result<HungerDisabilityOverallSummary>> GetOverallSummaryAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Aggregated response for disabled riders - sums orders across multiple days
/// Target = TotalDays × 15 orders/day
/// </summary>
/// <summary>
/// Overall summary with top and bottom performers
/// </summary>



/// <summary>
/// Excel import result
/// </summary>
public record HungerDisabilityImportResult(
    int TotalRecords,
    int SuccessCount,
    int ErrorCount,
    List<ImportError> Errors
);

/// <summary>
/// Excel column mapping configuration
/// </summary>
public class ExcelColumnMapping
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public int ActualWorkingIdColumn { get; set; }
    public int DaysColumn { get; set; }
    public int AcceptedOrdersColumn { get; set; }
}

/// <summary>
/// Excel column names (English and Arabic)
/// </summary>
public static class HungerExcelColumns
{
    public static readonly string[] ActualWorkingIdColumns =
        { "Rider Id", "معرّف السائق", "Working_ID", "ID", "RiderID", "Rider_ID", "EmployeeID" };

    public static readonly string[] DaysColumns =
        { "Working Days", "أيام العمل الفعلية", "Days", "ايام", "NumberOfDays", "WorkDays" };

    public static readonly string[] AcceptedOrdersColumns =
        { "Completed Deliveries", "إجمالي الطلبات", "Total Orders", "Accepted_Orders", "المهام التي تم تسليمها", "Orders" };
}

// Updated HungerDisabilityAggregatedResponse
public record HungerDisabilityAggregatedResponse(
    int ActualRiderId,
    string ActualWorkingId,
    string ActualRiderNameEN,
    string ActualRiderNameAR,
    int? SubstituteRiderId,
    string? SubstituteWorkingId,
    string? SubstituteRiderNameEN,
    string? SubstituteRiderNameAR,
    bool HasSubstitute,
    int? HousingId,
    string HousingName,
    int TotalDays,
    int TotalOrders,
    int Target,
    int DifferenceFromTarget,
    decimal PerformancePercentage,
    string PerformanceStatus,
    int LastDayOrders,
    int RecordCount
);

// Updated HungerDisabilityOverallSummary
public record HungerDisabilityOverallSummary(
    int TotalRiders,
    int TotalDays,
    int TotalOrders,
    int TotalTarget,
    int TotalDifference,
    int RidersAboveTarget,
    int RidersBelowTarget,
    int RidersWithSubstitutes,
    int RidersWithoutSubstitutes,
    decimal AverageOrdersPerRider,
    decimal AverageOrdersPerDay,
    decimal OverallPerformanceRate,
    List<HousingSummaryDetail> HousingBreakdown,
    List<HungerDisabilityAggregatedResponse> TopPerformers,
    List<HungerDisabilityAggregatedResponse> BottomPerformers
);

// New Housing Summary Detail
public record HousingSummaryDetail(
    string HousingName,
    int RiderCount,
    int TotalOrders,
    int RidersAboveTarget
);

// Existing Company Summary Detail (unchanged)
public record CompanySummaryDetail(
    string CompanyName,
    int RiderCount,
    int TotalOrders,
    int RidersAboveTarget
);
