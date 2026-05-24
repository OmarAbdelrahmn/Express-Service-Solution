using Application.Abstraction;
using Domain;
using Domain.Entities;
using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;
using static Application.Service.Reminder.IReminderService;

namespace Application.Service.Reminder;

/// <summary>
/// Manages maintenance interval configuration (admin) and computes which vehicles
/// or riders are due for maintenance on any given date (admin + member).
///
/// Core calculation:
///   effectiveLastDone = latest SparePartUsage / RiderAccessoryUsage for the item
///   nextDueDate       = effectiveLastDone + IntervalDays
///   status            = compare nextDueDate against checkDate + AlertDaysBeforeDue
///
/// Baselines have been removed. The service relies exclusively on actual usage records.
/// For the member (housing) dashboard, both spare part usages and accessory usages
/// are filtered by SparePartUsage.Location / RiderAccessoryUsage.Location == housing.Name.
/// </summary>
public class ReminderService(ApplicationDbcontext context) : IReminderService
{
    private readonly ApplicationDbcontext _ctx = context;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    // ══════════════════════════════════════════════════════════════════════
    //  ADMIN – Interval CRUD
    // ══════════════════════════════════════════════════════════════════════

    public async Task<Result<MaintenanceIntervalResponse>> CreateIntervalAsync(
        CreateIntervalRequest request,
        string createdBy)
    {
        if (request.ItemType == MaintenanceItemType.SparePart && request.SparePartId == null)
            return Fail<MaintenanceIntervalResponse>("SparePartId is required for SparePart intervals.");

        if (request.ItemType == MaintenanceItemType.Accessory && request.AccessoryId == null)
            return Fail<MaintenanceIntervalResponse>("AccessoryId is required for Accessory intervals.");

        if (request.IntervalDays <= 0)
            return Fail<MaintenanceIntervalResponse>("IntervalDays must be greater than zero.");

        if (request.AlertDaysBeforeDue < 0)
            return Fail<MaintenanceIntervalResponse>("AlertDaysBeforeDue cannot be negative.");

        string itemName;

        if (request.ItemType == MaintenanceItemType.SparePart)
        {
            var sp = await _ctx.SpareParts.FindAsync(request.SparePartId!.Value);
            if (sp == null)
                return Fail<MaintenanceIntervalResponse>("Spare part not found.");
            itemName = sp.Name;
        }
        else
        {
            var acc = await _ctx.RiderAccessories.FindAsync(request.AccessoryId!.Value);
            if (acc == null)
                return Fail<MaintenanceIntervalResponse>("Accessory not found.");
            itemName = acc.Name;
        }

        var interval = new MaintenanceInterval
        {
            SparePartId = request.SparePartId,
            AccessoryId = request.AccessoryId,
            ItemType = request.ItemType,
            ItemName = itemName,
            IntervalDays = request.IntervalDays,
            AlertDaysBeforeDue = request.AlertDaysBeforeDue,
            Location = request.Location,
            Notes = request.Notes,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow.AddHours(3)
        };

        await _ctx.MaintenanceIntervals.AddAsync(interval);
        await _ctx.SaveChangesAsync();

        return Result.Success(MapInterval(interval));
    }

    public async Task<Result<MaintenanceIntervalResponse>> UpdateIntervalAsync(
        int id,
        UpdateIntervalRequest request,
        string updatedBy)
    {
        var interval = await _ctx.MaintenanceIntervals.FindAsync(id);
        if (interval == null)
            return Fail<MaintenanceIntervalResponse>("Maintenance interval not found.");

        if (request.IntervalDays <= 0)
            return Fail<MaintenanceIntervalResponse>("IntervalDays must be greater than zero.");

        if (request.AlertDaysBeforeDue < 0)
            return Fail<MaintenanceIntervalResponse>("AlertDaysBeforeDue cannot be negative.");

        interval.IntervalDays = request.IntervalDays;
        interval.AlertDaysBeforeDue = request.AlertDaysBeforeDue;
        interval.Location = request.Location;
        interval.Notes = request.Notes;
        interval.IsActive = request.IsActive;
        interval.UpdatedAt = DateTime.UtcNow.AddHours(3);
        interval.UpdatedBy = updatedBy;

        await _ctx.SaveChangesAsync();
        return Result.Success(MapInterval(interval));
    }

