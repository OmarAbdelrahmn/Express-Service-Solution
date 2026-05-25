using Application.Abstraction;
using Application.Contracts.SparePartCo;
using Domain;
using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;
using static Application.Service.SparePart.ISparePartService;

namespace Application.Service.SparePart;

public class SparePartService(ApplicationDbcontext dbcontext) : ISparePartService
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;

    private const string COMPANY_STOCK = "الشركة";

    public async Task<Result<ComprehensiveHousingCostReport>> GetAllHousingsCostReportAsync(
    DateTime fromDate,
    DateTime toDate)
    {
        try
        {
            // ── 1. Load all usages in range (location-driven, no housing join needed) ──
            var sparePartUsages = await _dbcontext.SparePartUsages
                .Include(u => u.SparePart)
                .Include(u => u.Vehicle)
                .Where(u => u.UsedAt >= fromDate &&
                            u.UsedAt <= toDate &&
                            u.Location != null)
                .OrderByDescending(u => u.UsedAt)
                .ToListAsync();

            var accessoryUsages = await _dbcontext.RiderAccessoryUsages
                .Include(u => u.RiderAccessory)
                .Include(u => u.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(u => u.IssuedAt >= fromDate &&
                            u.IssuedAt <= toDate &&
                            u.Location != null)
                .OrderByDescending(u => u.IssuedAt)
                .ToListAsync();

            // ── 2. Pre-load housings for Id look-up (single query, no N+1) ──────────
            var housings = await _dbcontext.Housings
                .AsNoTracking()
                .ToListAsync();

            var housingIdByName = housings.ToDictionary(h => h.Name, h => h.Id);

            // ── 3. Collect every distinct location across both usage tables ──────────
            var allLocations = sparePartUsages.Select(u => u.Location!)
                .Concat(accessoryUsages.Select(u => u.Location!))
                .Distinct()
                .ToList();

            // ── 4. Build per-location detail objects ──────────────────────────────────
            var housingDetails = new List<HousingCostDetail>();
            CompanyStockDetail? companyStockDetail = null;

            foreach (var location in allLocations)
            {
                var locationSpareParts = sparePartUsages
                    .Where(u => u.Location == location)
                    .ToList();

                var locationAccessories = accessoryUsages
                    .Where(u => u.Location == location)
                    .ToList();

                var vehicleUsages = BuildVehicleSparePartUsagesFromList(locationSpareParts);
                var riderUsages = BuildRiderAccessoryUsagesFromList(locationAccessories);

                var totalSparePartsCost = vehicleUsages.Sum(v => v.TotalVehicleCost);
                var totalAccessoriesCost = riderUsages.Sum(r => r.TotalRiderCost);
                var totalHousingCost = totalSparePartsCost + totalAccessoriesCost;

                if (location == COMPANY_STOCK)
                {
                    companyStockDetail = new CompanyStockDetail(
                        totalHousingCost,
                        totalSparePartsCost,
                        totalAccessoriesCost,
                        vehicleUsages,
                        riderUsages
                    );
                }
                else
                {
                    housingIdByName.TryGetValue(location, out var housingId);

                    housingDetails.Add(new HousingCostDetail(
                        housingId,
                        location,
                        totalHousingCost,
                        totalSparePartsCost,
                        totalAccessoriesCost,
                        vehicleUsages,
                        riderUsages
                    ));
                }
            }

            // ── 5. Fall-back: empty company-stock block if no usages found for it ─────
            companyStockDetail ??= new CompanyStockDetail(0, 0, 0, [], []);

            // ── 6. Roll up company-wide totals ────────────────────────────────────────
            var totalCompanySparePartsCost = housingDetails.Sum(h => h.TotalSparePartsCost)
                                            + companyStockDetail.TotalSparePartsCost;
            var totalCompanyAccessoriesCost = housingDetails.Sum(h => h.TotalAccessoriesCost)
                                            + companyStockDetail.TotalAccessoriesCost;
            var totalCompanyCost = totalCompanySparePartsCost + totalCompanyAccessoriesCost;

            var report = new ComprehensiveHousingCostReport(
                fromDate,
                toDate,
                totalCompanyCost,
                totalCompanySparePartsCost,
                totalCompanyAccessoriesCost,
                housingDetails.OrderByDescending(h => h.TotalHousingCost).ToList(),
                companyStockDetail
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<ComprehensiveHousingCostReport>(
                new Error("Error", $"Failed to generate housing cost report: {ex.Message}", 500));
        }
    }

    /// <summary>
    /// Builds vehicle spare-part usage groups from a pre-filtered, pre-loaded list.
    /// Replaces the async DB version for the location-driven report path.
    /// </summary>
    private static List<VehicleSparePartUsage> BuildVehicleSparePartUsagesFromList(
        List<Domain.Entities.Spare.SparePartUsage> usages)
    {
        if (!usages.Any())
            return [];

        return usages
            .GroupBy(u => u.VehicleNumber)
            .Select(g =>
            {
                var vehicle = g.First().Vehicle;

                var sparePartsUsed = g.Select(u => new SparePartUsageItem(
                    u.Id,
                    u.SparePart.Name,
                    u.QuantityUsed,
                    u.SparePart.Price,
                    u.QuantityUsed * u.SparePart.Price,
                    u.UsedAt
                )).ToList();

                return new VehicleSparePartUsage(
                    vehicle.VehicleNumber,
                    vehicle.PlateNumberA,
                    vehicle.Location,
                    sparePartsUsed,
                    sparePartsUsed.Sum(s => s.TotalCost)
                );
            })
            .OrderByDescending(v => v.TotalVehicleCost)
            .ToList();
    }

    /// <summary>
    /// Builds rider accessory usage groups from a pre-filtered, pre-loaded list.
    /// Replaces the async DB version for the location-driven report path.
    /// </summary>
    private static List<Contracts.SparePartCo.RiderAccessoryUsage> BuildRiderAccessoryUsagesFromList(
        List<Domain.Entities.Spare.RiderAccessoryUsage> usages)
    {
        if (!usages.Any())
            return [];

        return usages
            .GroupBy(u => u.RiderId)
            .Select(g =>
            {
                var rider = g.First().Rider;

                var accessories = g.Select(u => new AccessoryUsageItem(
                    u.Id,
                    u.RiderAccessory.Name,
                    u.RiderAccessory.Price,
                    u.IssuedAt
                )).ToList();

                return new Contracts.SparePartCo.RiderAccessoryUsage(
                    rider.Id,
                    rider.WorkingId ?? "N/A",
                    rider.Employee.NameEN,
                    rider.Employee.NameAR,
                    rider.EmployeeIqamaNo,
                    accessories,
                    accessories.Sum(a => a.Price)
                );
            })
            .OrderByDescending(r => r.TotalRiderCost)
            .ToList();
    }
    private async Task<HousingCostDetail?> GetHousingDetailAsync(
        Domain.Entities.Housing housing,
        DateTime fromDate,
        DateTime toDate)
    {
        // Get employees in this housing
        var employeeIqamas = housing.Employees
            .Where(e => !e.IsDeleted)
            .Select(e => e.IqamaNo)
            .ToList();

        if (!employeeIqamas.Any())
            return null;

        // Get riders in this housing
        var riders = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .ToListAsync();

        var riderIds = riders.Select(r => r.Id).ToList();

        // Get vehicle numbers associated with this housing
        var riderVehicleNumbers = riders
            .Where(r => r.VehicleNumber != null)
            .Select(r => r.VehicleNumber!)
            .Distinct()
            .ToList();

        var locationVehicleNumbers = await _dbcontext.Vehicles
            .Where(v => v.Location == housing.Name)
            .Select(v => v.VehicleNumber)
            .ToListAsync();

        var allVehicleNumbers = riderVehicleNumbers
            .Concat(locationVehicleNumbers)
            .Distinct()
            .ToList();

        // Get vehicle spare part usages
        var vehicleUsages = await GetVehicleSparePartUsagesAsync(
            allVehicleNumbers,
            fromDate,
            toDate);

        // Get rider accessory usages
        var riderUsages = await GetRiderAccessoryUsagesAsync(
            riderIds,
            fromDate,
            toDate);

        var totalSparePartsCost = vehicleUsages.Sum(v => v.TotalVehicleCost);
        var totalAccessoriesCost = riderUsages.Sum(r => r.TotalRiderCost);
        var totalHousingCost = totalSparePartsCost + totalAccessoriesCost;

        return new HousingCostDetail(
            housing.Id,
            housing.Name,
            totalHousingCost,
            totalSparePartsCost,
            totalAccessoriesCost,
            vehicleUsages,
            riderUsages
        );
    }

    private async Task<CompanyStockDetail> GetCompanyStockDetailAsync(
        DateTime fromDate,
        DateTime toDate)
    {
        // Get vehicles in company stock
        var companyVehicleNumbers = await _dbcontext.Vehicles
            .Where(v => v.Location == COMPANY_STOCK)
            .Select(v => v.VehicleNumber)
            .ToListAsync();

        // Get vehicle spare part usages
        var vehicleUsages = await GetVehicleSparePartUsagesAsync(
            companyVehicleNumbers,
            fromDate,
            toDate);

        // Get rider accessory usages from company stock
        var accessoryUsages = await _dbcontext.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Include(u => u.Rider)
                .ThenInclude(r => r.Employee)
            .Where(u => u.RiderAccessory.Location == COMPANY_STOCK &&
                       u.IssuedAt >= fromDate &&
                       u.IssuedAt <= toDate)
            .ToListAsync();

        var riderUsages = accessoryUsages
            .GroupBy(u => u.RiderId)
            .Select(g =>
            {
                var rider = g.First().Rider;
                var accessories = g.Select(u => new AccessoryUsageItem(
                    u.Id,
                    u.RiderAccessory.Name,
                    u.RiderAccessory.Price,
                    u.IssuedAt
                )).ToList();

                return new Contracts.SparePartCo.RiderAccessoryUsage(
                    rider.Id,
                    rider.WorkingId ?? "N/A",
                    rider.Employee.NameEN,
                    rider.Employee.NameAR,
                    rider.EmployeeIqamaNo,
                    accessories,
                    accessories.Sum(a => a.Price)
                );
            })
            .ToList();

        var totalSparePartsCost = vehicleUsages.Sum(v => v.TotalVehicleCost);
        var totalAccessoriesCost = riderUsages.Sum(r => r.TotalRiderCost);
        var totalCost = totalSparePartsCost + totalAccessoriesCost;

        return new CompanyStockDetail(
            totalCost,
            totalSparePartsCost,
            totalAccessoriesCost,
            vehicleUsages,
            riderUsages
        );
    }

    private async Task<List<VehicleSparePartUsage>> GetVehicleSparePartUsagesAsync(
        List<string> vehicleNumbers,
        DateTime fromDate,
        DateTime toDate)
    {
        if (!vehicleNumbers.Any())
            return new List<VehicleSparePartUsage>();

        var usages = await _dbcontext.SparePartUsages
            .Include(u => u.SparePart)
            .Include(u => u.Vehicle)
            .Where(u => vehicleNumbers.Contains(u.VehicleNumber) &&
                       u.UsedAt >= fromDate &&
                       u.UsedAt <= toDate)
            .OrderByDescending(u => u.UsedAt)
            .ToListAsync();

        return usages
            .GroupBy(u => u.VehicleNumber)
            .Select(g =>
            {
                var vehicle = g.First().Vehicle;
                var sparePartsUsed = g.Select(u => new SparePartUsageItem(
                    u.Id,
                    u.SparePart.Name,
                    u.QuantityUsed,
                    u.SparePart.Price,
                    u.QuantityUsed * u.SparePart.Price,
                    u.UsedAt
                )).ToList();

                return new VehicleSparePartUsage(
                    vehicle.VehicleNumber,
                    vehicle.PlateNumberA,
                    vehicle.Location,
                    sparePartsUsed,
                    sparePartsUsed.Sum(s => s.TotalCost)
                );
            })
            .OrderByDescending(v => v.TotalVehicleCost)
            .ToList();
    }

    private async Task<List<Contracts.SparePartCo.RiderAccessoryUsage>> GetRiderAccessoryUsagesAsync(
        List<int> riderIds,
        DateTime fromDate,
        DateTime toDate)
    {
        if (!riderIds.Any())
            return new List<Contracts.SparePartCo.RiderAccessoryUsage>();

        var usages = await _dbcontext.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Include(u => u.Rider)
                .ThenInclude(r => r.Employee)
            .Where(u => riderIds.Contains(u.RiderId) &&
                       u.IssuedAt >= fromDate &&
                       u.IssuedAt <= toDate)
            .OrderByDescending(u => u.IssuedAt)
            .ToListAsync();

        return usages
            .GroupBy(u => u.RiderId)
            .Select(g =>
            {
                var rider = g.First().Rider;
                var accessoriesUsed = g.Select(u => new AccessoryUsageItem(
                    u.Id,
                    u.RiderAccessory.Name,
                    u.RiderAccessory.Price,
                    u.IssuedAt
                )).ToList();

                return new Contracts.SparePartCo.RiderAccessoryUsage(
                    rider.Id,
                    rider.WorkingId ?? "N/A",
                    rider.Employee.NameEN,
                    rider.Employee.NameAR,
                    rider.EmployeeIqamaNo,
                    accessoriesUsed,
                    accessoriesUsed.Sum(a => a.Price)
                );
            })
            .OrderByDescending(r => r.TotalRiderCost)
            .ToList();
    }
    public async Task<Result<AllHousingsCostSummaryResponse>> GetAllHousingsCostSummaryAsync(
        DateTime fromDate,
        DateTime toDate)
    {
        try
        {
            // Get all housings
            var housings = await _dbcontext.Housings
                .Include(h => h.Employees)
                .Where(h => h.Employees.Any(e => !e.IsDeleted && !e.IsEmployee && e.Status =="enable"))
                .ToListAsync();

            var housingCosts = new List<HousingCostSummaryItem>();

            // Process each housing
            foreach (var housing in housings)
            {
                var housingCost = await GetHousingCostSummaryItemAsync(
                    housing.Id,
                    housing.Name,
                    fromDate,
                    toDate);

                if (housingCost != null)
                {
                    housingCosts.Add(housingCost);
                }
            }

            // Get company stock costs (الشركة)
            var companyStockCost = await GetCompanyStockCostSummaryAsync(fromDate, toDate);

            // Calculate totals
            var grandTotalSparePartsCost = housingCosts.Sum(h => h.SparePartsCost) +
                                          companyStockCost.SparePartsCost;
            var grandTotalAccessoriesCost = housingCosts.Sum(h => h.AccessoriesCost) +
                                           companyStockCost.AccessoriesCost;

            var grandTotalCost = grandTotalSparePartsCost + grandTotalAccessoriesCost;

            var response = new AllHousingsCostSummaryResponse(
                fromDate,
                toDate,
                grandTotalSparePartsCost,
                grandTotalAccessoriesCost,
                grandTotalCost,
                housingCosts.OrderByDescending(h => h.TotalCost).ToList(),
                companyStockCost
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<AllHousingsCostSummaryResponse>(
                new Error("Error", $"Failed to get housing costs summary: {ex.Message}", 500));
        }
    }

    public async Task<Result<HousingDetailedCostResponse>> GetHousingDetailedCostAsync(
        string housingName,
        DateTime fromDate,
        DateTime toDate)
    {
        try
        {
            var housing = await _dbcontext.Housings
                .Include(h => h.Employees)
                .FirstOrDefaultAsync(h => h.Name == housingName);

            if (housing == null)
                return Result.Failure<HousingDetailedCostResponse>(
                    new Error("NotFound", "Housing not found", 404));

            // Get rider IDs in this housing
            var employeeIqamas = housing.Employees
                .Where(e => !e.IsDeleted)
                .Select(e => e.IqamaNo)
                .ToList();

            var riderIds = await _dbcontext.RiderDetails
                .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
                .Select(r => r.Id)
                .ToListAsync();

            // Get vehicle numbers - both assigned to riders and by location
            var riderVehicleNumbers = await _dbcontext.RiderDetails
                .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo) &&
                           r.VehicleNumber != null)
                .Select(r => r.VehicleNumber!)
                .Distinct()
                .ToListAsync();

            var locationVehicleNumbers = await _dbcontext.Vehicles
                .Where(v => v.Location == housingName)
                .Select(v => v.VehicleNumber)
                .ToListAsync();

            var allVehicleNumbers = riderVehicleNumbers
                .Concat(locationVehicleNumbers)
                .Distinct()
                .ToList();

            // Get spare parts usage for these vehicles
            var vehicleCosts = await GetVehicleCostsForHousingAsync(
                allVehicleNumbers,
                fromDate,
                toDate);

            // Get rider accessories usage
            var riderCosts = await GetRiderCostsForHousingAsync(
                riderIds,
                housingName,
                fromDate,
                toDate);

            var totalSparePartsCost = vehicleCosts.Sum(v => v.TotalCost);
            var totalAccessoriesCost = riderCosts.Sum(r => r.TotalCost);
            var grandTotal = totalSparePartsCost + totalAccessoriesCost;

            // Calculate statistics
            var statistics = CalculateStatistics(
                vehicleCosts,
                riderCosts,
                totalSparePartsCost,
                totalAccessoriesCost);

            var response = new HousingDetailedCostResponse(
                housing.Id,
                housing.Name,
                fromDate,
                toDate,
                totalSparePartsCost,
                totalAccessoriesCost,
                grandTotal,
                vehicleCosts.OrderByDescending(v => v.TotalCost).ToList(),
                riderCosts.OrderByDescending(r => r.TotalCost).ToList(),
                statistics
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<HousingDetailedCostResponse>(
                new Error("Error", $"Failed to get housing detailed cost: {ex.Message}", 500));
        }
    }

    public async Task<Result<HousingDetailedCostResponse>> GetCompanyStockCostAsync(
        DateTime fromDate,
        DateTime toDate)
    {
        try
        {
            // Get vehicles with location = "الشركة"
            var companyVehicleNumbers = await _dbcontext.Vehicles
                .Where(v => v.Location == COMPANY_STOCK)
                .Select(v => v.VehicleNumber)
                .ToListAsync();

            // Get spare parts usage for company stock vehicles
            var vehicleCosts = await GetVehicleCostsForHousingAsync(
                companyVehicleNumbers,
                fromDate,
                toDate);

            // Get riders using accessories from company stock
            var companyAccessoryUsages = await _dbcontext.RiderAccessoryUsages
                .Include(u => u.RiderAccessory)
                .Include(u => u.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(u => u.RiderAccessory.Location == COMPANY_STOCK &&
                           u.IssuedAt >= fromDate &&
                           u.IssuedAt <= toDate)
                .ToListAsync();

            var riderCosts = companyAccessoryUsages
                .GroupBy(u => u.RiderId)
                .Select(g =>
                {
                    var rider = g.First().Rider;
                    var usageDetails = g.Select(u => new AccessoryUsageDetail(
                        u.RiderAccessory.Name,
                        u.RiderAccessory.Price,
                        u.IssuedAt
                    )).ToList();

                    return new RiderAccessoryCostDetail(
                        rider.Id,
                        rider.EmployeeIqamaNo,
                        rider.Employee.NameEN,
                        rider.Employee.NameAR,
                        rider.WorkingId ?? "N/A",
                        rider.Employee.Housing?.Name ?? "N/A",
                        usageDetails.Sum(d => d.Price),
                        usageDetails.Count,
                        usageDetails
                    );
                })
                .ToList();

            var totalSparePartsCost = vehicleCosts.Sum(v => v.TotalCost);
            var totalAccessoriesCost = riderCosts.Sum(r => r.TotalCost);
            var grandTotal = totalSparePartsCost + totalAccessoriesCost;

            var statistics = CalculateStatistics(
                vehicleCosts,
                riderCosts,
                totalSparePartsCost,
                totalAccessoriesCost);

            var response = new HousingDetailedCostResponse(
                null,
                COMPANY_STOCK,
                fromDate,
                toDate,
                totalSparePartsCost,
                totalAccessoriesCost,
                grandTotal,
                vehicleCosts.OrderByDescending(v => v.TotalCost).ToList(),
                riderCosts.OrderByDescending(r => r.TotalCost).ToList(),
                statistics
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<HousingDetailedCostResponse>(
                new Error("Error", $"Failed to get company stock cost: {ex.Message}", 500));
        }
    }

    public async Task<Result<HousingCostComparisonResponse>> CompareHousingCostsAsync(
        DateTime fromDate,
        DateTime toDate)
    {
        try
        {
            var allCosts = await GetAllHousingsCostSummaryAsync(fromDate, toDate);

            if (allCosts.IsFailure)
                return Result.Failure<HousingCostComparisonResponse>(allCosts.Error);

            var comparisons = allCosts.Value.HousingCosts
                .Select((h, index) => new HousingComparisonItem(
                    h.HousingName,
                    h.SparePartsCost,
                    h.AccessoriesCost,
                    h.TotalCost,
                    h.VehicleCount > 0 ? h.SparePartsCost / h.VehicleCount : 0,
                    h.RiderCount > 0 ? h.AccessoriesCost / h.RiderCount : 0,
                    index + 1
                ))
                .OrderByDescending(c => c.TotalCost)
                .Select((c, index) => c with { Rank = index + 1 })
                .ToList();

            var insights = CalculateComparisonInsights(comparisons);

            var response = new HousingCostComparisonResponse(
                fromDate,
                toDate,
                comparisons,
                insights
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<HousingCostComparisonResponse>(
                new Error("Error", $"Failed to compare housing costs: {ex.Message}", 500));
        }
    }

    // ============================================
    // PRIVATE HELPER METHODS
    // ============================================

    private async Task<HousingCostSummaryItem?> GetHousingCostSummaryItemAsync(
        int housingId,
        string housingName,
        DateTime fromDate,
        DateTime toDate)
    {
        var employeeIqamas = await _dbcontext.Employees
            .Where(e => e.HousingId == housingId && !e.IsDeleted)
            .Select(e => e.IqamaNo)
            .ToListAsync();

        if (!employeeIqamas.Any())
            return null;

        var riderIds = await _dbcontext.RiderDetails
            .Include(c=>c.Employee)
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo) && !r.Employee.IsEmployee && r.Employee.Status == "enable")
            .Select(r => r.Id)
            .ToListAsync();

        // Get vehicle numbers
        var riderVehicleNumbers = await _dbcontext.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo) &&
                       r.VehicleNumber != null)
            .Select(r => r.VehicleNumber!)
            .Distinct()
            .ToListAsync();

        var locationVehicleNumbers = await _dbcontext.Vehicles
            .Where(v => v.Location == housingName)
            .Select(v => v.VehicleNumber)
            .ToListAsync();

        var allVehicleNumbers = riderVehicleNumbers
            .Concat(locationVehicleNumbers)
            .Distinct()
            .ToList();

        // Calculate spare parts cost
        var sparePartsCost = await _dbcontext.SparePartUsages
            .Where(u => allVehicleNumbers.Contains(u.VehicleNumber) &&
                       u.UsedAt >= fromDate &&
                       u.UsedAt <= toDate)
            .SumAsync(u => (decimal?)(u.QuantityUsed * u.SparePart.Price)) ?? 0;

        // Calculate accessories cost
        var accessoriesCost = await _dbcontext.RiderAccessoryUsages
            .Where(u => riderIds.Contains(u.RiderId) &&
                       u.IssuedAt >= fromDate &&
                       u.IssuedAt <= toDate)
            .SumAsync(u => (decimal?)u.RiderAccessory.Price) ?? 0;

        return new HousingCostSummaryItem(
            housingId,
            housingName,
            sparePartsCost,
            accessoriesCost,
            sparePartsCost + accessoriesCost,
            allVehicleNumbers.Count,
            riderIds.Count
        );
    }

    private async Task<CompanyStockCostSummary> GetCompanyStockCostSummaryAsync(
        DateTime fromDate,
        DateTime toDate)
    {
        // Get vehicles with location = "الشركة"
        var companyVehicleNumbers = await _dbcontext.Vehicles
            .Where(v => v.Location == COMPANY_STOCK)
            .Select(v => v.VehicleNumber)
            .ToListAsync();

        // Calculate spare parts cost
        var sparePartsCost = await _dbcontext.SparePartUsages
            .Where(u => companyVehicleNumbers.Contains(u.VehicleNumber) &&
                       u.UsedAt >= fromDate &&
                       u.UsedAt <= toDate)
            .SumAsync(u => (decimal?)(u.QuantityUsed * u.SparePart.Price)) ?? 0;

        // Get distinct vehicles serviced
        var vehiclesServiced = await _dbcontext.SparePartUsages
            .Where(u => companyVehicleNumbers.Contains(u.VehicleNumber) &&
                       u.UsedAt >= fromDate &&
                       u.UsedAt <= toDate)
            .Select(u => u.VehicleNumber)
            .Distinct()
            .CountAsync();

        // Calculate accessories cost from company stock
        var accessoryUsages = await _dbcontext.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Where(u => u.RiderAccessory.Location == COMPANY_STOCK &&
                       u.IssuedAt >= fromDate &&
                       u.IssuedAt <= toDate)
            .ToListAsync();

        var accessoriesCost = accessoryUsages.Sum(u => u.RiderAccessory.Price);
        var ridersServiced = accessoryUsages.Select(u => u.RiderId).Distinct().Count();

        return new CompanyStockCostSummary(
            sparePartsCost,
            accessoriesCost,
            sparePartsCost + accessoriesCost,
            vehiclesServiced,
            ridersServiced
        );
    }

    private async Task<List<VehicleSparePartCostDetail>> GetVehicleCostsForHousingAsync(
        List<string> vehicleNumbers,
        DateTime fromDate,
        DateTime toDate)
    {
        if (!vehicleNumbers.Any())
            return new List<VehicleSparePartCostDetail>();

        var usages = await _dbcontext.SparePartUsages
            .Include(u => u.SparePart)
            .Include(u => u.Vehicle)
            .Where(u => vehicleNumbers.Contains(u.VehicleNumber) &&
                       u.UsedAt >= fromDate &&
                       u.UsedAt <= toDate)
            .OrderByDescending(u => u.UsedAt)
            .ToListAsync();

        return usages
            .GroupBy(u => u.VehicleNumber)
            .Select(g =>
            {
                var vehicle = g.First().Vehicle;
                var usageDetails = g.Select(u => new SparePartUsageDetail(
                    u.SparePart.Name,
                    u.QuantityUsed,
                    u.SparePart.Price,
                    u.QuantityUsed * u.SparePart.Price,
                    u.UsedAt
                )).ToList();

                return new VehicleSparePartCostDetail(
                    vehicle.VehicleNumber,
                    vehicle.PlateNumberA,
                    vehicle.Location,
                    usageDetails.Sum(d => d.TotalCost),
                    usageDetails.Count,
                    usageDetails
                );
            })
            .ToList();
    }

    private async Task<List<RiderAccessoryCostDetail>> GetRiderCostsForHousingAsync(
        List<int> riderIds,
        string housingName,
        DateTime fromDate,
        DateTime toDate)
    {
        if (!riderIds.Any())
            return new List<RiderAccessoryCostDetail>();

        var usages = await _dbcontext.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Include(u => u.Rider)
                .ThenInclude(r => r.Employee)
            .Where(u => riderIds.Contains(u.RiderId) &&
                       u.IssuedAt >= fromDate &&
                       u.IssuedAt <= toDate)
            .OrderByDescending(u => u.IssuedAt)
            .ToListAsync();

        return usages
            .GroupBy(u => u.RiderId)
            .Select(g =>
            {
                var rider = g.First().Rider;
                var usageDetails = g.Select(u => new AccessoryUsageDetail(
                    u.RiderAccessory.Name,
                    u.RiderAccessory.Price,
                    u.IssuedAt
                )).ToList();

                return new RiderAccessoryCostDetail(
                    rider.Id,
                    rider.EmployeeIqamaNo,
                    rider.Employee.NameEN,
                    rider.Employee.NameAR,
                    rider.WorkingId ?? "N/A",
                    housingName,
                    usageDetails.Sum(d => d.Price),
                    usageDetails.Count,
                    usageDetails
                );
            })
            .ToList();
    }

    private HousingCostStatistics CalculateStatistics(
        List<VehicleSparePartCostDetail> vehicleCosts,
        List<RiderAccessoryCostDetail> riderCosts,
        decimal totalSparePartsCost,
        decimal totalAccessoriesCost)
    {
        var totalVehicles = vehicleCosts.Count;
        var totalRiders = riderCosts.Count;
        var totalSparePartUsages = vehicleCosts.Sum(v => v.UsageCount);
        var totalAccessoryUsages = riderCosts.Sum(r => r.AccessoryCount);
        var avgCostPerVehicle = totalVehicles > 0 ? totalSparePartsCost / totalVehicles : 0;
        var avgCostPerRider = totalRiders > 0 ? totalAccessoriesCost / totalRiders : 0;
        var topCostVehicle = vehicleCosts
            .OrderByDescending(v => v.TotalCost)
            .FirstOrDefault()?.VehicleNumber ?? "N/A";
        var topCostRider = riderCosts
            .OrderByDescending(r => r.TotalCost)
            .FirstOrDefault()?.RiderNameEN ?? "N/A";

        return new HousingCostStatistics(
            totalVehicles,
            totalRiders,
            totalSparePartUsages,
            totalAccessoryUsages,
            avgCostPerVehicle,
            avgCostPerRider,
            topCostVehicle,
            topCostRider
        );
    }

    private ComparisonInsights CalculateComparisonInsights(
        List<HousingComparisonItem> comparisons)
    {
        if (!comparisons.Any())
        {
            return new ComparisonInsights(
                "N/A", 0, "N/A", 0, 0, 0, "N/A"
            );
        }

        var highest = comparisons.OrderByDescending(c => c.TotalCost).First();
        var lowest = comparisons.OrderBy(c => c.TotalCost).First();
        var averageCost = comparisons.Average(c => c.TotalCost);
        var totalCost = comparisons.Sum(c => c.TotalCost);

        // Most efficient = lowest cost per unit (vehicle + rider)
        var mostEfficient = comparisons
            .OrderBy(c => c.CostPerVehicle + c.CostPerRider)
            .First()
            .HousingName;

        return new ComparisonInsights(
            highest.HousingName,
            highest.TotalCost,
            lowest.HousingName,
            lowest.TotalCost,
            averageCost,
            totalCost,
            mostEfficient
        );
    }

    public async Task<Result<BatchUsageResponse>> RecordBatchSparePartUsageAsync(DateTime Date,
        BatchSparePartUsageRequest request)
    {
        var details = new List<UsageResultDetail>();
        int successCount = 0;
        int failureCount = 0;

        using var transaction = await _dbcontext.Database.BeginTransactionAsync();

        try
        {
            foreach (var usage in request.Usages)
            {
                try
                {
                    var sparePart = await _dbcontext.SpareParts.FindAsync(usage.SparePartId);

                    if (sparePart == null)
                    {
                        details.Add(new UsageResultDetail(
                            false,
                            $"ID: {usage.SparePartId}",
                            usage.VehicleNumber,
                            "Spare part not found"
                        ));
                        failureCount++;
                        continue;
                    }

                    if (sparePart.Quantity < usage.QuantityUsed)
                    {
                        details.Add(new UsageResultDetail(
                            false,
                            sparePart.Name,
                            usage.VehicleNumber,
                            $"Insufficient quantity. Available: {sparePart.Quantity}, Requested: {usage.QuantityUsed}"
                        ));
                        failureCount++;
                        continue;
                    }

                    var vehicle = await _dbcontext.Vehicles
                        .FirstOrDefaultAsync(v => v.VehicleNumber == usage.VehicleNumber);

                    if (vehicle == null)
                    {
                        details.Add(new UsageResultDetail(
                            false,
                            sparePart.Name,
                            usage.VehicleNumber,
                            "Vehicle not found"
                        ));
                        failureCount++;
                        continue;
                    }

                    // Record usage
                    var sparePartUsage = new SparePartUsage
                    {
                        SparePartId = usage.SparePartId,
                        VehicleNumber = usage.VehicleNumber,
                        QuantityUsed = usage.QuantityUsed,
                        UsedAt = Date,
                        Location = "الشركة",
                        Cost = sparePart.Price * usage.QuantityUsed
                    };

                    await _dbcontext.SparePartUsages.AddAsync(sparePartUsage);

                    // Update quantity
                    sparePart.Quantity -= usage.QuantityUsed;

                    details.Add(new UsageResultDetail(
                        true,
                        sparePart.Name,
                        usage.VehicleNumber,
                        $"Successfully recorded {usage.QuantityUsed} units"
                    ));
                    successCount++;
                }
                catch (Exception ex)
                {
                    details.Add(new UsageResultDetail(
                        false,
                        $"ID: {usage.SparePartId}",
                        usage.VehicleNumber,
                        $"Error: {ex.Message}"
                    ));
                    failureCount++;
                }
            }

            await _dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            var response = new BatchUsageResponse(
                request.Usages.Count,
                successCount,
                failureCount,
                details
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<BatchUsageResponse>(
                new Error("BatchError", $"Batch operation failed: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<SparePartResponse>>> GetAllAsync()
    {
        var spareParts = await _dbcontext.SpareParts
            .AsNoTracking()
            .OrderBy(sp => sp.Name)
            .ToListAsync();

        var response = spareParts.Select(MapToResponse);
        return Result.Success<IEnumerable<SparePartResponse>>(response);
    }
    public async Task<Result<IEnumerable<SparePartResponse>>> GetAllAsync2()
    {
        var spareParts = await _dbcontext.SpareParts
            .Where(c => c.Location == "الشركة")
            .AsNoTracking()
            .OrderBy(sp => sp.Name)
            .ToListAsync();

        var response = spareParts.Select(MapToResponse);
        return Result.Success<IEnumerable<SparePartResponse>>(response);
    }

    public async Task<Result<SparePartResponse>> GetByIdAsync(int id)
    {
        var sparePart = await _dbcontext.SpareParts
            .AsNoTracking()
            .FirstOrDefaultAsync(sp => sp.Id == id);

        if (sparePart == null)
            return Result.Failure<SparePartResponse>(
                new Error("NotFound", "Spare part not found", 404));

        return Result.Success(MapToResponse(sparePart));
    }

    public async Task<Result<SparePartResponse>> CreateAsync(SparePartRequest request)
    {
        var sparePart = new Domain.Entities.Spare.SparePart
        {
            Name = request.Name,
            Quantity = request.Quantity,
            Price = request.Price,
            Location = request.Location,
            CreatedAt = DateTime.UtcNow.AddHours(3)
        };

        await _dbcontext.SpareParts.AddAsync(sparePart);
        await _dbcontext.SaveChangesAsync();

        return Result.Success(MapToResponse(sparePart));
    }

    public async Task<Result<SparePartResponse>> UpdateAsync(int id, SparePartRequest request)
    {
        var sparePart = await _dbcontext.SpareParts.FindAsync(id);

        if (sparePart == null)
            return Result.Failure<SparePartResponse>(
                new Error("NotFound", "Spare part not found", 404));

        sparePart.Name = request.Name;
        sparePart.Quantity = request.Quantity;
        sparePart.Price = request.Price;
        sparePart.Location = request.Location;

        await _dbcontext.SaveChangesAsync();

        return Result.Success(MapToResponse(sparePart));
    }

    public async Task<Result> DeleteAsync(int id)
    {
        var sparePart = await _dbcontext.SpareParts.FindAsync(id);

        if (sparePart == null)
            return Result.Failure(
                new Error("NotFound", "Spare part not found", 404));

        _dbcontext.SpareParts.Remove(sparePart);
        await _dbcontext.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<IEnumerable<SparePartResponse>>> SearchAsync(string keyword)
    {
        keyword = keyword.ToLower();

        var spareParts = await _dbcontext.SpareParts
            .Where(sp => sp.Name.ToLower().Contains(keyword) ||
                        sp.Location.ToLower().Contains(keyword))
            .AsNoTracking()
            .ToListAsync();

        var response = spareParts.Select(MapToResponse);
        return Result.Success<IEnumerable<SparePartResponse>>(response);
    }

    public async Task<Result<SparePartResponse>> RecordUsageAsync(int sparePartId, SparePartUsageRequest request)
    {
        using var transaction = await _dbcontext.Database.BeginTransactionAsync();

        try
        {
            var sparePart = await _dbcontext.SpareParts.FindAsync(sparePartId);

            if (sparePart == null)
                return Result.Failure<SparePartResponse>(
                    new Error("NotFound", "Spare part not found", 404));

            if (sparePart.Quantity < request.QuantityUsed)
                return Result.Failure<SparePartResponse>(
                    new Error("InsufficientQuantity",
                        $"Only {sparePart.Quantity} units available", 400));

            var vehicle = await _dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.VehicleNumber == request.VehicleNumber);

            if (vehicle == null)
                return Result.Failure<SparePartResponse>(
                    new Error("VehicleNotFound", "Vehicle not found", 404));

            // Record usage
            var usage = new SparePartUsage
            {
                SparePartId = sparePartId,
                VehicleNumber = request.VehicleNumber,
                QuantityUsed = request.QuantityUsed,
                UsedAt = DateTime.UtcNow.AddHours(3),
                Location = "الشركة",
            };

            await _dbcontext.SparePartUsages.AddAsync(usage);

            // Update quantity
            sparePart.Quantity -= request.QuantityUsed;

            await _dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            return Result.Success(MapToResponse(sparePart));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<SparePartResponse>(
                new Error("UsageError", $"Failed to record usage: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<SparePartUsageResponse>>> GetUsageHistoryAsync(int sparePartId)
    {
        var usages = await _dbcontext.SparePartUsages
            .Include(u => u.SparePart)
            .Where(u => u.SparePartId == sparePartId)
            .OrderByDescending(u => u.UsedAt)
            .AsNoTracking()
            .ToListAsync();

        var response = usages.Select(u => new SparePartUsageResponse(
            u.Id,
            u.SparePartId,
            u.SparePart.Name,
            u.VehicleNumber,
            u.QuantityUsed,
            u.UsedAt,
            u.Cost
        ));

        return Result.Success<IEnumerable<SparePartUsageResponse>>(response);
    }

    public async Task<Result<IEnumerable<SparePartUsageResponse>>> GetVehicleUsageHistoryAsync(string vehicleNumber)
    {
        var usages = await _dbcontext.SparePartUsages
            .Include(u => u.SparePart)
            .Where(u => u.VehicleNumber == vehicleNumber)
            .OrderByDescending(u => u.UsedAt)
            .AsNoTracking()
            .ToListAsync();

        var response = usages.Select(u => new SparePartUsageResponse(
            u.Id,
            u.SparePartId,
            u.SparePart.Name,
            u.VehicleNumber,
            u.QuantityUsed,
            u.UsedAt,
            u.Cost
        ));

        return Result.Success<IEnumerable<SparePartUsageResponse>>(response);
    }

    private static SparePartResponse MapToResponse(Domain.Entities.Spare.SparePart sparePart)
    {
        return new SparePartResponse(
            sparePart.Id,
            sparePart.Name,
            sparePart.Quantity,
            sparePart.Price,
            sparePart.Location,
            sparePart.CreatedAt
        );
    }
}