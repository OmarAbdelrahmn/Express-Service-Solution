using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Service.Member;
using Domain;
using Domain.Entities;
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

        // Load all active intervals
        var intervals = await LoadActiveIntervalsAsync();
        if (!intervals.Any())
            return Result.Success(EmptyReport(date));

        // Load all vehicles and all riders (+ their housing names)
        var allVehicles = await _ctx.Vehicles
            .Include(v => v.RiderDetails).ThenInclude(r => r!.Employee)
            .AsNoTracking()
            .ToListAsync();

        var allRiders = await _ctx.RiderDetails
            .Include(r => r.Employee).ThenInclude(e => e.Housing)
            .AsNoTracking()
            .ToListAsync();

        return Result.Success(
            await BuildReportAsync(date, intervals, allVehicles, allRiders));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  MEMBER – Housing Reminder Dashboard
    // ══════════════════════════════════════════════════════════════════════

    public async Task<Result<MaintenanceReminderReport>> GetHousingDueMaintenanceAsync(
        long managerIqamaNo,
        DateOnly? checkDate = null)
    {
        var date = checkDate ?? Today;

        // Resolve housing
        var housing = await _ctx.Housings
            .Include(h => h.Employees)
            .FirstOrDefaultAsync(h => h.ManagerIqamaNo == managerIqamaNo);

        if (housing == null)
            return Fail<MaintenanceReminderReport>(
                "Housing not found or you are not assigned as a housing manager.");

        var employeeIqamas = housing.Employees
            .Where(e => !e.IsDeleted)
            .Select(e => e.IqamaNo)
            .ToList();

        // Load intervals that apply to this housing (global + housing-specific)
        var intervals = await LoadActiveIntervalsAsync(housing.Name);
        if (!intervals.Any())
            return Result.Success(EmptyReport(date));

        // Vehicles in housing (by rider assignment OR by location)
        var riderVehicleNumbers = await _ctx.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo)
                && r.VehicleNumber != null)
            .Select(r => r.VehicleNumber!)
            .Distinct()
            .ToListAsync();

        var housingVehicles = await _ctx.Vehicles
            .Include(v => v.RiderDetails).ThenInclude(r => r!.Employee)
            .Where(v => riderVehicleNumbers.Contains(v.VehicleNumber)
                || v.Location == housing.Name)
            .AsNoTracking()
            .ToListAsync();

        // Riders in housing
        var housingRiders = await _ctx.RiderDetails
            .Include(r => r.Employee)
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo)
                && !r.Employee.IsEmployee)
            .AsNoTracking()
            .ToListAsync();

        // Inject housing name into rider navigation for report rendering
        var report = await BuildReportAsync(date, intervals, housingVehicles, housingRiders,
            housingName: housing.Name);

        return Result.Success(report);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CORE CALCULATION ENGINE
    // ══════════════════════════════════════════════════════════════════════

    private async Task<MaintenanceReminderReport> BuildReportAsync(
        DateOnly checkDate,
        List<MaintenanceInterval> intervals,
        List<Vehicle> vehicles,
        List<RiderDetails> riders,
        string? housingName = null)
    {
        var vehicleReminders = new List<VehicleMaintenanceReminder>();
        var riderReminders = new List<RiderMaintenanceReminder>();

        // ── Spare Part intervals (per vehicle) ────────────────────────────
        var spIntervals = intervals
            .Where(i => i.ItemType == MaintenanceItemType.SparePart)
            .ToList();

        if (spIntervals.Any())
        {
            var spIds = spIntervals.Select(i => i.SparePartId!.Value).Distinct().ToList();
            var vehicleNumbers = vehicles.Select(v => v.VehicleNumber).ToList();

            // Load all relevant usage records in one query
            var usages = await _ctx.SparePartUsages
                .Where(u => spIds.Contains(u.SparePartId)
                    && vehicleNumbers.Contains(u.VehicleNumber))
                .GroupBy(u => new { u.SparePartId, u.VehicleNumber })
                .Select(g => new
                {
                    g.Key.SparePartId,
                    g.Key.VehicleNumber,
                    LastUsedAt = g.Max(u => u.UsedAt)
                })
                .AsNoTracking()
                .ToListAsync();

            // Load all relevant baselines
            var intervalIds = spIntervals.Select(i => i.Id).ToList();
            var baselines = await _ctx.VehicleMaintenanceBaselines
                .Where(b => intervalIds.Contains(b.MaintenanceIntervalId)
                    && b.VehicleNumber != null
                    && vehicleNumbers.Contains(b.VehicleNumber!))
                .AsNoTracking()
                .ToListAsync();

            foreach (var vehicle in vehicles)
            {
                var dueItems = new List<MaintenanceItem>();

                foreach (var interval in spIntervals)
                {
                    // Latest usage record
                    var usageEntry = usages.FirstOrDefault(u =>
                        u.SparePartId == interval.SparePartId!.Value &&
                        u.VehicleNumber == vehicle.VehicleNumber);

                    // Latest baseline
                    var baselineEntry = baselines.FirstOrDefault(b =>
                        b.MaintenanceIntervalId == interval.Id &&
                        b.VehicleNumber == vehicle.VehicleNumber);

                    // Effective last-done = latest of (usage, baseline)
                    DateTime? usageDate = usageEntry?.LastUsedAt;
                    DateTime? baselineDate = baselineEntry?.LastDoneAt;
                    string recordSource = "None";

                    DateTime? effectiveLastDone = null;
                    if (usageDate.HasValue && baselineDate.HasValue)
                    {
                        effectiveLastDone = usageDate > baselineDate ? usageDate : baselineDate;
                        recordSource = usageDate >= baselineDate ? "Usage" : "Baseline";
                    }
                    else if (usageDate.HasValue)
                    {
                        effectiveLastDone = usageDate;
                        recordSource = "Usage";
                    }
                    else if (baselineDate.HasValue)
                    {
                        effectiveLastDone = baselineDate;
                        recordSource = "Baseline";
                    }

                    var item = ComputeMaintenanceItem(
                        interval, checkDate, effectiveLastDone, recordSource);

                    // Only include items that are actionable (not OK)
                    if (item.Status != MaintenanceStatus.OK)
                        dueItems.Add(item);
                }

                if (!dueItems.Any()) continue;

                // Get assigned rider name from vehicle navigation
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
        }

        // ── Accessory intervals (per rider) ───────────────────────────────
        var accIntervals = intervals
            .Where(i => i.ItemType == MaintenanceItemType.Accessory)
            .ToList();

        if (accIntervals.Any())
        {
            var accIds = accIntervals.Select(i => i.AccessoryId!.Value).Distinct().ToList();
            var riderIds = riders.Select(r => r.Id).ToList();

            // Load all relevant usage records in one query
            var usages = await _ctx.RiderAccessoryUsages
                .Where(u => accIds.Contains(u.RiderAccessoryId)
                    && riderIds.Contains(u.RiderId))
                .GroupBy(u => new { u.RiderAccessoryId, u.RiderId })
                .Select(g => new
                {
                    g.Key.RiderAccessoryId,
                    g.Key.RiderId,
                    LastIssuedAt = g.Max(u => u.IssuedAt)
                })
                .AsNoTracking()
                .ToListAsync();

            // Load all relevant baselines
            var intervalIds = accIntervals.Select(i => i.Id).ToList();
            var baselines = await _ctx.VehicleMaintenanceBaselines
                .Where(b => intervalIds.Contains(b.MaintenanceIntervalId)
                    && b.RiderId != null
                    && riderIds.Contains(b.RiderId!.Value))
                .AsNoTracking()
                .ToListAsync();

            foreach (var rider in riders)
            {
                var dueItems = new List<MaintenanceItem>();

                foreach (var interval in accIntervals)
                {
                    var usageEntry = usages.FirstOrDefault(u =>
                        u.RiderAccessoryId == interval.AccessoryId!.Value &&
                        u.RiderId == rider.Id);

                    var baselineEntry = baselines.FirstOrDefault(b =>
                        b.MaintenanceIntervalId == interval.Id &&
                        b.RiderId == rider.Id);

                    DateTime? usageDate = usageEntry?.LastIssuedAt;
                    DateTime? baselineDate = baselineEntry?.LastDoneAt;
                    string recordSource = "None";

                    DateTime? effectiveLastDone = null;
                    if (usageDate.HasValue && baselineDate.HasValue)
                    {
                        effectiveLastDone = usageDate > baselineDate ? usageDate : baselineDate;
                        recordSource = usageDate >= baselineDate ? "Usage" : "Baseline";
                    }
                    else if (usageDate.HasValue)
                    {
                        effectiveLastDone = usageDate;
                        recordSource = "Usage";
                    }
                    else if (baselineDate.HasValue)
                    {
                        effectiveLastDone = baselineDate;
                        recordSource = "Baseline";
                    }

                    var item = ComputeMaintenanceItem(
                        interval, checkDate, effectiveLastDone, recordSource);

                    if (item.Status != MaintenanceStatus.OK)
                        dueItems.Add(item);
                }

                if (!dueItems.Any()) continue;

                var resolvedHousingName = housingName
                    ?? rider.Employee?.Housing?.Name
                    ?? "N/A";

                riderReminders.Add(new RiderMaintenanceReminder(
                    rider.Id,
                    rider.EmployeeIqamaNo,
                    rider.Employee?.NameAR ?? "N/A",
                    rider.Employee?.NameEN ?? "N/A",
                    rider.WorkingId ?? "N/A",
                    resolvedHousingName,
                    dueItems.OrderBy(i => i.DaysUntilDue).ToList()
                ));
            }
        }

        // ── Aggregate counters ────────────────────────────────────────────
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
            TotalNeverDoneItems: allItems.Count(i => i.Status == MaintenanceStatus.NeverDone),
            VehicleReminders: vehicleReminders
                .OrderBy(v => v.DueItems.Min(i => i.DaysUntilDue))
                .ToList(),
            RiderReminders: riderReminders
                .OrderBy(r => r.DueItems.Min(i => i.DaysUntilDue))
                .ToList()
        );
    }

    // ── Single item calculation ───────────────────────────────────────────

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