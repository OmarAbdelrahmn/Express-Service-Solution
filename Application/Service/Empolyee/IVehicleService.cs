using Application.Abstraction;
using Application.Contracts.Employees;
using Domain.Entities;

namespace Application.Service.Empolyee;

public interface IVehicleService
{
    Task<Result<IEnumerable<VehicleHistoryDto>>> GetVehicleHistoryByIqamaAsync(long iqamaNo);
    Task<Result<IEnumerable<VehicleResponse>>> GetAllEmployee();
    Task<Result<IEnumerable<VehicleResponse>>> Get(string VehicleNumber);
    Task<Result<IEnumerable<VehicleResponse>>> Getplate(string PlateNumberA);
    Task<Result<IEnumerable<VehicleResponse>>> GetSerial(int Serial);
    Task<Result<VehicleResponse>> CreateAsync(VehicleRequest Request);
    Task<Result<VehicleResponse>> UpdateAsync(string VehicleNumber, UVehicleRequest Request);
    Task<Result> ChangeLocation(string PlatNo, string NewLocation);
    Task<Result> DeleteAsync(string VehicleNumber, CancellationToken cancellationToken = default);


    Task<Result> SwitchVehicleAsync(long IqamaNo, string newVehiclePlateNumber, string reason, string permission);
    Task<Result<VehicleLocationSyncResponse>> SyncAllVehicleLocationsAsync();

    Task<Result<VehicleCostSplitResponse>> CalculateVehicleCostSplitAsync(
    string plateNumberA, DateTime date, decimal totalCost);

    Task<Result> TakeVehicleAsync(long IqamaNo, string vehicleId, string reason, string permission);
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
    Task<Result<IEnumerable<VehicleWithRiderDto>>> GetAllVehiclesRidersAsync();
    Task<Result<VehicleWithRiderDto>> GetVehicleWithRiderByVehicleNumberAsync(string vehicleNumber);
    Task<Result<UnavailableVehiclesResponse>> GetUnavailableVehiclesAsync(string statusFilter);
    Task<Result<GroupedVehicleStatusResponse>> GetVehiclesGroupedByStatusAsync();
    Task<Result<IEnumerable<Vehicle>>> GetAvailableVehiclesAsync();
    Task<Result<IEnumerable<Vehicle>>> GetStolenVehiclesAsync();
    Task<Result<IEnumerable<Vehicle>>> GetBreackupVehiclesAsync();
    Task<Result<IEnumerable<Vehicle>>> GetProblemVehiclesAsync();


    Task<Result> RequestTakeVehicleAsync(SVehicleResolutionRequest request, string UserId, string reason = "work");
    Task<Result> RequestReturnVehicleAsync(SVehicleResolutionRequest request, string UserId, string reason = "leave the work");
    Task<Result> RequestReportProblemAsync(SVehicleResolutionRequest request, string UserId, string reason = "problem at vehicle");
    Task<Result<IEnumerable<TempVehicleOperationResponse>>> GetPendingOperationsAsync();
    Task<Result> ResolveOperationAsync(VehicleResolutionRequest request);



    // Add to IVehicleService interface

    /// <summary>
    /// Approve or reject a vehicle switch request (Admin only)
    /// </summary>
    Task<Result> ResolveSwitchOperationAsync(VehicleSwitchResolutionRequest request);

    // Add at the end of IVehicleService.cs
    public record VehicleSwitchResolutionRequest(
        int OperationId,
        string Resolution, // "Approved" or "Rejected"
        string ResolvedBy,
        string? Note,
        string? Permission, // Required when approving
        DateTime? PermissionEndDate // Required when approving
    );

    Task<Result> MarkVehicleAsOutOfServiceAsync(string vehicleNumber, string reason);
    Task<Result> RestoreVehicleFromOutOfServiceAsync(string vehicleNumber, string reason);
    Task<Result<IEnumerable<Vehicle>>> GetOutOfServiceVehiclesAsync();
}




// Add these classes at the end of your VehicleService.cs file or in a separate Contracts file

/// <summary>
/// Response for bulk vehicle location synchronization
/// </summary>
public class VehicleLocationSyncResponse
{
    /// <summary>
    /// Total number of vehicles processed
    /// </summary>
    public int TotalVehicles { get; set; }

    /// <summary>
    /// Number of assigned vehicles that had their location updated to rider's housing
    /// </summary>
    public int AssignedVehiclesUpdated { get; set; }

    /// <summary>
    /// Number of unassigned vehicles that had their location updated to "الشركة"
    /// </summary>
    public int UnassignedVehiclesUpdated { get; set; }

    /// <summary>
    /// Number of vehicles that already had the correct location
    /// </summary>
    public int AlreadyCorrect { get; set; }

    /// <summary>
    /// Total number of vehicles updated
    /// </summary>
    public int TotalUpdated => AssignedVehiclesUpdated + UnassignedVehiclesUpdated;

    /// <summary>
    /// List of error messages if any vehicles failed to update
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Whether the operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Summary message
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Response for single vehicle location update
/// </summary>
public class VehicleLocationUpdateResult
{
    /// <summary>
    /// Vehicle number that was updated
    /// </summary>
    public string VehicleNumber { get; set; } = string.Empty;

    /// <summary>
    /// Previous location
    /// </summary>
    public string OldLocation { get; set; } = string.Empty;

    /// <summary>
    /// New location after sync
    /// </summary>
    public string NewLocation { get; set; } = string.Empty;

    /// <summary>
    /// Whether the vehicle is currently assigned to a rider
    /// </summary>
    public bool IsAssignedToRider { get; set; }

    /// <summary>
    /// Iqama number of the rider if assigned
    /// </summary>
    public long? RiderIqamaNo { get; set; }

    /// <summary>
    /// Name of the rider if assigned
    /// </summary>
    public string? RiderName { get; set; }

    /// <summary>
    /// Housing name if assigned
    /// </summary>
    public string? HousingName { get; set; }

    /// <summary>
    /// Whether the location was changed
    /// </summary>
    public bool LocationChanged { get; set; }

    /// <summary>
    /// Summary message
    /// </summary>
    public string Summary => LocationChanged
        ? $"Location updated from '{OldLocation}' to '{NewLocation}'"
        : $"Location already correct: '{NewLocation}'";
}