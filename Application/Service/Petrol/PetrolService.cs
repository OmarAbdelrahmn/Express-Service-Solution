using Application.Abstraction;
using Application.Contracts.Petrol;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Domain.Entities.Petrol;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace Application.Service.Petrol;

public class PetrolService(ApplicationDbcontext dbcontext) : IPetrolService
{
    private readonly ApplicationDbcontext _db = dbcontext;


    // ═══════════════════════════════════════════════════════════════════════
    // MANUAL ASSIGNMENT — attach a rider to an unrecognized (Unattributed) record
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<Result> AssignRiderToUnattributedAsync(
        string vehicleNumber,
        DateOnly date,
        long riderIqamaNo,
        string assignedBy,
        CancellationToken ct = default)
    {
        try
        {
            await using var transaction = await BeginPetrolTransactionAsync(ct);
            // ── 1. Resolve vehicle by plate number ─────────────────────────
            var vehicle = await _db.Vehicles
                .FirstOrDefaultAsync(v => v.PlateNumberE == vehicleNumber, ct);

            if (vehicle == null)
                return Result.Failure(
                    new Error("NotFound",
                        $"Vehicle with plate number {vehicleNumber} not found", 404));

            // ── 2. Validate the rider exists ────────────────────────────────
            var riderExists = await _db.Employees
                .AnyAsync(e => e.IqamaNo == riderIqamaNo, ct);

            if (!riderExists)
                return Result.Failure(
                    new Error("NotFound",
                        $"Rider with Iqama {riderIqamaNo} not found", 404));

            // ── 3. Locate that day's petrol cost record for the vehicle ────
            var vehicleCost = await _db.VehiclePetrolCosts
                .FirstOrDefaultAsync(c => c.VehicleNumber == vehicle.VehicleNumber && c.Date == date, ct);

            if (vehicleCost == null)
                return Result.Failure(
                    new Error("NotFound",
                        $"No petrol cost record found for vehicle {vehicleNumber} on date {date}", 404));

            // ── 4. Locate the unrecognized (Unattributed) row for that record ─
            var unattributedRow = await _db.RiderPetrolCosts
                .Where(r => r.VehiclePetrolCostId == vehicleCost.Id && r.RiderIqamaNo == null)
                .FirstOrDefaultAsync(ct);

            if (unattributedRow == null)
                return Result.Failure(
                    new Error("AlreadyAttributed",
                        $"No unrecognized petrol record found for vehicle {vehicleNumber} on {date:yyyy-MM-dd}. " +
                        "It may already be attributed to a rider.", 409));

            var otherVehicles = await _db.RiderPetrolCosts.AsNoTracking()
                .Where(r => r.RiderIqamaNo == riderIqamaNo && r.Date == date
                    && r.VehicleNumber != null && r.VehicleNumber != vehicle.VehicleNumber)
                .Select(r => r.VehicleNumber!).Distinct().ToListAsync(ct);
            if (otherVehicles.Count > 0)
            {
                var candidates = otherVehicles.Append(vehicle.VehicleNumber).Distinct().ToList();
                var windows = await LoadAssignmentWindowsAsync(candidates, ct);
                if (candidates.Any(number => !PetrolAssignmentTimeline.OnDate(windows, number, date)
                    .Any(r => r.IqamaNo == riderIqamaNo)))
                    return Result.Failure(new Error("AssignmentConflict",
                        "This rider already has petrol for another vehicle on this date and no matching vehicle switch was found.", 409));
            }

            // ── 5. Assign the rider ─────────────────────────────────────────
            var previousNotes = unattributedRow.Notes;

            unattributedRow.RiderIqamaNo = riderIqamaNo;
            unattributedRow.VehicleNumber ??= vehicle.VehicleNumber;
            unattributedRow.AttributionSource = PetrolAttributionSource.ManualOverride;
            unattributedRow.ResolvedFromStatusId = null;
            unattributedRow.Notes = $"Manually assigned to rider {riderIqamaNo} by {assignedBy} on " +
                                     $"{DateTime.UtcNow.AddHours(3):yyyy-MM-dd HH:mm}." +
                                     (string.IsNullOrWhiteSpace(previousNotes) ? "" : $" Previously: {previousNotes}");

            vehicleCost.IsAttributed = true;

            await _db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DiscardPetrolChanges();
            return Result.Failure(
                new Error("AssignmentError",
                    $"Failed to assign rider to petrol record: {ex.Message}", 500));
        }
    }

