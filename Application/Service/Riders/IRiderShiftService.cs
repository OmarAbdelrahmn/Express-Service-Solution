using Application.Abstraction;
using Application.Service.Reports;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Riders;

public interface IRiderShiftService
{
    Task<Result<RiderShiftResponse>> CreateShiftAsync(CreateRiderShiftRequest request, CancellationToken cancellationToken = default);
    Task<Result<RiderShiftResponse>> GetShiftAsync(int workingId, DateOnly shiftDate, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<RiderShiftResponse>>> GetShiftsByRiderAsync(int WorkingId, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<RiderShiftResponse>>> GetShiftsByDateAsync(DateOnly shiftDate, CancellationToken cancellationToken = default);
    Task<Result<RiderShiftResponse>> UpdateShiftAsync(UpdateRiderShiftRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteShiftAsync(int workingId, DateOnly shiftDate, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<RiderShiftResponse>>> GetShiftsByDateRangeAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
    Task<Result<BulkDeleteResult>> DeleteShiftsByDateAsync(DateOnly shiftDate, CancellationToken cancellationToken = default);
    Task<Result<BulkDeleteResult>> DeleteShiftsByDateRangeAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
    Task<Result<BulkDeleteResult>> DeleteShiftsByRiderAndDateRangeAsync(int workingId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
    Task<Result<BulkImportResult>> ImportShiftsFromExcelAsync(Stream excelStream, CancellationToken cancellationToken = default);
}


public record CreateRiderShiftRequest(
    int WorkingId,  // Current WorkingId
    DateOnly ShiftDate,
    int AcceptedDailyOrders,
    int RejectedDailyOrders,
    int RealRejectedDailyOrders,
    float WorkingHours
);

public record UpdateRiderShiftRequest(
    int WorkingId,  // Current WorkingId
    DateOnly ShiftDate,
    int? AcceptedDailyOrders,
    int? RejectedDailyOrders,
    int? RealRejectedDailyOrders,
    float? WorkingHours
);

public record RiderShiftResponse(
    int RiderId,
    int WorkingId,
    DateOnly ShiftDate,
    int AcceptedDailyOrders,
    int RejectedDailyOrders,
    int RealRejectedDailyOrders,
    float WorkingHours,
    int CompanyId,
    string CompanyName,
    string RiderName,
    ShiftStatus ShiftStatus,
    bool HasRejectionProblem,
    decimal PenaltyAmount,
    DateTime CreatedAt,
    bool IsSubstitution,
    int? OriginalWorkingId
);

public record BulkImportResult(
    int TotalRecords,
    int SuccessfulImports,
    int FailedImports,
    List<ImportError> Errors
);

public record ImportError(
    int RowNumber,
    string WorkingId,
    string ErrorMessage
);

public record BulkDeleteResult(
    int TotalDeleted,
    List<string> DeletedShiftDetails
);

// ✅ REPORTS SHOW CURRENT WORKINGID + HISTORY (IF ANY)
public record MonthlyRiderReport(
    int RiderId,
    string RiderName,
    int WorkingId,  // ✅ Current WorkingId - what admin uses
    int Year,
    int Month,

    // Overall totals (combined across all companies and historical WorkingIds)
    int TotalWorkingDays,
    int CompletedShifts,
    int IncompleteShifts,
    int FailedShifts,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    int TotalRealRejectedOrders,
    float TotalWorkingHours,
    int ProblematicShiftsCount,
    decimal TotalPenaltyAmount,
    decimal OverallPerformanceScore,

    // Per-company breakdown
    List<CompanyPeriodBreakdown> CompanyBreakdowns,

    // Problematic shifts
    List<ProblemShiftDetail> ProblematicShifts,

    // WorkingId change history (optional - only if changed during period)
    List<WorkingIdPeriod> WorkingIdHistory
);

public record CompanyPeriodBreakdown(
    string CompanyName,
    int DailyOrderTarget,
    int WorkingDays,
    int CompletedShifts,
    int IncompleteShifts,
    int FailedShifts,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    int TotalRealRejectedOrders,
    float TotalWorkingHours,
    int ProblematicShiftsCount,
    decimal PenaltyAmount,
    decimal PerformanceScore,
    int ExpectedOrders
);

public record YearlyRiderReport(
    int RiderId,
    string RiderName,
    int WorkingId,  // ✅ Current WorkingId
    int Year,

    // Overall totals
    int TotalWorkingDays,
    int CompletedShifts,
    int IncompleteShifts,
    int FailedShifts,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    int TotalRealRejectedOrders,
    float TotalWorkingHours,
    int ProblematicShiftsCount,
    decimal TotalPenaltyAmount,
    decimal AveragePerformanceScore,

    // Per-company breakdown for the year
    List<YearlyCompanyBreakdown> YearlyCompanyBreakdowns,

    // Monthly breakdowns
    List<MonthlyBreakdown> MonthlyBreakdowns,

    // WorkingId change history (optional)
    List<WorkingIdPeriod> WorkingIdHistory
);

public record YearlyCompanyBreakdowns(
    string CompanyName,
    int DailyOrderTarget,
    int TotalWorkingDays,
    int CompletedShifts,
    int IncompleteShifts,
    int FailedShifts,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    int TotalRealRejectedOrders,
    float TotalWorkingHours,
    int ProblematicShiftsCount,
    decimal TotalPenaltyAmount,
    decimal AveragePerformanceScore,
    int ExpectedOrders,
    List<MonthlyCompanyData> MonthlyDetails
);

public record MonthlyCompanyData(
    int Month,
    int WorkingDays,
    int AcceptedOrders,
    int RejectedOrders
);

public record MonthlyBreakdown(
    int Month,
    int WorkingDays,
    int CompletedShifts,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    decimal PerformanceScore,
    List<CompanyPeriodBreakdown> CompanyBreakdowns
);
public record DateRangeReport(
    int RiderId,
    string RiderName,
    int WorkingId,  // ✅ Current WorkingId
    DateOnly StartDate,
    DateOnly EndDate,

    // Overall totals
    int TotalWorkingDays,
    int CompletedShifts,
    int IncompleteShifts,
    int FailedShifts,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    int TotalRealRejectedOrders,
    float TotalWorkingHours,
    int ProblematicShiftsCount,
    decimal TotalPenaltyAmount,
    decimal OverallPerformanceScore,

    // Per-company breakdown
    List<CompanyPeriodBreakdown> CompanyBreakdowns,

    List<ProblemShiftDetail> ProblematicShifts,

    // WorkingId change history (optional)
    List<WorkingIdPeriod> WorkingIdHistory
);

public record CompanyPerformanceReport(
    int CompanyId,
    string CompanyName,
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalRiders,
    int TotalShifts,
    int CompletedShifts,
    int IncompleteShifts,
    int FailedShifts,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    int TotalRealRejectedOrders,
    float TotalWorkingHours,
    int ProblematicShiftsCount,
    decimal TotalPenaltyAmount,
    decimal CompanyPerformanceScore,
    List<RiderPerformanceSummary> TopPerformers,
    List<RiderPerformanceSummary> LowPerformers
);

public record ProblemShiftDetail(
    int RiderId,
    string RiderName,
    int WorkingId,  // Historical WorkingId for this shift
    DateOnly ShiftDate,
    string CompanyName,
    int AcceptedOrders,
    int RejectedOrders,
    int RealRejectedOrders,
    string Status,
    decimal PenaltyAmount,
    string ProblemDescription
);


public record RiderPerformanceSummary(
    int RiderId,
    string RiderName,
    int WorkingId,  // Current WorkingId
    int TotalShifts,
    int CompletedShifts,
    int TotalAcceptedOrders,
    int ProblematicShiftsCount,
    decimal TotalPenaltyAmount,
    decimal PerformanceScore
);

// NEW: Tracks WorkingId changes during a period
public record WorkingIdPeriod(
    int WorkingId,
    DateOnly StartDate,
    DateOnly EndDate,
    int ShiftCount
);

public enum ShiftStatus
{
    Completed = 1,
    Incomplete = 2,
    Failed = 3,
    Absent = 4
}

public class CompanyShiftConfiguration
{
    public static readonly Dictionary<string, int> CompanyDailyOrderTargets = new()
    {
        { "Jahez", 15 },
        { "Mrsool", 15 },
        { "HungerStation", 14 },
        { "Careem", 14 },
        { "ToYou", 15 }
    };

    public static int GetDailyOrderTarget(string companyName)
    {
        return CompanyDailyOrderTargets.TryGetValue(companyName, out var target)
            ? target
            : 15;
    }

    public const int RejectionThreshold = 2;
    public const decimal PenaltyPerExcessRejection = 10.0m;
}