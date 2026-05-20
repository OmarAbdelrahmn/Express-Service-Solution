using Application.Abstraction;
using Application.Contracts.SparePartCo;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static Application.Service.SparePart.ICostTrackingService;

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
            0m,
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
    public async Task<Result<IEnumerable<VehicleSparePartRiderCostResponse>>> GetVehiclesWithRiderCostsByDateRangeAsync(
    DateTime fromDate, DateTime toDate)
    {
        try
        {
            // Step 1: load usages — keep it simple, no GroupBy in EF
            var allUsages = await _dbcontext.SparePartUsages
                .Include(u => u.SparePart)
                .Include(u => u.Vehicle)
                .Where(u => u.UsedAt >= fromDate && u.UsedAt <= toDate)
                .AsNoTracking()
                .ToListAsync();

            if (!allUsages.Any())
                return Result.Success<IEnumerable<VehicleSparePartRiderCostResponse>>(
                    Enumerable.Empty<VehicleSparePartRiderCostResponse>());

            var vehicleNumbers = allUsages
                .Select(u => u.VehicleNumber)
                .Distinct()
                .ToList();

            // Step 2: load rider statuses — split the nullable condition into two simple queries
            //         then union in memory to avoid EF translation issues
            var activeStatuses = await _dbcontext.RiderVehicleStatus
                .Where(s => vehicleNumbers.Contains(s.VehicleNumber)
                         && s.StatusType == VehicleStatusType.Taken
                         && s.PermissionStartDate != null
                         && s.PermissionStartDate <= toDate
                         && s.IsActive)
                .AsNoTracking()
                .ToListAsync();

            var closedStatuses = await _dbcontext.RiderVehicleStatus
                .Where(s => vehicleNumbers.Contains(s.VehicleNumber)
                         && s.StatusType == VehicleStatusType.Taken
                         && s.PermissionStartDate != null
                         && s.PermissionStartDate <= toDate
                         && !s.IsActive
                         && s.PermissionEndDate != null
                         && s.PermissionEndDate >= fromDate)
                .AsNoTracking()
                .ToListAsync();

            // Merge and deduplicate by Id
            var riderStatuses = activeStatuses
                .Concat(closedStatuses)
                .DistinctBy(s => s.Id)
                .ToList();

            // Step 3: load rider details in one query
            var riderIqamas = riderStatuses
                .Where(s => s.EmployeeIqamaNo.HasValue)
                .Select(s => s.EmployeeIqamaNo!.Value)
                .Distinct()
                .ToList();

            var riderDetails = riderIqamas.Any()
                ? await _dbcontext.RiderDetails
                    .Include(r => r.Employee)
                    .Where(r => riderIqamas.Contains(r.EmployeeIqamaNo))
                    .AsNoTracking()
                    .ToListAsync()
                : new List<RiderDetails>();

            // Step 4: group usages in memory and build response
            var grouped = allUsages.GroupBy(u => u.VehicleNumber).ToList();
            var result = new List<VehicleSparePartRiderCostResponse>();

            foreach (var group in grouped)
            {
                try
                {
                    var vehicleNumber = group.Key;
                    var vehicle = group.First().Vehicle;

                    if (vehicle == null) continue;

                    var totalCost = group.Sum(u => u.QuantityUsed * u.SparePart.Price);
                    var usageCount = group.Count();

                    var vehicleStatuses = riderStatuses
                        .Where(s => s.VehicleNumber == vehicleNumber)
                        .ToList();

                    var riderShares = new List<RiderCostShareDto>();

                    if (!vehicleStatuses.Any())
                    {
                        result.Add(new VehicleSparePartRiderCostResponse(
                            vehicle.VehicleNumber,
                            vehicle.PlateNumberA,
                            vehicle.PlateNumberE,
                            vehicle.Location,
                            totalCost,
                            usageCount,
                            riderShares
                        ));
                        continue;
                    }

                    // Clamp windows to [fromDate, toDate]
                    var windows = vehicleStatuses.Select(s => new
                    {
                        Status = s,
                        Start = s.PermissionStartDate!.Value < fromDate
                                    ? fromDate : s.PermissionStartDate.Value,
                        End = s.PermissionEndDate == null || s.PermissionEndDate.Value > toDate
                                    ? toDate : s.PermissionEndDate.Value
                    })
                    .Where(w => w.End > w.Start)
                    .ToList();

                    if (!windows.Any())
                    {
                        result.Add(new VehicleSparePartRiderCostResponse(
                            vehicle.VehicleNumber,
                            vehicle.PlateNumberA,
                            vehicle.PlateNumberE,
                            vehicle.Location,
                            totalCost,
                            usageCount,
                            riderShares
                        ));
                        continue;
                    }

                    if (windows.Count == 1)
                    {
                        var s = windows[0].Status;
                        var rider = riderDetails.FirstOrDefault(r => r.EmployeeIqamaNo == s.EmployeeIqamaNo);

                        riderShares.Add(new RiderCostShareDto(
                            s.EmployeeIqamaNo ?? 0,
                            rider?.Employee?.NameAR ?? "N/A",
                            rider?.Employee?.NameEN ?? "N/A",
                            s.PermissionStartDate!.Value,
                            s.PermissionEndDate ?? toDate,
                            s.IsActive,
                            totalCost,
                            "Sole"
                        ));
                    }
                    else
                    {
                        double totalHours = windows.Sum(w => (w.End - w.Start).TotalHours);
                        decimal distributed = 0;

                        for (int i = 0; i < windows.Count; i++)
                        {
                            var w = windows[i];
                            var rider = riderDetails.FirstOrDefault(r => r.EmployeeIqamaNo == w.Status.EmployeeIqamaNo);
                            double hours = (w.End - w.Start).TotalHours;

                            decimal share = i == windows.Count - 1
                                ? totalCost - distributed
                                : Math.Round(totalCost * (decimal)(hours / totalHours), 2);

                            distributed += share;

                            riderShares.Add(new RiderCostShareDto(
                                w.Status.EmployeeIqamaNo ?? 0,
                                rider?.Employee?.NameAR ?? "N/A",
                                rider?.Employee?.NameEN ?? "N/A",
                                w.Status.PermissionStartDate!.Value,
                                w.Status.PermissionEndDate ?? toDate,
                                w.Status.IsActive,
                                share,
                                "TimeBased"
                            ));
                        }
                    }

                    // ← GROUP HERE before adding to result
                    var mergedShares = riderShares
                        .GroupBy(r => r.EmployeeIqamaNo)
                        .Select(g =>
                        {
                            var first = g.First();
                            return new RiderCostShareDto(
                                first.EmployeeIqamaNo,
                                first.RiderNameAR,
                                first.RiderNameEN,
                                g.Min(r => r.PermissionStart),
                                g.Max(r => r.PermissionEnd),
                                g.Any(r => r.IsActive),
                                g.Sum(r => r.CostShare),
                                g.Count() > 1 ? "TimeBased" : first.SplitMethod
                            );
                        })
                        .OrderBy(r => r.PermissionStart)
                        .ToList();

                    result.Add(new VehicleSparePartRiderCostResponse(
                        vehicle.VehicleNumber,
                        vehicle.PlateNumberA,
                        vehicle.PlateNumberE,
                        vehicle.Location,
                        totalCost,
                        usageCount,
                        mergedShares
                    ));
                    result.Add(new VehicleSparePartRiderCostResponse(
                        vehicle.VehicleNumber,
                        vehicle.PlateNumberA,
                        vehicle.PlateNumberE,
                        vehicle.Location,
                        totalCost,
                        usageCount,
                        riderShares.OrderBy(r => r.PermissionStart).ToList()
                    ));
                }
                catch (Exception innerEx)
                {
                    continue;
                }
            }

            return Result.Success<IEnumerable<VehicleSparePartRiderCostResponse>>(
                result.OrderByDescending(v => v.TotalVehicleCost));
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<VehicleSparePartRiderCostResponse>>(
                new Error("GetVehicleRiderCostError",
                    $"{ex.GetType().Name}: {ex.Message} | {ex.InnerException?.Message}", 500));
        }
    }
}