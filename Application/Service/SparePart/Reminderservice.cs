using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Service.Member;
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
///   effectiveLastDone = MAX(latest SparePartUsage/RiderAccessoryUsage, latest Baseline)
///   nextDueDate       = effectiveLastDone + IntervalDays
///   status            = compare nextDueDate against checkDate + AlertDaysBeforeDue
/// </summary>
public class ReminderService(ApplicationDbcontext context) : IReminderService
{
    private readonly ApplicationDbcontext _ctx = context;

    // Today in KSA (+3)
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    // ══════════════════════════════════════════════════════════════════════
    //  ADMIN – Interval CRUD
    // ══════════════════════════════════════════════════════════════════════

    public async Task<Result<MaintenanceIntervalResponse>> CreateIntervalAsync(
        CreateIntervalRequest request,
        string createdBy)
    {
        // Validate that exactly one item reference is supplied
        if (request.ItemType == MaintenanceItemType.SparePart && request.SparePartId == null)
            return Fail<MaintenanceIntervalResponse>("SparePartId is required for SparePart intervals.");

        if (request.ItemType == MaintenanceItemType.Accessory && request.AccessoryId == null)
            return Fail<MaintenanceIntervalResponse>("AccessoryId is required for Accessory intervals.");

        if (request.IntervalDays <= 0)
            return Fail<MaintenanceIntervalResponse>("IntervalDays must be greater than zero.");

        if (request.AlertDaysBeforeDue < 0)
            return Fail<MaintenanceIntervalResponse>("AlertDaysBeforeDue cannot be negative.");

        // Resolve the display name from the referenced item
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
            .Include(i => i.Baselines)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (interval == null)
            return Fail("Maintenance interval not found.");

        if (interval.Baselines.Any())
            return Fail("Cannot delete an interval that has baseline records. Deactivate it instead.");

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
    //  ADMIN – Baseline CRUD
    // ══════════════════════════════════════════════════════════════════════

    public async Task<Result<BaselineResponse>> SetBaselineAsync(
        SetBaselineRequest request,
        string setBy)
    {
        var interval = await _ctx.MaintenanceIntervals.FindAsync(request.MaintenanceIntervalId);
        if (interval == null)
            return Fail<BaselineResponse>("Maintenance interval not found.");

        if (!interval.IsActive)
            return Fail<BaselineResponse>("Cannot set a baseline on an inactive interval.");

        // Validate target
        if (interval.ItemType == MaintenanceItemType.SparePart)
        {
            if (string.IsNullOrWhiteSpace(request.VehicleNumber))
                return Fail<BaselineResponse>("VehicleNumber is required for SparePart intervals.");

            var vehicle = await _ctx.Vehicles
                .FirstOrDefaultAsync(v => v.VehicleNumber == request.VehicleNumber);
            if (vehicle == null)
                return Fail<BaselineResponse>("Vehicle not found.");
        }
        else
        {
            if (request.RiderId == null)
                return Fail<BaselineResponse>("RiderId is required for Accessory intervals.");

            var rider = await _ctx.RiderDetails.FindAsync(request.RiderId.Value);
            if (rider == null)
                return Fail<BaselineResponse>("Rider not found.");
        }

        // Upsert: one baseline per (interval + vehicle/rider)
        var existing = await _ctx.VehicleMaintenanceBaselines
            .FirstOrDefaultAsync(b =>
                b.MaintenanceIntervalId == request.MaintenanceIntervalId &&
                b.VehicleNumber == request.VehicleNumber &&
                b.RiderId == request.RiderId);

        if (existing != null)
        {
            existing.LastDoneAt = request.LastDoneAt;
            existing.UpdatedAt = DateTime.UtcNow.AddHours(3);
            existing.UpdatedBy = setBy;
            existing.Notes = request.Notes;
        }
        else
        {
            existing = new VehicleMaintenanceBaseline
            {
                MaintenanceIntervalId = request.MaintenanceIntervalId,
                VehicleNumber = request.VehicleNumber,
                RiderId = request.RiderId,
                LastDoneAt = request.LastDoneAt,
                SetBy = setBy,
                CreatedAt = DateTime.UtcNow.AddHours(3),
                Notes = request.Notes
            };
            await _ctx.VehicleMaintenanceBaselines.AddAsync(existing);
        }

        await _ctx.SaveChangesAsync();

        return Result.Success(await BuildBaselineResponseAsync(existing, interval));
    }

    public async Task<Result<IEnumerable<BaselineResponse>>> GetBaselinesByIntervalAsync(int intervalId)
    {
        var interval = await _ctx.MaintenanceIntervals.FindAsync(intervalId);
        if (interval == null)
            return Fail<IEnumerable<BaselineResponse>>("Maintenance interval not found.");

        var baselines = await _ctx.VehicleMaintenanceBaselines
            .Include(b => b.Vehicle)
            .Include(b => b.Rider).ThenInclude(r => r!.Employee)
            .Where(b => b.MaintenanceIntervalId == intervalId)
            .AsNoTracking()
            .ToListAsync();

        var responses = new List<BaselineResponse>();
        foreach (var b in baselines)
            responses.Add(await BuildBaselineResponseAsync(b, interval));

        return Result.Success<IEnumerable<BaselineResponse>>(responses);
    }

    public async Task<Result<IEnumerable<BaselineResponse>>> GetBaselinesByVehicleAsync(
        string vehicleNumber)
    {
        var baselines = await _ctx.VehicleMaintenanceBaselines
            .Include(b => b.MaintenanceInterval)
            .Include(b => b.Vehicle)
            .Where(b => b.VehicleNumber == vehicleNumber)
            .AsNoTracking()
            .ToListAsync();

        var responses = new List<BaselineResponse>();
        foreach (var b in baselines)
            responses.Add(await BuildBaselineResponseAsync(b, b.MaintenanceInterval));

        return Result.Success<IEnumerable<BaselineResponse>>(responses);
    }

    public async Task<Result<IEnumerable<BaselineResponse>>> GetBaselinesByRiderAsync(int riderId)
    {
        var baselines = await _ctx.VehicleMaintenanceBaselines
            .Include(b => b.MaintenanceInterval)
            .Include(b => b.Rider).ThenInclude(r => r!.Employee)
            .Where(b => b.RiderId == riderId)
            .AsNoTracking()
            .ToListAsync();

        var responses = new List<BaselineResponse>();
        foreach (var b in baselines)
            responses.Add(await BuildBaselineResponseAsync(b, b.MaintenanceInterval));

        return Result.Success<IEnumerable<BaselineResponse>>(responses);
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

        // ── Load all spare part usages for the relevant spare parts ──────────
        var allSparePartUsages = spIntervalSparePartIds.Any()
            ? await _ctx.SparePartUsages
                .Where(u => spIntervalSparePartIds.Contains(u.SparePartId))
                .ToListAsync()
            : new List<SparePartUsage>();

        // ── Vehicles that actually have usage records ─────────────────────────
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

        // ── Load all accessory usages for the relevant accessories ────────────
        var allAccessoryUsages = accIntervalAccessoryIds.Any()
            ? await _ctx.RiderAccessoryUsages
                .Where(u => accIntervalAccessoryIds.Contains(u.RiderAccessoryId))
                .ToListAsync()
            : new List<RiderAccessoryUsage>();

        // ── Riders that actually have usage records ───────────────────────────
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
            BuildReportFromUsages(
                date, intervals,
                allVehicles, allSparePartUsages,
                allRiders, allAccessoryUsages,
                housingName: null));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  MEMBER – Housing Reminder Dashboard
    // ══════════════════════════════════════════════════════════════════════
    // In GetHousingDueMaintenanceAsync — pass housingName to BuildReportAsync
    // (already done), but also pre-filter by usage location

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

        var spIntervalSparePartIds = intervals
            .Where(i => i.ItemType == MaintenanceItemType.SparePart && i.SparePartId.HasValue)
            .Select(i => i.SparePartId!.Value)
            .Distinct()
            .ToList();

        // ── Pull all usages for this housing directly ─────────────────────────
        var allSparePartUsages = spIntervalSparePartIds.Any()
            ? await _ctx.SparePartUsages
                .Where(u => u.Location == housing.Name
                    && spIntervalSparePartIds.Contains(u.SparePartId))
                .ToListAsync()
            : new List<SparePartUsage>();

        // ── Distinct vehicle numbers that actually have usage here ────────────
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

        // ── Riders ────────────────────────────────────────────────────────────
        var accIntervalAccessoryIds = intervals
            .Where(i => i.ItemType == MaintenanceItemType.Accessory && i.AccessoryId.HasValue)
            .Select(i => i.AccessoryId!.Value)
            .Distinct()
            .ToList();

        var allAccessoryUsages = new List<RiderAccessoryUsage>();
        List<RiderDetails> housingRiders = new();

        if (accIntervalAccessoryIds.Any())
        {
            // Get all employees in this housing
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
      string housingName)
    {
        var vehicleReminders = new List<VehicleMaintenanceReminder>();
        var riderReminders = new List<RiderMaintenanceReminder>();

        // ── Spare part intervals ──────────────────────────────────────────────
        var spIntervals = intervals
            .Where(i => i.ItemType == MaintenanceItemType.SparePart && i.SparePartId.HasValue)
            .ToList();

        foreach (var vehicle in vehicles)
        {
            var dueItems = new List<MaintenanceItem>();

            foreach (var interval in spIntervals)
            {
                // Get the latest usage for this specific spare part on this vehicle
                var lastUsedAt = sparePartUsages
                    .Where(u => u.VehicleNumber == vehicle.VehicleNumber
                        && u.SparePartId == interval.SparePartId!.Value)
                    .OrderByDescending(u => u.UsedAt)
                    .Select(u => u.UsedAt)
                    .FirstOrDefault();

                // No usage record for this interval on this vehicle — skip
                if (lastUsedAt == default)
                    continue;

                var item = ComputeMaintenanceItem(
                    interval, checkDate, lastUsedAt, "Usage");

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
                    .Select(u => u.IssuedAt)
                    .FirstOrDefault();

                if (lastIssuedAt == default)
                    continue;

                var item = ComputeMaintenanceItem(
                    interval, checkDate, lastIssuedAt, "Usage");

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
                housingName ?? rider.Employee?.Housing?.Name ?? "N/A",  // ← resolves per-rider for admin
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
    private static MaintenanceItem ComputeMaintenanceItem(
        MaintenanceInterval interval,
        DateOnly checkDate,
        DateTime? effectiveLastDone,
        string recordSource)
    {
        if (effectiveLastDone == null)
        {
            // No record at all → NeverDone
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

    /// <summary>
    /// Load active intervals.  When housingName is provided, returns intervals
    /// that are either global (Location == null) or scoped to that housing.
    /// </summary>
    private async Task<List<MaintenanceInterval>> LoadActiveIntervalsAsync(
        string? housingName = null)
    {
        var query = _ctx.MaintenanceIntervals
            .Where(i => i.IsActive);

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

    private async Task<BaselineResponse> BuildBaselineResponseAsync(
        VehicleMaintenanceBaseline b,
        MaintenanceInterval interval)
    {
        string? vehiclePlate = null;
        if (b.VehicleNumber != null)
        {
            vehiclePlate = await _ctx.Vehicles
                .Where(v => v.VehicleNumber == b.VehicleNumber)
                .Select(v => v.PlateNumberA)
                .FirstOrDefaultAsync();
        }

        string? riderName = null;
        if (b.RiderId != null)
        {
            riderName = await _ctx.RiderDetails
                .Include(r => r.Employee)
                .Where(r => r.Id == b.RiderId.Value)
                .Select(r => r.Employee.NameAR)
                .FirstOrDefaultAsync();
        }

        var nextDue = b.LastDoneAt.AddDays(interval.IntervalDays);
        var daysUntilDue = DateOnly.FromDateTime(nextDue).DayNumber - Today.DayNumber;

        return new BaselineResponse(
            Id: b.Id,
            MaintenanceIntervalId: b.MaintenanceIntervalId,
            ItemName: interval.ItemName,
            ItemType: interval.ItemType,
            VehicleNumber: b.VehicleNumber,
            VehiclePlate: vehiclePlate,
            RiderId: b.RiderId,
            RiderName: riderName,
            LastDoneAt: b.LastDoneAt,
            NextDueAt: nextDue,
            DaysUntilDue: daysUntilDue,
            SetBy: b.SetBy,
            CreatedAt: b.CreatedAt,
            Notes: b.Notes
        );
    }

    // ── Result helpers ────────────────────────────────────────────────────

    private static Result<T> Fail<T>(string message) =>
        Result.Failure<T>(new Error("ReminderService.Error", message, 400));

    private static Result Fail(string message) =>
        Result.Failure(new Error("ReminderService.Error", message, 400));
}