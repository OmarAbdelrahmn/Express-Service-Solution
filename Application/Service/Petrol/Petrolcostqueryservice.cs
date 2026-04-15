using Domain.Entities;
using Domain.Models.Petrol;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Read-only query service for all petrol cost reports.
/// All methods are optimised to issue the minimum number of SQL round-trips.
/// </summary>
public class PetrolCostQueryService(IApplicationDbContext db)
{
    // ═════════════════════════════════════════════════════════════════════
    // RIDER REPORTS
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Full monthly petrol breakdown for a single rider:
    /// one entry per vehicle used, with a per-day cost list inside each.
    /// Answers: "How much petrol did rider X spend per vehicle in month Y?"
    /// </summary>
    public async Task<RiderPetrolMonthlyReport?> GetRiderMonthlyReportAsync(
        long riderIqamaNo,
        int year,
        int month,
        CancellationToken ct = default)
    {
        var rider = await db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.IqamaNo == riderIqamaNo, ct);

        if (rider is null) return null;

        var costs = await db.RiderPetrolCosts
            .AsNoTracking()
            .Where(r => r.RiderIqamaNo == riderIqamaNo
                     && r.Date.Year == year
                     && r.Date.Month == month)
            .Include(r => r.Vehicle)
            .OrderBy(r => r.VehicleNumber)
            .ThenBy(r => r.Date)
            .ToListAsync(ct);

        var vehicleEntries = costs
            .GroupBy(r => r.VehicleNumber)
            .Select(g =>
            {
                var daily = g
                    .Select(r => new RiderDailyPetrolEntry(
                        r.Date,
                        r.Cost,
                        r.AttributionSource,
                        r.Notes))
                    .ToList();

                return new RiderVehicleEntry(
                    VehicleNumber: g.Key,
                    PlateNumberE: g.First().Vehicle?.PlateNumberE ?? string.Empty,
                    VehicleTotalCost: g.Sum(r => r.Cost),
                    DaysUsed: g.Select(r => r.Date).Distinct().Count(),
                    DailyEntries: daily);
            })
            .ToList();