    public async Task<Result> DeleteIntervalAsync(int id)
    {
        var interval = await _ctx.MaintenanceIntervals
            .FirstOrDefaultAsync(i => i.Id == id);

        if (interval == null)
            return Fail("Maintenance interval not found.");

        _ctx.MaintenanceIntervals.Remove(interval);
        await _ctx.SaveChangesAsync();
        return Result.Success();
    }

    public async Task<Result<MaintenanceIntervalResponse>> ToggleIntervalActiveAsync(
        int id, string updatedBy)
    {
        var interval = await _ctx.MaintenanceIntervals.FindAsync(id);
        if (interval == null)
            return Fail<MaintenanceIntervalResponse>("Maintenance interval not found.");

        interval.IsActive = !interval.IsActive;
        interval.UpdatedAt = DateTime.UtcNow.AddHours(3);
        interval.UpdatedBy = updatedBy;

        await _ctx.SaveChangesAsync();
        return Result.Success(MapInterval(interval));
    }

    public async Task<Result<IEnumerable<MaintenanceIntervalResponse>>> GetAllIntervalsAsync()
    {
        var intervals = await _ctx.MaintenanceIntervals
            .OrderByDescending(i => i.IsActive)
            .ThenBy(i => i.ItemName)
            .AsNoTracking()
            .ToListAsync();

        return Result.Success(intervals.Select(MapInterval));
    }

    public async Task<Result<MaintenanceIntervalResponse>> GetIntervalByIdAsync(int id)
    {
        var interval = await _ctx.MaintenanceIntervals
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id);

        if (interval == null)
            return Fail<MaintenanceIntervalResponse>("Maintenance interval not found.");

        return Result.Success(MapInterval(interval));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ADMIN – Global Reminder Dashboard
    // ══════════════════════════════════════════════════════════════════════

