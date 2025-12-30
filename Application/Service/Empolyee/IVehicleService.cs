using Application.Abstraction;
using Application.Contracts.Employees;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Empolyee;

public interface IVehicleService
{
    Task<Result<IEnumerable<VehicleResponse>>> GetAllEmployee();
    Task<Result<IEnumerable<VehicleResponse>>> Get(string VehicleNumber);
    Task<Result<IEnumerable<VehicleResponse>>> Getplate(string PlateNumberA);
    Task<Result<IEnumerable<VehicleResponse>>> GetSerial(int Serial);
    Task<Result<VehicleResponse>> CreateAsync(VehicleRequest Request);
    Task<Result<VehicleResponse>> UpdateAsync(string VehicleNumber, UVehicleRequest Request);
    Task<Result> ChangeLocation(string PlatNo, string NewLocation);
    Task<Result> DeleteAsync(string VehicleNumber, CancellationToken cancellationToken = default);


    Task<Result> TakeVehicleAsync(long IqamaNo, string vehicleId, string reason, string permission, DateTime permissionEndDate);
    Task<Result> ReturnVehicleAsync(long IqamaNo, string vehicleId, string reason);
    Task<Result> ReportProblemAsync(long? IqamaNo, string vehicleId, string reason);
    Task<Result> ReportVehicleStolenAsync(string vehicleNumber, long? reportedByIqamaNo, string? reason);
    Task<Result> MarkVehicleAsBreakUpAsync(string vehicleNumber, string reason);
    Task<Result> RecoverStolenVehicleAsync(string vehicleNumber, string recoveryDetails);
    Task<Result> FixVehicleProblemAsync(string vehicleNumber, string reason);


    // Query operations
    Task<Result> IsVehicleAvailableAsync(string vehicleId);
    Task<Result<IEnumerable<RiderVehicleStatus>>> GetVehicleHistoryAsync(string vehicleId);
    Task<Result<IEnumerable<VehicleHistoryDto>>> GetVehicleHistoryAsync1(string vehicleNumber);
    Task<Result<IEnumerable<VehicleWithRiderDto>>> GetAllVehiclesWithRidersAsync();
    Task<Result<VehicleWithRiderDto>> GetVehicleWithRiderByVehicleNumberAsync(string vehicleNumber);
    Task<Result<UnavailableVehiclesResponse>> GetUnavailableVehiclesAsync(string statusFilter);
    Task<Result<GroupedVehicleStatusResponse>> GetVehiclesGroupedByStatusAsync();
    Task<Result<IEnumerable<Vehicle>>> GetAvailableVehiclesAsync();
    Task<Result<IEnumerable<Vehicle>>> GetStolenVehiclesAsync();
    Task<Result<IEnumerable<Vehicle>>> GetBreackupVehiclesAsync();
    Task<Result<IEnumerable<Vehicle>>> GetProblemVehiclesAsync();


    Task<Result> RequestTakeVehicleAsync(SVehicleResolutionRequest request,string UserId, string reason = "work");
    Task<Result> RequestReturnVehicleAsync(SVehicleResolutionRequest request,string UserId, string reason = "leave the work");
    Task<Result> RequestReportProblemAsync(SVehicleResolutionRequest request,string UserId, string reason = "problem at vehicle");
    Task<Result<IEnumerable<TempVehicleOperationResponse>>> GetPendingOperationsAsync();
    Task<Result> ResolveOperationAsync(VehicleResolutionRequest request);


}



