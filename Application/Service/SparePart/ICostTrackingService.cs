using Application.Abstraction;
using Application.Contracts.SparePartCo;

namespace Application.Service.SparePart;

public interface ICostTrackingService
{
    Task<Result<VehicleCostResponse>> GetVehicleCostAsync(string vehicleNumber);
    Task<Result<VehicleCostResponse>> GetVehicleCostByDateRangeAsync(string vehicleNumber, DateTime fromDate, DateTime toDate);
    Task<Result<RiderCostResponse>> GetRiderCostAsync(int riderId);
    Task<Result<RiderCostResponse>> GetRiderCostByDateRangeAsync(int riderId, DateTime fromDate, DateTime toDate);
    Task<Result<CostSummaryResponse>> GetCostSummaryAsync(DateTime fromDate, DateTime toDate);

    Task<Result<IEnumerable<VehicleSparePartRiderCostResponse>>> GetVehiclesWithRiderCostsByDateRangeAsync(
    DateTime fromDate, DateTime toDate);

    public record VehicleSparePartRiderCostResponse(
        string VehicleNumber,
        string PlateNumberA,
        string PlateNumberE,
        string Location,
        decimal TotalVehicleCost,
        int TotalUsageCount,
        List<RiderCostShareDto> RiderShares
    );

    public record RiderCostShareDto(
        long EmployeeIqamaNo,
        string RiderNameAR,
        string RiderNameEN,
        DateTime PermissionStart,
        DateTime PermissionEnd,
        bool IsActive,
        decimal CostShare,
        string SplitMethod   // "TimeBased" or "Sole"
    );
}
