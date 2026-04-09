using Application.Abstraction;
using Application.Contracts.Employees;
using Application.Contracts.rider;
using Application.Service.Empolyee;
using Domain.Entities;

namespace Application.Service.Riders;

public interface IRiderService
{
    Task<Result<IEnumerable<RiderResponse>>> GetAllEmployee();
    Task<Result<IEnumerable<RiderResponse>>> GetAllEmployee2();
    Task<Result<IEnumerable<RiderResponse>>> GetAllEmployeeNO();
    Task<Result<IEnumerable<RiderResponse>>> Get(long IqamaNo);
    Task<Result<RiderResponse>> Getbyid(long Id);
    Task<Result> CreateAsync(RiderRequest Request);
    Task<Result<RiderResponse>> UpdateAsync(long IqamaNo, URiderRequest Request,string userId);
    Task<Result> DeleteAsync(long IqamaNo, string Reason, CancellationToken cancellationToken = default);
    Task<List<RiderResponse>> SmartSearch(string keyword);
    Task<Result> ChangeWorkinId(string OldWorkinId, string NewWorkingId);
    Task<Result> AddETOR(long IqamaNo, EMTOR request);
    Task<Result<EmployeeStatisticsResponse>> GetEmployeeStatistics();
    Task<Result<IEnumerable<RiderResponse>>> Filter(EmployeeFilterr filter);
    Task<Result<VehicleResponse>> GetRiderVehicle(long IqamaNo);
    Task<Result<EmployeeStatusLogsWithInfoResponse>> GetStatusLogsAsync(long iqamaNo);
}
public record EmployeeStatisticsResponse(
    int Total,
    int Riders,
    int Employees
);

public record EmployeeStatusLogResponse(
    int Id,
    long EmployeeIqamaNo,
    string NameAR,
    string NameEN,
    string JobTitle,
    string Country,
    string Sponsor,
    string? HousingName,
    string? HousingAddress,
    string OldStatus,
    string NewStatus,
    string ChangedBy,
    DateTime ChangedAt,
    string? Reason,
    string ChangeSource
);

public record EmployeeStatusLogsWithInfoResponse(
    // ── Employee snapshot ─────────────────────────────────────────────────
    long IqamaNo,
    string NameAR,
    string NameEN,
    string JobTitle,
    string Country,
    string Sponsor,
    string CurrentStatus,
    string? HousingName,
    string? HousingAddress,

    // ── Log summary ───────────────────────────────────────────────────────
    int TotalChanges,
    int DirectUpdates,
    int StatusRequests,
    DateTime? FirstChangeAt,
    DateTime? LastChangeAt,

    // ── Log entries ───────────────────────────────────────────────────────
    IEnumerable<EmployeeStatusLogResponse> Logs
);