    // ── Vehicles excluded from the unattributed report (by PlateNumberE) ─
    // Add any plate number here to hide it from GetUnattributedCostsAsync
    private static readonly HashSet<string> _unattributedExclusions =
    [
         "4762GUR",
         "6306DJR",
         "8475UBR",
         "5577LGA",
    ];

    public async Task<Result> ShiftPermissionStartDateAsync(
    long iqamaNo,
    CancellationToken ct = default)
    {
        var record = await _db.RiderVehicleStatus
            .Where(s => s.EmployeeIqamaNo == iqamaNo)
            .OrderByDescending(s => s.Timestamp)
            .FirstOrDefaultAsync(ct);

        if (record is null)
            return Result.Failure(
                new Error("NotFound",
                    $"No RiderVehicleStatus found for IqamaNo {iqamaNo}", 404));

        if (record.PermissionStartDate is null)
            return Result.Failure(
                new Error("InvalidOperation",
                    $"Latest record (Id {record.Id}) has no PermissionStartDate to shift", 400));

        record.PermissionStartDate = record.PermissionStartDate.Value.AddDays(-1);

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
    // ═══════════════════════════════════════════════════════════════════════
    // DAILY REPORT
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<Result<IReadOnlyList<RiderCompanyHousingReportRow>>> GetRidersCompanyHousingReportAsync(
    int year,
    int month,
    CancellationToken ct = default)
    {
        try
        {
            // ── Step 1: Load petrol data for the month ────────────────────────
            var data = await _db.RiderPetrolCosts
                .AsNoTracking()
                .Where(r => r.RiderIqamaNo != null
                         && r.Date.Year == year
                         && r.Date.Month == month)
                .Select(r => new
                {
                    RiderIqamaNo = r.RiderIqamaNo!.Value,
                    r.VehicleNumber,
                    r.Cost,
                    r.Date
                })
                .ToListAsync(ct);

            if (data.Count == 0)
                return Result.Success<IReadOnlyList<RiderCompanyHousingReportRow>>([]);

            var riderIqamas = data.Select(d => d.RiderIqamaNo).Distinct().ToList();

            // ── Step 2: Employee info + housing ────────────────────────────────
            var employees = await _db.Employees
                .AsNoTracking()
                .Where(e => riderIqamas.Contains(e.IqamaNo))
                .Select(e => new
                {
                    e.IqamaNo,
                    e.NameEN,
                    e.NameAR,
                    HousingName = e.Housing != null ? e.Housing.Name : null
                })
                .ToListAsync(ct);

            var employeeMap = employees.ToDictionary(e => e.IqamaNo);

            // ── Step 3: Company via RiderDetails ──────────────────────────────
            var riderDetails = await _db.RiderDetails
                .AsNoTracking()
                .Where(rd => riderIqamas.Contains(rd.EmployeeIqamaNo))
                .Select(rd => new
                {
                    rd.EmployeeIqamaNo,
                    CompanyName = rd.Company != null ? rd.Company.Name : null
                })
                .ToListAsync(ct);

            var companyMap = riderDetails
                .GroupBy(rd => rd.EmployeeIqamaNo)
                .ToDictionary(g => g.Key, g => g.First().CompanyName);

            // ── Step 4: Plate numbers for the vehicles used this month ───────
            var vehicleNumbers = data
                .Where(d => d.VehicleNumber != null)
                .Select(d => d.VehicleNumber!)
                .Distinct()
                .ToList();

            var plateMap = await _db.Vehicles
                .AsNoTracking()
                .Where(v => vehicleNumbers.Contains(v.VehicleNumber))
                .ToDictionaryAsync(v => v.VehicleNumber, v => v.PlateNumberE, ct);

            // ── Step 5: Build rows ─────────────────────────────────────────────
            var rows = data
                .GroupBy(d => d.RiderIqamaNo)
                .Select(g =>
                {
                    employeeMap.TryGetValue(g.Key, out var emp);
                    companyMap.TryGetValue(g.Key, out var companyName);

                    var vehiclesUsed = g
                        .Where(x => x.VehicleNumber != null)
                        .Select(x => x.VehicleNumber!)
                        .Distinct()
                        .Select(vn => plateMap.TryGetValue(vn, out var plate) ? plate : vn)
                        .ToList();

                    return new RiderCompanyHousingReportRow(
                        RiderIqamaNo: g.Key,
                        RiderNameEN: emp?.NameEN ?? string.Empty,
                        RiderNameAR: emp?.NameAR ?? string.Empty,
                        CompanyName: companyName ?? "Unknown",
                        HousingName: emp?.HousingName ?? "Unassigned",
                        VehiclesUsed: vehiclesUsed,
                        TotalCost: g.Sum(x => x.Cost),
                        DaysWithCost: g.Select(x => x.Date).Distinct().Count());
                })
                .OrderBy(r => r.HousingName)
                .ThenBy(r => r.CompanyName)
                .ThenByDescending(r => r.TotalCost)
                .ToList();

            return Result.Success<IReadOnlyList<RiderCompanyHousingReportRow>>(rows);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<RiderCompanyHousingReportRow>>(
                new Error("QueryError", $"Failed to get riders company/housing report: {ex.Message}", 500));
        }
    }
    public async Task<Result<DailyPetrolReport>> GetDailyReportAsync(
        DateOnly date,
        CancellationToken ct = default)
    {
        try
        {
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

            var riderCosts = await _db.RiderPetrolCosts
                .AsNoTracking()
                .Where(r => r.Date == date)
                .Include(r => r.Rider)
                .ToListAsync(ct);

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
            return Result.Failure<PetrolUploadResult>(new Error("InvalidFile", "File is empty or null", 400));
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            && !file.FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
            return Result.Failure<PetrolUploadResult>(new Error("InvalidFormat", "File must be Excel format (.xlsx or .xls)", 400));

        try
        {
            using var stream = file.OpenReadStream();
            var rows = ParseExcel(stream);
            if (rows.Count == 0)
                return Result.Failure<PetrolUploadResult>(new Error("EmptyFile", "No data rows found in Excel file", 400));

            var duplicates = rows.GroupBy(r => PlateKey(r.PlateNumberE))
                .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicates.Count > 0)
                return Result.Failure<PetrolUploadResult>(new Error("InvalidFile",
                    $"Duplicate vehicle plates in Excel: {string.Join(", ", duplicates)}", 400));

            await using var transaction = await BeginPetrolTransactionAsync(ct);
            var vehicles = await _db.Vehicles.AsNoTracking().ToListAsync(ct);
            var lookup = vehicles.GroupBy(v => PlateKey(v.PlateNumberE))
                .ToDictionary(g => g.Key, g => g.ToList());
            var existing = await _db.VehiclePetrolCosts.AsNoTracking()
                .Where(v => v.Date == reportDate).ToListAsync(ct);
            var newRecords = new List<VehiclePetrolCost>();
            foreach (var row in rows)
            {
                var key = PlateKey(row.PlateNumberE);
                lookup.TryGetValue(key, out var matches);
                var vehicle = matches?.Count == 1 ? matches[0] : null;
                var previous = existing.Where(c => PlateKey(c.PlateNumberE) == key
                    || (vehicle != null && c.VehicleNumber == vehicle.VehicleNumber)).ToList();
                if (previous.Count > 0)
                {
                    if (previous.Count != 1 || previous[0].Cost != row.Cost)
                        return Result.Failure<PetrolUploadResult>(new Error("DuplicateUpload",
                            $"Petrol cost for {row.PlateNumberE} on {reportDate:yyyy-MM-dd} already exists with a different or duplicated amount.", 409));
                    continue;
                }

                newRecords.Add(new VehiclePetrolCost
                {
                    PlateNumberE = row.PlateNumberE,
                    VehicleNumber = vehicle?.VehicleNumber,
                    Cost = row.Cost,
                    Date = reportDate,
                    UploadedAt = DateTime.UtcNow.AddHours(3),
                    UploadedBy = uploadedBy,
                    HasResolutionError = vehicle == null,
                    ResolutionErrorMessage = vehicle == null
                        ? $"Plate '{row.PlateNumberE}' could not be uniquely matched to a vehicle." : null
                });
            }

            if (newRecords.Count == 0)
                return Result.Failure<PetrolUploadResult>(new Error("DuplicateUpload",
                    $"All supplied petrol records for {reportDate:yyyy-MM-dd} were already uploaded.", 409));

            _db.VehiclePetrolCosts.AddRange(newRecords);
            var counts = await RebuildAllocationsAsync(newRecords, ct);
            // The source costs and their allocations commit together.
            await _db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);

            var details = newRecords.Select(r => new PetrolUploadRowDetail(
                r.PlateNumberE, r.VehicleNumber, r.Cost, !r.HasResolutionError,
                counts[r], r.ResolutionErrorMessage ?? (counts[r] == 0
                    ? "No rider assignment matched this vehicle/date." : null))).ToList();
            return Result.Success(new PetrolUploadResult(reportDate, newRecords.Count,
                newRecords.Count(r => counts[r] > 0),
                newRecords.Count(r => !r.HasResolutionError && counts[r] == 0),
                newRecords.Count(r => r.HasResolutionError), details));
        }
        catch (FormatException ex)
        {
            return Result.Failure<PetrolUploadResult>(new Error("InvalidFile", ex.Message, 400));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DiscardPetrolChanges();
            return Result.Failure<PetrolUploadResult>(new Error("UploadError",
                $"Failed to process file: {ex.Message}", 500));
        }
    }

