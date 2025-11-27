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


    Task<Result> TakeVehicleAsync(int riderId, string vehicleId, string reason);
    Task<Result> ReturnVehicleAsync(int riderId, string vehicleId, string reason);
    Task<Result> ReportProblemAsync(int riderId, string vehicleId, string reason);
    Task<Result> IsVehicleAvailableAsync(string vehicleId);
    Task<Result<IEnumerable<RiderVehicleStatus>>> GetVehicleHistoryAsync(string vehicleId);
    Task<Result> FixVehicleProblemAsync(string vehicleNumber, string reason);
    Task<Result<IEnumerable<Vehicle>>> GetAvailableVehiclesAsync();
    Task<Result> ReportVehicleStolenAsync(string vehicleNumber, int? reportedByIqamaNo, string? reason);
    Task<Result<IEnumerable<VehicleHistoryDto>>> GetVehicleHistoryAsync1(string vehicleNumber);
    Task<Result<IEnumerable<VehicleWithRiderDto>>> GetAllVehiclesWithRidersAsync();
    Task<Result<VehicleWithRiderDto>> GetVehicleWithRiderByVehicleNumberAsync(string vehicleNumber);
    Task<Result<UnavailableVehiclesResponse>> GetUnavailableVehiclesAsync(string statusFilter = "all");
    Task<Result> MarkVehicleAsBreakUpAsync(string vehicleNumber, string reason);
    Task<Result> RecoverStolenVehicleAsync(string vehicleNumber, string recoveryDetails);
    Task<Result<GroupedVehicleStatusResponse>> GetVehiclesGroupedByStatusAsync();



    Task<Result> RequestTakeVehicleAsync(int riderIqamaNo, string plateNumber, string reason, string requestedBy);
    Task<Result> RequestReturnVehicleAsync(int riderIqamaNo, string plateNumber, string reason, string requestedBy);
    Task<Result> RequestReportProblemAsync(int riderIqamaNo, string plateNumber, string reason, string requestedBy);
    Task<Result<IEnumerable<TempVehicleOperationResponse>>> GetPendingOperationsAsync();
    Task<Result> ResolveOperationAsync(VehicleResolutionRequest request);

}



