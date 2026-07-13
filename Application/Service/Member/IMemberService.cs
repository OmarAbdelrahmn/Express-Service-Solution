using Application.Abstraction;
using Application.Contracts.InventoryAudit;
using Application.Contracts.RiderAccessoryCon;
using Application.Contracts.SparePartCo;
using Application.Contracts.SupplierCon;
using Application.Service.Reports;
using Domain.Entities.Spare;
using static Application.Service.Member.MemberService;

namespace Application.Service.Member;

public interface IMemberService
{
    /// <summary>
    /// Get all spare-part and accessory usage records whose location is this housing
    /// </summary>
    Task<Result<HousingUsageHistoryResponse>> GetHousingUsageHistoryAsync(
        long managerIqamaNo,
        DateTime? fromDate = null,
        DateTime? toDate = null);

    /// <summary>
    /// Get every manual spare-part / rider-accessory change (quantity, price,
    /// location, etc.) made at this housing — who did it, when, and the
    /// before/after values — regardless of who performed the edit.
    /// </summary>
    Task<Result<IEnumerable<InventoryAuditLogResponse>>> GetHousingInventoryAuditLogAsync(
        long managerIqamaNo,
        DateTime? fromDate = null,
        DateTime? toDate = null);

    Task<Result<HousingDetailedDailyPerformanceReport>> GetHousingDetailedDailyPerformanceForManagerAsync(
    long managerIqamaNo,
    DateOnly startDate,
    DateOnly endDate,
    CancellationToken cancellationToken = default);
    // Add to IMemberService interface

    /// <summary>
    /// Transfer spare parts and accessories from housing to another housing or back to main company
    /// </summary>
    Task<Result<TransferResponse>> TransferFromHousingAsync(
        long managerIqamaNo,
        MemberTransferRequest request);

    /// <summary>
    /// Get all transfers made by this housing
    /// </summary>
    Task<Result<IEnumerable<TransferResponse>>> GetHousingTransfersAsync(
        long managerIqamaNo);

    // Add these records at the end of IMemberService.cs

    public record MemberTransferRequest(
        int? ToHousingId,  // null means transfer to main company "الشركة"
        List<MemberTransferItemRequest> Items
    );

    public record MemberTransferItemRequest(
        int ItemId,
        TransferItemType ItemType,
        int Quantity
    );

    /// <summary>
    /// Request to switch rider's current vehicle to a new one
    /// </summary>
    Task<Result> RequestSwitchVehicleForHousingAsync(
        long managerIqamaNo,
        MemberSwitchVehicleRequest request);

    /// <summary>
    /// Get all pending switch vehicle requests for the housing
    /// </summary>
    Task<Result<List<PendingSwitchVehicleResponse>>> GetPendingSwitchVehicleRequests(
        long managerIqamaNo);

    // Request DTOs at the end of IMemberService.cs
    public record MemberSwitchVehicleRequest(
        long RiderIqamaNo,
        string NewVehiclePlate,
        string Reason
    );

    public record PendingSwitchVehicleResponse(
        int Id,
        long RiderIqamaNo,
        string RiderName,
        string CurrentVehicleNumber,
        string CurrentVehiclePlate,
        string NewVehicleNumber,
        string NewVehiclePlate,
        string Reason,
        DateTime RequestedAt,
        string RequestedBy,
        VehicleSwitchValidation Validation
    );

    public record VehicleSwitchValidation(
        bool IsValid,
        List<string> Errors,
        List<string> Warnings
    );
    Task<Result> RequestFixVehicleProblemForHousingAsync(long managerIqamaNo, MemberFixVehicleRequest request);
    Task<Result<List<HousingProblemVehicleResponse>>> GetHousingProblemVehicles(long managerIqamaNo);
    Task<Result<HousingRiderDailyDetailReport>> GetHousingRiderDailyDetailReportAsync(
    long managerIqamaNo,
    string workingId,
    DateOnly startDate,
    DateOnly endDate,
    CancellationToken cancellationToken = default);


    // Cancel Requests
    Task<Result> CancelVehicleOperationRequestAsync(long managerIqamaNo, int requestId);
    Task<Result> CancelEmployeeStatusChangeRequestAsync(long managerIqamaNo, int requestId);