    public async Task<Result<(int total, int attributed, int unattributed)>> AttributePendingAsync(
        CancellationToken ct = default)
    {
        try
        {
            await using var transaction = await BeginPetrolTransactionAsync(ct);
            // Revalidate saved automatic allocations as well: assignment history may
            // have been corrected since upload. Manual overrides survive the rebuild.
            var records = await _db.VehiclePetrolCosts.OrderBy(v => v.Date).ThenBy(v => v.Id).ToListAsync(ct);
            if (records.Count == 0) return Result.Success((0, 0, 0));
            var counts = await RebuildAllocationsAsync(records, ct);
            await _db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            var attributed = records.Count(r => counts[r] > 0);
            return Result.Success((records.Count, attributed, records.Count - attributed));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DiscardPetrolChanges();
            return Result.Failure<(int, int, int)>(new Error("AttributionError",
                $"Failed to attribute pending costs: {ex.Message}", 500));
        }
    }

    public async Task<Result> AttributeSingleByIdAsync(
        int vehiclePetrolCostId,
        CancellationToken ct = default)
    {
        try
        {
            await using var transaction = await BeginPetrolTransactionAsync(ct);
            var record = await _db.VehiclePetrolCosts.FirstOrDefaultAsync(v => v.Id == vehiclePetrolCostId, ct);
            if (record == null)
                return Result.Failure(new Error("NotFound", $"VehiclePetrolCost with Id {vehiclePetrolCostId} not found", 404));
            await RebuildAllocationsAsync([record], ct);
            await _db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DiscardPetrolChanges();
            return Result.Failure(new Error("AttributionError", $"Failed to attribute record: {ex.Message}", 500));
        }
    }
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
            // ── Step 1: Load petrol data ──────────────────────────────────
            var data = await _db.RiderPetrolCosts
                .AsNoTracking()
                .Where(r => r.RiderIqamaNo != null
                         && r.Date.Year == year
                         && r.Date.Month == month)
                .Select(r => new
                {
                    RiderIqamaNo = r.RiderIqamaNo!.Value,
                    NameEN = r.Rider != null ? r.Rider.NameEN : string.Empty,
                    NameAR = r.Rider != null ? r.Rider.NameAR : string.Empty,
                    r.Cost,
                    r.VehicleNumber,
                    r.Date
                })
                .ToListAsync(ct);

