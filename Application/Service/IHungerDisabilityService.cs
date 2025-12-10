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
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<HungerDisabilityReportResponse>>> GetAllReportsAsync(
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<HungerDisabilityReportResponse>>> GetReportsByDateAsync(
        DateOnly shiftDate,
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<HungerDisabilityReportResponse>>> GetReportsByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<HungerDisabilityReportResponse>>> GetReportsByRiderAsync(
        string actualWorkingId,
        CancellationToken cancellationToken = default);

    Task<Result<HungerDisabilitySummary>> GetSummaryByRiderAsync(
        string actualWorkingId,
        CancellationToken cancellationToken = default);

    Task<Result<HungerDisabilitySummary>> GetSummaryByDateRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);
}
public record HungerDisabilityReportResponse(
    int Id,
    int ActualRiderId,
    string ActualWorkingId,
    string ActualRiderNameEN,
    string ActualRiderNameAR,
    string RiderStatus,
    int? SubstituteRiderId,
    string? SubstituteWorkingId,
    string? SubstituteRiderNameEN,
    string? SubstituteRiderNameAR,
    bool HasSubstitute,
    DateOnly ShiftDate,
    int Days,
    int CompanyId,
    string CompanyName,
    int AcceptedDailyOrders,
    int DailyTarget,
    bool TargetAchieved,
    int DifferenceFromTarget,
    decimal PerformancePercentage,
    string PerformanceStatus,
    string PerformanceNote,
    DateTime CreatedAt
);

public record HungerDisabilitySummary(
    int TotalRecords,
    int TotalDays,
    int TotalOrders,
    decimal AverageOrdersPerDay,
    int DailyTarget,
    int DaysMetTarget,
    int DaysFailedTarget,
    decimal TargetAchievementRate,
    int DaysWithSubstitute,
    int DaysWithoutSubstitute,
    List<RiderSummaryDetail> RiderDetails,
    List<CompanySummaryDetail> CompanyBreakdown
);

public record RiderSummaryDetail(
    int RiderId,
    string WorkingId,
    string RiderName,
    int TotalRecords,
    int TotalDays,
    int TotalOrders,
    int DaysMetTarget,
    int DaysFailedTarget
);

public record CompanySummaryDetail(
    string CompanyName,
    int TotalRecords,
    int TotalOrders,
    int DaysMetTarget
);

public record HungerDisabilityImportResult(
    int TotalRecords,
    int SuccessCount,
    int ErrorCount,
    List<ImportError> Errors
);

public record TargetAnalysis(
    bool Achieved,
    int Difference,
    decimal Percentage,
    string Status,
    string Note
);

public class ExcelColumnMapping
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public int ActualWorkingIdColumn { get; set; }
    public int ShiftDateColumn { get; set; }
    public int DaysColumn { get; set; }
    public int AcceptedOrdersColumn { get; set; }
}

public static class HungerExcelColumns
{
    public static readonly string[] ActualWorkingIdColumns =
        { "Rider Id", "Working_ID", "معرّف السائق", "ID", "RiderID", "Rider_ID", "EmployeeID" };

    public static readonly string[] DaysColumns =
        { "Working Days", "أيام العمل الفعلية", "ايام", "NumberOfDays", "WorkDays" };

    public static readonly string[] AcceptedOrdersColumns =
        { "Completed Deliveries", "Accepted_Orders", "Accepted Orders", "المهام التي تم تسليمها", "AcceptedDaily", "Accepted_Daily" };
}