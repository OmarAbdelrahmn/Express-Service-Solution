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
}