            var allIqamas = data.Select(r => r.RiderIqamaNo).Distinct().ToList();

            // ── Step 2: Load RiderDetails.Id for those IqamaNos ──────────
            // RiderShift links via RiderDetails.Id (int), not IqamaNo directly
            var riderDetailsMap = await _db.RiderDetails
                .AsNoTracking()
                .Where(rd => allIqamas.Contains(rd.EmployeeIqamaNo))
                .Select(rd => new { rd.Id, rd.EmployeeIqamaNo })
                .ToListAsync(ct);

            var iqamaToRiderDetailsId = riderDetailsMap
                .ToDictionary(rd => rd.EmployeeIqamaNo, rd => rd.Id);

            var riderDetailsIds = riderDetailsMap.Select(rd => rd.Id).ToList();

            // ── Step 3: Load RiderShift totals for this month ─────────────
            var shiftTotals = await _db.RiderShifts
                .AsNoTracking()
                .Where(s => riderDetailsIds.Contains(s.RiderId)
                         && s.ShiftDate.Year == year
                         && s.ShiftDate.Month == month)
                .GroupBy(s => s.RiderId)
                .Select(g => new { RiderId = g.Key, TotalOrders = g.Sum(s => s.AcceptedDailyOrders) })
                .ToListAsync(ct);

