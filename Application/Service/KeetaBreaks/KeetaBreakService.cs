using System.Text.Json;
using System.Text.RegularExpressions;
using System.Data;
using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.KeetaBreaks;
using ClosedXML.Excel;
using Domain;
using Domain.Entities.Keeta;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Application.Service.KeetaBreaks;

public class KeetaBreakService(ApplicationDbcontext dbcontext) : IKeetaBreakService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex ShiftRegex = new(@"(?<start>\d{1,2}:\d{2})\s*(?:-|–|—|～|~)\s*(?<end>\d{1,2}:\d{2})", RegexOptions.Compiled);

    public async Task<Result<List<KeetaBreakConfigurationResponse>>> GetConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        var configurations = await dbcontext.KeetaBreakConfigurations.AsNoTracking()
            .Include(x => x.ShiftDefinitions).Include(x => x.ShiftPatterns)
            .OrderByDescending(x => x.EffectiveFrom).ToListAsync(cancellationToken);
        return Result.Success(configurations.Select(ToConfigurationResponse).ToList());
    }

    public async Task<Result> DeleteConfigurationVersionAsync(int version, CancellationToken cancellationToken = default)
    {
        var configuration = await dbcontext.KeetaBreakConfigurations
            .Include(x => x.ShiftDefinitions)
            .Include(x => x.ShiftPatterns)
            .SingleOrDefaultAsync(x => x.Version == version, cancellationToken);
        if (configuration is null) return Result.Failure(KeetaBreakErrors.NotFound);

        var batches = await dbcontext.KeetaBreakBatches
            .Where(x => x.ConfigurationId == configuration.Id)
            .ToListAsync(cancellationToken);
        var batchIds = batches.Select(x => x.Id).ToList();
        var riders = await dbcontext.KeetaBreakImportedRiders.Where(x => batchIds.Contains(x.BatchId)).ToListAsync(cancellationToken);
        var assignments = await dbcontext.KeetaBreakAssignments.Where(x => batchIds.Contains(x.BatchId)).ToListAsync(cancellationToken);

        await using var transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            dbcontext.KeetaBreakAssignments.RemoveRange(assignments);
            dbcontext.KeetaBreakImportedRiders.RemoveRange(riders);
            dbcontext.KeetaBreakBatches.RemoveRange(batches);
            dbcontext.KeetaBreakShiftPatterns.RemoveRange(configuration.ShiftPatterns);
            dbcontext.KeetaBreakShiftDefinitions.RemoveRange(configuration.ShiftDefinitions);
            dbcontext.KeetaBreakConfigurations.Remove(configuration);
            await dbcontext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure(KeetaBreakErrors.CannotDelete);
        }
        return Result.Success();
    }

    public async Task<Result<KeetaBreakConfigurationResponse>> CreateConfigurationAsync(CreateKeetaBreakConfigurationRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        if (request.EffectiveTo < request.EffectiveFrom || request.BreakPercentage is < 0 or > 100 || request.Shifts.Count == 0 || request.ShiftPatterns.Count == 0 || request.Shifts.Any(x => string.IsNullOrWhiteSpace(x.ShiftKey) || x.MinimumRiders < 0 || x.MaximumRiders < x.MinimumRiders) || request.Shifts.Select(x => NormalizeShiftKey(x.ShiftKey)).Distinct(StringComparer.Ordinal).Count() != request.Shifts.Count)
            return Result.Failure<KeetaBreakConfigurationResponse>(KeetaBreakErrors.InvalidRequest);
        var requestedEnd = request.EffectiveTo ?? DateOnly.MaxValue;
        var overlappingConfigurations = await dbcontext.KeetaBreakConfigurations
            .Where(x => x.IsActive && x.EffectiveFrom <= requestedEnd && (x.EffectiveTo == null || x.EffectiveTo >= request.EffectiveFrom))
            .ToListAsync(cancellationToken);
        if (overlappingConfigurations.Any(x => request.EffectiveFrom <= x.EffectiveFrom))
            return Result.Failure<KeetaBreakConfigurationResponse>(KeetaBreakErrors.InvalidRequest);
        foreach (var previous in overlappingConfigurations)
        {
            previous.EffectiveTo = request.EffectiveFrom.AddDays(-1);
            previous.IsActive = false;
        }
        var version = (await dbcontext.KeetaBreakConfigurations.MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var entity = new KeetaBreakConfiguration { Version = version, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, BreakPercentage = request.BreakPercentage, RoundingPolicy = request.RoundingPolicy, CreatedBy = actorId };
        entity.ShiftDefinitions = request.Shifts.Select(x => new KeetaBreakShiftDefinition { ShiftKey = NormalizeShiftKey(x.ShiftKey), StartTime = x.StartTime, EndTime = x.EndTime, MinimumRiders = x.MinimumRiders, MaximumRiders = x.MaximumRiders }).ToList();
        var validShiftKeys = entity.ShiftDefinitions.Select(x => x.ShiftKey).ToHashSet(StringComparer.Ordinal);
        if (request.ShiftPatterns.Any(x => x.RiderCount <= 0))
            return Result.Failure<KeetaBreakConfigurationResponse>(KeetaBreakErrors.InvalidRequest);
        var patterns = request.ShiftPatterns.Select(x => new { Shifts = ParseShifts(x.Periods), x.RiderCount }).ToList();
        if (patterns.Any(x => x.Shifts.Count == 0 || x.Shifts.Any(s => !validShiftKeys.Contains(s))))
            return Result.Failure<KeetaBreakConfigurationResponse>(KeetaBreakErrors.InvalidRequest);
        entity.ShiftPatterns = patterns
            .GroupBy(x => ToPatternKey(x.Shifts), StringComparer.Ordinal)
            .Select(x => new KeetaBreakShiftPattern { PatternKey = x.Key, ShiftKeysJson = JsonSerializer.Serialize(x.First().Shifts, JsonOptions), RiderCount = x.Sum(y => y.RiderCount) })
            .ToList();
        var totalByShift = validShiftKeys.ToDictionary(key => key, key => entity.ShiftPatterns.Where(p => DeserializeShifts(p.ShiftKeysJson).Contains(key, StringComparer.Ordinal)).Sum(p => p.RiderCount), StringComparer.Ordinal);
        if (entity.ShiftDefinitions.Any(shift => totalByShift[shift.ShiftKey] < shift.MinimumRiders || totalByShift[shift.ShiftKey] > shift.MaximumRiders))
            return Result.Failure<KeetaBreakConfigurationResponse>(KeetaBreakErrors.InvalidRequest);
        dbcontext.KeetaBreakConfigurations.Add(entity);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToConfigurationResponse(entity));
    }

    public async Task<Result<KeetaBreakCapacityPlanResponse>> CreateCapacityPlanAsync(CreateKeetaBreakCapacityPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (request.PeriodStart > request.PeriodEnd)
            return Result.Failure<KeetaBreakCapacityPlanResponse>(KeetaBreakErrors.InvalidRequest);

        var configuration = request.ConfigurationId.HasValue
            ? await dbcontext.KeetaBreakConfigurations.AsNoTracking().Include(x => x.ShiftDefinitions).Include(x => x.ShiftPatterns).SingleOrDefaultAsync(x => x.Id == request.ConfigurationId.Value, cancellationToken)
            : await dbcontext.KeetaBreakConfigurations.AsNoTracking().Include(x => x.ShiftDefinitions).Include(x => x.ShiftPatterns).SingleOrDefaultAsync(x => x.IsActive && x.EffectiveFrom <= request.PeriodStart && (x.EffectiveTo == null || x.EffectiveTo >= request.PeriodEnd), cancellationToken);
        if (configuration is null)
            return Result.Failure<KeetaBreakCapacityPlanResponse>(KeetaBreakErrors.NoConfiguration);

        if (configuration.EffectiveFrom > request.PeriodStart || configuration.EffectiveTo < request.PeriodEnd)
            return Result.Failure<KeetaBreakCapacityPlanResponse>(KeetaBreakErrors.NoConfiguration);

        var definitions = configuration.ShiftDefinitions.ToDictionary(x => x.ShiftKey, StringComparer.Ordinal);
        var totalByShift = definitions.Keys.ToDictionary(key => key, key => configuration.ShiftPatterns.Where(p => DeserializeShifts(p.ShiftKeysJson).Contains(key, StringComparer.Ordinal)).Sum(p => p.RiderCount), StringComparer.Ordinal);
        var shiftTotals = configuration.ShiftDefinitions.OrderBy(x => x.StartTime).Select(shift =>
        {
            var total = totalByShift[shift.ShiftKey];
            var byPercentage = CalculatePercentageLimit(total, configuration.BreakPercentage, configuration.RoundingPolicy);
            var byMinimum = Math.Max(0, total - shift.MinimumRiders);
            var effective = Math.Min(byPercentage, byMinimum);
            return new KeetaBreakShiftTotalResponse(shift.ShiftKey, total, shift.MinimumRiders, shift.MaximumRiders, byPercentage, byMinimum, effective, total >= shift.MinimumRiders && total <= shift.MaximumRiders ? "سليم" : "خارج الحدود");
        }).ToList();
        var dates = KeetaBreakScheduler.Dates(request.PeriodStart, request.PeriodEnd).Select(date =>
        {
            var eligible = KeetaBreakScheduler.IsEligible(date);
            var patterns = configuration.ShiftPatterns.OrderBy(x => x.PatternKey).Select(pattern =>
            {
                var shiftKeys = DeserializeShifts(pattern.ShiftKeysJson);
                var limits = shiftKeys.Select(key => CalculateBreakLimit(definitions[key], totalByShift[key], configuration.BreakPercentage, configuration.RoundingPolicy)).ToArray();
                var capacity = eligible && limits.Length > 0 ? limits.Min() : 0;
                var reason = !eligible ? GetProhibitionReason(date) : capacity == 0 ? "أحد الشفتات في التركيبة لا يسمح بأي راحة وفق النسبة والحد الأدنى" : null;
                return new KeetaBreakPatternCapacityResponse(pattern.Id, pattern.PatternKey, shiftKeys, pattern.RiderCount, capacity, capacity > 0 ? "متاح" : "غير متاح", reason);
            }).ToList();
            return new KeetaBreakCapacityDateResponse(date, GetArabicDayName(date.DayOfWeek), eligible, eligible ? null : GetProhibitionReason(date), patterns);
        }).ToList();

        return Result.Success(new KeetaBreakCapacityPlanResponse(configuration.Id, configuration.Version, request.PeriodStart, request.PeriodEnd, configuration.BreakPercentage, configuration.RoundingPolicy, shiftTotals, dates));
    }

    public async Task<Result<KeetaBreakBatchResponse>> ImportAsync(DateOnly periodStart, DateOnly periodEnd, string fileName, Stream file, string actorId, CancellationToken cancellationToken = default)
    {
        if (periodStart > periodEnd) return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.InvalidRequest);
        var configuration = await FindConfigurationAsync(periodStart, periodEnd, cancellationToken);
        if (configuration is null) return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.NoConfiguration);
        if (await dbcontext.KeetaBreakBatches.AnyAsync(x => x.Status != KeetaBreakBatchStatus.Superseded && x.PeriodStart == periodStart && x.PeriodEnd == periodEnd, cancellationToken)) return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.DuplicatePeriod);
        List<KeetaBreakImportedRider> riders;
        try { riders = ParseWorkbook(file, configuration); }
        catch (Exception) { return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.InvalidFile); }
        if (riders.Count == 0) return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.InvalidRequest);
        var duplicateIds = riders.GroupBy(x => x.RiderIdentifier, StringComparer.Ordinal).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var rider in riders.Where(x => duplicateIds.Contains(x.RiderIdentifier))) rider.ValidationError = "معرّف الراكب مكرر في ملف Excel";
        var batch = new KeetaBreakBatch { ConfigurationId = configuration.Id, PeriodStart = periodStart, PeriodEnd = periodEnd, SourceFileName = Path.GetFileName(fileName), ImportedBy = actorId, Riders = riders };
        dbcontext.KeetaBreakBatches.Add(batch);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return await BuildResponseAsync(batch.Id, cancellationToken);
    }

    public async Task<Result<KeetaBreakBatchResponse>> GetBatchAsync(Guid id, CancellationToken cancellationToken = default) => await BuildResponseAsync(id, cancellationToken);

    public async Task<Result<KeetaBreakBatchResponse>> GenerateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var batch = await LoadBatchAsync(id, cancellationToken);
        if (batch is null) return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.NotFound);
        if (batch.Status is KeetaBreakBatchStatus.Confirmed or KeetaBreakBatchStatus.Superseded) return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.InvalidState);
        if (batch.Riders.Any(x => x.ValidationError is not null)) return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.InvalidRequest);
        var result = await ComputeScheduleAsync(batch, cancellationToken);
        batch.Assignments.Clear();
        foreach (var item in result.Assignments) batch.Assignments.Add(new KeetaBreakAssignment { RiderIdentifier = item.RiderIdentifier, BreakDate = item.Date, AssignedShiftsJson = JsonSerializer.Serialize(item.Shifts, JsonOptions), Reason = null });
        batch.Status = KeetaBreakBatchStatus.Draft;
        await dbcontext.SaveChangesAsync(cancellationToken);
        return await BuildResponseAsync(id, cancellationToken);
    }

    public async Task<Result<KeetaBreakBatchResponse>> AddManualAssignmentAsync(Guid id, ManualKeetaBreakAssignmentRequest request, CancellationToken cancellationToken = default)
    {
        var batch = await LoadBatchAsync(id, cancellationToken);
        if (batch is null) return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.NotFound);
        if (batch.Status != KeetaBreakBatchStatus.Draft || request.BreakDate < batch.PeriodStart || request.BreakDate > batch.PeriodEnd) return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.InvalidState);
        var rider = batch.Riders.SingleOrDefault(x => x.RiderIdentifier == request.RiderIdentifier && x.ValidationError is null);
        if (rider is null || batch.Assignments.Any(x => x.RiderIdentifier == request.RiderIdentifier && x.BreakDate == request.BreakDate && x.Status == KeetaBreakAssignmentStatus.Planned)) return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.InvalidRequest);
        var original = batch.Assignments.ToList();
        batch.Assignments.Add(new KeetaBreakAssignment { RiderIdentifier = rider.RiderIdentifier, BreakDate = request.BreakDate, AssignedShiftsJson = rider.ShiftsJson });
        if (!await IsDraftValidAsync(batch, cancellationToken)) { batch.Assignments.Remove(batch.Assignments.Last()); return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.ValidationFailed); }
        await dbcontext.SaveChangesAsync(cancellationToken);
        return await BuildResponseAsync(id, cancellationToken);
    }

    public async Task<Result<KeetaBreakBatchResponse>> RemoveAssignmentAsync(Guid id, Guid assignmentId, CancellationToken cancellationToken = default)
    {
        var assignment = await dbcontext.KeetaBreakAssignments.Include(x => x.Batch).SingleOrDefaultAsync(x => x.Id == assignmentId && x.BatchId == id, cancellationToken);
        if (assignment is null) return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.NotFound);
        if (assignment.Batch.Status != KeetaBreakBatchStatus.Draft || assignment.Status != KeetaBreakAssignmentStatus.Planned) return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.InvalidState);
        dbcontext.KeetaBreakAssignments.Remove(assignment);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return await BuildResponseAsync(id, cancellationToken);
    }

    public async Task<Result<KeetaBreakBatchResponse>> ConfirmAsync(Guid id, ConfirmKeetaBreakBatchRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var batch = await LoadBatchAsync(id, cancellationToken);
        if (batch is null) return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.NotFound);
        if (batch.Status != KeetaBreakBatchStatus.Draft || !batch.RowVersion.SequenceEqual(request.RowVersion)) return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.ConcurrentUpdate);
        await using var transaction = await dbcontext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            if (!await IsDraftValidAsync(batch, cancellationToken)) return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.ValidationFailed);
            foreach (var assignment in batch.Assignments.Where(x => x.Status == KeetaBreakAssignmentStatus.Planned)) assignment.Status = KeetaBreakAssignmentStatus.Confirmed;
            batch.Status = KeetaBreakBatchStatus.Confirmed; batch.ConfirmedAt = DateTime.UtcNow.AddHours(3); batch.ConfirmedBy = actorId;
            await dbcontext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { await transaction.RollbackAsync(cancellationToken); return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.ConcurrentUpdate); }
        catch (DbUpdateException) { await transaction.RollbackAsync(cancellationToken); return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.ValidationFailed); }
        return await BuildResponseAsync(id, cancellationToken);
    }

    private async Task<ScheduleResult> ComputeScheduleAsync(KeetaBreakBatch batch, CancellationToken ct)
    {
        var existing = await dbcontext.KeetaBreakAssignments.AsNoTracking().Where(x => x.Status == KeetaBreakAssignmentStatus.Confirmed && x.BreakDate >= batch.PeriodStart.AddMonths(-1) && x.BreakDate <= batch.PeriodEnd.AddMonths(1)).Select(x => new ExistingBreak(x.RiderIdentifier, x.BreakDate)).ToListAsync(ct);
        var riders = batch.Riders.Where(x => x.ValidationError is null).Select(x => new SchedulerRider(x.RiderIdentifier, x.RiderName, x.HousingGroup, DeserializeShifts(x.ShiftsJson))).ToList();
        var shifts = batch.Configuration.ShiftDefinitions.Select(x => new SchedulerShift(x.ShiftKey, x.MinimumRiders, x.MaximumRiders)).ToList();
        return new KeetaBreakScheduler().Schedule(batch.PeriodStart, batch.PeriodEnd, riders, shifts, batch.Configuration.BreakPercentage, batch.Configuration.RoundingPolicy, existing);
    }

    private async Task<bool> IsDraftValidAsync(KeetaBreakBatch batch, CancellationToken ct)
    {
        var draft = batch.Assignments.Where(x => x.Status == KeetaBreakAssignmentStatus.Planned).ToArray();
        if (draft.GroupBy(x => (x.RiderIdentifier, x.BreakDate)).Any(x => x.Count() > 1)) return false;
        var riderById = batch.Riders.Where(x => x.ValidationError is null).ToDictionary(x => x.RiderIdentifier, StringComparer.Ordinal);
        if (draft.Any(x => !riderById.ContainsKey(x.RiderIdentifier) || x.BreakDate < batch.PeriodStart || x.BreakDate > batch.PeriodEnd || !KeetaBreakScheduler.IsEligible(x.BreakDate))) return false;
        var existing = await dbcontext.KeetaBreakAssignments.AsNoTracking().Where(x => x.Status == KeetaBreakAssignmentStatus.Confirmed && x.BatchId != batch.Id && x.BreakDate >= batch.PeriodStart.AddMonths(-1) && x.BreakDate <= batch.PeriodEnd.AddMonths(1)).ToListAsync(ct);
        if (draft.Any(x => existing.Any(e => e.RiderIdentifier == x.RiderIdentifier && e.BreakDate == x.BreakDate))) return false;
        foreach (var month in draft.Concat(existing.Select(x => new KeetaBreakAssignment { RiderIdentifier = x.RiderIdentifier, BreakDate = x.BreakDate })).GroupBy(x => (x.RiderIdentifier, x.BreakDate.Year, x.BreakDate.Month))) if (month.Count() > 3) return false;
        foreach (var date in KeetaBreakScheduler.Dates(batch.PeriodStart, batch.PeriodEnd))
        foreach (var definition in batch.Configuration.ShiftDefinitions)
        {
            var assigned = batch.Riders.Count(r => r.ValidationError is null && DeserializeShifts(r.ShiftsJson).Contains(definition.ShiftKey, StringComparer.Ordinal));
            var usedExisting = existing.Count(x => x.BreakDate == date && riderById.TryGetValue(x.RiderIdentifier, out var r) && DeserializeShifts(r.ShiftsJson).Contains(definition.ShiftKey, StringComparer.Ordinal));
            var usedDraft = draft.Count(x => x.BreakDate == date && DeserializeShifts(riderById[x.RiderIdentifier].ShiftsJson).Contains(definition.ShiftKey, StringComparer.Ordinal));
            var percentage = batch.Configuration.RoundingPolicy switch { KeetaBreakRoundingPolicy.Ceiling => (int)Math.Ceiling(assigned * batch.Configuration.BreakPercentage / 100m), KeetaBreakRoundingPolicy.Nearest => (int)Math.Round(assigned * batch.Configuration.BreakPercentage / 100m, MidpointRounding.AwayFromZero), _ => (int)Math.Floor(assigned * batch.Configuration.BreakPercentage / 100m) };
            if (usedExisting + usedDraft > Math.Min(percentage, Math.Max(0, assigned - definition.MinimumRiders))) return false;
        }
        return true;
    }

    private async Task<Result<KeetaBreakBatchResponse>> BuildResponseAsync(Guid id, CancellationToken ct)
    {
        var batch = await LoadBatchAsync(id, ct);
        if (batch is null) return Result.Failure<KeetaBreakBatchResponse>(KeetaBreakErrors.NotFound);
        var existing = await dbcontext.KeetaBreakAssignments.AsNoTracking().Where(x => x.Status == KeetaBreakAssignmentStatus.Confirmed && x.BatchId != id && x.BreakDate >= batch.PeriodStart.AddMonths(-1) && x.BreakDate <= batch.PeriodEnd.AddMonths(1)).Select(x => new ExistingBreak(x.RiderIdentifier, x.BreakDate)).ToListAsync(ct);
        var schedule = batch.Status == KeetaBreakBatchStatus.Imported ? null : await ComputeScheduleAsync(batch, ct);
        var assignments = batch.Assignments.Where(x => x.Status != KeetaBreakAssignmentStatus.Removed).OrderBy(x => x.BreakDate).ThenBy(x => x.RiderIdentifier).ToList();
        var results = batch.Riders.Select(r =>
        {
            var dates = assignments.Where(a => a.RiderIdentifier == r.RiderIdentifier).Select(a => a.BreakDate).OrderBy(x => x).ToList();
            var before = existing.Count(x => x.RiderIdentifier == r.RiderIdentifier && x.Date.Year == batch.PeriodStart.Year && x.Date.Month == batch.PeriodStart.Month);
            var reason = r.ValidationError ?? schedule?.Rejections.FirstOrDefault(x => x.RiderIdentifier == r.RiderIdentifier)?.Reason;
            return new KeetaBreakRiderResultResponse(r.RiderIdentifier, r.RiderName, r.HousingGroup, DeserializeShifts(r.ShiftsJson).ToList(), dates, before, dates.Count, before + dates.Count, dates.Count > 0 ? "تم تحديد يوم الراحة" : reason ?? "لا توجد أيام متاحة", dates.Count > 0 ? null : reason);
        }).ToList();
        var capacities = schedule?.Capacities ?? [];
        return Result.Success(new KeetaBreakBatchResponse(batch.Id, batch.PeriodStart, batch.PeriodEnd, batch.Status, batch.ConfigurationId, batch.SourceFileName,
            KeetaBreakScheduler.Dates(batch.PeriodStart, batch.PeriodEnd).Select(d => new KeetaBreakDateSummaryResponse(d, KeetaBreakScheduler.IsEligible(d), KeetaBreakScheduler.IsEligible(d) ? null : "تاريخ محظور حسب قواعد الراحات")).ToList(),
            batch.Riders.Select(r => new KeetaBreakImportedRiderResponse(r.RiderNumber, r.RiderIdentifier, r.RiderName, r.HousingGroup, DeserializeShifts(r.ShiftsJson).ToList(), r.Notes, r.ValidationError)).ToList(),
            assignments.Select(a => new KeetaBreakAssignmentResponse(a.Id, a.RiderIdentifier, a.BreakDate, DeserializeShifts(a.AssignedShiftsJson).ToList(), a.Status, a.Reason)).ToList(), results,
            capacities.Select(x => new KeetaBreakShiftSummaryResponse(x.Date, x.Shift, x.AssignedRiders, batch.Configuration.ShiftDefinitions.Single(s => s.ShiftKey == x.Shift).MinimumRiders, batch.Configuration.ShiftDefinitions.Single(s => s.ShiftKey == x.Shift).MaximumRiders, x.ExistingBreaks, x.PlannedBreaks, x.ExistingBreaks + x.PlannedBreaks, x.Limit, Math.Max(0, x.Limit - x.ExistingBreaks - x.PlannedBreaks), x.ActiveRiders, x.ActiveRiders >= batch.Configuration.ShiftDefinitions.Single(s => s.ShiftKey == x.Shift).MinimumRiders ? "سليم" : "تحذير")).ToList()));
    }

    private Task<KeetaBreakBatch?> LoadBatchAsync(Guid id, CancellationToken ct) => dbcontext.KeetaBreakBatches.Include(x => x.Configuration).ThenInclude(x => x.ShiftDefinitions).Include(x => x.Riders).Include(x => x.Assignments).SingleOrDefaultAsync(x => x.Id == id, ct);
    private Task<KeetaBreakConfiguration?> FindConfigurationAsync(DateOnly start, DateOnly end, CancellationToken ct) => dbcontext.KeetaBreakConfigurations.Include(x => x.ShiftDefinitions).Include(x => x.ShiftPatterns).SingleOrDefaultAsync(x => x.IsActive && x.EffectiveFrom <= start && (x.EffectiveTo == null || x.EffectiveTo >= end), ct);
    private static KeetaBreakConfigurationResponse ToConfigurationResponse(KeetaBreakConfiguration x) => new(x.Id, x.Version, x.EffectiveFrom, x.EffectiveTo, x.BreakPercentage, x.RoundingPolicy, x.IsActive, x.ShiftDefinitions.OrderBy(s => s.StartTime).Select(s => new KeetaBreakShiftDefinitionResponse(s.ShiftKey, s.StartTime, s.EndTime, s.MinimumRiders, s.MaximumRiders)).ToList(), x.ShiftPatterns.OrderBy(p => p.PatternKey).Select(p => new KeetaBreakShiftPatternResponse(p.Id, p.PatternKey, DeserializeShifts(p.ShiftKeysJson), p.RiderCount)).ToList());
    private static List<string> DeserializeShifts(string json) => JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
    private static string NormalizeShiftKey(string value) => value.Replace("–", "-").Replace("—", "-").Replace("～", "-").Replace("~", "-").Replace(" ", string.Empty).Trim();

    private static List<KeetaBreakImportedRider> ParseWorkbook(
        Stream stream,
        KeetaBreakConfiguration configuration)
    {
        using var workbook = new XLWorkbook(stream);

        var sheet = workbook.Worksheets.First();
        var headerRow = sheet.FirstRowUsed()
            ?? throw new InvalidDataException("ملف Excel فارغ.");

        var columns = headerRow
            .CellsUsed()
            .Select(cell => new
            {
                Header = NormalizeHeader(cell.GetString()),
                ColumnNumber = cell.Address.ColumnNumber
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Header))
            .GroupBy(x => x.Header, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().ColumnNumber,
                StringComparer.OrdinalIgnoreCase);

        int Find(params string[] names)
        {
            foreach (var name in names)
            {
                var normalizedName = NormalizeHeader(name);

                if (columns.TryGetValue(normalizedName, out var columnNumber))
                {
                    return columnNumber;
                }
            }

            return 0;
        }

        int FindStartingWith(params string[] names)
        {
            var normalizedNames = names
                .Select(NormalizeHeader)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            foreach (var column in columns)
            {
                if (normalizedNames.Any(name =>
                        column.Key.StartsWith(
                            name,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    return column.Value;
                }
            }

            return 0;
        }

        var numberCol = Find(
            "#",
            "ridernumber",
            "رقمالراكب",
            "رقمالسائق");

        var identifierCol = Find(
            "المعرف",
            "rideridentifier",
            "معرفالسائق",
            "معرفالراكب",
            "riderid",
            "id");

        var nameCol = Find(
            "الاسم",
            "ridername",
            "اسمالراكب",
            "اسمالسائق",
            "name");

        var housingCol = Find(
            "السكن",
            "housing",
            "group",
            "المجموعة");

        var shiftsCol = FindStartingWith(
            "الشفتات",
            "الشفتات من يوم",
            "shifts",
            "shift",
            "الوردية");

        var notesCol = Find(
            "ملاحظات",
            "notes");

        if (identifierCol == 0)
        {
            throw new InvalidDataException(
                "لم يتم العثور على عمود المعرف في ملف Excel.");
        }

        if (shiftsCol == 0)
        {
            throw new InvalidDataException(
                "لم يتم العثور على عمود الشفتات في ملف Excel.");
        }

        var validKeys = configuration.ShiftDefinitions
            .Select(shift => shift.ShiftKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return sheet
            .RowsUsed()
            .Where(row => row.RowNumber() > headerRow.RowNumber())
            .Where(row => !row.IsEmpty())
            .Select(row =>
            {
                var identifier = row
                    .Cell(identifierCol)
                    .GetString()
                    .Trim();

                var riderNumber = numberCol == 0
                    ? string.Empty
                    : row.Cell(numberCol).GetString().Trim();

                var shiftsText = row
                    .Cell(shiftsCol)
                    .GetString()
                    .Trim();

                var shifts = ParseShifts(shiftsText);

                string? error = null;

                if (string.IsNullOrWhiteSpace(identifier))
                {
                    error = "معرّف الراكب مطلوب";
                }
                else if (shifts.Count == 0)
                {
                    error = "بيانات الشفت مطلوبة";
                }
                else if (shifts.Any(shift => !validKeys.Contains(shift)))
                {
                    error = "بيانات الشفت غير صحيحة";
                }

                return new KeetaBreakImportedRider
                {
                    RiderNumber = riderNumber,
                    RiderIdentifier = identifier,

                    RiderName = nameCol == 0
                        ? string.Empty
                        : row.Cell(nameCol).GetString().Trim(),

                    HousingGroup = housingCol == 0
                        ? null
                        : row.Cell(housingCol).GetString().Trim(),

                    Notes = notesCol == 0
                        ? null
                        : row.Cell(notesCol).GetString().Trim(),

                    ShiftsJson = JsonSerializer.Serialize(shifts, JsonOptions),
                    ValidationError = error
                };
            })
            .ToList();
    }
    private static List<string> ParseShifts(string value) => ShiftRegex.Matches(value).Select(x => NormalizeShiftKey($"{x.Groups["start"].Value}-{x.Groups["end"].Value}")).Distinct(StringComparer.Ordinal).ToList();
    private static string NormalizeHeader(string value) => Regex.Replace(value ?? "", "[\\s_\\-–—]", "").Trim().ToLowerInvariant();
    private static string ToPatternKey(IEnumerable<string> shifts) => string.Join(" + ", shifts);
    private static int CalculateBreakLimit(KeetaBreakShiftDefinition shift, int assignedRiders, decimal percentage, KeetaBreakRoundingPolicy roundingPolicy)
    {
        var byPercentage = CalculatePercentageLimit(assignedRiders, percentage, roundingPolicy);
        return Math.Min(byPercentage, Math.Max(0, assignedRiders - shift.MinimumRiders));
    }
    private static int CalculatePercentageLimit(int assignedRiders, decimal percentage, KeetaBreakRoundingPolicy roundingPolicy)
    {
        return roundingPolicy switch
        {
            KeetaBreakRoundingPolicy.Ceiling => (int)Math.Ceiling(assignedRiders * percentage / 100m),
            KeetaBreakRoundingPolicy.Nearest => (int)Math.Round(assignedRiders * percentage / 100m, MidpointRounding.AwayFromZero),
            _ => (int)Math.Floor(assignedRiders * percentage / 100m)
        };
    }
    private static string GetProhibitionReason(DateOnly date)
    {
        if (date.Day <= 3) return "أول ثلاثة أيام من الشهر محظورة";
        if (date.Day > DateTime.DaysInMonth(date.Year, date.Month) - 3) return "آخر ثلاثة أيام من الشهر محظورة";
        return date.DayOfWeek switch { DayOfWeek.Thursday => "يوم الخميس محظور", DayOfWeek.Friday => "يوم الجمعة محظور", DayOfWeek.Saturday => "يوم السبت محظور", _ => "التاريخ محظور" };
    }
    private static string GetArabicDayName(DayOfWeek day) => day switch { DayOfWeek.Sunday => "الأحد", DayOfWeek.Monday => "الاثنين", DayOfWeek.Tuesday => "الثلاثاء", DayOfWeek.Wednesday => "الأربعاء", DayOfWeek.Thursday => "الخميس", DayOfWeek.Friday => "الجمعة", _ => "السبت" };
}
