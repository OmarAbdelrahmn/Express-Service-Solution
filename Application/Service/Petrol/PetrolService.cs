using Application.Abstraction;
using Application.Contracts.Petrol;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Domain.Entities.Petrol;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.Petrol;

public class PetrolService(ApplicationDbcontext dbcontext) : IPetrolService
{
    private readonly ApplicationDbcontext _db = dbcontext;



    public async Task<Result<DailyPetrolReport>> GetDailyReportAsync(
    DateOnly date,
    CancellationToken ct = default)
    {
        try
        {
            // Load all VehiclePetrolCost rows for the date (the raw upload rows)
            var vehicleCosts = await _db.VehiclePetrolCosts
                .AsNoTracking()
                .Where(v => v.Date == date)
                .OrderBy(v => v.VehicleNumber)
                .ToListAsync(ct);

            if (vehicleCosts.Count == 0)
                return Result.Success(new DailyPetrolReport(
                    Date: date,
                    TotalCost: 0,
                    TotalVehicles: 0,
                    TotalAttributedRows: 0,
                    TotalUnattributedRows: 0,
                    Vehicles: []));

            // Load all RiderPetrolCost rows for the date in one query
            var riderCosts = await _db.RiderPetrolCosts
                .AsNoTracking()
                .Where(r => r.Date == date)
                .Include(r => r.Rider)
                .ToListAsync(ct);

            // Group rider attributions by their parent VehiclePetrolCostId
            var attributionsByVehicleCostId = riderCosts
                .GroupBy(r => r.VehiclePetrolCostId)
                .ToDictionary(g => g.Key, g => g.ToList());

            int totalAttributed = 0;
            int totalUnattributed = 0;

            var vehicleEntries = vehicleCosts.Select(vc =>
            {
                attributionsByVehicleCostId.TryGetValue(vc.Id, out var attributions);
                attributions ??= [];

                var attributed = attributions
                    .Select(r => new DailyRiderAttribution(
                        RiderIqamaNo: r.RiderIqamaNo,
                        RiderNameEN: r.Rider?.NameEN,
                        RiderNameAR: r.Rider?.NameAR,
                        Cost: r.Cost,
                        AttributionSource: r.AttributionSource,
                        Notes: r.Notes))
                    .ToList();

                int unattributedCount = attributions.Count(r => r.RiderIqamaNo == null);
                int attributedCount = attributions.Count(r => r.RiderIqamaNo != null);

                totalAttributed += attributedCount;
                totalUnattributed += unattributedCount;

                return new DailyVehicleEntry(
                    VehicleNumber: vc.VehicleNumber,
                    PlateNumberE: vc.PlateNumberE,
                    Cost: vc.Cost,
                    HasResolutionError: vc.HasResolutionError,
                    ResolutionErrorMessage: vc.ResolutionErrorMessage,
                    Note: vc.Note,
                    Attributions: attributed);
            }).ToList();

            return Result.Success(new DailyPetrolReport(
                Date: date,
                TotalCost: vehicleCosts.Sum(v => v.Cost),
                TotalVehicles: vehicleCosts.Count,
                TotalAttributedRows: totalAttributed,
                TotalUnattributedRows: totalUnattributed,
                Vehicles: vehicleEntries));
        }
        catch (Exception ex)
        {
            return Result.Failure<DailyPetrolReport>(
                new Error("QueryError", $"Failed to get daily report: {ex.Message}", 500));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // UPLOAD
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<Result<PetrolUploadResult>> ProcessUploadAsync(
        IFormFile file,
        DateOnly reportDate,
        string uploadedBy,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return Result.Failure<PetrolUploadResult>(
                new Error("InvalidFile", "File is empty or null", 400));

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Result.Failure<PetrolUploadResult>(
                new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        // Add this check immediately after validating the file, before ParseExcel:
        bool alreadyUploaded = await _db.VehiclePetrolCosts
            .AnyAsync(v => v.Date == reportDate, ct);

        if (alreadyUploaded)
            return Result.Failure<PetrolUploadResult>(
                new Error("DuplicateUpload",
                    $"Petrol data for {reportDate:yyyy-MM-dd} was already uploaded. " +
                    "Delete the existing records first or choose a different date.", 409));

        try
        {
            using var stream = file.OpenReadStream();
            var rows = ParseExcel(stream);

            if (rows.Count == 0)
                return Result.Failure<PetrolUploadResult>(
                    new Error("EmptyFile", "No data rows found in Excel file", 400));

            var allVehicles = await _db.Vehicles
                .AsNoTracking()
                .ToDictionaryAsync(v => v.PlateNumberE.Trim().ToUpperInvariant(), ct);

            var newCostRecords = new List<VehiclePetrolCost>();
            var rowDetails = new List<PetrolUploadRowDetail>();

            // REPLACE the existing error-record foreach with this:
            foreach (var record in newCostRecords.Where(r => r.HasResolutionError))
            {
                // Create an unattributed row so this vehicle is visible in all rider-cost queries
                _db.RiderPetrolCosts.Add(new RiderPetrolCost
                {
                    VehiclePetrolCostId = record.Id,
                    VehicleNumber = null,          // unknown — plate unresolved
                    Date = record.Date,
                    Cost = record.Cost,
                    RiderIqamaNo = null,
                    AttributionSource = PetrolAttributionSource.Unattributed,
                    Notes = record.ResolutionErrorMessage,
                    CreatedAt = DateTime.UtcNow.AddHours(3)
                });

                rowDetails.Add(new PetrolUploadRowDetail(
                    PlateNumberE: record.PlateNumberE,
                    ResolvedVehicleNumber: null,
                    Cost: record.Cost,
                    VehicleResolved: false,
                    AttributedRiderCount: 0,
                    ErrorMessage: record.ResolutionErrorMessage));
            }

            _db.VehiclePetrolCosts.AddRange(newCostRecords);
            await _db.SaveChangesAsync(ct);

            int attributed = 0;
            int unattributed = 0;

            foreach (var record in newCostRecords.Where(r => !r.HasResolutionError))
            {
                var attributedCount = await AttributeSingleAsync(record, ct);

                if (attributedCount > 0)
                    attributed++;
                else
                    unattributed++;

                rowDetails.Add(new PetrolUploadRowDetail(
                    PlateNumberE: record.PlateNumberE,
                    ResolvedVehicleNumber: record.VehicleNumber,
                    Cost: record.Cost,
                    VehicleResolved: true,
                    AttributedRiderCount: attributedCount,
                    ErrorMessage: attributedCount == 0
                        ? "No rider matched for this vehicle/date"
                        : null
                ));
            }

            foreach (var record in newCostRecords.Where(r => r.HasResolutionError))
            {
                rowDetails.Add(new PetrolUploadRowDetail(
                    PlateNumberE: record.PlateNumberE,
                    ResolvedVehicleNumber: null,
                    Cost: record.Cost,
                    VehicleResolved: false,
                    AttributedRiderCount: 0,
                    ErrorMessage: record.ResolutionErrorMessage));
            }

            await _db.SaveChangesAsync(ct);

            return Result.Success(new PetrolUploadResult(
                ReportDate: reportDate,
                TotalRows: rows.Count,
                SuccessfullyAttributed: attributed,
                Unattributed: unattributed,
                UnresolvedVehicles: newCostRecords.Count(r => r.HasResolutionError),
                Rows: rowDetails));
        }
        catch (Exception ex)
        {
            return Result.Failure<PetrolUploadResult>(
                new Error(ex.InnerException?.Message ?? ex.Message, $"Failed to process file: {ex.Message}", 500));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ATTRIBUTION
    // ═══════════════════════════════════════════════════════════════════════

    //public async Task<Result<(int total, int attributed, int unattributed)>> AttributePendingAsync(CancellationToken ct = default)
    //{
    //    try
    //    {
    //        var pending = await _db.VehiclePetrolCosts
    //            .Where(v => !v.IsAttributed && !v.HasResolutionError)
    //            .ToListAsync(ct);

    //        int attributed = 0;
    //        int unattributed = 0;

    //        foreach (var record in pending)
    //        {
    //            var count = await AttributeSingleAsync(record, ct);

    //            if (count > 0)
    //                attributed++;
    //            else
    //                unattributed++;
    //        }

    //        await _db.SaveChangesAsync(ct);

    //        return Result.Success((pending.Count, attributed, unattributed));
    //    }
    //    catch (Exception ex)
    //    {
    //        return Result.Failure<(int, int, int)>(
    //            new Error("AttributionError", $"Failed to attribute pending costs: {ex.Message}", 500));
    //    }
    //}

    public async Task<Result<(int total, int attributed, int unattributed)>> AttributePendingAsync(CancellationToken ct = default)
    {
        try
        {
            var pending = await _db.VehiclePetrolCosts
                .Where(v => !v.IsAttributed && !v.HasResolutionError)
                .ToListAsync(ct);

            int attributed = 0;
            int unattributed = 0;

            foreach (var record in pending)
            {
                var count = await AttributeSingleAsync(record, ct);
                if (count > 0) attributed++;
                else unattributed++;
            }

            // ── NEW: second pass — find today's active Permission for yesterday's unattributed rows ──
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
            var todayDt = today.ToDateTime(TimeOnly.MinValue);

            var unattributedRows = await _db.RiderPetrolCosts
                .Where(r => r.RiderIqamaNo == null && r.Date == today.AddDays(-1))
                .ToListAsync(ct);
            // ── second pass — find today's active Permission for yesterday's unattributed rows ──
            foreach (var row in unattributedRows)
            {
                if (string.IsNullOrWhiteSpace(row.VehicleNumber))
                    continue;

                var permission = await _db.RiderVehicleStatus
                    .Where(s => s.VehicleNumber == row.VehicleNumber
                             && s.EmployeeIqamaNo.HasValue
                             && s.PermissionStartDate.HasValue
                             && s.PermissionEndDate.HasValue
                             && s.PermissionStartDate.Value.Date <= todayDt.Date
                             && s.PermissionEndDate.Value.Date >= todayDt.Date)
                    .FirstOrDefaultAsync(ct);

                if (permission == null)
                    continue;

                // ── NEW: verify the employee actually exists ──────────────────────
                var employeeExists = await _db.Employees
                    .AnyAsync(e => e.IqamaNo == permission.EmployeeIqamaNo!.Value, ct);

                if (!employeeExists)
                {
                    row.Notes = $"[Second-pass skipped] IqamaNo {permission.EmployeeIqamaNo} not found in Employees.";
                    continue;
                }
                // ─────────────────────────────────────────────────────────────────

                row.RiderIqamaNo = permission.EmployeeIqamaNo!.Value;
                row.AttributionSource = PetrolAttributionSource.Permission;
                row.ResolvedFromStatusId = permission.Id;
                row.Notes = $"[Resolved via today's permission] "
                                        + $"Window: {permission.PermissionStartDate:yyyy-MM-dd} → {permission.PermissionEndDate:yyyy-MM-dd}";

                unattributed--;
                attributed++;
            }
            // ────────────────────────────────────────────────────────────────────

            await _db.SaveChangesAsync(ct);

            return Result.Success((pending.Count, attributed, unattributed));
        }
        catch (Exception ex)
        {
            return Result.Failure<(int, int, int)>(
                new Error("AttributionError", $"Failed to attribute pending costs: {ex.Message}", 500));
        }
    }

    public async Task<Result> AttributeSingleByIdAsync(int vehiclePetrolCostId, CancellationToken ct = default)
    {
        var record = await _db.VehiclePetrolCosts
            .FirstOrDefaultAsync(v => v.Id == vehiclePetrolCostId, ct);

        if (record is null)
            return Result.Failure(
                new Error("NotFound", $"VehiclePetrolCost with Id {vehiclePetrolCostId} not found", 404));

        try
        {
            await AttributeSingleAsync(record, ct);
            await _db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(
                new Error("AttributionError", $"Failed to attribute record: {ex.Message}", 500));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RIDER QUERIES
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<Result<RiderPetrolMonthlyReport>> GetRiderMonthlyReportAsync(
        long riderIqamaNo,
        int year,
        int month,
        CancellationToken ct = default)
    {
        var rider = await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.IqamaNo == riderIqamaNo, ct);

        if (rider is null)
            return Result.Failure<RiderPetrolMonthlyReport>(
                new Error("NotFound", "Rider not found", 404));

        var costs = await _db.RiderPetrolCosts
            .AsNoTracking()
            .Where(r => r.RiderIqamaNo == riderIqamaNo && r.Date.Year == year && r.Date.Month == month)
            .Include(r => r.Vehicle)
            .OrderBy(r => r.VehicleNumber)
            .ThenBy(r => r.Date)
            .ToListAsync(ct);

        var vehicleEntries = costs
            .GroupBy(r => r.VehicleNumber)
            .Select(g =>
            {
                var daily = g
                    .Select(r => new RiderDailyPetrolEntry(r.Date, r.Cost, r.AttributionSource, r.Notes))
                    .ToList();

                return new RiderVehicleEntry(
                    VehicleNumber: g.Key,
                    PlateNumberE: g.First().Vehicle?.PlateNumberE ?? string.Empty,
                    VehicleTotalCost: g.Sum(r => r.Cost),
                    DaysUsed: g.Select(r => r.Date).Distinct().Count(),
                    DailyEntries: daily);
            })
            .ToList();

        return Result.Success(new RiderPetrolMonthlyReport(
            RiderIqamaNo: riderIqamaNo,
            RiderNameEN: rider.NameEN,
            RiderNameAR: rider.NameAR,
            Year: year,
            Month: month,
            TotalCost: vehicleEntries.Sum(v => v.VehicleTotalCost),
            TotalDaysWithCost: costs.Select(c => c.Date).Distinct().Count(),
            UniqueVehiclesUsed: vehicleEntries.Count,
            VehicleEntries: vehicleEntries));
    }

    public async Task<Result<IReadOnlyList<RiderPetrolSummaryRow>>> GetAllRidersSummaryAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        try
        {
            var data = await _db.RiderPetrolCosts
                .AsNoTracking()
                .Where(r => r.RiderIqamaNo != null && r.Date.Year == year && r.Date.Month == month)
                .Select(r => new
                {
                    RiderIqamaNo = r.RiderIqamaNo!.Value,
                    NameEN = r.Rider != null ? r.Rider.NameEN : string.Empty,
                    NameAR = r.Rider != null ? r.Rider.NameAR : string.Empty,
                    r.Cost,
                    r.VehicleNumber,
                    r.Date
                })
                .ToListAsync(ct); // ✅ move to memory

            var rows = data
                .GroupBy(r => new { r.RiderIqamaNo, r.NameEN, r.NameAR })
                .Select(g => new RiderPetrolSummaryRow(
                    g.Key.RiderIqamaNo,
                    g.Key.NameEN,
                    g.Key.NameAR,
                    g.Sum(r => r.Cost),
                    g.Select(r => r.VehicleNumber).Distinct().Count(),
                    g.Select(r => r.Date).Distinct().Count()
                ))
                .OrderByDescending(r => r.TotalCost)
                .ToList();

            return Result.Success<IReadOnlyList<RiderPetrolSummaryRow>>(rows);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<RiderPetrolSummaryRow>>(
                new Error("QueryError", $"Failed to get riders summary: {ex.Message}", 500));
        }
    }

    public async Task<Result<IReadOnlyList<RiderDailyPetrolEntry>>> GetRiderCostsOnDateAsync(
        long riderIqamaNo,
        DateOnly date,
        CancellationToken ct = default)
    {
        try
        {
            var entries = await _db.RiderPetrolCosts
                .AsNoTracking()
                .Where(r => r.RiderIqamaNo.HasValue
                         && r.RiderIqamaNo.Value == riderIqamaNo
                         && r.Date == date)
                .Select(r => new RiderDailyPetrolEntry(r.Date, r.Cost, r.AttributionSource, r.Notes))
                .ToListAsync(ct);

            return Result.Success<IReadOnlyList<RiderDailyPetrolEntry>>(entries);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<RiderDailyPetrolEntry>>(
                new Error("QueryError", $"Failed to get rider costs: {ex.Message}", 500));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // VEHICLE QUERIES
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<Result<VehiclePetrolMonthlyReport>> GetVehicleMonthlyReportAsync(
        string vehicleNumber,
        int year,
        int month,
        CancellationToken ct = default)
    {
        var vehicle = await _db.Vehicles
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.VehicleNumber == vehicleNumber, ct);

        if (vehicle is null)
            return Result.Failure<VehiclePetrolMonthlyReport>(
                new Error("NotFound", "Vehicle not found", 404));

        var costs = await _db.RiderPetrolCosts
            .Include(v=>v.Vehicle)
            .AsNoTracking()
            .Where(r => r.VehicleNumber == vehicleNumber && r.Date.Year == year && r.Date.Month == month)
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
                    .Select(c => new VehicleDailyPetrolEntry(c.Date, c.Cost, c.AttributionSource, c.Notes))
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
            .Select(c => new VehicleUnattributedEntry(c.Vehicle?.PlateNumberE,c.Date, c.Cost, c.Notes))
            .ToList();

        return Result.Success(new VehiclePetrolMonthlyReport(
            VehicleNumber: vehicleNumber,
            PlateNumberE: vehicle.PlateNumberE,
            Year: year,
            Month: month,
            TotalCost: costs.Sum(c => c.Cost),
            TotalDaysWithCost: costs.Select(c => c.Date).Distinct().Count(),
            UniqueRidersCount: riderEntries.Count,
            RiderEntries: riderEntries,
            UnattributedEntries: unattributedEntries));
    }

    public async Task<Result<IReadOnlyList<VehiclePetrolSummaryRow>>> GetAllVehiclesSummaryAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        try
        {
            var data = await _db.RiderPetrolCosts
                .AsNoTracking()
                .Where(r => r.Date.Year == year && r.Date.Month == month)
                .Select(r => new
                {
                    r.VehicleNumber,
                    PlateNumberE = r.Vehicle != null ? r.Vehicle.PlateNumberE : null,
                    r.Cost,
                    r.RiderIqamaNo,
                    r.Date
                })
                .ToListAsync(ct); // ✅ move to memory

            var rows = data
                .GroupBy(r => new { r.VehicleNumber, r.PlateNumberE })
                .Select(g => new VehiclePetrolSummaryRow(
                    g.Key.VehicleNumber,
                    g.Key.PlateNumberE ?? string.Empty,
                    g.Sum(r => r.Cost),
                    g.Where(r => r.RiderIqamaNo != null)
                     .Select(r => r.RiderIqamaNo)
                     .Distinct()
                     .Count(),
                    g.Select(r => r.Date)
                     .Distinct()
                     .Count(),
                    g.Count(r => r.RiderIqamaNo == null)
                ))
                .OrderByDescending(r => r.TotalCost)
                .ToList();

            return Result.Success<IReadOnlyList<VehiclePetrolSummaryRow>>(rows);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<VehiclePetrolSummaryRow>>(
                new Error("QueryError", $"Failed to get vehicles summary: {ex.Message}", 500));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DELETE BY DATE
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<Result<PetrolDeleteResult>> DeleteByDateAsync(
        DateOnly date,
        CancellationToken ct = default)
    {
        try
        {
            // Load rider costs first (child records — FK to VehiclePetrolCosts)
            var riderCosts = await _db.RiderPetrolCosts
                .Where(r => r.Date == date)
                .ToListAsync(ct);

            // Load vehicle costs (parent records)
            var vehicleCosts = await _db.VehiclePetrolCosts
                .Where(v => v.Date == date)
                .ToListAsync(ct);

            if (riderCosts.Count == 0 && vehicleCosts.Count == 0)
                return Result.Failure<PetrolDeleteResult>(
                    new Error("NotFound",
                        $"No petrol records found for date {date:yyyy-MM-dd}.", 404));

            // Delete children before parents to respect FK constraints
            _db.RiderPetrolCosts.RemoveRange(riderCosts);
            _db.VehiclePetrolCosts.RemoveRange(vehicleCosts);

            await _db.SaveChangesAsync(ct);

            return Result.Success(new PetrolDeleteResult(
                Date: date,
                VehicleCostsDeleted: vehicleCosts.Count,
                RiderCostsDeleted: riderCosts.Count,
                DeletedAt: DateTime.UtcNow.AddHours(3)));
        }
        catch (Exception ex)
        {
            return Result.Failure<PetrolDeleteResult>(
                new Error("DeleteError",
                    $"Failed to delete petrol records for {date:yyyy-MM-dd}: {ex.Message}", 500));
        }
    }

    public async Task<Result<IReadOnlyList<VehicleUnattributedEntry>>> GetUnattributedCostsAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        try
        {
            // Materialise into memory first, then project — Include() is ignored inside Select()
            var raw = await _db.RiderPetrolCosts
                .Include(r => r.Vehicle)
                .Include(r => r.VehiclePetrolCost)
                .AsNoTracking()
                .Where(r => r.RiderIqamaNo == null
                         && r.Date.Year == year
                         && r.Date.Month == month)
                .OrderBy(r => r.VehicleNumber)
                .ThenBy(r => r.Date)
                .ToListAsync(ct);

            // FIX: project in memory so navigation properties are accessible
            var entries = raw
                .Select(r => new VehicleUnattributedEntry(
                    r.Vehicle?.PlateNumberE ?? r.VehicleNumber ?? "",  // null-safe
                    r.Date,
                    r.Cost,
                    r.VehiclePetrolCost?.Note))
                .ToList();

            return Result.Success<IReadOnlyList<VehicleUnattributedEntry>>(entries);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<VehicleUnattributedEntry>>(
                new Error("QueryError", $"Failed to get unattributed costs: {ex.Message}", 500));
        }
    }

    public async Task<Result<IReadOnlyList<VehicleDailyPetrolEntry>>> GetVehicleCostsOnDateAsync(
        string vehicleNumber,
        DateOnly date,
        CancellationToken ct = default)
    {
        try
        {
            var entries = await _db.RiderPetrolCosts
                .AsNoTracking()
                .Where(r => r.VehicleNumber == vehicleNumber && r.Date == date)
                .Select(r => new VehicleDailyPetrolEntry(r.Date, r.Cost, r.AttributionSource, r.Notes))
                .ToListAsync(ct);

            return Result.Success<IReadOnlyList<VehicleDailyPetrolEntry>>(entries);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<VehicleDailyPetrolEntry>>(
                new Error("QueryError", $"Failed to get vehicle costs: {ex.Message}", 500));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PRIVATE — ATTRIBUTION ENGINE
    // ═══════════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════════
    // PRIVATE — ATTRIBUTION ENGINE
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<int> AttributeSingleAsync(VehiclePetrolCost record, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(record.VehicleNumber))
            return 0;

        var dayStart = record.Date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = record.Date.ToDateTime(TimeOnly.MaxValue);

        var riders = await ResolveRidersAsync(record.VehicleNumber, record.Date, dayStart, dayEnd, ct);

        // ── NEW: drop any resolved rider whose IqamaNo is not in Employees ──
        if (riders.Count > 0)
        {
            var iqamaSet = riders.Select(r => r.IqamaNo).ToHashSet();

            var validIqamas = await _db.Employees
                .Where(e => iqamaSet.Contains(e.IqamaNo))
                .Select(e => e.IqamaNo)
                .ToHashSetAsync(ct);

            var invalidRiders = riders
                .Where(r => !validIqamas.Contains(r.IqamaNo))
                .ToList();

            if (invalidRiders.Count > 0)
            {
                // Log which iqamas were dropped so this is traceable
                var dropped = string.Join(", ", invalidRiders.Select(r => r.IqamaNo));

                // Keep only valid riders; fall through to unattributed if none remain
                riders = riders
                    .Where(r => validIqamas.Contains(r.IqamaNo))
                    .ToList();

                // If ALL resolved riders were invalid, add an unattributed row with a clear note
                if (riders.Count == 0)
                {
                    _db.RiderPetrolCosts.Add(new RiderPetrolCost
                    {
                        VehiclePetrolCostId = record.Id,
                        VehicleNumber = record.VehicleNumber,
                        Date = record.Date,
                        Cost = record.Cost,
                        RiderIqamaNo = null,
                        AttributionSource = PetrolAttributionSource.Unattributed,
                        Notes = $"Resolved rider(s) [{dropped}] not found in Employees table. Manual review required.",
                        CreatedAt = DateTime.UtcNow.AddHours(3)
                    });

                    record.IsAttributed = true;
                    return 0;
                }
            }
        }

        if (riders.Count == 0)
        {
            _db.RiderPetrolCosts.Add(new RiderPetrolCost
            {
                VehiclePetrolCostId = record.Id,
                VehicleNumber = record.VehicleNumber,
                Date = record.Date,
                Cost = record.Cost,
                RiderIqamaNo = null,
                AttributionSource = PetrolAttributionSource.Unattributed,
                Notes = "No active rider found for this vehicle on this date.",
                CreatedAt = DateTime.UtcNow.AddHours(3)
            });

            record.IsAttributed = true;
            return 0;
        }
        else
        {
            var splits = await ComputeSplitAsync(record.Cost, riders, record.VehicleNumber, dayStart, dayEnd, ct);

            for (int i = 0; i < riders.Count; i++)
            {
                var resolved = riders[i];
                var (share, splitNote) = splits[i];

                _db.RiderPetrolCosts.Add(new RiderPetrolCost
                {
                    VehiclePetrolCostId = record.Id,
                    VehicleNumber = record.VehicleNumber,
                    Date = record.Date,
                    Cost = share,
                    RiderIqamaNo = resolved.IqamaNo,
                    AttributionSource = resolved.Source,
                    ResolvedFromStatusId = resolved.StatusId,
                    Notes = string.IsNullOrEmpty(resolved.Notes)
                        ? splitNote
                        : $"{resolved.Notes} | {splitNote}",
                    CreatedAt = DateTime.UtcNow.AddHours(3)
                });
            }

            record.IsAttributed = true;
            return riders.Count;
        }
    }
    /// <summary>
    /// Splits totalCost among resolved riders.
    /// Uses time-based split if PermissionStartDate/EndDate are available on the status records,
    /// otherwise falls back to equal split.
    /// Returns a list of (share, note) in the same order as <paramref name="riders"/>.
    /// </summary>
    private async Task<List<(decimal Share, string Note)>> ComputeSplitAsync(
        decimal totalCost,
        IReadOnlyList<ResolvedRider> riders,
        string vehicleNumber,
        DateTime dayStart,
        DateTime dayEnd,
        CancellationToken ct)
    {
        if (riders.Count == 1)
            return [(totalCost, "Single rider — full cost attributed.")];

        var statusIds = riders
            .Where(r => r.StatusId > 0)
            .Select(r => r.StatusId)
            .ToList();

        // FIX: was synchronous .ToList() — now properly async
        var statuses = await _db.RiderVehicleStatus
            .Where(s => statusIds.Contains(s.Id))
            .AsNoTracking()
            .ToListAsync(ct);

        var windows = riders.Select(r =>
        {
            var s = statuses.FirstOrDefault(st => st.Id == r.StatusId);
            if (s?.PermissionStartDate == null) return (Hours: (double?)null, Rider: r);

            var start = s.PermissionStartDate!.Value < dayStart ? dayStart : s.PermissionStartDate.Value;
            var end = s.PermissionEndDate.HasValue
                ? (s.PermissionEndDate.Value > dayEnd ? dayEnd : s.PermissionEndDate.Value)
                : dayEnd;

            var hours = (end - start).TotalHours;
            return (Hours: hours > 0 ? (double?)hours : null, Rider: r);
        }).ToList();

        bool canUseTimeBased = windows.All(w => w.Hours.HasValue);
        double totalHours = canUseTimeBased ? windows.Sum(w => w.Hours!.Value) : 0;
        canUseTimeBased = canUseTimeBased && totalHours > 0;

        var result = new List<(decimal Share, string Note)>();

        if (canUseTimeBased)
        {
            var shares = windows
                .Select(w => Math.Round(totalCost * (decimal)(w.Hours!.Value / totalHours), 2))
                .ToList();

            // Fix rounding: make the last rider absorb any penny difference
            decimal distributed = shares.Sum();
            shares[^1] += totalCost - distributed;

            for (int i = 0; i < riders.Count; i++)
                result.Add((shares[i],
                    $"Time-based split: {windows[i].Hours:F2}h of {totalHours:F2}h total → {shares[i]:F2} SAR"));
        }
        else
        {
            // Equal split
            decimal equalShare = Math.Round(totalCost / riders.Count, 2);
            decimal lastShare = totalCost - equalShare * (riders.Count - 1);

            for (int i = 0; i < riders.Count; i++)
            {
                decimal share = i == riders.Count - 1 ? lastShare : equalShare;
                result.Add((share, $"Equal split ({riders.Count} riders) → {share:F2} SAR"));
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<ResolvedRider>> ResolveRidersAsync(
        string vehicleNumber,
        DateOnly reportDate,
        DateTime dayStart,
        DateTime dayEnd,
        CancellationToken ct)
    {
        var allStatuses = await _db.RiderVehicleStatus
            .Where(s => s.VehicleNumber == vehicleNumber)
            .OrderBy(s => s.Timestamp)
            .AsNoTracking()
            .ToListAsync(ct);

        var results = new List<ResolvedRider>();
        
        // Priority 1: explicit permission window
        var permissionHolders = allStatuses
            .Where(s => s.EmployeeIqamaNo.HasValue
                     && s.PermissionStartDate.HasValue
                     && s.PermissionEndDate.HasValue
                     && s.PermissionStartDate.Value.Date <= dayStart.Date
                     && s.PermissionEndDate.Value.Date >= dayEnd.Date)
            .ToList();

        if (permissionHolders.Count > 0)
        {
            foreach (var s in permissionHolders)
                results.Add(new ResolvedRider(
                    s.EmployeeIqamaNo!.Value,
                    PetrolAttributionSource.Permission,
                    s.Id,
                    $"Permission window: {s.PermissionStartDate:yyyy-MM-dd} → {s.PermissionEndDate:yyyy-MM-dd}"));

            return Deduplicate(results);
        }

        // Priority 2: Taken/Returned timeline
        var activeToday = new Dictionary<long, int>();
        long? currentHolder = null;
        int? currentStatusId = null;

        foreach (var evt in allStatuses)
        {
            if (evt.Timestamp > dayEnd) break;

            switch (evt.StatusType)
            {
                case VehicleStatusType.Taken:
                case VehicleStatusType.switched:
                    if (evt.EmployeeIqamaNo.HasValue)
                    {
                        currentHolder = evt.EmployeeIqamaNo.Value;
                        currentStatusId = evt.Id;
                        if (evt.Timestamp.Date <= dayEnd.Date)
                            activeToday[currentHolder.Value] = currentStatusId!.Value;
                    }
                    break;

                case VehicleStatusType.Returned:
                case VehicleStatusType.BreakUp:
                case VehicleStatusType.Stolen:
                case VehicleStatusType.OutOfService:
                    if (evt.Timestamp.Date < dayStart.Date)
                    {
                        if (currentHolder.HasValue) activeToday.Remove(currentHolder.Value);
                        currentHolder = null;
                        currentStatusId = null;
                    }
                    break;
            }
        }

        foreach (var (iqama, statusId) in activeToday)
            results.Add(new ResolvedRider(
                iqama,
                PetrolAttributionSource.VehicleStatusTimeline,
                statusId,
                activeToday.Count > 1
                    ? $"Vehicle had {activeToday.Count} riders on this date; cost attributed to each."
                    : null));

        return Deduplicate(results);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PRIVATE — EXCEL PARSER
    // ═══════════════════════════════════════════════════════════════════════

    private static List<PetrolExcelRow> ParseExcel(Stream stream)
    {
        var result = new List<PetrolExcelRow>();

        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.First();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var plateRaw = row.Cell(1).GetString().Trim();
            var costRaw = row.Cell(2).GetString().Trim();

            if (string.IsNullOrWhiteSpace(plateRaw)) continue;
            if (!decimal.TryParse(costRaw, out var cost)) continue;

            var plate = NormalizePlate(plateRaw);

            result.Add(new PetrolExcelRow(plate, cost));
        }

        return result;
    }

    private static string NormalizePlate(string plate)
    {
        if (string.IsNullOrWhiteSpace(plate))
            return plate;

        plate = plate.Trim();

        // split digits and letters
        var digits = new string(plate.Where(char.IsDigit).ToArray());
        var letters = new string(plate.Where(char.IsLetter).ToArray());

        return $"{letters}{digits}";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    private static IReadOnlyList<ResolvedRider> Deduplicate(List<ResolvedRider> riders) =>
        riders.GroupBy(r => r.IqamaNo).Select(g => g.First()).ToList();

    public async Task<Result> AddVehicleNoteAsync(string vehicleNumber, string note,DateOnly Date, CancellationToken ct = default)
    {
        var vehicle =await  _db.Vehicles.FirstOrDefaultAsync(v => v.PlateNumberE == (vehicleNumber), ct);
        if (vehicle == null)
            return Result.Failure(
                new Error("NotFound", $"Vehicle with number {vehicleNumber} not found", 404));

        var VehicelCosts = await _db.VehiclePetrolCosts.Where(c => c.VehicleNumber == vehicle.VehicleNumber && c.Date == Date).SingleOrDefaultAsync(ct);

        if (VehicelCosts == null)
            return Result.Failure(
                new Error("NotFounddddd", $"No petrol cost record found for vehicle {vehicleNumber} on date {Date}", 404));


        var existingNote = VehicelCosts.Note;

        VehicelCosts.Note = string.IsNullOrEmpty(existingNote)
            ? note
            : note;

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }


    private readonly record struct ResolvedRider(
        long IqamaNo,
        PetrolAttributionSource Source,
        int StatusId,
        string? Notes);

    private record PetrolExcelRow(string PlateNumberE, decimal Cost);
}