            var shiftByRiderDetailsId = shiftTotals
                .ToDictionary(s => s.RiderId, s => s.TotalOrders);

            // ── Step 4: Load KetaFreeLancer for this month ────────────────
            // Month is stored as "yyyy-MM" e.g. "2025-12"
            var monthKey = $"{year}-{month:D2}";

            var freelancerTotals = await _db.KetaFreeLancers
                .AsNoTracking()
                .Where(f => riderDetailsIds.Contains(f.RiderId) && f.Month == monthKey)
                .Select(f => new { f.RiderId, f.TotalOrders })
                .ToListAsync(ct);

            var freelancerByRiderDetailsId = freelancerTotals
                .ToDictionary(f => f.RiderId, f => f.TotalOrders);

            // ── Step 5: Build summary rows ────────────────────────────────
            var rows = data
                .GroupBy(r => new { r.RiderIqamaNo, r.NameEN, r.NameAR })
                .Select(g =>
                {
                    int totalOrders = 0;
                    string source = "None";

                    if (iqamaToRiderDetailsId.TryGetValue(g.Key.RiderIqamaNo, out var detailsId))
                    {
                        if (shiftByRiderDetailsId.TryGetValue(detailsId, out var shiftOrders))
                        {
                            totalOrders = shiftOrders;
                            source = "Shifts";
                        }
                        else if (freelancerByRiderDetailsId.TryGetValue(detailsId, out var flOrders))
                        {
                            totalOrders = flOrders;
                            source = "KetaFreelancer";
                        }
                    }

                    return new RiderPetrolSummaryRow(
                        g.Key.RiderIqamaNo,
                        g.Key.NameEN,
                        g.Key.NameAR,
                        g.Sum(r => r.Cost),
                        g.Select(r => r.VehicleNumber).Distinct().Count(),
                        g.Select(r => r.Date).Distinct().Count(),
                        totalOrders,
                        source);
                })
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
            .Include(v => v.Vehicle)
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
            .Select(c => new VehicleUnattributedEntry(c.Vehicle?.PlateNumberE, c.Date, c.Cost, c.Notes))
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
                .ToListAsync(ct);

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
                    g.Select(r => r.Date).Distinct().Count(),
                    g.Count(r => r.RiderIqamaNo == null)))
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
            var riderCosts = await _db.RiderPetrolCosts
                .Where(r => r.Date == date)
                .ToListAsync(ct);

            var vehicleCosts = await _db.VehiclePetrolCosts
                .Where(v => v.Date == date)
                .ToListAsync(ct);

            if (riderCosts.Count == 0 && vehicleCosts.Count == 0)
                return Result.Failure<PetrolDeleteResult>(
                    new Error("NotFound",
                        $"No petrol records found for date {date:yyyy-MM-dd}.", 404));

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

            var entries = raw
                // ── Exclude plates in the exclusion list ──────────────────
                .Where(r => !_unattributedExclusions.Contains(r.Vehicle?.PlateNumberE ?? r.VehiclePetrolCost.PlateNumberE))
                .Select(r => new VehicleUnattributedEntry(
                    r.Vehicle?.PlateNumberE ?? r.VehiclePetrolCost.PlateNumberE,
                    r.Date,
                    r.Cost,
                    r.VehiclePetrolCost?.Note ?? r.Notes))
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

    private async Task<Dictionary<VehiclePetrolCost, int>> RebuildAllocationsAsync(
        IReadOnlyList<VehiclePetrolCost> records, CancellationToken ct)
    {
        var ids = records.Where(r => r.Id > 0).Select(r => r.Id).ToList();
        var previous = await _db.RiderPetrolCosts.Where(r => ids.Contains(r.VehiclePetrolCostId)).ToListAsync(ct);
        var byCost = previous.ToLookup(r => r.VehiclePetrolCostId);
        var vehicles = await _db.Vehicles.AsNoTracking().ToListAsync(ct);
        var lookup = vehicles.GroupBy(v => PlateKey(v.PlateNumberE)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var record in records.Where(r => r.HasResolutionError || string.IsNullOrWhiteSpace(r.VehicleNumber)))
        {
            lookup.TryGetValue(PlateKey(record.PlateNumberE), out var matches);
            record.VehicleNumber = matches?.Count == 1 ? matches[0].VehicleNumber : null;
            record.HasResolutionError = record.VehicleNumber == null;
            record.ResolutionErrorMessage = record.HasResolutionError
                ? $"Plate '{record.PlateNumberE}' could not be uniquely matched to a vehicle." : null;
        }

        var dates = records.Select(r => r.Date).Distinct().ToList();
        var manualOnDates = await _db.RiderPetrolCosts.AsNoTracking()
            .Where(r => dates.Contains(r.Date) && r.AttributionSource == PetrolAttributionSource.ManualOverride
                && r.RiderIqamaNo.HasValue).ToListAsync(ct);
        var manualByRiderDate = manualOnDates.ToLookup(r => (r.RiderIqamaNo!.Value, r.Date));
        var vehicleNumbers = records.Where(r => r.VehicleNumber != null).Select(r => r.VehicleNumber!)
            .Concat(manualOnDates.Where(r => r.VehicleNumber != null).Select(r => r.VehicleNumber!)).Distinct().ToList();
        var windows = await LoadAssignmentWindowsAsync(vehicleNumbers, ct);
        var windowsByVehicle = windows.ToLookup(w => w.Status.VehicleNumber);
        var iqamas = windows.Select(w => w.Status.EmployeeIqamaNo!.Value).Distinct().ToList();
        var validIqamas = await _db.Employees.Where(e => iqamas.Contains(e.IqamaNo)).Select(e => e.IqamaNo).ToHashSetAsync(ct);
        var cache = new Dictionary<(string Vehicle, DateOnly Date), IReadOnlyList<PetrolAssignmentTimeline.RiderShare>>();
        var counts = new Dictionary<VehiclePetrolCost, int>();
        foreach (var record in records)
        {
            var oldRows = record.Id > 0 ? byCost[record.Id].ToList() : [];
            var manual = oldRows.Where(r => r.AttributionSource == PetrolAttributionSource.ManualOverride).ToList();
            var manualCost = manual.Sum(r => r.Cost);
            if (manualCost > record.Cost)
                throw new InvalidOperationException($"Manual allocations exceed the cost of petrol record {record.Id}; review is required.");
            _db.RiderPetrolCosts.RemoveRange(oldRows.Except(manual));
            var remaining = record.Cost - manualCost;
            if (manual.Count > 0 && remaining == 0)
            {
                record.IsAttributed = true;
                counts[record] = manual.Count(r => r.RiderIqamaNo.HasValue);
                continue;
            }

            IReadOnlyList<PetrolAssignmentTimeline.RiderShare> shares = [];
            if (record.VehicleNumber != null)
            {
                var key = (record.VehicleNumber, record.Date);
                if (!cache.TryGetValue(key, out shares!))
                {
                    shares = PetrolAssignmentTimeline.OnDate(windowsByVehicle[record.VehicleNumber], record.VehicleNumber, record.Date);
                    cache[key] = shares;
                }
            }
            var manualIqamas = manual.Select(r => r.RiderIqamaNo).ToHashSet();
            var riders = shares.Where(r => validIqamas.Contains(r.IqamaNo) && !manualIqamas.Contains(r.IqamaNo)
                && manualByRiderDate[(r.IqamaNo, record.Date)].All(m => m.VehicleNumber == record.VehicleNumber
                    || (m.VehicleNumber != null && PetrolAssignmentTimeline.OnDate(windowsByVehicle[m.VehicleNumber], m.VehicleNumber, record.Date)
                        .Any(holder => holder.IqamaNo == r.IqamaNo)))).ToList();
            if (riders.Count == 0)
            {
                AddAllocation(record, remaining, null, PetrolAttributionSource.Unattributed, null,
                    record.ResolutionErrorMessage ?? (shares.Count > 0
                        ? "Assigned rider could not be allocated; employee or manual allocation requires review."
                        : "No rider assignment found for this vehicle on this date."));
            }
            else
            {
                var totalTicks = riders.Sum(r => (decimal)r.DurationTicks);
                decimal distributed = 0;
                for (var i = 0; i < riders.Count; i++)
                {
                    var rider = riders[i];
                    var amount = i == riders.Count - 1 ? remaining - distributed
                        : decimal.Truncate(remaining * rider.DurationTicks / totalTicks * 100) / 100;
                    distributed += amount;
                    AddAllocation(record, amount, rider.IqamaNo, rider.Source, rider.StatusId,
                        riders.Count == 1 ? "Single rider — full cost attributed."
                            : $"Assignment time split: {TimeSpan.FromTicks(rider.DurationTicks).TotalHours:F2}h → {amount:F2} SAR");
                }
            }
            counts[record] = manual.Count(r => r.RiderIqamaNo.HasValue) + riders.Count;
            record.IsAttributed = !record.HasResolutionError && riders.Count > 0;
        }
        return counts;
    }

    private void AddAllocation(VehiclePetrolCost record, decimal cost, long? iqama,
        PetrolAttributionSource source, int? statusId, string notes) => _db.RiderPetrolCosts.Add(new RiderPetrolCost
        {
            VehiclePetrolCost = record,
            VehiclePetrolCostId = record.Id,
            VehicleNumber = record.VehicleNumber,
            Date = record.Date,
            Cost = cost,
            RiderIqamaNo = iqama,
            AttributionSource = source,
            ResolvedFromStatusId = statusId,
            Notes = notes,
            CreatedAt = DateTime.UtcNow.AddHours(3)
        });

    private async Task<List<PetrolAssignmentTimeline.Window>> LoadAssignmentWindowsAsync(
        List<string> vehicleNumbers, CancellationToken ct)
    {
        if (vehicleNumbers.Count == 0) return [];
        var relevantIqamas = await _db.RiderVehicleStatus.AsNoTracking()
            .Where(s => vehicleNumbers.Contains(s.VehicleNumber) && s.EmployeeIqamaNo.HasValue)
            .Select(s => s.EmployeeIqamaNo!.Value).Distinct().ToListAsync(ct);
        // Include the rider's other vehicles so an imported replacement closes the
        // previous take, even when its old permission was left open.
        var relatedVehicles = await _db.RiderVehicleStatus.AsNoTracking()
            .Where(s => s.EmployeeIqamaNo.HasValue && relevantIqamas.Contains(s.EmployeeIqamaNo.Value))
            .Select(s => s.VehicleNumber).Distinct().ToListAsync(ct);
        var allVehicleNumbers = vehicleNumbers.Concat(relatedVehicles).Distinct().ToList();
        var statuses = await _db.RiderVehicleStatus.AsNoTracking()
            .Where(s => allVehicleNumbers.Contains(s.VehicleNumber))
            .ToListAsync(ct);
        return PetrolAssignmentTimeline.Build(statuses);
    }

    private async Task<IDbContextTransaction?> BeginPetrolTransactionAsync(CancellationToken ct) =>
        _db.Database.IsRelational() && _db.Database.CurrentTransaction == null
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;

    private void DiscardPetrolChanges()
    {
        foreach (var entry in _db.ChangeTracker.Entries()
            .Where(e => e.Entity is RiderPetrolCost or VehiclePetrolCost)
            // Detach allocations before source costs to avoid severing a required
            // relationship after the database has generated the source ID.
            .OrderBy(e => e.Entity is RiderPetrolCost ? 0 : 1).ToList())
        {
            if (entry.State == EntityState.Added) entry.State = EntityState.Detached;
            else if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;
            }
        }
    }

    private static List<PetrolExcelRow> ParseExcel(Stream stream)
    {
        var result = new List<PetrolExcelRow>();

        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.First();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            var plateRaw = row.Cell(1).GetString().Trim();
            var costRaw = row.Cell(2).GetString().Trim();

            if (string.IsNullOrWhiteSpace(plateRaw) && string.IsNullOrWhiteSpace(costRaw)) continue;
            if (string.IsNullOrWhiteSpace(plateRaw))
                throw new FormatException($"Excel row {row.RowNumber()} has a cost but no vehicle plate.");
            if (!row.Cell(2).TryGetValue<decimal>(out var cost)
                || cost < 0 || cost > 9999999999999999.99m || decimal.Round(cost, 2) != cost)
                throw new FormatException($"Excel row {row.RowNumber()} has an invalid petrol cost: '{costRaw}'. Use a non-negative amount with at most two decimal places.");

            var plate = NormalizePlate(plateRaw);

            plate = plate.ToUpperInvariant() switch
            {
                "BE7191" => "BE7291",
                // add more aliases here if needed: "OLD" => "NEW",
                _ => plate
            };

            result.Add(new PetrolExcelRow(plate, cost));
        }

        return result;
    }

    private static string NormalizePlate(string plate)
    {
        if (string.IsNullOrWhiteSpace(plate))
            return plate;

        plate = plate.Trim();

        var digits = new string(plate.Where(char.IsDigit).ToArray());
        var letters = new string(plate.Where(char.IsLetter).ToArray());

        return $"{letters}{digits}";
    }

    private static string PlateKey(string plate) => NormalizePlate(plate).ToUpperInvariant();

    // ═══════════════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<Result> AddVehicleNoteAsync(
        string vehicleNumber,
        string note,
        DateOnly Date,
        CancellationToken ct = default)
    {
        var vehicle = await _db.Vehicles
            .FirstOrDefaultAsync(v => v.PlateNumberE == vehicleNumber, ct);

        if (vehicle == null)
            return Result.Failure(
                new Error("NotFound",
                    $"Vehicle with plate number {vehicleNumber} not found", 404));

        var vehicleCosts = await _db.VehiclePetrolCosts
            .Where(c => c.VehicleNumber == vehicle.VehicleNumber && c.Date == Date)
            .SingleOrDefaultAsync(ct);

        if (vehicleCosts == null)
            return Result.Failure(
                new Error("NotFound",
                    $"No petrol cost record found for vehicle {vehicleNumber} on date {Date}", 404));

        vehicleCosts.Note = note;

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private record PetrolExcelRow(string PlateNumberE, decimal Cost);
}