    Task<Result> CancelRequestAsync(long managerIqamaNo, RequestType requestType, int requestId);

    public enum RequestType
    {
        VehicleOperation = 1,
        EmployeeStatusChange = 2
    }

    Task<Result<HousingAllRidersSummaryReport>> GetHousingAllRidersSummaryReportAsync(
        long managerIqamaNo,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    Task<Result<HousingRejectionReport>> GetHousingRejectionReportAsync(
        long managerIqamaNo,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    Task<Result<RiderDailyDetailReport>> GetRiderDailyDetailReportAsync(
    long managerIqamaNo,
    string workingId,
    DateOnly startDate,
    DateOnly endDate);

    /// <summary>
    /// Get summary report for all riders in housing
    /// </summary>
    Task<Result<AllRidersSummaryReport>> GetAllRidersSummaryReportAsync(
        long managerIqamaNo,
        DateOnly startDate,
        DateOnly endDate);

    /// <summary>
    /// Get rejection report for all riders in housing
    /// </summary>
    Task<Result<RejectionReport>> GetRejectionReportAsync(
        long managerIqamaNo,
        DateOnly startDate,
        DateOnly endDate);

    Task<Result<PeriodOrdersComparison>> ComparePeriodOrdersAsync(
    long managerIqamaNo,
    DateOnly period2Start,
    DateOnly period2End);

    /// <summary>
    /// Get daily summary report grouped by housing (housing-specific)
    /// </summary>
    Task<Result<HousingDailySummary>> GetHousingDailySummaryAsync(
        long managerIqamaNo,
        DateOnly reportDate);

    /// <summary>
    /// Get detailed daily report with individual riders in housing
    /// </summary>
    Task<Result<HousingDailyDetailedReport>> GetHousingDailyDetailedReportAsync(
        long managerIqamaNo,
        DateOnly reportDate);
    Task<Result<MemberAuthResponse>> MemberSignInAsync(MemberAuthRequest request);
    Task<Result<HousingDashboardResponse>> GetHousingDashboard(long managerIqamaNo);
    Task<Result<HousingDetailResponse>> GetHousingDetails(long managerIqamaNo);

    // Employees & Riders
    Task<Result<List<HousingEmployeeResponse>>> GetHousingEmployees(long managerIqamaNo);
    Task<Result<List<HousingRiderResponses>>> GetHousingRiders(long managerIqamaNo);
    Task<Result<EmployeeDetailResponse>> GetEmployeeDetails(long managerIqamaNo, long employeeIqamaNo);

    // Shifts & Performance
    Task<Result<List<RiderShiftResponse>>> GetRiderShifts(long managerIqamaNo, DateOnly? startDate, DateOnly? endDate);
    Task<Result<RiderPerformanceResponse>> GetRiderPerformance(long managerIqamaNo, int riderId, DateOnly startDate, DateOnly endDate);
    Task<Result<HousingShiftSummaryResponse>> GetHousingShiftSummary(long managerIqamaNo, DateOnly date);

    // Vehicles
    Task<Result<List<HousingVehicleResponse>>> GetHousingVehicles(long managerIqamaNo);
    Task<Result<List<VehicleStatusHistoryResponse>>> GetVehicleStatusHistory(long managerIqamaNo, string vehicleNumber);
    Task<Result<List<PendingVehicleOperationResponse>>> GetPendingVehicleOperations(long managerIqamaNo);

    // Disabilities & Substitutions
    Task<Result<List<HungerDisabilityResponse>>> GetHousingDisabilities(long managerIqamaNo, DateOnly? startDate, DateOnly? endDate);
    Task<Result<List<ShiftSubstitutionResponse>>> GetActiveSubstitutions(long managerIqamaNo);

    // Pending Requests
    Task<Result<List<PendingEmployeeUpdateResponse>>> GetPendingEmployeeUpdates(long managerIqamaNo);
    Task<Result<List<PendingStatusChangeResponse>>> GetPendingStatusChanges(long managerIqamaNo);

    // Reports
    Task<Result<HousingMonthlyReportResponse>> GetMonthlyReport(long managerIqamaNo, int year, int month);
    Task<Result<byte[]>> ExportHousingReport(long managerIqamaNo, DateOnly startDate, DateOnly endDate);
    Task<Result> RequestTakeVehicleForHousingAsync(long managerIqamaNo, MemberVehicleOperationRequest request);
    Task<Result> RequestReturnVehicleForHousingAsync(long managerIqamaNo, MemberVehicleOperationRequest request);
    Task<Result> RequestReportProblemForHousingAsync(long managerIqamaNo, MemberVehicleOperationRequest request);

    // Member Employee Status Change
    Task<Result> RequestEmployeeStatusChangeForHousingAsync(long managerIqamaNo, MemberStatusChangeRequest request);

    //special reports
    Task<Result<RiderMonthlyHistory>> GetRiderMonthlyHistoryForHousingAsync(
    long managerIqamaNo,
    long riderIqamaNo,
    CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<SparePartResponse>>> GetHousingSparePartsAsync(long managerIqamaNo);
    Task<Result<SparePartResponse>> GetSparePartByIdAsync(long managerIqamaNo, int id);
    Task<Result<IEnumerable<SparePartResponse>>> SearchSparePartsAsync(long managerIqamaNo, string keyword);

    // Spare Parts Usage
    Task<Result<BatchUsageResponse>> RecordBatchSparePartUsageAsync(DateTime Date,
        long managerIqamaNo,
        MemberBatchSparePartUsageRequest request);

    Task<Result<IEnumerable<SparePartUsageResponse>>> GetSparePartUsageHistoryAsync(
        long managerIqamaNo,
        int sparePartId);

    Task<Result<IEnumerable<SparePartUsageResponse>>> GetVehicleSparePartHistoryAsync(
        long managerIqamaNo,
        string vehicleNumber);

    // Rider Accessories Management
    Task<Result<IEnumerable<RiderAccessoryResponse>>> GetHousingAccessoriesAsync(long managerIqamaNo);
    Task<Result<RiderAccessoryResponse>> GetAccessoryByIdAsync(long managerIqamaNo, int id);
    Task<Result<IEnumerable<RiderAccessoryResponse>>> SearchAccessoriesAsync(long managerIqamaNo, string keyword);

    // Rider Accessories Usage
    Task<Result<BatchUsageResponse>> RecordBatchAccessoryUsageAsync(
        DateTime Date,
        long managerIqamaNo,
        MemberBatchAccessoryUsageRequest request);

    Task<Result<IEnumerable<RiderAccessoryUsageResponse>>> GetAccessoryUsageHistoryAsync(
        long managerIqamaNo,
        int accessoryId);

    Task<Result<IEnumerable<RiderAccessoryUsageResponse>>> GetRiderAccessoryHistoryAsync(
        long managerIqamaNo,
        int riderId);

    // Cost Tracking
    Task<Result<MemberVehicleCostResponse>> GetVehicleCostAsync(
        long managerIqamaNo,
        string vehicleNumber);

    Task<Result<MemberVehicleCostResponse>> GetVehicleCostByDateRangeAsync(
        long managerIqamaNo,
        string vehicleNumber,
        DateTime fromDate,
        DateTime toDate);

    Task<Result<MemberRiderCostResponse>> GetRiderCostAsync(
        long managerIqamaNo,
        int riderId);

    Task<Result<MemberRiderCostResponse>> GetRiderCostByDateRangeAsync(
        long managerIqamaNo,
        int riderId,
        DateTime fromDate,
        DateTime toDate);

    Task<Result<MemberHousingCostSummaryResponse>> GetHousingCostSummaryAsync(
        long managerIqamaNo,
        DateTime fromDate,
        DateTime toDate);

    /// <summary>
    /// Update rider's company assignment
    /// </summary>
    Task<Result<UpdateRiderCompanyResponse>> UpdateRiderCompanyAsync(
        long managerIqamaNo,
        MemberUpdateRiderCompanyRequest request);

    /// <summary>
    /// Get detailed spending report for spare parts (per vehicle) and accessories (per rider)
    /// over a date range
    /// </summary>
    Task<Result<HousingSpendingReportResponse>> GetHousingSpendingReportAsync(
        long managerIqamaNo,
        DateOnly startDate,
        DateOnly endDate);


}
// Add these records at the end of the IMemberService.cs file

public record HousingUsageHistoryResponse(
    string HousingName,
    int TotalSparePartUsages,
    int TotalAccessoryUsages,
    decimal TotalSparePartsCost,
    decimal TotalAccessoriesCost,
    decimal GrandTotal,
    List<SparePartUsageResponse> SparePartUsages,
    List<RiderAccessoryUsageResponse> AccessoryUsages
);

public record HousingSpendingReportResponse(
    DateOnly StartDate,
    DateOnly EndDate,
    string HousingName,
    decimal TotalSparePartsCost,
    decimal TotalAccessoriesCost,
    decimal GrandTotal,
    List<VehicleSpendingDetail> VehicleSpending,
    List<RiderSpendingDetail> RiderSpending
);

public record VehicleSpendingDetail(
    string VehicleNumber,
    string VehiclePlate,
    decimal TotalCost,
    List<SparePartSpendingItem> SparePartUsages
);

public record SparePartSpendingItem(
    int SparePartId,
    string SparePartName,
    int TotalQuantityUsed,
    decimal UnitPrice,
    decimal TotalCost,
    List<DateTime> UsageDates
);

public record RiderSpendingDetail(
    int RiderId,
    string RiderNameAR,
    string RiderNameEN,
    string WorkingId,
    decimal TotalCost,
    List<AccessorySpendingItem> AccessoryUsages
);

public record AccessorySpendingItem(
    int AccessoryId,
    string AccessoryName,
    int TotalQuantityIssued,
    decimal UnitPrice,
    decimal TotalCost,
    List<DateTime> IssuanceDates
);
public record MemberBatchSparePartUsageRequest(
    List<MemberSparePartUsageItem> Usages
);

public record MemberSparePartUsageItem(
    int SparePartId,
    string VehicleNumber,
    int QuantityUsed
);

public record MemberBatchAccessoryUsageRequest(
    List<MemberAccessoryUsageItem> Usages
);

public record MemberAccessoryUsageItem(
    int AccessoryId,
    int RiderId
);

// Member-specific response DTOs
public record MemberVehicleCostResponse(
    string VehicleNumber,
    string VehiclePlate,
    decimal TotalSparePartsCost,
    decimal TotalCost,
    List<CostItemDetail> SparePartDetails
);

public record MemberRiderCostResponse(
    int RiderId,
    long RiderIqamaNo,
    string RiderNameEN,
    string RiderNameAR,
    string WorkingId,
    decimal TotalAccessoriesCost,
    List<CostItemDetail> AccessoryDetails
);

public record MemberHousingCostSummaryResponse(
    string HousingName,
    decimal TotalSparePartsCost,
    decimal TotalAccessoriesCost,
    decimal GrandTotal,
    DateTime FromDate,
    DateTime ToDate,
    int TotalVehicles,
    int TotalRiders,
    List<VehicleCostSummaryItem> VehicleCosts,
    List<RiderCostSummaryItem> RiderCosts
);

public record VehicleCostSummaryItem(
    string VehicleNumber,
    string VehiclePlate,
    decimal TotalCost
);

public record RiderCostSummaryItem(
    int RiderId,
    string RiderName,
    string WorkingId,
    decimal TotalCost
);

public record RiderMonthlyHistory(
    long IqamaNo,
    string RiderName,
    string WorkingId,
    DateOnly FirstShiftDate,
    DateOnly LastShiftDate,
    int TotalMonths,
    List<MonthlyShiftSummary> MonthlyData
);

public record RiderMonthlyHistorys(
    long IqamaNo,
    string RiderName,
    string WorkingId,
    DateOnly FirstShiftDate,
    DateOnly LastShiftDate,
    int TotalMonths,
    int ActiveMonthsCount,
    decimal AverageOrdersPerActiveMonth,
    List<int> ActiveMonthNumbers,
    List<MonthlyShiftSummary> MonthlyData
);

public record MonthlyShiftSummary(
    int Year,
    int Month,
    string MonthName,
    int TotalShifts,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    int TotalRealRejectedOrders,
    float TotalWorkingHours,
    int CompletedShifts,
    int IncompleteShifts,
    int FailedShifts,
    decimal CompletionRate
);
public record RiderDailyDetailReport(
    int RiderId,
    long IqamaNo,
    string RiderNameAR,
    string RiderNameEN,
    string WorkingId,
    DateOnly StartDate,
    DateOnly EndDate,
    List<DailyShiftDetail> DailyDetails,
    int TotalWorkingDays,
    int MissingDays,
    float TotalWorkingHours,
    float TargetWorkingHours,
    float HoursDifference,
    bool IsAboveTarget,
    int TotalOrders,
    int TotalRejections,
    int TotalRealRejections
);

public record DailyShiftDetail(
    DateOnly Date,
    bool HasShift,
    int AcceptedOrders,
    int RejectedOrders,
    int RealRejectedOrders,
    float WorkingHours,
    float TargetHours,
    float HoursDifference,
    string ShiftStatus
);

// ============================================
// ALL RIDERS SUMMARY REPORT
// ============================================
public record AllRidersSummaryReport(
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalExpectedDays,
    List<RiderSummaryDetail> RiderSummaries,
    SummaryTotals Totals
);

public record RiderSummaryDetail(
    int RiderId,
    long IqamaNo,
    string RiderNameAR,
    string RiderNameEN,
    string WorkingId,
    int ActualWorkingDays,
    int MissingDays,
    float TotalWorkingHours,
    float TargetWorkingHours,
    float HoursDifference,
    int TotalOrders,
    int TargetOrders,
    int OrdersDifference
);

public record SummaryTotals(
    int TotalRiders,
    int TotalWorkingDays,
    int TotalMissingDays,
    float TotalWorkingHours,
    float TotalTargetHours,
    float HoursDifference,
    int TotalOrders,
    int TotalTargetOrders,
    int OrdersDifference
);

// ============================================
// REJECTION REPORT
// ============================================
public record RejectionReport(
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalDays,
    List<RiderRejectionDetail> RiderDetails,
    RejectionTotals Totals
);

public record RiderRejectionDetail(
    int RiderId,
    long IqamaNo,
    string RiderNameAR,
    string RiderNameEN,
    string WorkingId,
    int TotalShifts,
    int TotalOrders,
    int TargetOrders,
    int TotalRejections,
    int TotalRealRejections,
    decimal RejectionRate,
    decimal RealRejectionRate
);

public record RejectionTotals(
    int TotalRiders,
    int TotalShifts,
    int TotalOrders,
    int TotalTargetOrders,
    int TotalRejections,
    int TotalRealRejections,
    decimal OverallRejectionRate,
    decimal OverallRealRejectionRate
);

// ============================================
// HOUSING-SPECIFIC REPORT TYPES
// ============================================
public record HousingRiderDailyDetailReport(
    string HousingName,
    RiderDailyDetailReport RiderReport
);

public record HousingAllRidersSummaryReport(
    string HousingName,
    AllRidersSummaryReport SummaryReport
);

public record HousingRejectionReport(
    string HousingName,
    RejectionReport RejectionReport
);
public record MemberAuthRequest(
    long IqamaNo,
    string Password
);

public record MemberVehicleOperationRequest(
    long RiderIqamaNo,
    string VehiclePlate,
    string? Reason = null
);

public record MemberStatusChangeRequest(
    long EmployeeIqamaNo,
    string NewStatus,
    string Reason
);

public record MemberAuthResponse(
    string Id,
    long IqamaNo,
    string FullName,
    string Token,
    int ExpiresIn,
    HousingBasicInfo? HousingInfo
);

public record HousingBasicInfo(
    int HousingId,
    string HousingName,
    string Address,
    int Capacity,
    int EmployeeCount
   );



// Dashboard
public record HousingDashboardResponse(
    HousingInfo Housing,
    Statistics Stats,
    List<RecentActivityItem> RecentActivities,
    PreviousDayCompanySummary Summary
);

public record HousingInfo(
    int Id,
    string Name,
    string Address,
    int Capacity,
    int CurrentOccupancy,
    int AvailableSpace
);

public record Statistics(
    int TotalEmployees,
    int ActiveRiders,
    int InactiveRiders,
    int TotalVehicles,
    int VehiclesInUse,
    int VehiclesAvailable,
    int PendingRequests,
    int ActiveDisabilities,
    int TodayShifts
);

public record RecentActivityItem(
    string Type,
    string Description,
    DateTime Timestamp
);

// Housing Details
public record HousingDetailResponse(
    int Id,
    string Name,
    string Address,
    int Capacity,
    int CurrentOccupancy,
    long? ManagerIqamaNo,
    string? ManagerName,
    List<EmployeeSummary> Employees
);

public record EmployeeSummary(
    long IqamaNo,
    string NameEN,
    string NameAR,
    string JobTitle,
    string Status,
    bool IsRider,
    string? WorkingId
);

// Employees
public record HousingEmployeeResponse(
    long IqamaNo,
    string NameEN,
    string NameAR,
    string JobTitle,
    string Country,
    string Phone,
    string Status,
    DateOnly IqamaEndM,
    DateOnly IqamaEndH,
    bool IsRider,
    string? WorkingId,
    int? CompanyId,
    string? CompanyName
);

public record EmployeeDetailResponse(
    long IqamaNo,
    string NameEN,
    string NameAR,
    string JobTitle,
    string Country,
    string Phone,
    DateOnly DateOfBirth,
    string Status,
    DateOnly IqamaEndM,
    DateOnly IqamaEndH,
    string? PassportNo,
    DateOnly? PassportEnd,
    string Sponsor,
    long SponsorNo,
    string? IBAN,
    bool INKSA,
    bool IsEmployee,
    int? HousingId,
    string? HousingName,
    RiderInfo? RiderInfo,
    DocumentInfo? Documents
);

public record RiderInfo(
    int RiderId,
    string? WorkingId,
    string? TshirtSize,
    string? LicenseNumber,
    int CompanyId,
    string CompanyName,
    string? VehicleNumber,
    DateTime CreatedAt
);

public record DocumentInfo(
    string? ProfileImagePath,
    string? PassportImagePath,
    string? IqamaImagePath,
    string? LicenseImagePath,
    string? WorkPermitImagePath
);

// Riders
public record HousingRiderResponse(
    int RiderId,
    long EmployeeIqamaNo,
    string NameEN,
    string NameAR,
    string? WorkingId,
    int CompanyId,
    string CompanyName,
    string? VehicleNumber,
    string? VehiclePlate,
    string Status,
    string Phone,
    DateTime CreatedAt
);

public record HousingRiderResponses(
    int RiderId,
    long EmployeeIqamaNo,
    string NameEN,
    string NameAR,
    string? WorkingId,
    int CompanyId,
    string CompanyName,
    string? VehicleNumber,
    string? VehiclePlate,
    string Status,
    string Phone,
    DateTime CreatedAt,
    string? StatusChangeReason  // NEW: Reason from TempEmployeeStatusChange if status is not "enable"
);
// Shifts
public record RiderShiftResponse(
    int RiderId,
    string WorkingId,
    string RiderName,
    DateOnly ShiftDate,
    int AcceptedDailyOrders,
    int RejectedDailyOrders,
    int StackedDeliveries,
    int RealRejectedDailyOrders,
    float WorkingHours,
    string ShiftStatus,
    int CompanyId,
    string CompanyName,
    DateTime CreatedAt
);

public record RiderPerformanceResponse(
    int RiderId,
    string WorkingId,
    string RiderName,
    DateOnly StartDate,
    DateOnly EndDate,
    PerformanceMetrics Metrics,
    List<DailyPerformance> DailyBreakdown
);

public record PerformanceMetrics(
    int TotalShifts,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    int TotalStackedDeliveries,
    float TotalWorkingHours,
    float AverageOrdersPerShift,
    float AverageWorkingHours,
    float AcceptanceRate
);

public record DailyPerformance(
    DateOnly Date,
    int AcceptedOrders,
    int RejectedOrders,
    float WorkingHours,
    string Status
);

public record HousingShiftSummaryResponse(
    DateOnly Date,
    int TotalRiders,
    int ActiveRiders,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    float TotalWorkingHours,
    List<RiderShiftSummary> RiderShifts
);

public record RiderShiftSummary(
    int RiderId,
    string WorkingId,
    string RiderName,
    int AcceptedOrders,
    int RejectedOrders,
    float WorkingHours,
    string Status
);

// Vehicles
public record HousingVehicleResponse(
    string VehicleNumber,
    string VehicleType,
    string PlateNumberA,
    string PlateNumberE,
    int ManufactureYear,
    string Manufacturer,
    DateOnly LicenseExpiryDate,
    string Location,
    string? CurrentStatus,
    long? AssignedRiderIqamaNo,
    string? AssignedRiderName,
    string? AssignedRiderNameE,
    DateTime? StatusTimestamp
);

public record VehicleStatusHistoryResponse(
    int Id,
    string VehicleNumber,
    long? EmployeeIqamaNo,
    string? EmployeeName,
    string StatusType,
    string? Reason,
    string? Permission,
    DateTime? PermissionStartDate,
    DateTime? PermissionEndDate,
    DateTime Timestamp,
    bool IsActive
);

public record PendingVehicleOperationResponse(
    int Id,
    long RiderIqamaNo,
    string RiderName,
    string VehicleNumber,
    string VehiclePlate,
    string OperationType,
    string? Reason,
    string? Permission,
    DateTime? PermissionEndDate,
    DateTime RequestedAt,
    string RequestedBy
);

// Disabilities
public record HungerDisabilityResponse(
    int Id,
    int ActualRiderId,
    string ActualWorkingId,
    string ActualRiderName,
    int? SubstituteRiderId,
    string? SubstituteWorkingId,
    string? SubstituteRiderName,
    int Days,
    DateOnly ShiftDate,
    int CompanyId,
    string CompanyName,
    int AcceptedDailyOrders,
    DateTime CreatedAt
);

public record ShiftSubstitutionResponse(
    int Id,
    int? ActualRiderId,
    string ActualRiderWorkingId,
    string? ActualRiderName,
    int SubstituteRiderId,
    string SubstituteWorkingId,
    string SubstituteRiderName,
    DateTime StartDate,
    DateTime? EndDate,
    string? Reason,
    string CreatedBy,
    bool IsActive
);

// Pending Requests
public record PendingEmployeeUpdateResponse(
    int Id,
    long IqamaNo,
    string EmployeeName,
    bool IsNewEmployee,
    List<FieldChange> Changes,
    DateTime UploadedAt,
    string? UploadedBy
);

public record FieldChange(
    string FieldName,
    string? OldValue,
    string? NewValue
);

public record PendingStatusChangeResponse(
    int Id,
    long EmployeeIqamaNo,
    string EmployeeName,
    string Action,
    string Reason,
    string RequestedBy,
    DateTime RequestedAt
);

// Reports
public record HousingMonthlyReportResponse(
    int HousingId,
    string HousingName,
    int Year,
    int Month,
    MonthlyStatistics Statistics,
    List<RiderMonthlyPerformance> RiderPerformances,
    List<VehicleUtilization> VehicleUsage
);

public record MonthlyStatistics(
    int TotalShifts,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    float TotalWorkingHours,
    int TotalDisabilities,
    int TotalSubstitutions,
    int AverageRidersPerDay
);

public record RiderMonthlyPerformance(
    int RiderId,
    string WorkingId,
    string RiderName,
    int ShiftCount,
    int TotalAcceptedOrders,
    int TotalRejectedOrders,
    float TotalWorkingHours,
    float AverageOrdersPerShift
);

public record VehicleUtilization(
    string VehicleNumber,
    string VehiclePlate,
    int DaysInUse,
    int TotalOrders,
    string? PrimaryRiderName
);
public record MemberUpdateRiderCompanyRequest(
    int RiderId,
    int NewCompanyId,
    string? Reason = null
);

public record UpdateRiderCompanyResponse(
    int RiderId,
    long RiderIqamaNo,
    string RiderName,
    string WorkingId,
    int OldCompanyId,
    string OldCompanyName,
    int NewCompanyId,
    string NewCompanyName,
    DateTime ChangedAt,
    string ChangedBy,
    string? Reason
);