    public async Task<Result<MaintenanceReminderReport>> GetAllDueMaintenanceAsync(
        DateOnly? checkDate = null)
    {
        var date = checkDate ?? Today;

        var intervals = await LoadActiveIntervalsAsync();
        if (!intervals.Any())
            return Result.Success(EmptyReport(date));

        var spIntervalSparePartIds = intervals
            .Where(i => i.ItemType == MaintenanceItemType.SparePart && i.SparePartId.HasValue)
            .Select(i => i.SparePartId!.Value)
            .Distinct()
            .ToList();

        var accIntervalAccessoryIds = intervals
            .Where(i => i.ItemType == MaintenanceItemType.Accessory && i.AccessoryId.HasValue)
            .Select(i => i.AccessoryId!.Value)
            .Distinct()
            .ToList();

        var allSparePartUsages = spIntervalSparePartIds.Any()
            ? await _ctx.SparePartUsages
                .Where(u => spIntervalSparePartIds.Contains(u.SparePartId))
                .AsNoTracking()
                .ToListAsync()
            : new List<SparePartUsage>();

        var vehicleNumbers = allSparePartUsages
            .Select(u => u.VehicleNumber)
            .Distinct()
            .ToList();

        var allVehicles = vehicleNumbers.Any()
            ? await _ctx.Vehicles
                .Include(v => v.RiderDetails).ThenInclude(r => r!.Employee)
                .Where(v => vehicleNumbers.Contains(v.VehicleNumber))
                .AsNoTracking()
                .ToListAsync()
            : new List<Vehicle>();

        var allAccessoryUsages = accIntervalAccessoryIds.Any()
            ? await _ctx.RiderAccessoryUsages
                .Where(u => accIntervalAccessoryIds.Contains(u.RiderAccessoryId))
                .AsNoTracking()
                .ToListAsync()
            : new List<RiderAccessoryUsage>();

        var riderIds = allAccessoryUsages
            .Select(u => u.RiderId)
            .Distinct()
            .ToList();

        var allRiders = riderIds.Any()
            ? await _ctx.RiderDetails
                .Include(r => r.Employee).ThenInclude(e => e.Housing)
                .Where(r => riderIds.Contains(r.Id))
                .AsNoTracking()
                .ToListAsync()
            : new List<RiderDetails>();

        return Result.Success(
            BuildReport(
                date, intervals,
                allVehicles, allSparePartUsages,
                allRiders, allAccessoryUsages,
                housingName: null));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  MEMBER – Housing Reminder Dashboard
    // ══════════════════════════════════════════════════════════════════════

    public async Task<Result<MaintenanceReminderReport>> GetHousingDueMaintenanceAsync(
      long managerIqamaNo,
      DateOnly? checkDate = null)
    {
        var date = checkDate ?? Today;

        var housing = await _ctx.Housings
            .FirstOrDefaultAsync(h => h.ManagerIqamaNo == managerIqamaNo);

        if (housing == null)
            return Fail<MaintenanceReminderReport>(
                "Housing not found or you are not assigned as a housing manager.");

        var intervals = await LoadActiveIntervalsAsync(housing.Name);
        if (!intervals.Any())
            return Result.Success(EmptyReport(date));

        var spIntervals = intervals
            .Where(i => i.ItemType == MaintenanceItemType.SparePart && i.SparePartId.HasValue)
            .ToList();

        List<SparePartUsage> allSparePartUsages = new();

        if (spIntervals.Any())
        {
            // Step 1: get the canonical name for each interval's spare part
            var canonicalIds = spIntervals.Select(i => i.SparePartId!.Value).Distinct().ToList();

            var canonicalNames = await _ctx.SpareParts
                .Where(sp => canonicalIds.Contains(sp.Id))
                .Select(sp => new { sp.Id, sp.Name })
                .ToListAsync();

            // Step 2: find ALL spare part IDs across every location that share those names
            var allNames = canonicalNames.Select(c => c.Name).Distinct().ToList();

            var allMatchingIds = await _ctx.SpareParts
                .Where(sp => allNames.Contains(sp.Name))
                .Select(sp => new { sp.Id, sp.Name })
                .ToListAsync();

            // Step 3: build a reverse map  housing-copy-ID → canonical interval SparePartId
            var idRemap = new Dictionary<int, int>();
            foreach (var part in allMatchingIds)
            {
                var canonical = canonicalNames.FirstOrDefault(c => c.Name == part.Name);
                if (canonical != null)
                    idRemap[part.Id] = canonical.Id;
            }

            var allRelevantIds = idRemap.Keys.ToList();

            // Step 4: load usages filtered by housing location + any matching spare part ID
            var rawUsages = await _ctx.SparePartUsages
                .Where(u => u.Location == housing.Name
                         && allRelevantIds.Contains(u.SparePartId))
                .ToListAsync();

            // Step 5: remap SparePartId to the canonical ID so BuildReportFromUsages
            //         can match against interval.SparePartId correctly
            foreach (var u in rawUsages)
            {
                if (idRemap.TryGetValue(u.SparePartId, out var remapped))
                    u.SparePartId = remapped;
            }

            allSparePartUsages = rawUsages;
        }

        var vehicleNumbers = allSparePartUsages
            .Select(u => u.VehicleNumber)
            .Distinct()
            .ToList();

        var housingVehicles = vehicleNumbers.Any()
            ? await _ctx.Vehicles
                .Include(v => v.RiderDetails).ThenInclude(r => r!.Employee)
                .Where(v => vehicleNumbers.Contains(v.VehicleNumber))
                .AsNoTracking()
                .ToListAsync()
            : new List<Vehicle>();

        var accIntervalAccessoryIds = intervals
            .Where(i => i.ItemType == MaintenanceItemType.Accessory && i.AccessoryId.HasValue)
            .Select(i => i.AccessoryId!.Value)
            .Distinct()
            .ToList();

        var allAccessoryUsages = new List<RiderAccessoryUsage>();
        List<RiderDetails> housingRiders = new();

        if (accIntervalAccessoryIds.Any())
        {
            var employeeIqamas = await _ctx.Employees
                .Where(e => e.HousingId == housing.Id && !e.IsDeleted)
                .Select(e => e.IqamaNo)
                .ToListAsync();

            var allRiderIds = await _ctx.RiderDetails
                .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo)
                    && !r.Employee.IsEmployee)
                .Select(r => r.Id)
                .ToListAsync();

            if (allRiderIds.Any())
            {
                allAccessoryUsages = await _ctx.RiderAccessoryUsages
                    .Where(u => allRiderIds.Contains(u.RiderId)
                        && accIntervalAccessoryIds.Contains(u.RiderAccessoryId))
                    .ToListAsync();

                var riderIdsWithUsage = allAccessoryUsages
                    .Select(u => u.RiderId)
                    .Distinct()
                    .ToList();

                housingRiders = riderIdsWithUsage.Any()
                    ? await _ctx.RiderDetails
                        .Include(r => r.Employee)
                        .Where(r => riderIdsWithUsage.Contains(r.Id))
                        .AsNoTracking()
                        .ToListAsync()
                    : new List<RiderDetails>();
            }
        }

