using System.Data;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Service.Petrol;
using Domain;
using Domain.Entities;
using Domain.Entities.Petrol;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

// Explicit operator tool. No API startup hook or automatic database repair.
var mode = args.FirstOrDefault() ?? "help";
if (mode is not ("plan" or "rehearse" or "apply" or "verify" or "audit" or "inspect-vehicle" or "assign-pending" or "refresh-diagnostics"))
{
    Console.WriteLine("Run from the repository root: dotnet run --project tools/PetrolRepair -- plan|rehearse|apply|verify|audit <plan.json>, inspect-vehicle <output.json> <plate>, assign-pending <output.json> <exact-plate> <confirmed-employee-iqama>, or refresh-diagnostics <output.json>.");
    return;
}
var root = Directory.GetCurrentDirectory();
var planPath = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/petrol-repair-2026-10-18/plan.json");
Directory.CreateDirectory(Path.GetDirectoryName(planPath)!);
var json = new JsonSerializerOptions { WriteIndented = false };
using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Express Service/appsettings.json")));
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? configuration.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString();
var options = new DbContextOptionsBuilder<ApplicationDbcontext>().UseSqlServer(connectionString, sql => sql.CommandTimeout(180)).Options;
await using var db = new ApplicationDbcontext(options);

if (mode == "refresh-diagnostics")
{
    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    var pending = await db.RiderPetrolCosts.AsNoTracking().Where(r => r.RiderIqamaNo == null && r.VehicleNumber != null
        && r.Notes != null && r.Notes.Contains("employee or manual allocation requires review")).OrderBy(r => r.Id).ToListAsync();
    if (pending.Count == 0) { Console.WriteLine("No generic conflict diagnostics remain; nothing changed."); return; }
    var ids = pending.Select(r => r.VehiclePetrolCostId).Distinct().Order().ToList();
    var sources = await db.VehiclePetrolCosts.AsNoTracking().Where(c => ids.Contains(c.Id)).OrderBy(c => c.Id).ToListAsync();
    var allocations = await db.RiderPetrolCosts.AsNoTracking().Where(r => ids.Contains(r.VehiclePetrolCostId)).OrderBy(r => r.Id).ToListAsync();
    var manualBefore = await db.RiderPetrolCosts.AsNoTracking().Where(r => r.AttributionSource == PetrolAttributionSource.ManualOverride).OrderBy(r => r.Id).ToListAsync();
    await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(new { Sources = sources, Allocations = allocations, ManualOverrides = manualBefore }, json));
    var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..6];
    var sourceBackup = "PetrolDiagnosticSources_" + stamp;
    var allocationBackup = "PetrolDiagnosticAllocations_" + stamp;
    var idList = string.Join(",", ids); // IDs are integers read from the database, not operator SQL.
    var backupSql = $"SELECT * INTO dbo.[{sourceBackup}] FROM VehiclePetrolCosts WHERE Id IN ({idList}); SELECT * INTO dbo.[{allocationBackup}] FROM RiderPetrolCosts WHERE VehiclePetrolCostId IN ({idList});";
    await db.Database.ExecuteSqlRawAsync(backupSql);
    foreach (var batch in ids.Chunk(500))
    {
        var result = await new PetrolService(db).ReattributeCostsAsync(batch);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Description);
    }
    var afterSources = await db.VehiclePetrolCosts.AsNoTracking().Where(c => ids.Contains(c.Id)).OrderBy(c => c.Id).ToListAsync();
    var afterAllocations = await db.RiderPetrolCosts.AsNoTracking().Where(r => ids.Contains(r.VehiclePetrolCostId)).OrderBy(r => r.Id).ToListAsync();
    var manualAfter = await db.RiderPetrolCosts.AsNoTracking().Where(r => r.AttributionSource == PetrolAttributionSource.ManualOverride).OrderBy(r => r.Id).ToListAsync();
    string AllocationKey(RiderPetrolCost r) => JsonSerializer.Serialize(new { r.Id, r.VehiclePetrolCostId, r.VehicleNumber, r.RiderIqamaNo,
        r.Date, r.Cost, r.AttributionSource, r.ResolvedFromStatusId, CreatedTicks = r.CreatedAt.Ticks });
    if (JsonSerializer.Serialize(sources, json) != JsonSerializer.Serialize(afterSources, json)
        || !allocations.Select(AllocationKey).SequenceEqual(afterAllocations.Select(AllocationKey))
        || JsonSerializer.Serialize(manualBefore, json) != JsonSerializer.Serialize(manualAfter, json))
        throw new InvalidOperationException("Diagnostic refresh attempted a financial/identity/manual-allocation change; rolled back.");
    var beforeNotes = allocations.ToDictionary(r => r.Id, r => r.Notes);
    var updated = afterAllocations.Count(r => r.Notes != beforeNotes[r.Id]);
    var receipt = new { UpdatedDiagnosticRows = updated, FinancialChanges = 0, ManualOverridesPreserved = manualAfter.Count,
        SourceBackup = sourceBackup, AllocationBackup = allocationBackup };
    db.SystemAuditEvents.Add(new SystemAuditEvent { OperationId = Guid.NewGuid(), OccurredAtUtc = DateTimeOffset.UtcNow,
        ActorName = "User-requested petrol diagnostic correction", Source = "PetrolRepairTool", OperationName = "ClarifyManualAllocationConflicts",
        EntityType = "PetrolAllocationDiagnostics", EntityKey = stamp, Action = SystemAuditAction.Update,
        ChangedFieldsJson = "[\"Notes\"]", NewValuesJson = JsonSerializer.Serialize(receipt, json) });
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    await File.WriteAllTextAsync(Path.ChangeExtension(planPath, ".applied.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (mode == "assign-pending")
{
    var plate = args.ElementAtOrDefault(2) ?? throw new ArgumentException("Supply the exact plate as the third argument.");
    var iqama = long.Parse(args.ElementAtOrDefault(3) ?? throw new ArgumentException("Supply the confirmed employee Iqama as the fourth argument."));
    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    var vehicle = await db.Vehicles.AsNoTracking().SingleAsync(v => v.PlateNumberE == plate);
    var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.IqamaNo == iqama && !e.IsDeleted && e.Status == "enable");
    var possession = await db.RiderVehicleStatus.AsNoTracking().SingleAsync(s => s.VehicleNumber == vehicle.VehicleNumber
        && s.EmployeeIqamaNo == iqama && s.IsActive && (s.StatusType == VehicleStatusType.Taken || s.StatusType == VehicleStatusType.switched));
    var profile = await db.RiderDetails.AsNoTracking().SingleOrDefaultAsync(r => r.EmployeeIqamaNo == iqama);
    if (profile?.VehicleNumber != null && profile.VehicleNumber != vehicle.VehicleNumber)
        throw new InvalidOperationException("The employee's current rider profile identifies another vehicle.");
    var pending = await db.RiderPetrolCosts.AsNoTracking().Where(r => r.VehicleNumber == vehicle.VehicleNumber && r.RiderIqamaNo == null)
        .OrderBy(r => r.Date).ToListAsync();
    if (pending.Count == 0) { Console.WriteLine("No pending petrol costs for the confirmed employee/vehicle; nothing changed."); return; }
    var total = await db.VehiclePetrolCosts.SumAsync(c => c.Cost);
    var allocationTotal = await db.RiderPetrolCosts.SumAsync(c => c.Cost);
    var manualBefore = await db.RiderPetrolCosts.AsNoTracking().Where(r => r.AttributionSource == PetrolAttributionSource.ManualOverride).OrderBy(r => r.Id).ToListAsync();
    await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(new { vehicle.VehicleNumber, vehicle.PlateNumberE, employee.IqamaNo,
        employee.NameEN, ExistingProfileId = profile?.Id, CurrentPossessionStatusId = possession.Id,
        CurrentPossessionStart = possession.Timestamp, Pending = pending, SourceTotal = total, AllocationTotal = allocationTotal },
        new JsonSerializerOptions { WriteIndented = true }));
    var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..6];
    var sourceBackup = "PetrolEmployeeCosts_" + stamp;
    var allocationBackup = "PetrolEmployeeAllocations_" + stamp;
    var backupSql = $"SELECT * INTO dbo.[{sourceBackup}] FROM VehiclePetrolCosts WHERE VehicleNumber=@vehicle; SELECT * INTO dbo.[{allocationBackup}] FROM RiderPetrolCosts WHERE VehicleNumber=@vehicle;";
    await db.Database.ExecuteSqlRawAsync(backupSql, new SqlParameter("@vehicle", vehicle.VehicleNumber));
    var service = new PetrolService(db);
    foreach (var row in pending)
    {
        var actor = $"User correction 2026-10-19: pending {plate} petrol to current employee; possession status {possession.Id}";
        var result = await service.AssignRiderToUnattributedAsync(plate, row.Date, iqama, actor);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Description);
    }
    var pendingIds = pending.Select(r => r.Id).ToList();
    var assigned = await db.RiderPetrolCosts.AsNoTracking().Where(r => pendingIds.Contains(r.Id)).ToListAsync();
    if (assigned.Count != pending.Count || assigned.Any(r => r.RiderIqamaNo != iqama || r.AttributionSource != PetrolAttributionSource.ManualOverride))
        throw new InvalidOperationException("Not every requested pending allocation was assigned to the confirmed employee.");
    var oldManualIds = manualBefore.Select(r => r.Id).ToList();
    var retained = await db.RiderPetrolCosts.AsNoTracking().Where(r => oldManualIds.Contains(r.Id)).OrderBy(r => r.Id).ToListAsync();
    if (JsonSerializer.Serialize(manualBefore, json) != JsonSerializer.Serialize(retained, json))
        throw new InvalidOperationException("An existing manual override changed.");
    if (await db.VehiclePetrolCosts.SumAsync(c => c.Cost) != total || await db.RiderPetrolCosts.SumAsync(c => c.Cost) != allocationTotal)
        throw new InvalidOperationException("Cost totals changed.");
    var finalProfile = await db.RiderDetails.AsNoTracking().SingleAsync(r => r.EmployeeIqamaNo == iqama);
    var receipt = new { vehicle.PlateNumberE, EmployeeIqama = iqama, employee.NameEN, ProfileId = finalProfile.Id,
        CreatedProfile = profile == null, AssignedRows = assigned.Count, AssignedCost = assigned.Sum(r => r.Cost),
        Dates = assigned.Select(r => r.Date).Order().ToList(), SourceTotal = total, AllocationTotal = allocationTotal,
        ExistingManualOverridesPreserved = retained.Count, SourceBackup = sourceBackup, AllocationBackup = allocationBackup };
    db.SystemAuditEvents.Add(new SystemAuditEvent { OperationId = Guid.NewGuid(), OccurredAtUtc = DateTimeOffset.UtcNow,
        ActorName = "Explicit user petrol correction", Source = "PetrolRepairTool", OperationName = "AttachPendingPetrolToConfirmedEmployee",
        EntityType = "EmployeePetrolAssignment", EntityKey = vehicle.VehicleNumber, Action = SystemAuditAction.Update,
        ChangedFieldsJson = "[\"RiderIqamaNo\",\"AttributionSource\",\"IsAttributed\"]", NewValuesJson = JsonSerializer.Serialize(receipt, json) });
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    await File.WriteAllTextAsync(Path.ChangeExtension(planPath, ".applied.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (mode == "inspect-vehicle")
{
    var plate = args.ElementAtOrDefault(2) ?? throw new ArgumentException("Supply a plate as the third argument.");
    var vehicles = await db.Vehicles.AsNoTracking().Where(v => v.PlateNumberE.Contains(plate) || v.PlateNumberA.Contains(plate) || v.VehicleNumber == plate).ToListAsync();
    var numbers = vehicles.Select(v => v.VehicleNumber).ToList();
    var statuses = await db.RiderVehicleStatus.AsNoTracking().Where(s => numbers.Contains(s.VehicleNumber)).OrderBy(s => s.Timestamp).ThenBy(s => s.Id).ToListAsync();
    var iqamas = statuses.Where(s => s.EmployeeIqamaNo.HasValue).Select(s => s.EmployeeIqamaNo!.Value).Distinct().ToList();
    var employees = await db.Employees.AsNoTracking().Where(e => iqamas.Contains(e.IqamaNo)).Select(e => new {e.IqamaNo,e.NameEN,e.NameAR,e.Status,e.IsDeleted,e.HousingId}).ToListAsync();
    var riders = await db.RiderDetails.AsNoTracking().Where(r => iqamas.Contains(r.EmployeeIqamaNo) || (r.VehicleNumber != null && numbers.Contains(r.VehicleNumber)))
        .Select(r => new {r.Id,r.EmployeeIqamaNo,r.VehicleNumber,r.CompanyId,r.WorkingId}).ToListAsync();
    var costs = await db.VehiclePetrolCosts.AsNoTracking().Where(c => (c.VehicleNumber != null && numbers.Contains(c.VehicleNumber)) || c.PlateNumberE.Contains(plate)).ToListAsync();
    var ids = costs.Select(c => c.Id).ToList();
    var allocations = await db.RiderPetrolCosts.AsNoTracking().Where(r => ids.Contains(r.VehiclePetrolCostId)).Select(r => new {r.Id,r.VehiclePetrolCostId,r.Date,r.RiderIqamaNo,r.Cost,r.AttributionSource,r.Notes}).ToListAsync();
    var data = new { Vehicles=vehicles.Select(v=>new {v.VehicleNumber,v.PlateNumberE,v.PlateNumberA,v.VehicleType}), Employees=employees,RiderProfiles=riders,
        Statuses=statuses.Select(s=>new {s.Id,s.VehicleNumber,s.EmployeeIqamaNo,s.StatusType,s.IsActive,s.Timestamp,s.PermissionStartDate,s.PermissionEndDate,s.Reason}),
        SourceCosts=costs.Select(c=>new {c.Id,c.VehicleNumber,c.Date,c.Cost,c.IsAttributed}), Allocations=allocations };
    var output = JsonSerializer.Serialize(data,new JsonSerializerOptions {WriteIndented=true});
    await File.WriteAllTextAsync(planPath,output);
    Console.WriteLine(JsonSerializer.Serialize(new {data.Vehicles,data.Employees,data.RiderProfiles,data.Statuses,
        SourceRows=costs.Count,UnassignedRows=allocations.Count(r=>r.RiderIqamaNo==null),UnassignedCost=allocations.Where(r=>r.RiderIqamaNo==null).Sum(r=>r.Cost)},new JsonSerializerOptions {WriteIndented=true}));
    return;
}

if (mode == "audit")
{
    var snapshot = await ReadSnapshot(db);
    var windows = PetrolAssignmentTimeline.Build(snapshot.Statuses);
    var byVehicle = windows.ToLookup(w => w.Status.VehicleNumber);
    var sources = snapshot.Costs.ToDictionary(c => c.Id);
    var allocations = snapshot.Allocations.ToLookup(r => r.VehiclePetrolCostId);
    var validRiders = snapshot.EmployeeIqamas.ToHashSet();
    var latestDate = snapshot.Costs.Max(c => c.Date);
    var currentRiders = await db.RiderDetails.AsNoTracking().Where(r => r.VehicleNumber != null)
        .Select(r => new { r.EmployeeIqamaNo, r.VehicleNumber }).ToListAsync();
    var profileIqamas = await db.RiderDetails.AsNoTracking().Select(r => r.EmployeeIqamaNo).ToHashSetAsync();
    var activeEmployeesWithoutProfile = snapshot.Statuses.Where(s => s.IsActive && PetrolAssignmentTimeline.IsTake(s)
        && s.EmployeeIqamaNo.HasValue && validRiders.Contains(s.EmployeeIqamaNo.Value) && !profileIqamas.Contains(s.EmployeeIqamaNo.Value))
        .Select(s => new { s.EmployeeIqamaNo, s.VehicleNumber }).Distinct().ToList();
    var shares = new Dictionary<(string Vehicle, DateOnly Date), IReadOnlyList<PetrolAssignmentTimeline.RiderShare>>();
    IReadOnlyList<PetrolAssignmentTimeline.RiderShare> Shares(string number, DateOnly date)
    {
        var key = (number, date);
        if (!shares.TryGetValue(key, out var value))
        { value = PetrolAssignmentTimeline.OnDate(byVehicle[number], number, date); shares[key] = value; }
        return value;
    }
    var stale = snapshot.Allocations.Where(r => r.RiderIqamaNo.HasValue && r.AttributionSource != PetrolAttributionSource.ManualOverride
        && (r.VehicleNumber == null || !Shares(r.VehicleNumber, r.Date).Any(s => s.IqamaNo == r.RiderIqamaNo))).ToList();
    var exceptions = snapshot.Allocations.Where(r => r.RiderIqamaNo == null).Select(r =>
    {
        var source = sources[r.VehiclePetrolCostId];
        var latest = snapshot.Statuses.Where(s => s.VehicleNumber == r.VehicleNumber && s.Timestamp < r.Date.AddDays(1).ToDateTime(TimeOnly.MinValue))
            .OrderByDescending(s => s.Timestamp).ThenByDescending(s => s.Id).FirstOrDefault();
        var candidates = r.VehicleNumber == null ? [] : Shares(r.VehicleNumber, r.Date).Where(s => validRiders.Contains(s.IqamaNo)).ToList();
        var conflicts = snapshot.Allocations.Where(m => m.AttributionSource == PetrolAttributionSource.ManualOverride
            && m.Date == r.Date && m.VehicleNumber != r.VehicleNumber && candidates.Any(c => c.IqamaNo == m.RiderIqamaNo)
            && (m.VehicleNumber == null || !Shares(m.VehicleNumber, r.Date).Any(c => c.IqamaNo == m.RiderIqamaNo)))
            .Select(m => m.VehiclePetrolCostId).Distinct().Order().ToList();
        var reason = r.VehicleNumber == null ? "UnknownPlate" : conflicts.Count > 0 ? "ManualOverrideConflict" : candidates.Count > 0 ? "AssignmentConflictOrStaleAllocation"
            : latest?.StatusType == VehicleStatusType.Returned ? "ReturnedBeforeReportDate"
            : latest?.StatusType is VehicleStatusType.Problem or VehicleStatusType.Stolen or VehicleStatusType.BreakUp or VehicleStatusType.OutOfService
                ? "VehicleUnavailableOnReportDate" : "MissingAssignmentHistory";
        return new { SourceId = source.Id, AllocationId = r.Id, source.PlateNumberE, r.VehicleNumber, r.Date, r.Cost, ReasonCode = reason,
            StatusId = latest?.Id, StatusType = latest?.StatusType, StatusRecordedAt = latest?.Timestamp, CandidateCount = candidates.Count,
            HasCurrentRider = currentRiders.Any(c => c.VehicleNumber == r.VehicleNumber), ConflictingSourceIds = conflicts, r.Notes };
    }).ToList();
    var closedActive = windows.Where(w => w.Status.IsActive && w.End <= latestDate.ToDateTime(TimeOnly.MinValue))
        .Select(w => new { w.Status.Id, w.Status.VehicleNumber, w.Start, w.End,
            HasMatchingCurrentRider = currentRiders.Any(r => r.VehicleNumber == w.Status.VehicleNumber && r.EmployeeIqamaNo == w.Status.EmployeeIqamaNo) }).ToList();
    var audit = new { AuditedAtUtc = DateTimeOffset.UtcNow, LatestPetrolDate = latestDate, SourceRows = snapshot.Costs.Count,
        SourceCost = snapshot.Costs.Sum(c => c.Cost), AllocationCost = snapshot.Allocations.Sum(r => r.Cost),
        MissingAllocations = snapshot.Costs.Count(c => !allocations[c.Id].Any()),
        UnbalancedSources = snapshot.Costs.Count(c => allocations[c.Id].Sum(r => r.Cost) != c.Cost),
        WrongVehicleOrDate = snapshot.Allocations.Count(r => r.Date != sources[r.VehiclePetrolCostId].Date || r.VehicleNumber != sources[r.VehiclePetrolCostId].VehicleNumber),
        StaleAutomaticRows = stale.Count, ClosedActiveWindows = closedActive, ActiveEmployeesWithoutRiderProfile = activeEmployeesWithoutProfile,
        CurrentAssignmentWithoutActiveTake = currentRiders.Where(r => !snapshot.Statuses.Any(s => s.IsActive && PetrolAssignmentTimeline.IsTake(s)
            && s.VehicleNumber == r.VehicleNumber && s.EmployeeIqamaNo == r.EmployeeIqamaNo)).Select(r => r.VehicleNumber).ToList(),
        UnassignedByReason = exceptions.GroupBy(r => r.ReasonCode).Select(g => new { Reason = g.Key, Rows = g.Count(), Cost = g.Sum(r => r.Cost) }).ToList(),
        Exceptions = exceptions, StaleAutomaticSourceIds = stale.Select(r => r.VehiclePetrolCostId).Distinct().Order().ToList() };
    await File.WriteAllTextAsync(Path.ChangeExtension(planPath, ".audit.json"), JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(new { audit.LatestPetrolDate, audit.SourceRows, audit.SourceCost, audit.AllocationCost,
        audit.MissingAllocations, audit.UnbalancedSources, audit.WrongVehicleOrDate, audit.StaleAutomaticRows,
        ClosedActiveWindows = closedActive.Count, CurrentAssignmentWithoutActiveTake = audit.CurrentAssignmentWithoutActiveTake.Count,
        ActiveEmployeesWithoutRiderProfile = activeEmployeesWithoutProfile.Count, audit.UnassignedByReason }, new JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (mode == "plan")
{
    Snapshot before;
    await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
    { before = await ReadSnapshot(db); await transaction.CommitAsync(); }
    await File.WriteAllTextAsync(Path.ChangeExtension(planPath, ".before.json"), JsonSerializer.Serialize(before, json));
    var after = await Simulate(before, json);
    Validate(before, after);
    var plan = new RepairPlan(Fingerprint(before, json), before, after, Describe(before, after));
    await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, json));
    Console.WriteLine(JsonSerializer.Serialize(plan.Summary, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Plan saved: {planPath}; snapshot SHA-256: {plan.BeforeHash}");
    return;
}
var saved = JsonSerializer.Deserialize<RepairPlan>(await File.ReadAllTextAsync(planPath), json)!;
Validate(saved.Before, saved.After);
if (Fingerprint(saved.Before, json) != saved.BeforeHash) throw new InvalidOperationException("The saved baseline does not match its hash.");
if (mode == "verify")
{
    var actual = await ReadSnapshot(db);
    VerifyApplied(saved.Before, saved.After, actual, json);
    Console.WriteLine(JsonSerializer.Serialize(Describe(saved.Before, actual), new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Committed data matches the repair plan; source totals and manual allocations are preserved.");
    return;
}
await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
{
    // Hold source/allocation tables through validation, backups, writes and verification.
    await db.Database.ExecuteSqlRawAsync("SELECT TOP(1) Id FROM VehiclePetrolCosts WITH(TABLOCKX,HOLDLOCK); SELECT TOP(1) Id FROM RiderPetrolCosts WITH(TABLOCKX,HOLDLOCK);");
    var current = await ReadSnapshot(db);
    if (Fingerprint(current, json) != saved.BeforeHash)
        throw new InvalidOperationException("Database data changed after the preview. Generate and review a fresh plan.");
    Console.WriteLine("Locked petrol tables and verified the preview baseline.");
    var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + saved.BeforeHash[..8];
    var sourceBackup = "PetrolRepairVehicle_" + stamp;
    var allocationBackup = "PetrolRepairRider_" + stamp;
    await db.Database.ExecuteSqlRawAsync($"SELECT * INTO dbo.[{sourceBackup}] FROM VehiclePetrolCosts; SELECT * INTO dbo.[{allocationBackup}] FROM RiderPetrolCosts;");
    await db.Database.ExecuteSqlRawAsync("SELECT TOP(0) * INTO #PetrolSources FROM VehiclePetrolCosts; SELECT TOP(0) * INTO #PetrolAllocations FROM RiderPetrolCosts;");
    await Stage(db, saved.After.Costs, "#PetrolSources");
    var originalIds = saved.Before.Allocations.Select(r => r.Id).ToHashSet();
    var allocations = JsonSerializer.Deserialize<List<RiderPetrolCost>>(JsonSerializer.Serialize(saved.After.Allocations, json), json)!;
    foreach (var row in allocations.Where(r => !originalIds.Contains(r.Id))) row.Id = 0;
    await Stage(db, allocations, "#PetrolAllocations");
    Console.WriteLine("Created transaction-scoped backups and staged the reviewed repair.");
    // Existing manual IDs survive. Reparent them before deleting consolidated sources.
    await db.Database.ExecuteSqlRawAsync("""
        DELETE r FROM RiderPetrolCosts r WHERE NOT EXISTS(SELECT 1 FROM #PetrolAllocations p WHERE p.Id=r.Id);
        UPDATE r SET VehiclePetrolCostId=p.VehiclePetrolCostId,VehicleNumber=p.VehicleNumber,RiderIqamaNo=p.RiderIqamaNo,
            Date=p.Date,Cost=p.Cost,AttributionSource=p.AttributionSource,ResolvedFromStatusId=p.ResolvedFromStatusId,Notes=p.Notes
        FROM RiderPetrolCosts r JOIN #PetrolAllocations p ON p.Id=r.Id
        WHERE EXISTS(SELECT r.VehiclePetrolCostId,r.VehicleNumber,r.RiderIqamaNo,r.Date,r.Cost,r.AttributionSource,r.ResolvedFromStatusId,r.Notes
            EXCEPT SELECT p.VehiclePetrolCostId,p.VehicleNumber,p.RiderIqamaNo,p.Date,p.Cost,p.AttributionSource,p.ResolvedFromStatusId,p.Notes);
        UPDATE v SET PlateNumberE=p.PlateNumberE,VehicleNumber=p.VehicleNumber,Cost=p.Cost,IsAttributed=p.IsAttributed,
            HasResolutionError=p.HasResolutionError,ResolutionErrorMessage=p.ResolutionErrorMessage,Note=p.Note
        FROM VehiclePetrolCosts v JOIN #PetrolSources p ON p.Id=v.Id
        WHERE EXISTS(SELECT v.PlateNumberE,v.VehicleNumber,v.Cost,v.IsAttributed,v.HasResolutionError,v.ResolutionErrorMessage,v.Note
            EXCEPT SELECT p.PlateNumberE,p.VehicleNumber,p.Cost,p.IsAttributed,p.HasResolutionError,p.ResolutionErrorMessage,p.Note);
        DELETE v FROM VehiclePetrolCosts v WHERE NOT EXISTS(SELECT 1 FROM #PetrolSources p WHERE p.Id=v.Id);
        INSERT RiderPetrolCosts(VehiclePetrolCostId,VehicleNumber,RiderIqamaNo,Date,Cost,AttributionSource,ResolvedFromStatusId,Notes,CreatedAt)
            SELECT VehiclePetrolCostId,VehicleNumber,RiderIqamaNo,Date,Cost,AttributionSource,ResolvedFromStatusId,Notes,CreatedAt
            FROM #PetrolAllocations WHERE Id=0;
        """);
    Console.WriteLine("Reconciled source costs and rider allocations.");
    // Apply only this repair's EF migration, inside the same transaction as data repair.
    var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
    if (pending.Any(m => !m.EndsWith("_EnforcePetrolDailyUniqueness", StringComparison.Ordinal)))
        throw new InvalidOperationException("Unrelated pending migrations exist; no repair was committed.");
    foreach (var migrationId in pending)
    {
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[migrationId], db.Database.ProviderName!);
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        await db.Database.ExecuteSqlRawAsync(db.GetService<IHistoryRepository>().GetInsertScript(new HistoryRow(migrationId, "10.0.6")));
    }
    db.SystemAuditEvents.Add(new SystemAuditEvent { OperationId = Guid.NewGuid(), OccurredAtUtc = DateTimeOffset.UtcNow,
        ActorName = "Authorized petrol repair", Source = "PetrolRepairTool", OperationName = "RepairPetrolAssignmentsAndReconcileCosts",
        EntityType = "PetrolRepair", EntityKey = saved.BeforeHash, Action = SystemAuditAction.Update,
        ChangedFieldsJson = "[\"Allocations\",\"DailySourceConsolidation\",\"AttributionFlags\"]",
        OldValuesJson = JsonSerializer.Serialize(new { saved.BeforeHash, SourceBackup = sourceBackup, AllocationBackup = allocationBackup }),
        NewValuesJson = JsonSerializer.Serialize(saved.Summary) });
    await db.SaveChangesAsync();
    var applied = await ReadSnapshot(db);
    VerifyApplied(saved.Before, saved.After, applied, json);
    Console.WriteLine("Verified stored costs, allocation totals, manual overrides, and unchanged assignment history.");
    // Exercise actual SQL unique indexes; both inserts must fail, not just application guards.
    if (mode == "rehearse") await db.Database.ExecuteSqlRawAsync("""
        SET XACT_ABORT OFF;
        BEGIN TRY
            INSERT VehiclePetrolCosts(PlateNumberE,VehicleNumber,Cost,Date,UploadedAt,UploadedBy,IsAttributed,HasResolutionError)
            SELECT TOP(1) PlateNumberE,VehicleNumber,Cost,Date,UploadedAt,UploadedBy,IsAttributed,HasResolutionError
            FROM VehiclePetrolCosts WHERE VehicleNumber IS NOT NULL;
            THROW 51000, 'Resolved petrol uniqueness was not enforced', 1;
        END TRY BEGIN CATCH IF ERROR_NUMBER() NOT IN(2601,2627) THROW; END CATCH;
        BEGIN TRY
            INSERT VehiclePetrolCosts(PlateNumberE,VehicleNumber,Cost,Date,UploadedAt,UploadedBy,IsAttributed,HasResolutionError)
            SELECT TOP(1) PlateNumberE,VehicleNumber,Cost,Date,UploadedAt,UploadedBy,IsAttributed,HasResolutionError
            FROM VehiclePetrolCosts WHERE VehicleNumber IS NULL;
            THROW 51000, 'Unresolved petrol uniqueness was not enforced', 1;
        END TRY BEGIN CATCH IF ERROR_NUMBER() NOT IN(2601,2627) THROW; END CATCH;
        """);
    if (mode == "rehearse")
    {
        await transaction.RollbackAsync();
        if (Fingerprint(await ReadSnapshot(db), json) != saved.BeforeHash)
            throw new InvalidOperationException("Rollback verification failed.");
        Console.WriteLine("SQL Server rehearsal passed: reconciliation, manual overrides, unique constraints, and complete rollback verified.");
    }
    else
    {
        await transaction.CommitAsync();
        await File.WriteAllTextAsync(Path.ChangeExtension(planPath, ".applied.json"),
            JsonSerializer.Serialize(new { AppliedAtUtc = DateTimeOffset.UtcNow, saved.BeforeHash, SourceBackup = sourceBackup,
                AllocationBackup = allocationBackup, saved.Summary }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Committed. Database backups: dbo.{sourceBackup}; dbo.{allocationBackup}");
        Console.WriteLine(JsonSerializer.Serialize(saved.Summary, new JsonSerializerOptions { WriteIndented = true }));
    }
}

static async Task<Snapshot> ReadSnapshot(ApplicationDbcontext db) => new(
    await db.VehiclePetrolCosts.AsNoTracking().OrderBy(c => c.Id).ToListAsync(),
    await db.RiderPetrolCosts.AsNoTracking().OrderBy(c => c.Id).ToListAsync(),
    await db.RiderVehicleStatus.AsNoTracking().OrderBy(c => c.Id).ToListAsync(),
    await db.Vehicles.AsNoTracking().OrderBy(v => v.VehicleNumber).Select(v => new VehicleIdentity(v.VehicleNumber, v.PlateNumberE)).ToListAsync(),
    await db.Employees.AsNoTracking().Select(e => e.IqamaNo).OrderBy(i => i).ToListAsync());

static async Task<Snapshot> Simulate(Snapshot baseline, JsonSerializerOptions json)
{
    var copy = JsonSerializer.Deserialize<Snapshot>(JsonSerializer.Serialize(baseline, json), json)!;
    var rows = copy.Costs.ToDictionary(c => c.Id);
    foreach (var cost in copy.Costs) cost.PlateNumberE = PlateKey(cost.PlateNumberE);
    var lookup = copy.Vehicles.GroupBy(v => PlateKey(v.PlateNumberE)).ToDictionary(g => g.Key, g => g.ToList());
    foreach (var cost in copy.Costs.Where(c => c.VehicleNumber == null || c.HasResolutionError))
        if (lookup.TryGetValue(cost.PlateNumberE, out var vehicles) && vehicles.Count == 1)
        { cost.VehicleNumber = vehicles[0].VehicleNumber; cost.HasResolutionError = false; cost.ResolutionErrorMessage = null; }
    foreach (var group in copy.Costs.GroupBy(c => (Key: c.VehicleNumber is null ? "plate:" + c.PlateNumberE : "vehicle:" + c.VehicleNumber, c.Date)).Where(g => g.Count() > 1))
    {
        var keeper = group.OrderBy(c => c.Id).First();
        var mergedIds = group.Select(c => c.Id).ToHashSet();
        keeper.Cost = group.Sum(c => c.Cost); // Preserve every uploaded amount; do not guess which transaction to discard.
        keeper.Note = string.Join(" | ", group.Select(c => c.Note).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct()
            .Append("Consolidated source IDs " + string.Join(",", mergedIds.Order()) + "; original amounts retained in repair backup."));
        foreach (var allocation in copy.Allocations.Where(r => mergedIds.Contains(r.VehiclePetrolCostId))) allocation.VehiclePetrolCostId = keeper.Id;
        foreach (var extra in group.Where(c => c.Id != keeper.Id)) rows.Remove(extra.Id);
    }
    await using var memory = new ApplicationDbcontext(new DbContextOptionsBuilder<ApplicationDbcontext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    memory.ChangeTracker.AutoDetectChangesEnabled = false;
    memory.Vehicles.AddRange(copy.Vehicles.Select(v => new Vehicle { VehicleNumber = v.VehicleNumber, PlateNumberE = v.PlateNumberE }));
    memory.Employees.AddRange(copy.EmployeeIqamas.Select(i => new Employees { IqamaNo = i }));
    memory.RiderVehicleStatus.AddRange(copy.Statuses);
    memory.VehiclePetrolCosts.AddRange(rows.Values);
    memory.RiderPetrolCosts.AddRange(copy.Allocations);
    memory.ChangeTracker.AutoDetectChangesEnabled = true;
    await memory.SaveChangesAsync();
    memory.ChangeTracker.Clear();
    var ids = rows.Keys.Order().ToList();
    var done = 0;
    foreach (var batch in ids.Chunk(500))
    {
        var result = await new PetrolService(memory).ReattributeCostsAsync(batch);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Description);
        memory.ChangeTracker.Clear();
        done += batch.Length;
        Console.WriteLine($"Simulated {done}/{ids.Count} source costs");
    }
    return new Snapshot(await memory.VehiclePetrolCosts.AsNoTracking().OrderBy(c => c.Id).ToListAsync(),
        await memory.RiderPetrolCosts.AsNoTracking().OrderBy(c => c.Id).ToListAsync(), baseline.Statuses, copy.Vehicles, copy.EmployeeIqamas);
}

static string PlateKey(string plate) => (new string(plate.Where(char.IsLetter).ToArray()) + new string(plate.Where(char.IsDigit).ToArray())).ToUpperInvariant();
static string Fingerprint(Snapshot snapshot, JsonSerializerOptions json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, json))));

static void Validate(Snapshot before, Snapshot after)
{
    if (before.Costs.Sum(c => c.Cost) != after.Costs.Sum(c => c.Cost)) throw new InvalidOperationException("Source total changed.");
    var allocations = after.Allocations.ToLookup(r => r.VehiclePetrolCostId);
    if (after.Costs.Any(c => allocations[c.Id].Sum(r => r.Cost) != c.Cost || !allocations[c.Id].Any()))
        throw new InvalidOperationException("Allocation conservation failed.");
    if (after.Allocations.Any(r => r.Cost < 0)) throw new InvalidOperationException("Negative allocation.");
    var sources = after.Costs.ToDictionary(c => c.Id);
    if (after.Allocations.Any(r => !sources.TryGetValue(r.VehiclePetrolCostId, out var c) || r.Date != c.Date || r.VehicleNumber != c.VehicleNumber))
        throw new InvalidOperationException("Allocation source/date/vehicle mismatch.");
    if (after.Costs.GroupBy(c => (c.VehicleNumber is null ? "p:" + PlateKey(c.PlateNumberE) : "v:" + c.VehicleNumber, c.Date)).Any(g => g.Count() > 1))
        throw new InvalidOperationException("Duplicate daily sources remain.");
    var manual = after.Allocations.Where(r => r.AttributionSource == PetrolAttributionSource.ManualOverride).ToDictionary(r => r.Id);
    foreach (var row in before.Allocations.Where(r => r.AttributionSource == PetrolAttributionSource.ManualOverride))
        if (!manual.TryGetValue(row.Id, out var kept) || row.Cost != kept.Cost || row.RiderIqamaNo != kept.RiderIqamaNo
            || row.Notes != kept.Notes || row.CreatedAt != kept.CreatedAt)
            throw new InvalidOperationException("A manual override changed or disappeared.");
    if (manual.Count != before.Allocations.Count(r => r.AttributionSource == PetrolAttributionSource.ManualOverride))
        throw new InvalidOperationException("Manual override count changed.");
    if (after.Costs.Any(c => c.IsAttributed != (!c.HasResolutionError && allocations[c.Id].All(r => r.RiderIqamaNo.HasValue))))
        throw new InvalidOperationException("Attribution flags do not agree with allocations.");
}

static void VerifyApplied(Snapshot before, Snapshot expected, Snapshot actual, JsonSerializerOptions json)
{
    Validate(before, actual);
    if (JsonSerializer.Serialize(expected.Costs, json) != JsonSerializer.Serialize(actual.Costs, json))
        throw new InvalidOperationException("Stored source costs differ from the preview.");
    // SQL decimal(18,2) normalizes decimal scale; datetime2 stores wall-clock
    // ticks without DateTime.Kind. Compare stored values rather than JSON formatting.
    string Key(RiderPetrolCost r) => JsonSerializer.Serialize(new { r.VehiclePetrolCostId, r.VehicleNumber, r.RiderIqamaNo,
        r.Date, Cost = r.Cost.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), r.AttributionSource,
        r.ResolvedFromStatusId, r.Notes, CreatedAtTicks = r.CreatedAt.Ticks }, json);
    if (!expected.Allocations.Select(Key).Order(StringComparer.Ordinal).SequenceEqual(actual.Allocations.Select(Key).Order(StringComparer.Ordinal)))
        throw new InvalidOperationException("Stored allocations differ from the preview.");
    if (JsonSerializer.Serialize(before.Statuses, json) != JsonSerializer.Serialize(actual.Statuses, json))
        throw new InvalidOperationException("Assignment history changed.");
}

static object Describe(Snapshot before, Snapshot after)
{
    var old = before.Allocations.ToLookup(r => r.VehiclePetrolCostId);
    var current = after.Allocations.ToLookup(r => r.VehiclePetrolCostId);
    return new { SourceRowsBefore = before.Costs.Count, SourceRowsAfter = after.Costs.Count,
        ConsolidatedExtraSources = before.Costs.Count - after.Costs.Count, SourceTotal = after.Costs.Sum(c => c.Cost),
        AllocationTotalBefore = before.Allocations.Sum(r => r.Cost), AllocationTotalAfter = after.Allocations.Sum(r => r.Cost),
        MissingAllocationBefore = before.Costs.Count(c => !old[c.Id].Any()), MissingAllocationAfter = after.Costs.Count(c => !current[c.Id].Any()),
        UnassignedRowsBefore = before.Allocations.Count(r => r.RiderIqamaNo == null), UnassignedRowsAfter = after.Allocations.Count(r => r.RiderIqamaNo == null),
        ManualOverridesPreserved = after.Allocations.Count(r => r.AttributionSource == PetrolAttributionSource.ManualOverride),
        UnresolvedPlateSources = after.Costs.Count(c => c.HasResolutionError),
        ChangedRiderCostSources = after.Costs.Count(c => !old[c.Id].GroupBy(r => r.RiderIqamaNo).ToDictionary(g => g.Key?.ToString() ?? "unassigned", g => g.Sum(r => r.Cost))
            .OrderBy(p => p.Key).SequenceEqual(current[c.Id].GroupBy(r => r.RiderIqamaNo).ToDictionary(g => g.Key?.ToString() ?? "unassigned", g => g.Sum(r => r.Cost)).OrderBy(p => p.Key))) };
}

static async Task Stage<T>(ApplicationDbcontext db, IEnumerable<T> entities, string tableName) where T : class
{
    var properties = db.Model.FindEntityType(typeof(T))!.GetProperties().Where(p => p.PropertyInfo != null).ToList();
    var table = new DataTable();
    foreach (var property in properties)
    {
        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        table.Columns.Add(property.Name, type == typeof(DateOnly) ? typeof(DateTime) : type.IsEnum ? typeof(int) : type);
    }
    foreach (var entity in entities)
        table.Rows.Add(properties.Select(p => { var value = p.PropertyInfo!.GetValue(entity);
            return value is DateOnly date ? date.ToDateTime(TimeOnly.MinValue) : value is Enum e ? Convert.ToInt32(e) : value ?? DBNull.Value; }).ToArray());
    using var bulk = new SqlBulkCopy((SqlConnection)db.Database.GetDbConnection(), SqlBulkCopyOptions.KeepIdentity,
        (SqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction()) { DestinationTableName = tableName, BulkCopyTimeout = 180 };
    foreach (var property in properties) bulk.ColumnMappings.Add(property.Name, property.Name);
    await bulk.WriteToServerAsync(table);
}

record VehicleIdentity(string VehicleNumber, string PlateNumberE);
record Snapshot(List<VehiclePetrolCost> Costs, List<RiderPetrolCost> Allocations, List<RiderVehicleStatus> Statuses,
    List<VehicleIdentity> Vehicles, List<long> EmployeeIqamas);
record RepairPlan(string BeforeHash, Snapshot Before, Snapshot After, object Summary);