        return new RiderPetrolMonthlyReport(
            RiderIqamaNo: riderIqamaNo,
            RiderNameEN: rider.NameEN,
            RiderNameAR: rider.NameAR,
            Year: year,
            Month: month,
            TotalCost: vehicleEntries.Sum(v => v.VehicleTotalCost),
            TotalDaysWithCost: costs.Select(c => c.Date).Distinct().Count(),
            UniqueVehiclesUsed: vehicleEntries.Count,
            VehicleEntries: vehicleEntries);
    }

    /// <summary>
    /// Summary list of all riders who had petrol costs in a given month, ordered by total cost desc.
    /// Useful for a dashboard / export table.
    /// </summary>
    public async Task<IReadOnlyList<RiderPetrolSummaryRow>> GetAllRidersSummaryAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        // Single query: join employees for names, group in-DB
        var rows = await db.RiderPetrolCosts
            .AsNoTracking()
            .Where(r => r.RiderIqamaNo != null
                     && r.Date.Year == year
                     && r.Date.Month == month)
            .GroupBy(r => new { r.RiderIqamaNo, r.Rider!.NameEN, r.Rider.NameAR })
            .Select(g => new RiderPetrolSummaryRow(
                g.Key.RiderIqamaNo!.Value,
                g.Key.NameEN,
                g.Key.NameAR,
                g.Sum(r => r.Cost),
                g.Select(r => r.VehicleNumber).Distinct().Count(),
                g.Select(r => r.Date).Distinct().Count()))
            .OrderByDescending(r => r.TotalCost)
            .ToListAsync(ct);

        return rows;
    }

    // ═════════════════════════════════════════════════════════════════════
    // VEHICLE REPORTS
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Full monthly petrol breakdown for a single vehicle:
    /// one entry per rider who used it, with a per-day cost list inside each.
    /// Answers: "Who used vehicle X in month Y and how much did each cost?"
    /// </summary>
    public async Task<VehiclePetrolMonthlyReport?> GetVehicleMonthlyReportAsync(
        string vehicleNumber,
        int year,
        int month,
        CancellationToken ct = default)
    {
        var vehicle = await db.Vehicles
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.VehicleNumber == vehicleNumber, ct);

        if (vehicle is null) return null;

        var costs = await db.RiderPetrolCosts
            .AsNoTracking()
            .Where(r => r.VehicleNumber == vehicleNumber
                     && r.Date.Year == year
                     && r.Date.Month == month)
            .Include(r => r.Rider)
            .OrderBy(r => r.RiderIqamaNo)
            .ThenBy(r => r.Date)
            .ToListAsync(ct);

        var attributed = costs.Where(c => c.RiderIqamaNo.HasValue).ToList();
        var unattributed = costs.Where(c => !c.RiderIqamaNo.HasValue).ToList();

        var riderEntries = attributed
            .GroupBy(c => c.RiderIqamaNo!.Value)
            .Select(g =>
            {
                var riderName = g.First().Rider;
                var daily = g
                    .Select(c => new VehicleDailyPetrolEntry(
                        c.Date,
                        c.Cost,
                        c.AttributionSource,
                        c.Notes))
                    .ToList();

                return new VehicleRiderEntry(
                    RiderIqamaNo: g.Key,
                    RiderNameEN: riderName?.NameEN ?? "Unknown",
                    RiderNameAR: riderName?.NameAR ?? "Unknown",
                    RiderTotalCost: g.Sum(c => c.Cost),
                    DaysUsed: g.Select(c => c.Date).Distinct().Count(),
                    DailyEntries: daily);
            })
            .ToList();

        var unattributedEntries = unattributed
            .Select(c => new VehicleUnattributedEntry(c.Date, c.Cost, c.Notes))
            .ToList();

        return new VehiclePetrolMonthlyReport(
            VehicleNumber: vehicleNumber,
            PlateNumberE: vehicle.PlateNumberE,
            Year: year,
            Month: month,
            TotalCost: costs.Sum(c => c.Cost),
            TotalDaysWithCost: costs.Select(c => c.Date).Distinct().Count(),
            UniqueRidersCount: riderEntries.Count,
            RiderEntries: riderEntries,
            UnattributedEntries: unattributedEntries);
    }

    /// <summary>
    /// Summary list of all vehicles that had petrol costs in a given month, ordered by total cost desc.
    /// </summary>
    public async Task<IReadOnlyList<VehiclePetrolSummaryRow>> GetAllVehiclesSummaryAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        var rows = await db.RiderPetrolCosts
            .AsNoTracking()
            .Where(r => r.Date.Year == year && r.Date.Month == month)
            .GroupBy(r => new { r.VehicleNumber, r.Vehicle!.PlateNumberE })
            .Select(g => new VehiclePetrolSummaryRow(
                g.Key.VehicleNumber,
                g.Key.PlateNumberE,
                g.Sum(r => r.Cost),
                g.Where(r => r.RiderIqamaNo != null).Select(r => r.RiderIqamaNo).Distinct().Count(),
                g.Select(r => r.Date).Distinct().Count(),
                g.Count(r => r.RiderIqamaNo == null)))
            .OrderByDescending(r => r.TotalCost)
            .ToListAsync(ct);

        return rows;
    }

    // ═════════════════════════════════════════════════════════════════════
    // CROSS REPORTS
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// All unattributed petrol costs (no rider could be found) for a given month.
    /// Used by admins to manually assign or investigate.
    /// </summary>
    public async Task<IReadOnlyList<VehicleUnattributedEntry>> GetUnattributedCostsAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        return await db.RiderPetrolCosts
            .AsNoTracking()
            .Where(r => r.RiderIqamaNo == null
                     && r.Date.Year == year
                     && r.Date.Month == month)
            .OrderBy(r => r.VehicleNumber)
            .ThenBy(r => r.Date)
            .Select(r => new VehicleUnattributedEntry(r.Date, r.Cost, r.Notes))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Returns petrol costs for a specific rider on a specific date.
    /// Useful for a drill-down when a rider disputes a charge.
    /// </summary>
    public async Task<IReadOnlyList<RiderDailyPetrolEntry>> GetRiderCostsOnDateAsync(
        long riderIqamaNo,
        DateOnly date,
        CancellationToken ct = default)
    {
        return await db.RiderPetrolCosts
            .AsNoTracking()
            .Where(r => r.RiderIqamaNo == riderIqamaNo && r.Date == date)
            .Select(r => new RiderDailyPetrolEntry(
                r.Date,
                r.Cost,
                r.AttributionSource,
                r.Notes))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Returns petrol costs for a specific vehicle on a specific date — shows all riders that day.
    /// </summary>
    public async Task<IReadOnlyList<VehicleDailyPetrolEntry>> GetVehicleCostsOnDateAsync(
        string vehicleNumber,
        DateOnly date,
        CancellationToken ct = default)
    {
        return await db.RiderPetrolCosts
            .AsNoTracking()
            .Where(r => r.VehicleNumber == vehicleNumber && r.Date == date)
            .Select(r => new VehicleDailyPetrolEntry(
                r.Date,
                r.Cost,
                r.AttributionSource,
                r.Notes))
            .ToListAsync(ct);
    }
}