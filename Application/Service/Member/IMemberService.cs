using Application.Abstraction;
using Application.Service.Riders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Member;

public interface IMemberService
{
    Task<Result<MemberAuthResponse>> MemberSignInAsync(MemberAuthRequest request);

    Task<Result<HousingDashboardResponse>> GetHousingDashboard(long managerIqamaNo);
    Task<Result<HousingDetailResponse>> GetHousingDetails(long managerIqamaNo);

    // Employees & Riders
    Task<Result<List<HousingEmployeeResponse>>> GetHousingEmployees(long managerIqamaNo);
    Task<Result<List<HousingRiderResponse>>> GetHousingRiders(long managerIqamaNo);
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
}
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
    List<RecentActivityItem> RecentActivities
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