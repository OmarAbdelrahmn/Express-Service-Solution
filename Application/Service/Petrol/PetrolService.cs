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

            foreach (var row in rows)
            {
                var normalised = row.PlateNumberE.Trim().ToUpperInvariant();
                allVehicles.TryGetValue(normalised, out var vehicle);

                newCostRecords.Add(new VehiclePetrolCost
                {
                    PlateNumberE = row.PlateNumberE,
                    VehicleNumber = vehicle?.VehicleNumber,
                    Cost = row.Cost,
                    Date = reportDate,
                    UploadedAt = DateTime.UtcNow.AddHours(3),
                    UploadedBy = uploadedBy,
                    IsAttributed = false,
                    HasResolutionError = vehicle is null,
                    ResolutionErrorMessage = vehicle is null
                        ? $"No vehicle found with English plate '{row.PlateNumberE}'."
                        : null
                });
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
                new Error("ProcessingError", $"Failed to process file: {ex.Message}", 500));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ATTRIBUTION
    // ═══════════════════════════════════════════════════════════════════════

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

                if (count > 0)
                    attributed++;
                else
                    unattributed++;
            }

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
                .Where(r => r.RiderIqamaNo == riderIqamaNo && r.Date == date)
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

    public async Task<Result<IReadOnlyList<VehicleUnattributedEntry>>> GetUnattributedCostsAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        try
        {
            var entries = await _db.RiderPetrolCosts
                .Include(r => r.Vehicle)
                .AsNoTracking()
                .Where(r => r.RiderIqamaNo == null && r.Date.Year == year && r.Date.Month == month)
                .OrderBy(r => r.VehicleNumber)
                .ThenBy(r => r.Date)
                .Select(r => new VehicleUnattributedEntry(r.Vehicle.PlateNumberE ?? "", r.Date, r.Cost, r.Notes))
                .ToListAsync(ct);

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

    private async Task<int> AttributeSingleAsync(VehiclePetrolCost record, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(record.VehicleNumber))
            return 0;

        var dayStart = record.Date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = record.Date.ToDateTime(TimeOnly.MaxValue);

        var riders = await ResolveRidersAsync(record.VehicleNumber, record.Date, dayStart, dayEnd, ct);

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

            return 0; // ✅ no riders
        }
        else
        {
            var splits = ComputeSplit(record.Cost, riders, record.VehicleNumber, dayStart, dayEnd);

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

            return riders.Count; // ✅ number of riders
        }
    }
    /// <summary>
    /// Splits totalCost among resolved riders.
    /// Uses time-based split if PermissionStartDate/EndDate are available on the status records,
    /// otherwise falls back to equal split.
    /// Returns a list of (share, note) in the same order as <paramref name="riders"/>.
    /// </summary>
    private List<(decimal Share, string Note)> ComputeSplit(
        decimal totalCost,
        IReadOnlyList<ResolvedRider> riders,
        string vehicleNumber,
        DateTime dayStart,
        DateTime dayEnd)
    {
        if (riders.Count == 1)
            return [(totalCost, "Single rider — full cost attributed.")];

        // Try time-based split: look up the status records for each resolved rider
        var statusIds = riders
            .Where(r => r.StatusId > 0)
            .Select(r => r.StatusId)
            .ToList();

        var statuses = _db.RiderVehicleStatus
            .Where(s => statusIds.Contains(s.Id))
            .ToList();

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

    private readonly record struct ResolvedRider(
        long IqamaNo,
        PetrolAttributionSource Source,
        int StatusId,
        string? Notes);

    private record PetrolExcelRow(string PlateNumberE, decimal Cost);
}