using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.SparePartCo;
using Domain;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.SparePart;

public class CostTrackingService(ApplicationDbcontext dbcontext) : ICostTrackingService
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;

    public async Task<Result<VehicleCostResponse>> GetVehicleCostAsync(string vehicleNumber)
    {
        var vehicle = await _dbcontext.Vehicles
            .FirstOrDefaultAsync(v => v.VehicleNumber == vehicleNumber);

        if (vehicle == null)
            return Result.Failure<VehicleCostResponse>(
                new Error("NotFound", "Vehicle not found", 404));

        var sparePartUsages = await _dbcontext.SparePartUsages
            .Include(u => u.SparePart)
            .Where(u => u.VehicleNumber == vehicleNumber)
            .OrderByDescending(u => u.UsedAt)
            .AsNoTracking()
            .ToListAsync();

        var sparePartDetails = sparePartUsages.Select(u => new CostItemDetail(
            u.SparePart.Name,
            u.QuantityUsed,
            u.SparePart.Price,
            u.QuantityUsed * u.SparePart.Price,
            u.UsedAt
        )).ToList();

        var totalSparePartsCost = sparePartDetails.Sum(d => d.TotalCost);

        var response = new VehicleCostResponse(
            vehicleNumber,
            totalSparePartsCost,
            0m, // Accessories not linked to vehicles
            totalSparePartsCost,
            sparePartDetails,
            new List<CostItemDetail>()
        );

        return Result.Success(response);
    }

    public async Task<Result<VehicleCostResponse>> GetVehicleCostByDateRangeAsync(
        string vehicleNumber, DateTime fromDate, DateTime toDate)
    {
        var vehicle = await _dbcontext.Vehicles
            .FirstOrDefaultAsync(v => v.VehicleNumber == vehicleNumber);

        if (vehicle == null)
            return Result.Failure<VehicleCostResponse>(
                new Error("NotFound", "Vehicle not found", 404));

        var sparePartUsages = await _dbcontext.SparePartUsages
            .Include(u => u.SparePart)
            .Where(u => u.VehicleNumber == vehicleNumber &&
                       u.UsedAt >= fromDate &&
                       u.UsedAt <= toDate)
            .OrderByDescending(u => u.UsedAt)
            .AsNoTracking()
            .ToListAsync();

        var sparePartDetails = sparePartUsages.Select(u => new CostItemDetail(
            u.SparePart.Name,
            u.QuantityUsed,
            u.SparePart.Price,
            u.QuantityUsed * u.SparePart.Price,
            u.UsedAt
        )).ToList();

        var totalSparePartsCost = sparePartDetails.Sum(d => d.TotalCost);

        var response = new VehicleCostResponse(
            vehicleNumber,
            totalSparePartsCost,
            0m,
            totalSparePartsCost,
            sparePartDetails,
            new List<CostItemDetail>()
        );

        return Result.Success(response);
    }

    public async Task<Result<RiderCostResponse>> GetRiderCostAsync(int riderId)
    {
        var rider = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.Id == riderId);

        if (rider == null)
            return Result.Failure<RiderCostResponse>(
                new Error("NotFound", "Rider not found", 404));

        var accessoryUsages = await _dbcontext.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Where(u => u.RiderId == riderId)
            .OrderByDescending(u => u.IssuedAt)
            .AsNoTracking()
            .ToListAsync();

        var accessoryDetails = accessoryUsages.Select(u => new CostItemDetail(
            u.RiderAccessory.Name,
            1, // Each accessory issued is 1 unit
            u.RiderAccessory.Price,
            u.RiderAccessory.Price,
            u.IssuedAt
        )).ToList();

        var totalAccessoriesCost = accessoryDetails.Sum(d => d.TotalCost);

        var response = new RiderCostResponse(
            riderId,
            rider.Employee.NameEN,
            rider.Employee.NameAR,
            totalAccessoriesCost,
            accessoryDetails
        );

        return Result.Success(response);
    }

    public async Task<Result<RiderCostResponse>> GetRiderCostByDateRangeAsync(
        int riderId, DateTime fromDate, DateTime toDate)
    {
        var rider = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.Id == riderId);

        if (rider == null)
            return Result.Failure<RiderCostResponse>(
                new Error("NotFound", "Rider not found", 404));

        var accessoryUsages = await _dbcontext.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Where(u => u.RiderId == riderId &&
                       u.IssuedAt >= fromDate &&
                       u.IssuedAt <= toDate)
            .OrderByDescending(u => u.IssuedAt)
            .AsNoTracking()
            .ToListAsync();

        var accessoryDetails = accessoryUsages.Select(u => new CostItemDetail(
            u.RiderAccessory.Name,
            1,
            u.RiderAccessory.Price,
            u.RiderAccessory.Price,
            u.IssuedAt
        )).ToList();

        var totalAccessoriesCost = accessoryDetails.Sum(d => d.TotalCost);

        var response = new RiderCostResponse(
            riderId,
            rider.Employee.NameEN,
            rider.Employee.NameAR,
            totalAccessoriesCost,
            accessoryDetails
        );

        return Result.Success(response);
    }

    public async Task<Result<CostSummaryResponse>> GetCostSummaryAsync(DateTime fromDate, DateTime toDate)
    {
        var sparePartUsages = await _dbcontext.SparePartUsages
            .Include(u => u.SparePart)
            .Where(u => u.UsedAt >= fromDate && u.UsedAt <= toDate)
            .AsNoTracking()
            .ToListAsync();

        var accessoryUsages = await _dbcontext.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Where(u => u.IssuedAt >= fromDate && u.IssuedAt <= toDate)
            .AsNoTracking()
            .ToListAsync();

        var totalSparePartsCost = sparePartUsages.Sum(u => u.QuantityUsed * u.SparePart.Price);
        var totalAccessoriesCost = accessoryUsages.Sum(u => u.RiderAccessory.Price);
        var grandTotal = totalSparePartsCost + totalAccessoriesCost;

        var response = new CostSummaryResponse(
            totalSparePartsCost,
            totalAccessoriesCost,
            grandTotal,
            fromDate,
            toDate
        );

        return Result.Success(response);
    }
}