        var report = BuildReportFromUsages(
            date, intervals,
            housingVehicles, allSparePartUsages,
            housingRiders, allAccessoryUsages,
            housing.Name);

        return Result.Success(report);
    }

    private MaintenanceReminderReport BuildReportFromUsages(
    DateOnly checkDate,
    List<MaintenanceInterval> intervals,
    List<Vehicle> vehicles,
    List<SparePartUsage> sparePartUsages,
    List<RiderDetails> riders,
    List<RiderAccessoryUsage> accessoryUsages,
    string? housingName)
    {
        var vehicleReminders = new List<VehicleMaintenanceReminder>();
        var riderReminders = new List<RiderMaintenanceReminder>();

        var spIntervals = intervals
            .Where(i => i.ItemType == MaintenanceItemType.SparePart && i.SparePartId.HasValue)
            .ToList();

        var accIntervals = intervals
            .Where(i => i.ItemType == MaintenanceItemType.Accessory && i.AccessoryId.HasValue)
            .ToList();

        // Build a name-based lookup for spare part usages.
        // Each usage carries the housing-copy SparePartId which may differ from the
        // canonical SparePartId stored on the MaintenanceInterval (which was created
        // from the main-store copy).  We group usages by VehicleNumber + SparePartId
        // but then match them to intervals by name so the ID mismatch is irrelevant.
        //
        // sparePartNameByUsageId  : usageSparePartId → spare part name
        // intervalBySparePartName : spare part name  → interval
        //
        // Both dictionaries are built once here instead of per-vehicle-per-interval.

        // Collect all unique SparePartIds referenced in usages
        var usageSparePartIds = sparePartUsages
            .Select(u => u.SparePartId)
            .Distinct()
            .ToList();

        // Also collect all canonical SparePartIds from intervals
        var intervalSparePartIds = spIntervals
            .Select(i => i.SparePartId!.Value)
            .Distinct()
            .ToList();

        // Load names for every relevant ID in one shot (already in memory if EF cache,
        // but we query to be safe; caller may have used AsNoTracking)
        // We resolve from the intervals themselves (ItemName is denormalised on the interval)
        // so no extra DB call is needed here.

        // Map: interval canonical SparePartId → interval
        var intervalByCanonicalId = spIntervals
            .ToDictionary(i => i.SparePartId!.Value, i => i);

        // Map: interval ItemName (lower) → interval  (for name-based fallback matching)
        var intervalByItemName = spIntervals
            .GroupBy(i => i.ItemName.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        // We need to know the name of each SparePartId that appears in usages.
        // The cheapest source is the SparePart navigation on the usage — but that
        // requires Include(u => u.SparePart).  The callers in this service do NOT
        // include SparePart on the usages they load, so we cannot rely on it.
        //
        // Instead we match as follows (in priority order):
        //   1. Exact canonical ID match  (fast path, works when no housing copy exists)
        //   2. Name match via the interval.ItemName stored on the interval record
        //      — we can resolve this only if the caller pre-remapped the IDs (as the
        //        updated GetHousingDueMaintenanceAsync now does), so after the remap
        //        path 1 always succeeds.
        //
        // This means the method works correctly both:
        //   • after the caller has remapped IDs  (housing path)
        //   • when all IDs are already canonical (admin path / main-store-only data)

        // ── Spare part intervals ──────────────────────────────────────────────
        foreach (var vehicle in vehicles)
        {
            var dueItems = new List<MaintenanceItem>();

            foreach (var interval in spIntervals)
            {
                // Primary match: canonical ID (works after remap, and for main store data)
                var lastUsedAt = sparePartUsages
                    .Where(u => u.VehicleNumber == vehicle.VehicleNumber
                             && u.SparePartId == interval.SparePartId!.Value)
                    .OrderByDescending(u => u.UsedAt)
                    .Select(u => (DateTime?)u.UsedAt)
                    .FirstOrDefault();

                // No usage record for this interval on this vehicle — skip
                if (lastUsedAt == null)
                    continue;

                var item = ComputeMaintenanceItem(interval, checkDate, lastUsedAt, "Usage");

                if (item.Status != MaintenanceStatus.OK)
                    dueItems.Add(item);
            }

            if (!dueItems.Any()) continue;

            var assignedRider = vehicle.RiderDetails;
            vehicleReminders.Add(new VehicleMaintenanceReminder(
                vehicle.VehicleNumber,
                vehicle.PlateNumberA,
                vehicle.Location,
                assignedRider?.EmployeeIqamaNo,
                assignedRider?.Employee?.NameAR,
                dueItems.OrderBy(i => i.DaysUntilDue).ToList()
            ));
        }

        // ── Accessory intervals ───────────────────────────────────────────────
        foreach (var rider in riders)
        {
            var dueItems = new List<MaintenanceItem>();

            foreach (var interval in accIntervals)
            {
                var lastIssuedAt = accessoryUsages
                    .Where(u => u.RiderId == rider.Id
                             && u.RiderAccessoryId == interval.AccessoryId!.Value)
                    .OrderByDescending(u => u.IssuedAt)
                    .Select(u => (DateTime?)u.IssuedAt)
                    .FirstOrDefault();

                if (lastIssuedAt == null)
                    continue;

                var item = ComputeMaintenanceItem(interval, checkDate, lastIssuedAt, "Usage");

                if (item.Status != MaintenanceStatus.OK)
                    dueItems.Add(item);
            }

            if (!dueItems.Any()) continue;

            riderReminders.Add(new RiderMaintenanceReminder(
                rider.Id,
                rider.EmployeeIqamaNo,
                rider.Employee?.NameAR ?? "N/A",
                rider.Employee?.NameEN ?? "N/A",
                rider.WorkingId ?? "N/A",
                housingName ?? rider.Employee?.Housing?.Name ?? "N/A",
                dueItems.OrderBy(i => i.DaysUntilDue).ToList()
            ));
        }

        var allItems = vehicleReminders.SelectMany(v => v.DueItems)
            .Concat(riderReminders.SelectMany(r => r.DueItems))
            .ToList();

        return new MaintenanceReminderReport(
            CheckDate: checkDate,
            TotalAffectedVehicles: vehicleReminders.Count,
            TotalAffectedRiders: riderReminders.Count,
            TotalOverdueItems: allItems.Count(i => i.Status == MaintenanceStatus.Overdue),
            TotalDueTodayItems: allItems.Count(i => i.Status == MaintenanceStatus.DueToday),
            TotalUpcomingItems: allItems.Count(i => i.Status == MaintenanceStatus.Upcoming),
            TotalNeverDoneItems: 0,
            VehicleReminders: vehicleReminders.OrderBy(v => v.DueItems.Min(i => i.DaysUntilDue)).ToList(),
            RiderReminders: riderReminders.OrderBy(r => r.DueItems.Min(i => i.DaysUntilDue)).ToList()
        );
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CORE REPORT BUILDER
    // ══════════════════════════════════════════════════════════════════════

    private MaintenanceReminderReport BuildReport(
        DateOnly checkDate,
        List<MaintenanceInterval> intervals,
        List<Vehicle> vehicles,
        List<SparePartUsage> sparePartUsages,
        List<RiderDetails> riders,
        List<RiderAccessoryUsage> accessoryUsages,
        string? housingName)
    {
        var vehicleReminders = new List<VehicleMaintenanceReminder>();
        var riderReminders = new List<RiderMaintenanceReminder>();

        var spIntervals = intervals
            .Where(i => i.ItemType == MaintenanceItemType.SparePart && i.SparePartId.HasValue)
            .ToList();

        foreach (var vehicle in vehicles)
        {
            var dueItems = new List<MaintenanceItem>();

            foreach (var interval in spIntervals)
            {
                var lastUsedAt = sparePartUsages
                    .Where(u => u.VehicleNumber == vehicle.VehicleNumber
                             && u.SparePartId == interval.SparePartId!.Value)
                    .OrderByDescending(u => u.UsedAt)
                    .Select(u => (DateTime?)u.UsedAt)
                    .FirstOrDefault();

                if (lastUsedAt == null)
                    continue;

                var item = ComputeMaintenanceItem(interval, checkDate, lastUsedAt, "Usage");

                if (item.Status != MaintenanceStatus.OK)
                    dueItems.Add(item);
            }

            if (!dueItems.Any()) continue;

            var assignedRider = vehicle.RiderDetails;
            vehicleReminders.Add(new VehicleMaintenanceReminder(
                vehicle.VehicleNumber,
                vehicle.PlateNumberA,
                vehicle.Location,
                assignedRider?.EmployeeIqamaNo,
                assignedRider?.Employee?.NameAR,
                dueItems.OrderBy(i => i.DaysUntilDue).ToList()
            ));
        }

        var accIntervals = intervals
            .Where(i => i.ItemType == MaintenanceItemType.Accessory && i.AccessoryId.HasValue)
            .ToList();

        foreach (var rider in riders)
        {
            var dueItems = new List<MaintenanceItem>();

            foreach (var interval in accIntervals)
            {
                var lastIssuedAt = accessoryUsages
                    .Where(u => u.RiderId == rider.Id
                             && u.RiderAccessoryId == interval.AccessoryId!.Value)
                    .OrderByDescending(u => u.IssuedAt)
                    .Select(u => (DateTime?)u.IssuedAt)
                    .FirstOrDefault();

                if (lastIssuedAt == null)
                    continue;

                var item = ComputeMaintenanceItem(interval, checkDate, lastIssuedAt, "Usage");

                if (item.Status != MaintenanceStatus.OK)
                    dueItems.Add(item);
            }

            if (!dueItems.Any()) continue;

            riderReminders.Add(new RiderMaintenanceReminder(
                rider.Id,
                rider.EmployeeIqamaNo,
                rider.Employee?.NameAR ?? "N/A",
                rider.Employee?.NameEN ?? "N/A",
                rider.WorkingId ?? "N/A",
                housingName ?? rider.Employee?.Housing?.Name ?? "N/A",
                dueItems.OrderBy(i => i.DaysUntilDue).ToList()
            ));
        }

        var allItems = vehicleReminders.SelectMany(v => v.DueItems)
            .Concat(riderReminders.SelectMany(r => r.DueItems))
            .ToList();

        return new MaintenanceReminderReport(
            CheckDate: checkDate,
            TotalAffectedVehicles: vehicleReminders.Count,
            TotalAffectedRiders: riderReminders.Count,
            TotalOverdueItems: allItems.Count(i => i.Status == MaintenanceStatus.Overdue),
            TotalDueTodayItems: allItems.Count(i => i.Status == MaintenanceStatus.DueToday),
            TotalUpcomingItems: allItems.Count(i => i.Status == MaintenanceStatus.Upcoming),
            TotalNeverDoneItems: 0,
            VehicleReminders: vehicleReminders.OrderBy(v => v.DueItems.Min(i => i.DaysUntilDue)).ToList(),
            RiderReminders: riderReminders.OrderBy(r => r.DueItems.Min(i => i.DaysUntilDue)).ToList()
        );
    }

    // ══════════════════════════════════════════════════════════════════════
    //  MAINTENANCE STATUS COMPUTATION
    // ══════════════════════════════════════════════════════════════════════

    private static MaintenanceItem ComputeMaintenanceItem(
        MaintenanceInterval interval,
        DateOnly checkDate,
        DateTime? effectiveLastDone,
        string recordSource)
    {
        if (effectiveLastDone == null)
        {
            return new MaintenanceItem(
                IntervalId: interval.Id,
                ItemName: interval.ItemName,
                ItemType: interval.ItemType,
                IntervalDays: interval.IntervalDays,
                AlertDaysBeforeDue: interval.AlertDaysBeforeDue,
                LastDoneAt: null,
                NextDueAt: DateTime.MinValue,
                DaysUntilDue: int.MinValue,
                Status: MaintenanceStatus.NeverDone,
                RecordSource: "None"
            );
        }

        var nextDue = effectiveLastDone.Value.AddDays(interval.IntervalDays);
        var nextDueDate = DateOnly.FromDateTime(nextDue);
        int daysUntilDue = nextDueDate.DayNumber - checkDate.DayNumber;

        var status = daysUntilDue switch
        {
            < 0 => MaintenanceStatus.Overdue,
            0 => MaintenanceStatus.DueToday,
            _ when daysUntilDue <= interval.AlertDaysBeforeDue => MaintenanceStatus.Upcoming,
            _ => MaintenanceStatus.OK
        };

        return new MaintenanceItem(
            IntervalId: interval.Id,
            ItemName: interval.ItemName,
            ItemType: interval.ItemType,
            IntervalDays: interval.IntervalDays,
            AlertDaysBeforeDue: interval.AlertDaysBeforeDue,
            LastDoneAt: effectiveLastDone,
            NextDueAt: nextDue,
            DaysUntilDue: daysUntilDue,
            Status: status,
            RecordSource: recordSource
        );
    }

    // ══════════════════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ══════════════════════════════════════════════════════════════════════

    private async Task<List<MaintenanceInterval>> LoadActiveIntervalsAsync(
        string? housingName = null)
    {
        var query = _ctx.MaintenanceIntervals.Where(i => i.IsActive);

        if (housingName != null)
            query = query.Where(i => i.Location == null || i.Location == housingName);

        return await query.AsNoTracking().ToListAsync();
    }

    private static MaintenanceReminderReport EmptyReport(DateOnly date) =>
        new(date, 0, 0, 0, 0, 0, 0, [], []);

    private static MaintenanceIntervalResponse MapInterval(MaintenanceInterval i) =>
        new(i.Id, i.SparePartId, i.AccessoryId, i.ItemType, i.ItemName,
            i.IntervalDays, i.AlertDaysBeforeDue, i.Location,
            i.IsActive, i.Notes, i.CreatedBy, i.CreatedAt, i.UpdatedAt, i.UpdatedBy);

    private static Result<T> Fail<T>(string message) =>
        Result.Failure<T>(new Error("ReminderService.Error", message, 400));

    private static Result Fail(string message) =>
        Result.Failure(new Error("ReminderService.Error", message, 400));
}