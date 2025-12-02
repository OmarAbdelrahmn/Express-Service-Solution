using Application.Abstraction;
using Application.Service.Reports;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using static Application.Service.Riders.RiderShiftService;

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
    Task<Result<BulkComparisonResult>> CreateShiftComparisonsAsync(Stream excelStream,DateOnly shiftDate,int rejectionThreshold = 2, CancellationToken cancellationToken = default);
    Task<Result<BulkImportResult>> ImportShiftsFromExcelAsync(Stream excelStream,DateOnly shiftDate,int rejectionThreshold = 2,CancellationToken cancellationToken = default);
    Task<Result<ResolutionResult>> ResolveShiftComparisonsAsync(ResolveComparisonsRequest request,CancellationToken cancellationToken = default);
    Task<Result<BulkComparisonResult>> GetPendingComparisonsAsync(DateOnly shiftDate,CancellationToken cancellationToken = default);

}


public record CreateRiderShiftRequest(
    int WorkingId,  
    DateOnly ShiftDate,
    int AcceptedDailyOrders,
    int RejectedDailyOrders,
    int StackedDeliveries,
    int RealRejectedDailyOrders,
    float WorkingHours
);

public record UpdateRiderShiftRequest(
    int WorkingId, 
    DateOnly ShiftDate,
    int? AcceptedDailyOrders,
    int? RejectedDailyOrders,
    int? StackedDeliveries,
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
    int StackedDeliveries,
    float WorkingHours,
    int CompanyId,
    string CompanyName,
    string RiderName,
    string ShiftStatus,
    bool HasRejectionProblem,
    decimal PenaltyAmount,
    DateTime CreatedAt,
    bool IsSubstitution,
    int? OriginalWorkingId
);




public record BulkDeleteResult(
    int TotalDeleted,
    List<string> DeletedShiftDetails
);

public record MonthlyRiderReport(
    int RiderId,
    string RiderName,
    int WorkingId,  
    int Year,
    int Month,

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

    List<CompanyPeriodBreakdown> CompanyBreakdowns,

    List<ProblemShiftDetail> ProblematicShifts,

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
    int TotalStackedDeliveries, // ADD THIS
    decimal AverageStackedPerShift,
    float TotalWorkingHours,
    int ProblematicShiftsCount,
    decimal PenaltyAmount,
    decimal PerformanceScore,
    int ExpectedOrders
);

public record YearlyRiderReport(
    int RiderId,
    string RiderName,
    int WorkingId,  
    int Year,

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

    List<YearlyCompanyBreakdown> YearlyCompanyBreakdowns,

    List<MonthlyBreakdown> MonthlyBreakdowns,

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
    int IqamaNo,
    string RiderName,
    int WorkingId,  
    DateOnly StartDate,
    DateOnly EndDate,

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

    List<CompanyPeriodBreakdown> CompanyBreakdowns,

    List<ProblemShiftDetail> ProblematicShifts,

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
    int WorkingId,  
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
    int WorkingId,  
    int TotalShifts,
    int CompletedShifts,
    int TotalAcceptedOrders,
    int ProblematicShiftsCount,
    decimal TotalPenaltyAmount,
    decimal PerformanceScore
);

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
    Absent = 4,
    Average = 5
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

    public static int GetRejectionThreshold(string companyName)
    {
        return companyName switch
        {
            "Jahez" => 2,
            "HungerStation" => 2,
            "Careem" => 3,
            "Marsool" => 2,
            _ => 2
        };
    }


    public static int GetDailyOrderTarget(string companyName)
    {
        return CompanyDailyOrderTargets.TryGetValue(companyName, out var target)
            ? target
            : 15;
    }

    public const int RejectionThreshold = 2;
    public const decimal PenaltyPerExcessRejection = 10.0m;
}

