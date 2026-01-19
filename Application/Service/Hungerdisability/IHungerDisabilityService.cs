using Application.Abstraction;
using Application.Service.Riders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Hungerdisa;

public interface IHungerDisabilityService
{

    Task<Result<DeletionResult>> DeleteAllByDateAsync(
        DateOnly shiftDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a specific rider's record for a specific day
    /// </summary>
    Task<Result<DeletionResult>> DeleteByRiderAndDateAsync(
        string actualWorkingId,
        DateOnly shiftDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all records within a date range
    /// </summary>
    Task<Result<DeletionResult>> DeleteAllByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a specific rider's records within a date range
    /// </summary>
    Task<Result<DeletionResult>> DeleteByRiderAndDateRangeAsync(
        string actualWorkingId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

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

// ========== DELETION DTOs ==========

/// <summary>
/// Result of deletion operations
/// </summary>
public record DeletionResult(
    int DeletedCount,
    string Message,
    List<string> Details
);

/// <summary>
/// Request to delete all records for a specific date
/// </summary>
public record DeleteByDateRequest(
    DateOnly ShiftDate
);

/// <summary>
/// Request to delete a specific rider's record for a specific date
/// </summary>
public record DeleteByRiderAndDateRequest(
    string ActualWorkingId,
    DateOnly ShiftDate
);

/// <summary>
/// Request to delete all records within a date range
/// </summary>
public record DeleteByDateRangeRequest(
    DateOnly StartDate,
    DateOnly EndDate
);

/// <summary>
/// Request to delete a specific rider's records within a date range
/// </summary>
public record DeleteByRiderAndDateRangeRequest(
    string ActualWorkingId,
    DateOnly StartDate,
    DateOnly EndDate
);

/// <summary>
/// Detailed deletion result with affected entities
/// </summary>
public record DetailedDeletionResult(
    int DeletedCount,
    string Message,
    DeletionSummary Summary,
    List<DeletedRecordDetail> DeletedRecords
);

/// <summary>
/// Summary of what was deleted
/// </summary>
public record DeletionSummary(
    int TotalRecords,
    int UniqueRiders,
    int TotalDays,
    int TotalOrders,
    DateOnly? FirstDate,
    DateOnly? LastDate,
    List<string> AffectedRiders
);

/// <summary>
/// Detail of a single deleted record
/// </summary>
public record DeletedRecordDetail(
    int RecordId,
    string ActualWorkingId,
    string RiderName,
    DateOnly ShiftDate,
    int Days,
    int AcceptedOrders,
    bool HasSubstitute,
    string? SubstituteWorkingId
);

/// <summary>
/// Batch deletion request for multiple dates
/// </summary>
public record BatchDeleteRequest(
    List<DateOnly> ShiftDates,
    string? Reason
);

/// <summary>
/// Batch deletion result
/// </summary>
public record BatchDeletionResult(
    int TotalDeleted,
    int SuccessfulDates,
    int FailedDates,
    List<DateDeletionStatus> DateResults
);

/// <summary>
/// Status of deletion for a specific date
/// </summary>
public record DateDeletionStatus(
    DateOnly ShiftDate,
    bool Success,
    int DeletedCount,
    string? ErrorMessage
);

/// <summary>
/// Soft delete request (marks as deleted instead of removing)
/// </summary>
public record SoftDeleteRequest(
    string ActualWorkingId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Reason,
    string DeletedBy
);

/// <summary>
/// Confirmation required deletion request
/// </summary>
public record ConfirmDeleteRequest(
    string ConfirmationToken,
    bool Confirmed,
    string DeletedBy
);

/// <summary>
/// Pre-deletion validation result
/// </summary>
public record DeletionValidationResult(
    bool CanDelete,
    int RecordsToDelete,
    List<string> Warnings,
    List<string> BlockingIssues,
    DeletionImpactAnalysis ImpactAnalysis
);

/// <summary>
/// Analysis of deletion impact
/// </summary>
public record DeletionImpactAnalysis(
    int AffectedReports,
    int AffectedSummaries,
    List<string> AffectedRiders,
    List<string> AffectedCompanies,
    List<string> AffectedHousings,
    bool WillAffectPerformanceMetrics
);

/// <summary>
/// Deletion audit log entry
/// </summary>
public record DeletionAuditLog(
    int Id,
    string DeletedBy,
    DateTime DeletedAt,
    string DeletionType,
    string AffectedEntity,
    int RecordsDeleted,
    string Reason,
    string Details
);