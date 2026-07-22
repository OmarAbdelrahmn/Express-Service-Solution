using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Common;
using Application.Contracts.RiderPayroll;
using Application.Contracts.AccountingFiles;
using Application.Service.AccountingFiles;
using Application.Service.AccountingPosting;
using Application.Service.Compensation;
using Application.Service.FinancialAccess;
using Domain;
using Domain.Entities.AccountingCore;
using Domain.Entities.AccountingPlatform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ClosedXML.Excel;

namespace Application.Service.RiderPayroll;

public class RiderPayrollService(
    ApplicationDbcontext dbcontext,
    IFinancialAccessService financialAccessService,
    IAccountingPostingService accountingPostingService,
    IAccountingFileService accountingFileService) : IRiderPayrollService
{
    public async Task<Result<PagedResponse<RiderPayrollRunResponse>>> GetRunsAsync(
        PaginationRequest pagination,
        int legalEntityId,
        RiderPayrollStatus? status,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? search,
        string? sortBy,
        string? sortDirection,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var access = await financialAccessService.EnsurePermissionAsync(actorId, legalEntityId, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<PagedResponse<RiderPayrollRunResponse>>(access.Error);
        if (toDate < fromDate) return Result.Failure<PagedResponse<RiderPayrollRunResponse>>(AccountingPlatformErrors.InvalidRequest);

        var query = dbcontext.RiderPayrollRuns.AsNoTracking().Where(x => x.LegalEntityId == legalEntityId);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (fromDate.HasValue) query = query.Where(x => x.PeriodEnd >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(x => x.PeriodStart <= toDate.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToUpperInvariant();
            query = query.Where(x => x.RunNumber.ToUpper().Contains(normalizedSearch));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = pagination.NormalizedPageNumber;
        var pageSize = pagination.NormalizedPageSize;
        var runs = await ApplyRunOrdering(query, sortBy, sortDirection)
            .Include(x => x.Lines)
            .ThenInclude(x => x.Components)
            .AsSplitQuery()
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = new List<RiderPayrollRunResponse>(runs.Count);
        foreach (var run in runs) items.Add(await ToResponseAsync(run, cancellationToken));
        return Result.Success(new PagedResponse<RiderPayrollRunResponse>(items, pageNumber, pageSize, totalCount));
    }

    public async Task<Result<RiderPayrollRunResponse>> CreateRunAsync(CreateRiderPayrollRunRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var access = await financialAccessService.EnsurePermissionAsync(actorId, request.LegalEntityId, FinancialPermission.Prepare, cancellationToken);
        if (access.IsFailure) return Result.Failure<RiderPayrollRunResponse>(access.Error);
        if (request.PeriodEnd < request.PeriodStart ||
            !await dbcontext.LegalEntities.AnyAsync(x => x.Id == request.LegalEntityId && x.IsActive, cancellationToken) ||
            !await dbcontext.Currencies.AnyAsync(x => x.Code == request.CurrencyCode.Trim().ToUpper() && x.IsActive, cancellationToken))
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.InvalidRequest);
        if (await dbcontext.RiderPayrollRuns.AnyAsync(x => x.LegalEntityId == request.LegalEntityId && x.PeriodStart == request.PeriodStart && x.PeriodEnd == request.PeriodEnd, cancellationToken))
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.Duplicate);

        var run = new RiderPayrollRun
        {
            LegalEntityId = request.LegalEntityId,
            RunNumber = $"RPR-{request.PeriodEnd:yyyyMM}-{Guid.NewGuid():N}"[..25].ToUpperInvariant(),
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(),
            CreatedBy = actorId
        };
        dbcontext.RiderPayrollRuns.Add(run);
        await AppendAuditAsync(run.LegalEntityId, "RiderPayroll.RunCreated", actorId, new { run.Id, run.RunNumber, run.PeriodStart, run.PeriodEnd }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return await GetRunAsync(run.Id, actorId, cancellationToken);
    }

    public async Task<Result<RiderPayrollRunResponse>> CalculateAsync(Guid runId, CalculateRiderPayrollRunRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var run = await dbcontext.RiderPayrollRuns.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null) return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, run.LegalEntityId, FinancialPermission.Prepare, cancellationToken);
        if (access.IsFailure) return Result.Failure<RiderPayrollRunResponse>(access.Error);
        if (run.Status != RiderPayrollStatus.Draft || run.Lines.Count > 0 || !MatchesRowVersion(request.RowVersion, run.RowVersion))
            return Result.Failure<RiderPayrollRunResponse>(run.Status == RiderPayrollStatus.Draft ? AccountingPlatformErrors.ConcurrencyConflict : AccountingPlatformErrors.InvalidState);

        var facts = await dbcontext.PlatformNormalizedFacts.AsNoTracking()
            .Include(x => x.Override)
            .Include(x => x.PlatformImportBatch)
            .Where(x => x.LegalEntityId == run.LegalEntityId && x.RiderIqamaNo != null && x.IsResolved &&
                x.FactDate >= run.PeriodStart && x.FactDate <= run.PeriodEnd &&
                x.PlatformImportBatch.Status == PlatformImportStatus.Approved && x.PlatformImportBatch.SupersededByBatchId == null)
            .ToListAsync(cancellationToken);
        if (facts.Count == 0) return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.PayrollFactsMissing);

        var riderIds = facts.Select(x => x.RiderIqamaNo!.Value).Distinct().ToArray();
        var policies = await dbcontext.CompensationPolicyVersions.AsNoTracking().Include(x => x.Rules)
            .Where(x => x.LegalEntityId == run.LegalEntityId && x.Status == CompensationPolicyStatus.Active && x.EffectiveFrom <= run.PeriodEnd && (x.EffectiveTo == null || x.EffectiveTo >= run.PeriodStart))
            .ToListAsync(cancellationToken);
        var financialItems = await dbcontext.RiderFinancialItems.AsNoTracking().Include(x => x.ItemType).Include(x => x.Installments)
            .Where(x => x.LegalEntityId == run.LegalEntityId && riderIds.Contains(x.RiderIqamaNo) && x.Status == RiderFinancialItemStatus.Open && x.OutstandingAmount > 0 && x.EffectiveDate <= run.PeriodEnd && (x.DeductionStartDate == null || x.DeductionStartDate <= run.PeriodEnd))
            .ToListAsync(cancellationToken);
        var carries = await dbcontext.RiderPayrollCarryForwards.AsNoTracking()
            .Where(x => x.LegalEntityId == run.LegalEntityId && riderIds.Contains(x.RiderIqamaNo) && x.Status == RiderFinancialItemStatus.Open && x.OutstandingAmount > 0)
            .ToListAsync(cancellationToken);

        var missingPolicy = false;
        var invalidValidity = false;
        var lines = new List<RiderPayrollLine>();
        foreach (var riderGroup in facts.GroupBy(x => x.RiderIqamaNo!.Value).OrderBy(x => x.Key))
        {
            var line = new RiderPayrollLine { RiderIqamaNo = riderGroup.Key };
            var deductions = new List<DeductionCandidate>();
            foreach (var sourceGroup in riderGroup.GroupBy(x => new { x.PlatformAccountId, Category = (string.IsNullOrWhiteSpace(x.WorkerCategory) ? "Rider" : x.WorkerCategory).ToUpperInvariant() }))
            {
                var factDate = sourceGroup.Max(x => x.FactDate);
                var policy = policies.Where(x => x.PlatformAccountId == sourceGroup.Key.PlatformAccountId && x.EffectiveFrom <= factDate && (x.EffectiveTo == null || x.EffectiveTo >= factDate))
                    .OrderByDescending(x => string.Equals(x.WorkerCategory, sourceGroup.Key.Category, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(x => string.Equals(x.WorkerCategory, sourceGroup.Key.Category, StringComparison.OrdinalIgnoreCase) || string.Equals(x.WorkerCategory, "Rider", StringComparison.OrdinalIgnoreCase));
                if (policy is null) { missingPolicy = true; continue; }

                if (string.Equals(sourceGroup.Key.Category, "KeetaSegments", StringComparison.OrdinalIgnoreCase))
                {
                    var validity = sourceGroup.Where(x => string.Equals(x.MetricCode, "VALIDITY", StringComparison.OrdinalIgnoreCase)).Select(x => x.Override?.BooleanValue ?? x.BooleanValue).ToArray();
                    if (validity.Length == 0 || validity.Any(x => x != true)) { invalidValidity = true; continue; }
                }

                var metrics = sourceGroup.GroupBy(x => x.MetricCode, StringComparer.OrdinalIgnoreCase).ToDictionary(
                    x => x.Key,
                    x => x.Any(f => f.BooleanValue.HasValue || f.Override is not null)
                        ? (x.All(f => (f.Override?.BooleanValue ?? f.BooleanValue) == true) ? 1m : 0m)
                        : x.Sum(f => f.NumericValue ?? 0m),
                    StringComparer.OrdinalIgnoreCase);
                var simulation = CompensationService.Evaluate(policy, metrics);
                var batchIds = sourceGroup.Select(x => x.PlatformImportBatchId).Distinct().OrderBy(x => x).ToArray();
                foreach (var result in simulation.Components.Where(x => x.Selected))
                {
                    var rule = policy.Rules.Single(x => x.Id == result.RuleId);
                    var calculation = JsonSerializer.Serialize(new { sourceGroup.Key.Category, Metrics = metrics, SourceBatchIds = batchIds, PolicyVersionId = policy.Id, RuleId = rule.Id, result.Explanation });
                    if (result.ComponentType == CompensationComponentType.Deduction)
                    {
                        deductions.Add(new DeductionCandidate(RiderPayrollComponentSource.Policy, result.RuleCode, result.RuleName, Math.Abs(result.Amount), rule.Priority, sourceGroup.Key.PlatformAccountId, policy.Id, rule.Id, batchIds.Length == 1 ? batchIds[0] : null, null, null, calculation));
                    }
                    else if (result.ComponentType != CompensationComponentType.Informational && result.Amount > 0)
                    {
                        line.Components.Add(new RiderPayrollComponent
                        {
                            PlatformAccountId = sourceGroup.Key.PlatformAccountId, CompensationPolicyVersionId = policy.Id, CompensationRuleId = rule.Id,
                            SourceImportBatchId = batchIds.Length == 1 ? batchIds[0] : null, Source = RiderPayrollComponentSource.Policy,
                            ComponentType = result.ComponentType, ComponentCode = result.RuleCode, Description = result.RuleName,
                            Quantity = result.Quantity, Rate = result.Rate, Amount = result.Amount, CalculationJson = calculation, IsAutomatic = true
                        });
                        line.GrossEarnings += result.Amount;
                    }
                }
            }

            foreach (var item in financialItems.Where(x => x.RiderIqamaNo == line.RiderIqamaNo))
            {
                var due = DueAmount(item, run.PeriodEnd);
                if (due <= 0) continue;
                var calculation = JsonSerializer.Serialize(new { item.Id, item.Reference, item.OriginalAmount, item.OutstandingAmount, DueAmount = due, Installments = item.Installments.Where(x => x.DueDate <= run.PeriodEnd && !x.IsSettled).Select(x => new { x.Id, x.Sequence, x.ScheduledAmount, x.AppliedAmount }) });
                if (item.ItemType.Direction == RiderFinancialItemDirection.Earning)
                {
                    line.Components.Add(new RiderPayrollComponent { RiderFinancialItemId = item.Id, Source = RiderPayrollComponentSource.FinancialItem, ComponentType = CompensationComponentType.Allowance, ComponentCode = item.ItemType.Code, Description = item.Description, Quantity = 1, Rate = due, Amount = due, CalculationJson = calculation, IsAutomatic = true });
                    line.GrossEarnings += due;
                }
                else deductions.Add(new DeductionCandidate(RiderPayrollComponentSource.FinancialItem, item.ItemType.Code, item.Description, due, item.ItemType.Priority, null, null, null, null, item.Id, null, calculation));
            }
            foreach (var carry in carries.Where(x => x.RiderIqamaNo == line.RiderIqamaNo))
                deductions.Add(new DeductionCandidate(RiderPayrollComponentSource.CarryForward, carry.SourceCode, carry.Description, carry.OutstandingAmount, carry.Priority, null, null, null, null, null, carry.Id, JsonSerializer.Serialize(new { carry.Id, carry.OriginalAmount, carry.OutstandingAmount })));

            var remainingPay = line.GrossEarnings;
            foreach (var deduction in deductions.OrderBy(x => x.Priority).ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase))
            {
                var applied = Math.Min(remainingPay, deduction.RequestedAmount);
                if (applied > 0)
                {
                    line.Components.Add(new RiderPayrollComponent
                    {
                        PlatformAccountId = deduction.PlatformAccountId, CompensationPolicyVersionId = deduction.PolicyId, CompensationRuleId = deduction.RuleId,
                        SourceImportBatchId = deduction.SourceBatchId, RiderFinancialItemId = deduction.FinancialItemId, RiderPayrollCarryForwardId = deduction.CarryForwardId,
                        Source = deduction.Source, ComponentType = CompensationComponentType.Deduction, ComponentCode = deduction.Code, Description = deduction.Description,
                        Quantity = 1, Rate = applied, Amount = applied, CalculationJson = deduction.CalculationJson, IsAutomatic = true
                    });
                    line.AppliedDeductions += applied;
                    remainingPay -= applied;
                }
                var unapplied = deduction.RequestedAmount - applied;
                if (unapplied <= 0) continue;
                line.CarriedDeductions += unapplied;
                if (deduction.Source == RiderPayrollComponentSource.Policy)
                    line.Components.Add(new RiderPayrollComponent
                    {
                        PlatformAccountId = deduction.PlatformAccountId, CompensationPolicyVersionId = deduction.PolicyId, CompensationRuleId = deduction.RuleId,
                        SourceImportBatchId = deduction.SourceBatchId, Source = RiderPayrollComponentSource.CarryForward, ComponentType = CompensationComponentType.Informational,
                        ComponentCode = deduction.Code, Description = deduction.Description, Quantity = 1, Rate = unapplied, Amount = unapplied,
                        CalculationJson = JsonSerializer.Serialize(new { deduction.CalculationJson, UnappliedAmount = unapplied, deduction.Priority }), IsAutomatic = true
                    });
            }
            line.GrossEarnings = decimal.Round(line.GrossEarnings, 2, MidpointRounding.AwayFromZero);
            line.AppliedDeductions = decimal.Round(line.AppliedDeductions, 2, MidpointRounding.AwayFromZero);
            line.CarriedDeductions = decimal.Round(line.CarriedDeductions, 2, MidpointRounding.AwayFromZero);
            line.NetPay = Math.Max(0, decimal.Round(line.GrossEarnings - line.AppliedDeductions, 2, MidpointRounding.AwayFromZero));
            lines.Add(line);
        }

        if (missingPolicy) return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.PayrollPolicyMissing);
        if (invalidValidity) return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.PayrollValidityRequired);
        run.Lines = lines;
        RefreshTotals(run);
        run.Status = RiderPayrollStatus.Calculated;
        await AppendAuditAsync(run.LegalEntityId, "RiderPayroll.Calculated", actorId, new { run.Id, RiderCount = lines.Count, run.GrossEarnings, run.AppliedDeductions, run.CarriedDeductions, run.NetPay }, cancellationToken);
        try
        {
            await dbcontext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.ConcurrencyConflict);
        }
        return await GetRunAsync(run.Id, actorId, cancellationToken);
    }

    public async Task<Result<RiderPayrollRunResponse>> AddAdjustmentAsync(Guid runId, AddRiderPayrollAdjustmentRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var run = await dbcontext.RiderPayrollRuns.Include(x => x.Lines).ThenInclude(x => x.Components).SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null) return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, run.LegalEntityId, FinancialPermission.Prepare, cancellationToken);
        if (access.IsFailure) return Result.Failure<RiderPayrollRunResponse>(access.Error);
        var line = run.Lines.SingleOrDefault(x => x.RiderIqamaNo == request.RiderIqamaNo);
        if (run.Status != RiderPayrollStatus.Calculated || line is null || request.Amount == 0 || string.IsNullOrWhiteSpace(request.Reason) || (request.Amount < 0 && Math.Abs(request.Amount) > line.NetPay))
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.InvalidRequest);
        if (request.EvidenceFileId.HasValue && !await dbcontext.AccountingStoredFiles.AnyAsync(x => x.Id == request.EvidenceFileId && x.LegalEntityId == run.LegalEntityId, cancellationToken))
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.NotFound);

        var adjustment = new RiderPayrollAdjustment { RiderPayrollLineId = line.Id, Amount = request.Amount, Reason = request.Reason.Trim(), Notes = request.Notes?.Trim(), EvidenceFileId = request.EvidenceFileId, CreatedBy = actorId };
        dbcontext.RiderPayrollAdjustments.Add(adjustment);
        var amount = Math.Abs(request.Amount);
        line.Components.Add(new RiderPayrollComponent { Source = RiderPayrollComponentSource.Adjustment, ComponentType = request.Amount > 0 ? CompensationComponentType.Earning : CompensationComponentType.Deduction, ComponentCode = request.Amount > 0 ? "ADJUSTMENT_EARNING" : "ADJUSTMENT_DEDUCTION", Description = request.Reason.Trim(), Quantity = 1, Rate = amount, Amount = amount, CalculationJson = JsonSerializer.Serialize(new { adjustment.Id, request.Notes, request.EvidenceFileId, actorId }), IsAutomatic = false });
        if (request.Amount > 0) line.GrossEarnings += amount; else line.AppliedDeductions += amount;
        line.NetPay = line.GrossEarnings - line.AppliedDeductions;
        RefreshTotals(run);
        await AppendAuditAsync(run.LegalEntityId, "RiderPayroll.AdjustmentAdded", actorId, new { run.Id, line.RiderIqamaNo, request.Amount, Reason = request.Reason.Trim(), request.EvidenceFileId }, cancellationToken);
        try
        {
            await dbcontext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.ConcurrencyConflict);
        }
        return await GetRunAsync(run.Id, actorId, cancellationToken);
    }

    public async Task<Result<RiderPayrollRunResponse>> ApproveAsync(Guid runId, ApproveRiderPayrollRunRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var run = await dbcontext.RiderPayrollRuns.Include(x => x.Lines).ThenInclude(x => x.Components).SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null) return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, run.LegalEntityId, FinancialPermission.Approve, cancellationToken);
        if (access.IsFailure) return Result.Failure<RiderPayrollRunResponse>(access.Error);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.IdempotencyKeyRequired);
        var idempotencyKey = request.IdempotencyKey.Trim();
        var postingProfileCode = request.PostingProfileCode?.Trim();
        var correlationId = request.CorrelationId?.Trim();
        if (idempotencyKey.Length > 128 || string.IsNullOrWhiteSpace(postingProfileCode) || postingProfileCode.Length > 64 ||
            string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64)
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.InvalidRequest);
        var idempotencyPayload = JsonSerializer.Serialize(new
        {
            PostingProfileCode = postingProfileCode.ToUpperInvariant(),
            CorrelationId = correlationId,
            request.RowVersion
        });
        var isReplay = run.AccrualFinancialDocumentId.HasValue &&
            run.Status is not (RiderPayrollStatus.Draft or RiderPayrollStatus.Calculated or RiderPayrollStatus.Reversed) &&
            await dbcontext.FinancialDocuments.AsNoTracking().AnyAsync(x =>
                x.Id == run.AccrualFinancialDocumentId.Value &&
                x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (!isReplay && (run.Status != RiderPayrollStatus.Calculated || run.Lines.Any(x => x.IsHeld)))
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.InvalidState);
        if (!isReplay && !MatchesRowVersion(request.RowVersion, run.RowVersion))
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.ConcurrencyConflict);

        IDbContextTransaction? transaction = null;
        if (dbcontext.Database.IsRelational() && dbcontext.Database.CurrentTransaction is null)
            transaction = await dbcontext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var dimensions = await LoadDimensionContextAsync(run, cancellationToken);
            var itemTypes = await dbcontext.RiderFinancialItems.AsNoTracking().Include(x => x.ItemType).Where(x => x.LegalEntityId == run.LegalEntityId).ToDictionaryAsync(x => x.Id, x => x.ItemType, cancellationToken);
            var events = run.Lines.SelectMany(line => line.Components.Where(x => x.Amount > 0 && x.ComponentType != CompensationComponentType.Informational).Select(component =>
                new PostingEventAmount(PostingEventCode(component, itemTypes), component.Amount, $"{line.RiderIqamaNo}: {component.Description}", dimensions.For(line.RiderIqamaNo, component.PlatformAccountId)))).ToArray();
            if (events.Length == 0) return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.InvalidRequest);
            var posting = await accountingPostingService.PostAsync(new PostSourceDocumentRequest(
                 run.LegalEntityId, null, run.PeriodEnd, "RiderPayrollAccrual", run.RunNumber, postingProfileCode,
                 $"Rider payroll {run.PeriodStart:yyyy-MM-dd} to {run.PeriodEnd:yyyy-MM-dd}", run.CurrencyCode,
                 idempotencyKey, correlationId, AccountingModule.Payroll, events, idempotencyPayload), actorId, cancellationToken);
            if (posting.IsFailure)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                return Result.Failure<RiderPayrollRunResponse>(posting.Error);
            }
            if (isReplay)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return await GetRunAsync(run.Id, actorId, cancellationToken);
            }

            var itemIds = run.Lines.SelectMany(x => x.Components).Where(x => x.RiderFinancialItemId.HasValue && x.ComponentType != CompensationComponentType.Informational).Select(x => x.RiderFinancialItemId!.Value).Distinct().ToArray();
            var trackedItems = await dbcontext.RiderFinancialItems.Include(x => x.Installments).Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
            foreach (var component in run.Lines.SelectMany(x => x.Components).Where(x => x.RiderFinancialItemId.HasValue && x.ComponentType != CompensationComponentType.Informational))
                ApplyToFinancialItem(trackedItems[component.RiderFinancialItemId!.Value], component.Amount);

            var carryIds = run.Lines.SelectMany(x => x.Components).Where(x => x.RiderPayrollCarryForwardId.HasValue && x.ComponentType == CompensationComponentType.Deduction).Select(x => x.RiderPayrollCarryForwardId!.Value).Distinct().ToArray();
            var trackedCarries = await dbcontext.RiderPayrollCarryForwards.Where(x => carryIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
            foreach (var component in run.Lines.SelectMany(x => x.Components).Where(x => x.RiderPayrollCarryForwardId.HasValue && x.ComponentType == CompensationComponentType.Deduction))
            {
                var carry = trackedCarries[component.RiderPayrollCarryForwardId!.Value];
                carry.OutstandingAmount -= component.Amount;
                if (carry.OutstandingAmount <= 0) { carry.OutstandingAmount = 0; carry.Status = RiderFinancialItemStatus.Settled; }
            }
            foreach (var line in run.Lines)
            foreach (var component in line.Components.Where(x => x.Source == RiderPayrollComponentSource.CarryForward && x.ComponentType == CompensationComponentType.Informational && !x.RiderPayrollCarryForwardId.HasValue))
            {
                var carry = new RiderPayrollCarryForward { LegalEntityId = run.LegalEntityId, RiderIqamaNo = line.RiderIqamaNo, CreatedFromPayrollRunId = run.Id, SourceCode = component.ComponentCode, Description = component.Description, Priority = ReadPriority(component.CalculationJson), OriginalAmount = component.Amount, OutstandingAmount = component.Amount };
                dbcontext.RiderPayrollCarryForwards.Add(carry);
                component.RiderPayrollCarryForwardId = carry.Id;
            }
            run.AccrualFinancialDocumentId = posting.Value.Id;
            run.Status = RiderPayrollStatus.Approved;
            run.ApprovedBy = actorId;
            run.ApprovedAt = DateTime.UtcNow;
            await AppendAuditAsync(run.LegalEntityId, "RiderPayroll.Approved", actorId, new { run.Id, run.RunNumber, FinancialDocumentId = posting.Value.Id, run.NetPay }, cancellationToken);
            await dbcontext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.ConcurrencyConflict);
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
        return await GetRunAsync(run.Id, actorId, cancellationToken);
    }

    public async Task<Result<RiderPayrollRunResponse>> ReverseRunAsync(Guid runId, ReverseRiderPayrollRunRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var run = await dbcontext.RiderPayrollRuns.Include(x => x.Lines).ThenInclude(x => x.Components)
            .SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null) return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, run.LegalEntityId, FinancialPermission.Post, cancellationToken);
        if (access.IsFailure) return Result.Failure<RiderPayrollRunResponse>(access.Error);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.IdempotencyKeyRequired);
        var idempotencyKey = request.IdempotencyKey.Trim();
        var reason = request.Reason?.Trim();
        var correlationId = request.CorrelationId?.Trim();
        if (idempotencyKey.Length > 128 || string.IsNullOrWhiteSpace(reason) || reason.Length > 500 ||
            string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64)
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.InvalidRequest);
        var idempotencyPayload = JsonSerializer.Serialize(new
        {
            request.ReversalDate,
            Reason = reason,
            CorrelationId = correlationId,
            request.RowVersion
        });
        if (run.Status == RiderPayrollStatus.Reversed)
        {
            if (!run.AccrualFinancialDocumentId.HasValue)
                return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.InvalidState);
            var replay = await accountingPostingService.ReverseAsync(new ReverseSourceDocumentRequest(
                run.AccrualFinancialDocumentId.Value, request.ReversalDate, reason, idempotencyKey,
                correlationId, AccountingModule.Payroll, idempotencyPayload), actorId, cancellationToken);
            return replay.IsFailure
                ? Result.Failure<RiderPayrollRunResponse>(replay.Error)
                : await GetRunAsync(run.Id, actorId, cancellationToken);
        }
        if (!run.AccrualFinancialDocumentId.HasValue || run.Status is RiderPayrollStatus.Draft or RiderPayrollStatus.Calculated or RiderPayrollStatus.PartiallyPaid or RiderPayrollStatus.Paid)
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.InvalidState);
        if (!MatchesRowVersion(request.RowVersion, run.RowVersion))
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.ConcurrencyConflict);
        if (await dbcontext.RiderPaymentBatchLines.AnyAsync(x => x.RiderPayrollLine.RiderPayrollRunId == run.Id && x.IsConfirmed, cancellationToken))
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.InvalidState);

        IDbContextTransaction? transaction = null;
        if (dbcontext.Database.IsRelational() && dbcontext.Database.CurrentTransaction is null)
            transaction = await dbcontext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var reversal = await accountingPostingService.ReverseAsync(new ReverseSourceDocumentRequest(
                run.AccrualFinancialDocumentId.Value, request.ReversalDate, reason, idempotencyKey,
                correlationId, AccountingModule.Payroll, idempotencyPayload), actorId, cancellationToken);
            if (reversal.IsFailure)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                return Result.Failure<RiderPayrollRunResponse>(reversal.Error);
            }

            var itemComponents = run.Lines.SelectMany(x => x.Components)
                .Where(x => x.RiderFinancialItemId.HasValue && x.ComponentType != CompensationComponentType.Informational).ToArray();
            var itemIds = itemComponents.Select(x => x.RiderFinancialItemId!.Value).Distinct().ToArray();
            var items = await dbcontext.RiderFinancialItems.Include(x => x.Installments).Where(x => itemIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
            foreach (var component in itemComponents)
                RestoreFinancialItem(items[component.RiderFinancialItemId!.Value], component.Amount);

            var appliedCarryComponents = run.Lines.SelectMany(x => x.Components)
                .Where(x => x.RiderPayrollCarryForwardId.HasValue && x.ComponentType == CompensationComponentType.Deduction).ToArray();
            var carryIds = appliedCarryComponents.Select(x => x.RiderPayrollCarryForwardId!.Value).Distinct().ToArray();
            var carries = await dbcontext.RiderPayrollCarryForwards.Where(x => carryIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
            foreach (var component in appliedCarryComponents)
            {
                var carry = carries[component.RiderPayrollCarryForwardId!.Value];
                carry.OutstandingAmount = Math.Min(carry.OriginalAmount, carry.OutstandingAmount + component.Amount);
                carry.Status = RiderFinancialItemStatus.Open;
            }
            var createdCarries = await dbcontext.RiderPayrollCarryForwards.Where(x => x.CreatedFromPayrollRunId == run.Id).ToListAsync(cancellationToken);
            foreach (var carry in createdCarries)
            {
                carry.OutstandingAmount = 0;
                carry.Status = RiderFinancialItemStatus.Reversed;
            }
            var preparedBatches = await dbcontext.RiderPaymentBatches.Where(x => x.RiderPayrollRunId == run.Id && x.Status != RiderPaymentBatchStatus.Reversed).ToListAsync(cancellationToken);
            foreach (var batch in preparedBatches) batch.Status = RiderPaymentBatchStatus.Reversed;
            run.Status = RiderPayrollStatus.Reversed;
            await AppendAuditAsync(run.LegalEntityId, "RiderPayroll.Reversed", actorId, new { run.Id, run.RunNumber, ReversalFinancialDocumentId = reversal.Value.Id, Reason = reason }, cancellationToken);
            await dbcontext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.ConcurrencyConflict);
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
        return await GetRunAsync(run.Id, actorId, cancellationToken);
    }

    public async Task<Result<PagedResponse<RiderFinancialItemTypeResponse>>> GetItemTypesAsync(
        PaginationRequest pagination,
        int legalEntityId,
        RiderFinancialItemDirection? direction,
        bool? active,
        string? search,
        string? sortBy,
        string? sortDirection,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var access = await financialAccessService.EnsurePermissionAsync(actorId, legalEntityId, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<PagedResponse<RiderFinancialItemTypeResponse>>(access.Error);

        var query = dbcontext.RiderFinancialItemTypes.AsNoTracking().Where(x => x.LegalEntityId == legalEntityId);
        if (direction.HasValue) query = query.Where(x => x.Direction == direction.Value);
        if (active.HasValue) query = query.Where(x => x.IsActive == active.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToUpperInvariant();
            query = query.Where(x => x.Code.ToUpper().Contains(normalizedSearch) || x.Name.ToUpper().Contains(normalizedSearch));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = pagination.NormalizedPageNumber;
        var pageSize = pagination.NormalizedPageSize;
        var items = await ApplyItemTypeOrdering(query, sortBy, sortDirection)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new RiderFinancialItemTypeResponse(x.Id, x.LegalEntityId, x.Code, x.Name, x.Direction, x.Priority, x.LedgerAccountId, x.IsActive))
            .ToListAsync(cancellationToken);
        return Result.Success(new PagedResponse<RiderFinancialItemTypeResponse>(items, pageNumber, pageSize, totalCount));
    }

    public async Task<Result<RiderFinancialItemTypeResponse>> CreateItemTypeAsync(CreateRiderFinancialItemTypeRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var access = await financialAccessService.EnsurePermissionAsync(actorId, request.LegalEntityId, FinancialPermission.Configure, cancellationToken);
        if (access.IsFailure) return Result.Failure<RiderFinancialItemTypeResponse>(access.Error);
        var code = NormalizeCode(request.Code);
        if (string.IsNullOrWhiteSpace(code) || await dbcontext.RiderFinancialItemTypes.AnyAsync(x => x.LegalEntityId == request.LegalEntityId && x.Code == code, cancellationToken) ||
            !await dbcontext.AccountingAccounts.AnyAsync(x => x.Id == request.LedgerAccountId && x.LegalEntityId == request.LegalEntityId && x.IsActive, cancellationToken))
            return Result.Failure<RiderFinancialItemTypeResponse>(AccountingPlatformErrors.InvalidRequest);
        var type = new RiderFinancialItemType { LegalEntityId = request.LegalEntityId, Code = code, Name = request.Name.Trim(), Direction = request.Direction, Priority = request.Priority, LedgerAccountId = request.LedgerAccountId };
        dbcontext.RiderFinancialItemTypes.Add(type);
        await AppendAuditAsync(type.LegalEntityId, "RiderFinancialItem.TypeCreated", actorId, new { type.Id, type.Code, type.Direction, type.Priority, type.LedgerAccountId }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToResponse(type));
    }

    public async Task<Result<PagedResponse<RiderFinancialItemResponse>>> GetFinancialItemsAsync(
        PaginationRequest pagination,
        int legalEntityId,
        long? riderIqamaNo,
        RiderFinancialItemStatus? status,
        int? typeId,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? search,
        string? sortBy,
        string? sortDirection,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var access = await financialAccessService.EnsurePermissionAsync(actorId, legalEntityId, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<PagedResponse<RiderFinancialItemResponse>>(access.Error);
        if (toDate < fromDate) return Result.Failure<PagedResponse<RiderFinancialItemResponse>>(AccountingPlatformErrors.InvalidRequest);

        var query = dbcontext.RiderFinancialItems.AsNoTracking().Where(x => x.LegalEntityId == legalEntityId);
        if (riderIqamaNo.HasValue) query = query.Where(x => x.RiderIqamaNo == riderIqamaNo.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (typeId.HasValue) query = query.Where(x => x.RiderFinancialItemTypeId == typeId.Value);
        if (fromDate.HasValue) query = query.Where(x => x.EffectiveDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(x => x.EffectiveDate <= toDate.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToUpperInvariant();
            query = query.Where(x => x.Reference.ToUpper().Contains(normalizedSearch) ||
                x.Description.ToUpper().Contains(normalizedSearch) ||
                x.ItemType.Code.ToUpper().Contains(normalizedSearch) ||
                x.ItemType.Name.ToUpper().Contains(normalizedSearch));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = pagination.NormalizedPageNumber;
        var pageSize = pagination.NormalizedPageSize;
        var entities = await ApplyFinancialItemOrdering(query, sortBy, sortDirection)
            .Include(x => x.ItemType)
            .Include(x => x.Installments)
            .AsSplitQuery()
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Result.Success(new PagedResponse<RiderFinancialItemResponse>(entities.Select(x => ToResponse(x, x.ItemType)).ToArray(), pageNumber, pageSize, totalCount));
    }

    public async Task<Result<RiderFinancialItemResponse>> GetFinancialItemAsync(Guid id, string actorId, CancellationToken cancellationToken = default)
    {
        var item = await dbcontext.RiderFinancialItems
            .AsNoTracking()
            .Include(x => x.ItemType)
            .Include(x => x.Installments)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return Result.Failure<RiderFinancialItemResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, item.LegalEntityId, FinancialPermission.View, cancellationToken);
        return access.IsFailure
            ? Result.Failure<RiderFinancialItemResponse>(access.Error)
            : Result.Success(ToResponse(item, item.ItemType));
    }

    public async Task<Result<RiderFinancialItemResponse>> CreateFinancialItemAsync(CreateRiderFinancialItemRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var access = await financialAccessService.EnsurePermissionAsync(actorId, request.LegalEntityId, FinancialPermission.Prepare, cancellationToken);
        if (access.IsFailure) return Result.Failure<RiderFinancialItemResponse>(access.Error);
        var type = await dbcontext.RiderFinancialItemTypes.SingleOrDefaultAsync(x => x.Id == request.RiderFinancialItemTypeId && x.LegalEntityId == request.LegalEntityId && x.IsActive, cancellationToken);
        if (type is null || request.Amount <= 0 || request.InstallmentCount <= 0 || (request.InstallmentCount.HasValue && !request.FirstInstallmentDate.HasValue) ||
            !await dbcontext.Employees.AnyAsync(x => x.IqamaNo == request.RiderIqamaNo && !x.IsDeleted, cancellationToken) ||
            await dbcontext.RiderFinancialItems.AnyAsync(x => x.LegalEntityId == request.LegalEntityId && x.Reference == request.Reference.Trim(), cancellationToken))
            return Result.Failure<RiderFinancialItemResponse>(AccountingPlatformErrors.InvalidRequest);
        var item = new RiderFinancialItem { LegalEntityId = request.LegalEntityId, RiderIqamaNo = request.RiderIqamaNo, RiderFinancialItemTypeId = type.Id, Reference = request.Reference.Trim(), Description = request.Description.Trim(), EffectiveDate = request.EffectiveDate, DeductionStartDate = request.DeductionStartDate, OriginalAmount = decimal.Round(request.Amount, 2), OutstandingAmount = decimal.Round(request.Amount, 2), InstallmentCount = request.InstallmentCount, EvidenceFileId = request.EvidenceFileId, CreatedBy = actorId };
        if (request.InstallmentCount.HasValue)
        {
            var count = request.InstallmentCount.Value;
            var regular = Math.Floor(item.OriginalAmount / count * 100m) / 100m;
            for (var i = 0; i < count; i++) item.Installments.Add(new RiderFinancialInstallment { Sequence = i + 1, DueDate = request.FirstInstallmentDate!.Value.AddMonths(i), ScheduledAmount = i == count - 1 ? item.OriginalAmount - regular * (count - 1) : regular });
        }
        dbcontext.RiderFinancialItems.Add(item);
        await AppendAuditAsync(item.LegalEntityId, "RiderFinancialItem.Created", actorId, new { item.Id, item.RiderIqamaNo, type.Code, item.Reference, item.OriginalAmount, item.InstallmentCount }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToResponse(item, type));
    }

    public async Task<Result<PagedResponse<RiderPaymentBatchResponse>>> GetPaymentBatchesAsync(
        PaginationRequest pagination,
        int legalEntityId,
        Guid? runId,
        RiderPaymentMethod? method,
        RiderPaymentBatchStatus? status,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? search,
        string? sortBy,
        string? sortDirection,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var access = await financialAccessService.EnsurePermissionAsync(actorId, legalEntityId, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<PagedResponse<RiderPaymentBatchResponse>>(access.Error);
        if (toDate < fromDate) return Result.Failure<PagedResponse<RiderPaymentBatchResponse>>(AccountingPlatformErrors.InvalidRequest);

        var query = dbcontext.RiderPaymentBatches.AsNoTracking().Where(x => x.LegalEntityId == legalEntityId);
        if (runId.HasValue) query = query.Where(x => x.RiderPayrollRunId == runId.Value);
        if (method.HasValue) query = query.Where(x => x.Method == method.Value || x.Lines.Any(line => line.Method == method.Value));
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (fromDate.HasValue)
        {
            var from = fromDate.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(x => x.CreatedAt >= from);
        }
        if (toDate.HasValue)
        {
            var through = toDate.Value.ToDateTime(TimeOnly.MaxValue);
            query = query.Where(x => x.CreatedAt <= through);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToUpperInvariant();
            query = query.Where(x => x.BatchNumber.ToUpper().Contains(normalizedSearch));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = pagination.NormalizedPageNumber;
        var pageSize = pagination.NormalizedPageSize;
        var batches = await ApplyPaymentBatchOrdering(query, sortBy, sortDirection)
            .Include(x => x.Lines)
            .ThenInclude(x => x.RiderPayrollLine)
            .AsSplitQuery()
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Result.Success(new PagedResponse<RiderPaymentBatchResponse>(batches.Select(x => ToResponse(x)).ToArray(), pageNumber, pageSize, totalCount));
    }

    public async Task<Result<RiderPaymentBatchResponse>> GetPaymentBatchAsync(Guid id, string actorId, CancellationToken cancellationToken = default)
    {
        var batch = await dbcontext.RiderPaymentBatches
            .AsNoTracking()
            .Include(x => x.Lines)
            .ThenInclude(x => x.RiderPayrollLine)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (batch is null) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, batch.LegalEntityId, FinancialPermission.View, cancellationToken);
        return access.IsFailure
            ? Result.Failure<RiderPaymentBatchResponse>(access.Error)
            : Result.Success(ToResponse(batch));
    }

    public async Task<Result<RiderPaymentBatchResponse>> PreparePaymentBatchAsync(Guid runId, PrepareRiderPaymentBatchRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? transaction = null;
        if (dbcontext.Database.IsRelational() && dbcontext.Database.CurrentTransaction is null)
            transaction = await dbcontext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await AcquirePaymentRunLockAsync(runId, cancellationToken);
            var run = await dbcontext.RiderPayrollRuns.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
            if (run is null) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.NotFound);
            var access = await financialAccessService.EnsurePermissionAsync(actorId, run.LegalEntityId, FinancialPermission.Prepare, cancellationToken);
            if (access.IsFailure) return Result.Failure<RiderPaymentBatchResponse>(access.Error);
            if (run.Status is not (RiderPayrollStatus.Approved or RiderPayrollStatus.PaymentPrepared or RiderPayrollStatus.PartiallyPaid or RiderPayrollStatus.Held)) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.InvalidState);
            var selectedIqamas = request.RiderIqamaNumbers?.ToHashSet();
            var allocations = request.Allocations?.GroupBy(x => x.RiderIqamaNo).ToDictionary(x => x.Key, x => x.Single());
            if (request.Allocations?.GroupBy(x => x.RiderIqamaNo).Any(x => x.Count() > 1) == true) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.InvalidRequest);
            var employees = await dbcontext.Employees.AsNoTracking().Where(x => run.Lines.Select(l => l.RiderIqamaNo).Contains(x.IqamaNo)).ToDictionaryAsync(x => x.IqamaNo, cancellationToken);
            var allocated = await dbcontext.RiderPaymentBatchLines
                .AsNoTracking()
                .Where(x => x.RiderPayrollLine.RiderPayrollRunId == run.Id &&
                    x.RejectionReason == null &&
                    x.RiderPaymentBatch.Status != RiderPaymentBatchStatus.Rejected &&
                    x.RiderPaymentBatch.Status != RiderPaymentBatchStatus.Reversed)
                .GroupBy(x => x.RiderPayrollLineId)
                .Select(x => new { LineId = x.Key, Amount = x.Sum(v => v.Amount) })
                .ToDictionaryAsync(x => x.LineId, x => x.Amount, cancellationToken);
            var batch = new RiderPaymentBatch { LegalEntityId = run.LegalEntityId, RiderPayrollRunId = run.Id, BatchNumber = $"RPB-{run.PeriodEnd:yyyyMM}-{Guid.NewGuid():N}"[..25].ToUpperInvariant(), Method = request.Method, CreatedBy = actorId };
            foreach (var line in run.Lines.Where(x => !x.IsHeld && x.NetPay > 0 && (selectedIqamas == null || selectedIqamas.Contains(x.RiderIqamaNo)) && (allocations == null || allocations.ContainsKey(x.RiderIqamaNo))))
            {
                var remaining = line.NetPay - allocated.GetValueOrDefault(line.Id);
                if (remaining <= 0) continue;
                var allocation = allocations?.GetValueOrDefault(line.RiderIqamaNo);
                var method = allocation?.Method ?? request.Method;
                var amount = allocation?.Amount ?? remaining;
                if (amount <= 0 || amount > remaining || method == RiderPaymentMethod.Mixed) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.PaymentExceedsUnpaid);
                var employee = employees.GetValueOrDefault(line.RiderIqamaNo);
                var iban = NormalizeIban(employee?.IBAN);
                if (method == RiderPaymentMethod.Bank && !IsSaudiIban(iban)) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.InvalidIban);
                if (method == RiderPaymentMethod.Hold) { line.IsHeld = true; line.HoldReason = "Payment held by Accountant during batch preparation."; }
                batch.Lines.Add(new RiderPaymentBatchLine { RiderPayrollLineId = line.Id, Method = method, Amount = decimal.Round(amount, 2), IbanSnapshot = method == RiderPaymentMethod.Bank ? iban : null, HousingId = employee?.HousingId });
            }
            if (batch.Lines.Count == 0) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.InvalidRequest);
            dbcontext.RiderPaymentBatches.Add(batch);
            run.Status = batch.Lines.All(x => x.Method == RiderPaymentMethod.Hold) ? RiderPayrollStatus.Held : RiderPayrollStatus.PaymentPrepared;
            await AppendAuditAsync(run.LegalEntityId, "RiderPaymentBatch.Prepared", actorId, new { batch.Id, batch.BatchNumber, batch.Method, Count = batch.Lines.Count, Total = batch.Lines.Sum(x => x.Amount) }, cancellationToken);
            await dbcontext.SaveChangesAsync(cancellationToken);
            var response = await ToResponseAsync(batch, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Result.Success(response);
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    public async Task<Result<AccountingFileResponse>> ExportPaymentBatchAsync(Guid batchId, ExportRiderPaymentBatchRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var batch = await dbcontext.RiderPaymentBatches.Include(x => x.Lines).ThenInclude(x => x.RiderPayrollLine)
            .SingleOrDefaultAsync(x => x.Id == batchId, cancellationToken);
        if (batch is null) return Result.Failure<AccountingFileResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, batch.LegalEntityId, FinancialPermission.Prepare, cancellationToken);
        if (access.IsFailure) return Result.Failure<AccountingFileResponse>(access.Error);
        if (batch.Method == RiderPaymentMethod.Hold || batch.Status is RiderPaymentBatchStatus.Rejected or RiderPaymentBatchStatus.Reversed)
            return Result.Failure<AccountingFileResponse>(AccountingPlatformErrors.InvalidState);

        if (batch.ExportFileId.HasValue)
        {
            var existing = await dbcontext.AccountingStoredFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == batch.ExportFileId && x.Status == StoredFileStatus.Active, cancellationToken);
            if (existing is not null) return Result.Success(ToFileResponse(existing));
        }

        var format = request.Format.Trim().ToLowerInvariant();
        if (format is not ("xlsx" or "csv")) return Result.Failure<AccountingFileResponse>(AccountingPlatformErrors.InvalidRequest);
        await using var content = format == "xlsx" ? CreatePaymentWorkbook(batch) : CreatePaymentCsv(batch);
        var fileName = $"{batch.BatchNumber}.{format}";
        var stored = await accountingFileService.UploadAsync(new UploadAccountingFileRequest(batch.LegalEntityId, DateTime.UtcNow.AddYears(7)), fileName,
            format == "xlsx" ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "text/csv", content, actorId, cancellationToken);
        if (stored.IsFailure) return Result.Failure<AccountingFileResponse>(stored.Error);
        batch.ExportFileId = stored.Value.Id;
        batch.Status = RiderPaymentBatchStatus.Exported;
        await AppendAuditAsync(batch.LegalEntityId, "RiderPaymentBatch.Exported", actorId, new { batch.Id, batch.BatchNumber, Format = format, FileId = stored.Value.Id, Count = batch.Lines.Count, Total = batch.Lines.Sum(x => x.Amount) }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return stored;
    }

    public async Task<Result<RiderPaymentBatchResponse>> ConfirmPaymentBatchAsync(Guid batchId, ConfirmRiderPaymentBatchRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var batch = await dbcontext.RiderPaymentBatches.Include(x => x.Lines).ThenInclude(x => x.RiderPayrollLine).SingleOrDefaultAsync(x => x.Id == batchId, cancellationToken);
        if (batch is null) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, batch.LegalEntityId, FinancialPermission.Post, cancellationToken);
        if (access.IsFailure) return Result.Failure<RiderPaymentBatchResponse>(access.Error);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.IdempotencyKeyRequired);
        var idempotencyKey = request.IdempotencyKey.Trim();
        var postingProfileCode = request.PostingProfileCode?.Trim();
        var correlationId = request.CorrelationId?.Trim();
        if (idempotencyKey.Length > 128 || string.IsNullOrWhiteSpace(postingProfileCode) || postingProfileCode.Length > 64 ||
            string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64)
            return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.InvalidRequest);

        IDbContextTransaction? transaction = null;
        if (dbcontext.Database.IsRelational() && dbcontext.Database.CurrentTransaction is null)
            transaction = await dbcontext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await AcquirePaymentBatchLockAsync(batch.Id, cancellationToken);
            await RefreshPaymentBatchStateAsync(batch, cancellationToken);

            var selectedIds = request.LineIds?.ToHashSet();
            var replayDocumentId = await dbcontext.FinancialDocuments
                .AsNoTracking()
                .Where(x => x.LegalEntityId == batch.LegalEntityId &&
                    x.DocumentType == "RiderPayrollPayment" &&
                    x.IdempotencyKey == idempotencyKey)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);
            var replayLines = replayDocumentId.HasValue
                ? batch.Lines.Where(x => x.PaymentFinancialDocumentId == replayDocumentId.Value).ToArray()
                : [];
            var isReplay = replayLines.Length > 0 && batch.Status != RiderPaymentBatchStatus.Reversed;
            if (isReplay && selectedIds is not null && !selectedIds.SetEquals(replayLines.Select(x => x.Id)))
                return Result.Failure<RiderPaymentBatchResponse>(LedgerErrors.IdempotencyConflict);
            if (!isReplay && batch.Status is RiderPaymentBatchStatus.Confirmed or RiderPaymentBatchStatus.Rejected or RiderPaymentBatchStatus.Reversed)
                return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.InvalidState);
            var selected = isReplay
                ? replayLines
                : batch.Lines.Where(x => !x.IsConfirmed && x.RejectionReason == null && x.Method != RiderPaymentMethod.Hold && (selectedIds == null || selectedIds.Contains(x.Id))).ToArray();
            if (selected.Length == 0) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.InvalidRequest);
            var idempotencyPayload = JsonSerializer.Serialize(new
            {
                request.SettlementDate,
                PostingProfileCode = postingProfileCode.ToUpperInvariant(),
                CorrelationId = correlationId,
                LineIds = request.LineIds?.Distinct().Order().ToArray(),
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
            });
            var run = await dbcontext.RiderPayrollRuns.SingleAsync(x => x.Id == batch.RiderPayrollRunId, cancellationToken);
            var dimensions = await LoadDimensionContextAsync(run, cancellationToken);
            var events = selected.Select(x => new PostingEventAmount(x.Method == RiderPaymentMethod.Bank ? "PAYROLL_PAYMENT_BANK" : "PAYROLL_PAYMENT_CASH", x.Amount, $"Payroll payment for rider {x.RiderPayrollLine.RiderIqamaNo}", dimensions.For(x.RiderPayrollLine.RiderIqamaNo, null))).ToArray();
            var posting = await accountingPostingService.PostAsync(new PostSourceDocumentRequest(batch.LegalEntityId, null, request.SettlementDate, "RiderPayrollPayment", $"{batch.BatchNumber}:{string.Join(',', selected.Select(x => x.Id).Order())}", postingProfileCode, $"Settlement of {batch.BatchNumber}", run.CurrencyCode, idempotencyKey, correlationId, AccountingModule.Payroll, events, idempotencyPayload), actorId, cancellationToken);
            if (posting.IsFailure)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                return Result.Failure<RiderPaymentBatchResponse>(posting.Error);
            }
            if (isReplay)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return Result.Success(ToResponse(batch));
            }
            foreach (var line in selected) { line.IsConfirmed = true; line.ConfirmedAt = DateTime.UtcNow; line.ConfirmedBy = actorId; line.PaymentFinancialDocumentId = posting.Value.Id; }
            batch.PaymentFinancialDocumentId ??= posting.Value.Id;
            batch.Status = batch.Lines.All(x => x.IsConfirmed || x.Method == RiderPaymentMethod.Hold) ? RiderPaymentBatchStatus.Confirmed : RiderPaymentBatchStatus.Sent;
            var confirmedTotal = await dbcontext.RiderPaymentBatchLines.Where(x => x.RiderPayrollLine.RiderPayrollRunId == run.Id && x.IsConfirmed).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
            confirmedTotal += selected.Where(x => !dbcontext.Entry(x).Property(v => v.IsConfirmed).OriginalValue).Sum(x => x.Amount);
            run.Status = confirmedTotal >= run.NetPay ? RiderPayrollStatus.Paid : RiderPayrollStatus.PartiallyPaid;
            await AppendAuditAsync(batch.LegalEntityId, "RiderPaymentBatch.Confirmed", actorId, new { batch.Id, FinancialDocumentId = posting.Value.Id, Lines = selected.Select(x => x.Id), Total = selected.Sum(x => x.Amount), request.Notes }, cancellationToken);
            await dbcontext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Result.Success(ToResponse(batch));
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    public async Task<Result<RiderPaymentBatchResponse>> RejectPaymentLineAsync(Guid batchId, long lineId, RejectRiderPaymentLineRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var batch = await dbcontext.RiderPaymentBatches
            .Include(x => x.Lines)
            .ThenInclude(x => x.RiderPayrollLine)
            .SingleOrDefaultAsync(x => x.Id == batchId, cancellationToken);
        if (batch is null) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.NotFound);

        var access = await financialAccessService.EnsurePermissionAsync(actorId, batch.LegalEntityId, FinancialPermission.Prepare, cancellationToken);
        if (access.IsFailure) return Result.Failure<RiderPaymentBatchResponse>(access.Error);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.InvalidRequest);

        IDbContextTransaction? transaction = null;
        if (dbcontext.Database.IsRelational() && dbcontext.Database.CurrentTransaction is null)
            transaction = await dbcontext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await AcquirePaymentBatchLockAsync(batch.Id, cancellationToken);
            await RefreshPaymentBatchStateAsync(batch, cancellationToken);

            var line = batch.Lines.SingleOrDefault(x => x.Id == lineId);
            if (line is null) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.NotFound);
            if (batch.Status is RiderPaymentBatchStatus.Confirmed or RiderPaymentBatchStatus.Rejected or RiderPaymentBatchStatus.Reversed ||
                line.IsConfirmed || line.Method == RiderPaymentMethod.Hold || !string.IsNullOrWhiteSpace(line.RejectionReason))
                return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.InvalidState);

            line.RejectionReason = request.Reason.Trim();
            batch.Status = batch.Lines.All(x => x.Method == RiderPaymentMethod.Hold || !string.IsNullOrWhiteSpace(x.RejectionReason))
                ? RiderPaymentBatchStatus.Rejected
                : RiderPaymentBatchStatus.PartiallyRejected;
            await AppendAuditAsync(batch.LegalEntityId, "RiderPaymentBatch.LineRejected", actorId, new
            {
                batch.Id,
                LineId = line.Id,
                RiderIqamaNo = line.RiderPayrollLine.RiderIqamaNo,
                Reason = line.RejectionReason
            }, cancellationToken);
            await dbcontext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Result.Success(ToResponse(batch));
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    public async Task<Result<RiderPaymentBatchResponse>> ReversePaymentBatchAsync(Guid batchId, ReverseRiderPaymentBatchRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var batch = await dbcontext.RiderPaymentBatches.Include(x => x.Lines).ThenInclude(x => x.RiderPayrollLine)
            .SingleOrDefaultAsync(x => x.Id == batchId, cancellationToken);
        if (batch is null) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, batch.LegalEntityId, FinancialPermission.Post, cancellationToken);
        if (access.IsFailure) return Result.Failure<RiderPaymentBatchResponse>(access.Error);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.IdempotencyKeyRequired);
        var idempotencyKey = request.IdempotencyKey.Trim();
        var reason = request.Reason?.Trim();
        var correlationId = request.CorrelationId?.Trim();
        if (idempotencyKey.Length > 128 || string.IsNullOrWhiteSpace(reason) || reason.Length > 500 ||
            string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64)
            return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.InvalidRequest);

        IDbContextTransaction? transaction = null;
        if (dbcontext.Database.IsRelational() && dbcontext.Database.CurrentTransaction is null)
            transaction = await dbcontext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await AcquirePaymentBatchLockAsync(batch.Id, cancellationToken);
            await RefreshPaymentBatchStateAsync(batch, cancellationToken);

            var isReplay = batch.Status == RiderPaymentBatchStatus.Reversed;
            var affectedLines = isReplay
                ? batch.Lines.Where(x => x.PaymentFinancialDocumentId.HasValue || batch.PaymentFinancialDocumentId.HasValue).ToArray()
                : batch.Lines.Where(x => x.IsConfirmed).ToArray();
            if (affectedLines.Length == 0) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.InvalidState);
            var documentIds = affectedLines.Select(x => x.PaymentFinancialDocumentId ?? batch.PaymentFinancialDocumentId).Distinct().ToArray();
            if (documentIds.Any(x => !x.HasValue)) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.InvalidState);
            var idempotencyPayload = JsonSerializer.Serialize(new
            {
                request.ReversalDate,
                Reason = reason,
                CorrelationId = correlationId
            });

            var reversalIds = new List<Guid>();
            foreach (var documentId in documentIds.Select(x => x!.Value).OrderBy(x => x))
            {
                var result = await accountingPostingService.ReverseAsync(new ReverseSourceDocumentRequest(
                    documentId, request.ReversalDate, reason, DeriveDocumentIdempotencyKey(idempotencyKey, documentId), correlationId,
                    AccountingModule.Payroll, idempotencyPayload), actorId, cancellationToken);
                if (result.IsFailure)
                {
                    if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                    return Result.Failure<RiderPaymentBatchResponse>(result.Error);
                }
                reversalIds.Add(result.Value.Id);
            }
            if (isReplay)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return Result.Success(await ToResponseAsync(batch, cancellationToken));
            }
            foreach (var line in affectedLines)
            {
                line.IsConfirmed = false;
                line.ConfirmedAt = null;
                line.ConfirmedBy = null;
            }
            batch.Status = RiderPaymentBatchStatus.Reversed;
            var run = await dbcontext.RiderPayrollRuns.SingleAsync(x => x.Id == batch.RiderPayrollRunId, cancellationToken);
            var remainingConfirmed = await dbcontext.RiderPaymentBatchLines
                .Where(x => x.RiderPayrollLine.RiderPayrollRunId == run.Id && x.RiderPaymentBatchId != batch.Id && x.IsConfirmed)
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
            run.Status = remainingConfirmed <= 0
                ? RiderPayrollStatus.Approved
                : remainingConfirmed >= run.NetPay
                    ? RiderPayrollStatus.Paid
                    : RiderPayrollStatus.PartiallyPaid;
            await AppendAuditAsync(batch.LegalEntityId, "RiderPaymentBatch.Reversed", actorId, new { batch.Id, batch.BatchNumber, ReversalFinancialDocumentIds = reversalIds, Reason = reason }, cancellationToken);
            await dbcontext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Result.Success(ToResponse(batch));
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    public async Task<Result<PagedResponse<HousingCashAccessResponse>>> GetHousingCashAccessesAsync(
        PaginationRequest pagination,
        int legalEntityId,
        string? userId,
        int? housingId,
        bool? active,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? sortBy,
        string? sortDirection,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var permission = await financialAccessService.EnsurePermissionAsync(actorId, legalEntityId, FinancialPermission.View, cancellationToken);
        if (permission.IsFailure) return Result.Failure<PagedResponse<HousingCashAccessResponse>>(permission.Error);
        if (toDate < fromDate) return Result.Failure<PagedResponse<HousingCashAccessResponse>>(AccountingPlatformErrors.InvalidRequest);

        var query = dbcontext.HousingCashUserAccesses.AsNoTracking().Where(x => x.LegalEntityId == legalEntityId);
        if (!string.IsNullOrWhiteSpace(userId)) query = query.Where(x => x.UserId == userId.Trim());
        if (housingId.HasValue) query = query.Where(x => x.HousingId == housingId.Value);
        if (active.HasValue) query = query.Where(x => x.IsActive == active.Value);
        if (fromDate.HasValue)
        {
            var from = fromDate.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(x => x.GrantedAt >= from);
        }
        if (toDate.HasValue)
        {
            var through = toDate.Value.ToDateTime(TimeOnly.MaxValue);
            query = query.Where(x => x.GrantedAt <= through);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = pagination.NormalizedPageNumber;
        var pageSize = pagination.NormalizedPageSize;
        var entities = await ApplyHousingAccessOrdering(query, sortBy, sortDirection)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Result.Success(new PagedResponse<HousingCashAccessResponse>(entities.Select(ToResponse).ToArray(), pageNumber, pageSize, totalCount));
    }

    public async Task<Result<HousingCashAccessResponse>> GrantHousingCashAccessAsync(GrantHousingCashAccessRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var permission = await financialAccessService.EnsurePermissionAsync(actorId, request.LegalEntityId, FinancialPermission.Configure, cancellationToken);
        if (permission.IsFailure) return Result.Failure<HousingCashAccessResponse>(permission.Error);
        var isMember = await dbcontext.UserRoles
            .Where(x => x.UserId == request.UserId)
            .Join(dbcontext.ApplicationRoles, userRole => userRole.RoleId, role => role.Id, (_, role) => role.Name)
            .AnyAsync(roleName => roleName == "Member", cancellationToken);
        if (!isMember) return Result.Failure<HousingCashAccessResponse>(AccountingPlatformErrors.HousingCashMemberRequired);
        if (!await dbcontext.ApplicationUsers.AnyAsync(x => x.Id == request.UserId, cancellationToken) ||
            !await dbcontext.LegalEntities.AnyAsync(x => x.Id == request.LegalEntityId && x.IsActive, cancellationToken) ||
            !await dbcontext.Housings.AnyAsync(x => x.Id == request.HousingId, cancellationToken))
            return Result.Failure<HousingCashAccessResponse>(AccountingPlatformErrors.InvalidRequest);
        var access = await dbcontext.HousingCashUserAccesses.SingleOrDefaultAsync(x => x.UserId == request.UserId && x.LegalEntityId == request.LegalEntityId && x.HousingId == request.HousingId, cancellationToken);
        if (access is null)
        {
            access = new HousingCashUserAccess { UserId = request.UserId, LegalEntityId = request.LegalEntityId, HousingId = request.HousingId, GrantedBy = actorId };
            dbcontext.HousingCashUserAccesses.Add(access);
        }
        else
        {
            access.IsActive = true;
            access.GrantedBy = actorId;
            access.GrantedAt = DateTime.UtcNow;
        }
        await AppendAuditAsync(request.LegalEntityId, "RiderCash.HousingAccessGranted", actorId, new { request.UserId, request.HousingId }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToResponse(access));
    }

    public async Task<Result> RevokeHousingCashAccessAsync(int id, string actorId, CancellationToken cancellationToken = default)
    {
        var access = await dbcontext.HousingCashUserAccesses.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (access is null) return Result.Failure(AccountingPlatformErrors.NotFound);

        var permission = await financialAccessService.EnsurePermissionAsync(actorId, access.LegalEntityId, FinancialPermission.Configure, cancellationToken);
        if (permission.IsFailure) return Result.Failure(permission.Error);
        if (!access.IsActive) return Result.Success();

        access.IsActive = false;
        await AppendAuditAsync(access.LegalEntityId, "RiderCash.HousingAccessRevoked", actorId, new
        {
            AccessId = access.Id,
            access.UserId,
            access.HousingId
        }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<PagedResponse<RiderPaymentBatchResponse>>> GetHousingCashInboxAsync(
        PaginationRequest pagination,
        int? legalEntityId,
        RiderPaymentBatchStatus? status,
        string? sortBy,
        string? sortDirection,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var assignments = await dbcontext.HousingCashUserAccesses
            .AsNoTracking()
            .Where(x => x.UserId == actorId && x.IsActive && (!legalEntityId.HasValue || x.LegalEntityId == legalEntityId.Value))
            .Select(x => new { x.LegalEntityId, x.HousingId })
            .ToListAsync(cancellationToken);
        var pageNumber = pagination.NormalizedPageNumber;
        var pageSize = pagination.NormalizedPageSize;
        if (assignments.Count == 0)
            return Result.Success(new PagedResponse<RiderPaymentBatchResponse>([], pageNumber, pageSize, 0));

        var query = dbcontext.RiderPaymentBatches
            .AsNoTracking()
            .Where(batch => (!legalEntityId.HasValue || batch.LegalEntityId == legalEntityId.Value) &&
                batch.Lines.Any(line => line.Method == RiderPaymentMethod.Cash && line.HousingId.HasValue &&
                    dbcontext.HousingCashUserAccesses.Any(access => access.UserId == actorId && access.IsActive &&
                        access.LegalEntityId == batch.LegalEntityId && access.HousingId == line.HousingId.Value)));
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var batches = await ApplyPaymentBatchOrdering(query, sortBy, sortDirection)
            .Include(x => x.Lines)
            .ThenInclude(x => x.RiderPayrollLine)
            .AsSplitQuery()
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var assignedPairs = assignments.Select(x => (x.LegalEntityId, x.HousingId)).ToHashSet();
        var items = batches.Select(batch => ToResponse(batch, line =>
            line.Method == RiderPaymentMethod.Cash &&
            line.HousingId.HasValue &&
            assignedPairs.Contains((batch.LegalEntityId, line.HousingId.Value)))).ToArray();
        return Result.Success(new PagedResponse<RiderPaymentBatchResponse>(items, pageNumber, pageSize, totalCount));
    }

    public async Task<Result<RiderPaymentBatchResponse>> GetHousingCashPaymentBatchAsync(Guid batchId, string actorId, CancellationToken cancellationToken = default)
    {
        var batch = await dbcontext.RiderPaymentBatches
            .AsNoTracking()
            .Include(x => x.Lines)
            .ThenInclude(x => x.RiderPayrollLine)
            .SingleOrDefaultAsync(x => x.Id == batchId, cancellationToken);
        if (batch is null) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.NotFound);

        var housingIds = await dbcontext.HousingCashUserAccesses
            .AsNoTracking()
            .Where(x => x.UserId == actorId && x.LegalEntityId == batch.LegalEntityId && x.IsActive)
            .Select(x => x.HousingId)
            .ToArrayAsync(cancellationToken);
        if (!batch.Lines.Any(x => x.Method == RiderPaymentMethod.Cash && x.HousingId.HasValue && housingIds.Contains(x.HousingId.Value)))
            return Result.Failure<RiderPaymentBatchResponse>(LedgerErrors.AccessDenied);

        return Result.Success(ToResponse(batch, line =>
            line.Method == RiderPaymentMethod.Cash && line.HousingId.HasValue && housingIds.Contains(line.HousingId.Value)));
    }

    public async Task<Result<RiderPaymentBatchResponse>> ConfirmHousingCashDeliveryAsync(Guid batchId, ConfirmHousingCashDeliveryRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var batch = await dbcontext.RiderPaymentBatches.Include(x => x.Lines).ThenInclude(x => x.RiderPayrollLine)
            .SingleOrDefaultAsync(x => x.Id == batchId, cancellationToken);
        if (batch is null) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.NotFound);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.IdempotencyKeyRequired);
        var idempotencyKey = request.IdempotencyKey.Trim();
        var postingProfileCode = request.PostingProfileCode?.Trim();
        var correlationId = request.CorrelationId?.Trim();
        if (idempotencyKey.Length > 128 || string.IsNullOrWhiteSpace(postingProfileCode) || postingProfileCode.Length > 64 ||
            string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64 || request.LineIds is null || request.LineIds.Count == 0)
            return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.InvalidRequest);
        IDbContextTransaction? transaction = null;
        if (dbcontext.Database.IsRelational() && dbcontext.Database.CurrentTransaction is null)
            transaction = await dbcontext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await AcquirePaymentBatchLockAsync(batch.Id, cancellationToken);
            await RefreshPaymentBatchStateAsync(batch, cancellationToken);

            var selectedIds = request.LineIds.ToHashSet();
            var assignedHousingIds = await dbcontext.HousingCashUserAccesses.AsNoTracking()
                .Where(x => x.UserId == actorId && x.LegalEntityId == batch.LegalEntityId && x.IsActive)
                .Select(x => x.HousingId)
                .ToArrayAsync(cancellationToken);
            if (assignedHousingIds.Length == 0 || batch.Lines
                .Where(x => selectedIds.Contains(x.Id))
                .Any(x => !x.HousingId.HasValue || !assignedHousingIds.Contains(x.HousingId.Value)))
                return Result.Failure<RiderPaymentBatchResponse>(LedgerErrors.AccessDenied);

            var replayDocumentId = await dbcontext.FinancialDocuments
                .AsNoTracking()
                .Where(x => x.LegalEntityId == batch.LegalEntityId &&
                    x.DocumentType == "RiderPayrollCashDelivery" &&
                    x.IdempotencyKey == idempotencyKey)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);
            var replayLines = replayDocumentId.HasValue
                ? batch.Lines.Where(x => x.PaymentFinancialDocumentId == replayDocumentId.Value).ToArray()
                : [];
            var isReplay = replayLines.Length > 0 && batch.Status != RiderPaymentBatchStatus.Reversed;
            if (isReplay && !selectedIds.SetEquals(replayLines.Select(x => x.Id)))
                return Result.Failure<RiderPaymentBatchResponse>(LedgerErrors.IdempotencyConflict);
            if (!isReplay && batch.Status is RiderPaymentBatchStatus.Confirmed or RiderPaymentBatchStatus.Rejected or RiderPaymentBatchStatus.Reversed)
                return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.InvalidState);
            var selected = isReplay
                ? replayLines
                : batch.Lines.Where(x => selectedIds.Contains(x.Id) && !x.IsConfirmed && x.RejectionReason == null && x.Method == RiderPaymentMethod.Cash).ToArray();
            if (selected.Length != selectedIds.Count) return Result.Failure<RiderPaymentBatchResponse>(AccountingPlatformErrors.InvalidRequest);
            if (selected.Any(x => !x.HousingId.HasValue || !assignedHousingIds.Contains(x.HousingId.Value)))
                return Result.Failure<RiderPaymentBatchResponse>(LedgerErrors.AccessDenied);
            var idempotencyPayload = JsonSerializer.Serialize(new
            {
                request.SettlementDate,
                PostingProfileCode = postingProfileCode.ToUpperInvariant(),
                CorrelationId = correlationId,
                LineIds = request.LineIds.Distinct().Order().ToArray(),
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
            });

            var run = await dbcontext.RiderPayrollRuns.SingleAsync(x => x.Id == batch.RiderPayrollRunId, cancellationToken);
            var dimensions = await LoadDimensionContextAsync(run, cancellationToken);
            var events = selected.Select(x => new PostingEventAmount("PAYROLL_PAYMENT_CASH", x.Amount, $"Cash payroll delivery for rider {x.RiderPayrollLine.RiderIqamaNo}", dimensions.For(x.RiderPayrollLine.RiderIqamaNo, null))).ToArray();
            var command = new PostSourceDocumentRequest(batch.LegalEntityId, null, request.SettlementDate, "RiderPayrollCashDelivery", $"{batch.BatchNumber}:{string.Join(',', selected.Select(x => x.Id).Order())}", postingProfileCode, $"Housing cash delivery for {batch.BatchNumber}", run.CurrencyCode, idempotencyKey, correlationId, AccountingModule.Payroll, events, idempotencyPayload);
            var posting = await accountingPostingService.PostAfterScopeValidationAsync(command, actorId, cancellationToken);
            if (posting.IsFailure)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                return Result.Failure<RiderPaymentBatchResponse>(posting.Error);
            }
            if (isReplay)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return Result.Success(ToResponse(batch, line =>
                    line.Method == RiderPaymentMethod.Cash && line.HousingId.HasValue && assignedHousingIds.Contains(line.HousingId.Value)));
            }
            foreach (var line in selected)
            {
                line.IsConfirmed = true;
                line.ConfirmedAt = DateTime.UtcNow;
                line.ConfirmedBy = actorId;
                line.PaymentFinancialDocumentId = posting.Value.Id;
            }
            batch.PaymentFinancialDocumentId ??= posting.Value.Id;
            batch.Status = batch.Lines.All(x => x.IsConfirmed || x.Method == RiderPaymentMethod.Hold) ? RiderPaymentBatchStatus.Confirmed : RiderPaymentBatchStatus.Sent;
            var alreadyConfirmed = await dbcontext.RiderPaymentBatchLines.Where(x => x.RiderPayrollLine.RiderPayrollRunId == run.Id && x.IsConfirmed).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
            alreadyConfirmed += selected.Where(x => !dbcontext.Entry(x).Property(v => v.IsConfirmed).OriginalValue).Sum(x => x.Amount);
            run.Status = alreadyConfirmed >= run.NetPay ? RiderPayrollStatus.Paid : RiderPayrollStatus.PartiallyPaid;
            await AppendAuditAsync(batch.LegalEntityId, "RiderCash.Delivered", actorId, new { batch.Id, Lines = selected.Select(x => x.Id), HousingIds = selected.Select(x => x.HousingId).Distinct(), Total = selected.Sum(x => x.Amount), FinancialDocumentId = posting.Value.Id, request.Notes }, cancellationToken);
            await dbcontext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Result.Success(ToResponse(batch, line =>
                line.Method == RiderPaymentMethod.Cash && line.HousingId.HasValue && assignedHousingIds.Contains(line.HousingId.Value)));
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    public async Task<Result<RiderPayrollRunResponse>> GetRunAsync(Guid runId, string actorId, CancellationToken cancellationToken = default)
    {
        var run = await dbcontext.RiderPayrollRuns.AsNoTracking().Include(x => x.Lines).ThenInclude(x => x.Components).SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null) return Result.Failure<RiderPayrollRunResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, run.LegalEntityId, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<RiderPayrollRunResponse>(access.Error);
        return Result.Success(await ToResponseAsync(run, cancellationToken));
    }

    public async Task<Result<RiderFinancialProfileResponse>> GetFinancialProfileAsync(long riderIqamaNo, int legalEntityId, string actorId, CancellationToken cancellationToken = default)
    {
        var access = await financialAccessService.EnsurePermissionAsync(actorId, legalEntityId, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<RiderFinancialProfileResponse>(access.Error);
        var employee = await dbcontext.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.IqamaNo == riderIqamaNo && !x.IsDeleted, cancellationToken);
        if (employee is null) return Result.Failure<RiderFinancialProfileResponse>(AccountingPlatformErrors.NotFound);
        var items = await dbcontext.RiderFinancialItems.AsNoTracking().Include(x => x.ItemType).Include(x => x.Installments).Where(x => x.LegalEntityId == legalEntityId && x.RiderIqamaNo == riderIqamaNo).OrderByDescending(x => x.EffectiveDate).ToListAsync(cancellationToken);
        var payrollLines = await dbcontext.RiderPayrollLines.AsNoTracking().Include(x => x.Components).Include(x => x.RiderPayrollRun).Where(x => x.RiderPayrollRun.LegalEntityId == legalEntityId && x.RiderIqamaNo == riderIqamaNo && x.RiderPayrollRun.Status != RiderPayrollStatus.Reversed).OrderByDescending(x => x.RiderPayrollRun.PeriodEnd).ToListAsync(cancellationToken);
        var facts = await dbcontext.PlatformNormalizedFacts.AsNoTracking().Where(x => x.LegalEntityId == legalEntityId && x.RiderIqamaNo == riderIqamaNo && x.PlatformImportBatch.Status == PlatformImportStatus.Approved && x.PlatformImportBatch.SupersededByBatchId == null).ToListAsync(cancellationToken);
        var platformSummaries = facts.GroupBy(x => new { x.PlatformAccountId, x.WorkerCategory }).Select(g => new RiderPlatformFinancialSummary(g.Key.PlatformAccountId, g.Key.WorkerCategory, g.Where(x => x.MetricCode == "ACCEPTED_ORDERS").Sum(x => x.NumericValue ?? 0), g.Where(x => x.Category == PlatformFactCategory.CompanyBilling).Sum(x => x.NumericValue ?? 0), g.Where(x => x.MetricCode == "VAT").Sum(x => x.NumericValue ?? 0), payrollLines.SelectMany(x => x.Components).Where(x => x.PlatformAccountId == g.Key.PlatformAccountId && x.ComponentType is CompensationComponentType.Earning or CompensationComponentType.Allowance or CompensationComponentType.Bonus).Sum(x => x.Amount))).ToArray();
        var confirmed = await dbcontext.RiderPaymentBatchLines.AsNoTracking().Where(x => x.RiderPayrollLine.RiderIqamaNo == riderIqamaNo && x.RiderPayrollLine.RiderPayrollRun.LegalEntityId == legalEntityId && x.IsConfirmed).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
        var lineResponses = new List<RiderPayrollLineResponse>();
        foreach (var line in payrollLines) lineResponses.Add(ToResponse(line, employee.NameEN));
        return Result.Success(new RiderFinancialProfileResponse(riderIqamaNo, string.IsNullOrWhiteSpace(employee.NameEN) ? employee.NameAR : employee.NameEN, employee.IBAN, employee.HousingId, platformSummaries, items.Select(x => ToResponse(x, x.ItemType)).ToArray(), lineResponses, items.Where(x => x.ItemType.Direction == RiderFinancialItemDirection.Deduction && x.Status == RiderFinancialItemStatus.Open).Sum(x => x.OutstandingAmount), Math.Max(0, payrollLines.Sum(x => x.NetPay) - confirmed)));
    }

    private async Task<RiderPayrollRunResponse> ToResponseAsync(RiderPayrollRun run, CancellationToken ct)
    {
        var ids = run.Lines.Select(x => x.RiderIqamaNo).Distinct().ToArray();
        var names = await dbcontext.Employees.AsNoTracking().Where(x => ids.Contains(x.IqamaNo)).ToDictionaryAsync(x => x.IqamaNo, x => string.IsNullOrWhiteSpace(x.NameEN) ? x.NameAR : x.NameEN, ct);
        return new RiderPayrollRunResponse(run.Id, run.LegalEntityId, run.RunNumber, run.PeriodStart, run.PeriodEnd, run.CurrencyCode, run.Status, run.GrossEarnings, run.AppliedDeductions, run.CarriedDeductions, run.NetPay, run.AccrualFinancialDocumentId, Convert.ToBase64String(run.RowVersion), run.Lines.OrderBy(x => x.RiderIqamaNo).Select(x => ToResponse(x, names.GetValueOrDefault(x.RiderIqamaNo) ?? x.RiderIqamaNo.ToString())).ToArray());
    }

    private static RiderPayrollLineResponse ToResponse(RiderPayrollLine x, string name) => new(x.Id, x.RiderIqamaNo, name, x.GrossEarnings, x.AppliedDeductions, x.CarriedDeductions, x.NetPay, x.IsHeld, x.HoldReason, x.Components.OrderBy(c => c.Id).Select(c => new RiderPayrollComponentResponse(c.Id, c.PlatformAccountId, c.CompensationPolicyVersionId, c.SourceImportBatchId, c.RiderFinancialItemId, c.Source, c.ComponentType, c.ComponentCode, c.Description, c.Quantity, c.Rate, c.Amount, c.IsAutomatic, c.CalculationJson)).ToArray());
    private static RiderFinancialItemTypeResponse ToResponse(RiderFinancialItemType x) => new(x.Id, x.LegalEntityId, x.Code, x.Name, x.Direction, x.Priority, x.LedgerAccountId, x.IsActive);
    private static RiderFinancialItemResponse ToResponse(RiderFinancialItem x, RiderFinancialItemType type) => new(x.Id, x.LegalEntityId, x.RiderIqamaNo, x.RiderFinancialItemTypeId, type.Code, x.Reference, x.Description, x.EffectiveDate, x.DeductionStartDate, x.OriginalAmount, x.OutstandingAmount, x.Status, Convert.ToBase64String(x.RowVersion), x.Installments.OrderBy(i => i.Sequence).Select(i => new RiderFinancialInstallmentResponse(i.Id, i.Sequence, i.DueDate, i.ScheduledAmount, i.AppliedAmount, i.IsSettled)).ToArray());
    private async Task<RiderPaymentBatchResponse> ToResponseAsync(RiderPaymentBatch batch, CancellationToken ct)
    {
        await dbcontext.Entry(batch).Collection(x => x.Lines).Query().Include(x => x.RiderPayrollLine).LoadAsync(ct);
        return ToResponse(batch);
    }

    private static string DeriveDocumentIdempotencyKey(string idempotencyKey, Guid documentId)
    {
        var suffix = ":" + documentId.ToString("N")[..12];
        if (idempotencyKey.Length + suffix.Length <= 128)
            return idempotencyKey + suffix;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{idempotencyKey}|{documentId:N}")));
        return $"RIDER-PAYMENT-REVERSAL:{hash}";
    }

    private async Task AcquirePaymentBatchLockAsync(Guid batchId, CancellationToken cancellationToken)
    {
        if (dbcontext.Database.IsSqlServer() && dbcontext.Database.CurrentTransaction is not null)
            await dbcontext.Database.ExecuteSqlInterpolatedAsync(
                $"EXEC sp_getapplock @Resource={"Accounting:RiderPaymentBatch:" + batchId}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000",
                cancellationToken);
    }

    private async Task AcquirePaymentRunLockAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (dbcontext.Database.IsSqlServer() && dbcontext.Database.CurrentTransaction is not null)
            await dbcontext.Database.ExecuteSqlInterpolatedAsync(
                $"EXEC sp_getapplock @Resource={"Accounting:RiderPaymentRun:" + runId}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000",
                cancellationToken);
    }

    private async Task RefreshPaymentBatchStateAsync(RiderPaymentBatch batch, CancellationToken cancellationToken)
    {
        if (!dbcontext.Database.IsRelational()) return;
        await dbcontext.Entry(batch).ReloadAsync(cancellationToken);
        foreach (var line in batch.Lines)
            await dbcontext.Entry(line).ReloadAsync(cancellationToken);
    }

    private static RiderPaymentBatchResponse ToResponse(RiderPaymentBatch batch, Func<RiderPaymentBatchLine, bool>? lineFilter = null)
    {
        var lines = lineFilter is null ? batch.Lines : batch.Lines.Where(lineFilter);
        return new RiderPaymentBatchResponse(
            batch.Id,
            batch.LegalEntityId,
            batch.RiderPayrollRunId,
            batch.BatchNumber,
            batch.Method,
            batch.Status,
            batch.ExportFileId,
            batch.PaymentFinancialDocumentId,
            lines.OrderBy(x => x.Id).Select(x => new RiderPaymentBatchLineResponse(
                x.Id,
                x.RiderPayrollLineId,
                x.RiderPayrollLine.RiderIqamaNo,
                x.Method,
                x.Amount,
                x.IbanSnapshot,
                x.HousingId,
                x.IsConfirmed,
                x.RejectionReason,
                x.ConfirmedAt,
                x.ConfirmedBy,
                x.PaymentFinancialDocumentId)).ToArray());
    }

    private static MemoryStream CreatePaymentWorkbook(RiderPaymentBatch batch)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(batch.Method == RiderPaymentMethod.Cash ? "Cash Payments" : "Bank Payments");
        var headers = new[] { "Batch / الدفعة", "Rider Iqama / إقامة السائق", "IBAN / آيبان", "Housing / السكن", "Method / الطريقة", "Amount SAR / المبلغ" };
        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
            sheet.Cell(1, column + 1).Style.Font.Bold = true;
        }
        var row = 2;
        foreach (var line in batch.Lines.OrderBy(x => x.HousingId).ThenBy(x => x.RiderPayrollLine.RiderIqamaNo))
        {
            sheet.Cell(row, 1).Value = FormulaSafe(batch.BatchNumber);
            sheet.Cell(row, 2).Value = line.RiderPayrollLine.RiderIqamaNo.ToString();
            sheet.Cell(row, 3).Value = FormulaSafe(line.IbanSnapshot ?? string.Empty);
            sheet.Cell(row, 4).Value = line.HousingId?.ToString() ?? string.Empty;
            sheet.Cell(row, 5).Value = line.Method.ToString();
            sheet.Cell(row, 6).Value = line.Amount;
            sheet.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
            row++;
        }
        sheet.Cell(row, 5).Value = "Total / الإجمالي";
        sheet.Cell(row, 5).Style.Font.Bold = true;
        sheet.Cell(row, 6).Value = batch.Lines.Sum(x => x.Amount);
        sheet.Cell(row, 6).Style.Font.Bold = true;
        sheet.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(8, 45);
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreatePaymentCsv(RiderPaymentBatch batch)
    {
        var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 1024, leaveOpen: true))
        {
            writer.WriteLine("Batch,RiderIqama,IBAN,Housing,Method,AmountSAR");
            foreach (var line in batch.Lines.OrderBy(x => x.HousingId).ThenBy(x => x.RiderPayrollLine.RiderIqamaNo))
                writer.WriteLine(string.Join(',', Csv(batch.BatchNumber), Csv(line.RiderPayrollLine.RiderIqamaNo.ToString()), Csv(line.IbanSnapshot ?? string.Empty), Csv(line.HousingId?.ToString() ?? string.Empty), Csv(line.Method.ToString()), line.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
            writer.Flush();
        }
        stream.Position = 0;
        return stream;
    }

    private static string Csv(string value)
    {
        var safe = FormulaSafe(value);
        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }

    private static string FormulaSafe(string value) => !string.IsNullOrEmpty(value) && value[0] is '=' or '+' or '-' or '@' ? $"'{value}" : value;

    private static AccountingFileResponse ToFileResponse(AccountingStoredFile file) =>
        new(file.Id, file.LegalEntityId, file.OriginalFileName, file.ContentType, file.PlaintextLength, file.Sha256, file.RetainUntil, file.CreatedAt);

    private static HousingCashAccessResponse ToResponse(HousingCashUserAccess access) => new(access.Id, access.UserId, access.LegalEntityId, access.HousingId, access.IsActive, access.GrantedBy, access.GrantedAt, Convert.ToBase64String(access.RowVersion));

    private async Task<DimensionContext> LoadDimensionContextAsync(RiderPayrollRun run, CancellationToken ct)
    {
        var values = await dbcontext.FinancialDimensionValues.AsNoTracking().Include(x => x.FinancialDimension).Where(x => x.FinancialDimension.LegalEntityId == run.LegalEntityId && x.IsActive && x.FinancialDimension.IsActive).ToListAsync(ct);
        var platformCodes = await dbcontext.PlatformAccounts.AsNoTracking().Where(x => x.LegalEntityId == run.LegalEntityId).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var riderIds = run.Lines.Select(x => x.RiderIqamaNo).ToArray();
        var housing = await dbcontext.Employees.AsNoTracking().Where(x => riderIds.Contains(x.IqamaNo)).ToDictionaryAsync(x => x.IqamaNo, x => x.HousingId, ct);
        return new DimensionContext(values, platformCodes, housing);
    }

    private static string PostingEventCode(RiderPayrollComponent component, IReadOnlyDictionary<Guid, RiderFinancialItemType> itemTypes)
    {
        if (component.Source == RiderPayrollComponentSource.FinancialItem && component.RiderFinancialItemId.HasValue)
        {
            var type = itemTypes[component.RiderFinancialItemId.Value];
            return $"PAYROLL_ITEM_{NormalizeCode(type.Code)}_{(type.Direction == RiderFinancialItemDirection.Earning ? "EARNING" : "DEDUCTION")}";
        }
        if (component.Source == RiderPayrollComponentSource.Adjustment) return component.ComponentType == CompensationComponentType.Deduction ? "PAYROLL_ADJUSTMENT_DEDUCTION" : "PAYROLL_ADJUSTMENT_EARNING";
        if (component.ComponentType == CompensationComponentType.Deduction || component.Source == RiderPayrollComponentSource.CarryForward) return "PAYROLL_POLICY_DEDUCTION";
        return $"PAYROLL_{component.ComponentType.ToString().ToUpperInvariant()}";
    }

    private static decimal DueAmount(RiderFinancialItem item, DateOnly throughDate) => item.Installments.Count == 0 ? item.OutstandingAmount : Math.Min(item.OutstandingAmount, item.Installments.Where(x => x.DueDate <= throughDate && !x.IsSettled).Sum(x => x.ScheduledAmount - x.AppliedAmount));
    private static void ApplyToFinancialItem(RiderFinancialItem item, decimal amount)
    {
        var remaining = amount;
        foreach (var installment in item.Installments.Where(x => !x.IsSettled).OrderBy(x => x.DueDate).ThenBy(x => x.Sequence))
        {
            var applied = Math.Min(remaining, installment.ScheduledAmount - installment.AppliedAmount);
            installment.AppliedAmount += applied;
            installment.IsSettled = installment.AppliedAmount >= installment.ScheduledAmount;
            remaining -= applied;
            if (remaining <= 0) break;
        }
        item.OutstandingAmount -= amount;
        if (item.OutstandingAmount <= 0) { item.OutstandingAmount = 0; item.Status = RiderFinancialItemStatus.Settled; }
    }
    private static void RestoreFinancialItem(RiderFinancialItem item, decimal amount)
    {
        var remaining = amount;
        foreach (var installment in item.Installments.Where(x => x.AppliedAmount > 0).OrderByDescending(x => x.DueDate).ThenByDescending(x => x.Sequence))
        {
            var restored = Math.Min(remaining, installment.AppliedAmount);
            installment.AppliedAmount -= restored;
            installment.IsSettled = installment.AppliedAmount >= installment.ScheduledAmount;
            remaining -= restored;
            if (remaining <= 0) break;
        }
        item.OutstandingAmount = Math.Min(item.OriginalAmount, item.OutstandingAmount + amount);
        item.Status = RiderFinancialItemStatus.Open;
    }
    private static int ReadPriority(string json) { try { using var document = JsonDocument.Parse(json); return document.RootElement.TryGetProperty("Priority", out var value) || document.RootElement.TryGetProperty("priority", out value) ? value.GetInt32() : 0; } catch (JsonException) { return 0; } }
    private static bool MatchesRowVersion(string? supplied, byte[] actual) { if (string.IsNullOrWhiteSpace(supplied)) return true; try { return Convert.FromBase64String(supplied).SequenceEqual(actual); } catch (FormatException) { return false; } }
    private static void RefreshTotals(RiderPayrollRun run) { run.GrossEarnings = run.Lines.Sum(x => x.GrossEarnings); run.AppliedDeductions = run.Lines.Sum(x => x.AppliedDeductions); run.CarriedDeductions = run.Lines.Sum(x => x.CarriedDeductions); run.NetPay = run.Lines.Sum(x => x.NetPay); }
    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant().Replace(' ', '_');
    private static string? NormalizeIban(string? value) => string.IsNullOrWhiteSpace(value) ? null : new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    private static bool IsSaudiIban(string? value) => value is { Length: 24 } && value.StartsWith("SA", StringComparison.Ordinal) && value[2..].All(char.IsDigit);

    private static IOrderedQueryable<RiderPayrollRun> ApplyRunOrdering(IQueryable<RiderPayrollRun> query, string? sortBy, string? sortDirection)
    {
        var ascending = string.Equals(sortDirection?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);
        var field = sortBy?.Trim().ToLowerInvariant();
        IOrderedQueryable<RiderPayrollRun> ordered = (field, ascending) switch
        {
            ("runnumber", true) => query.OrderBy(x => x.RunNumber),
            ("runnumber", false) => query.OrderByDescending(x => x.RunNumber),
            ("periodstart", true) => query.OrderBy(x => x.PeriodStart),
            ("periodstart", false) => query.OrderByDescending(x => x.PeriodStart),
            ("periodend", true) => query.OrderBy(x => x.PeriodEnd),
            ("status", true) => query.OrderBy(x => x.Status),
            ("status", false) => query.OrderByDescending(x => x.Status),
            ("grossearnings", true) => query.OrderBy(x => x.GrossEarnings),
            ("grossearnings", false) => query.OrderByDescending(x => x.GrossEarnings),
            ("netpay", true) => query.OrderBy(x => x.NetPay),
            ("netpay", false) => query.OrderByDescending(x => x.NetPay),
            ("createdat", true) => query.OrderBy(x => x.CreatedAt),
            ("createdat", false) => query.OrderByDescending(x => x.CreatedAt),
            _ => query.OrderByDescending(x => x.PeriodEnd)
        };
        return ordered.ThenBy(x => x.Id);
    }

    private static IOrderedQueryable<RiderFinancialItemType> ApplyItemTypeOrdering(IQueryable<RiderFinancialItemType> query, string? sortBy, string? sortDirection)
    {
        var ascending = string.Equals(sortDirection?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);
        var field = sortBy?.Trim().ToLowerInvariant();
        IOrderedQueryable<RiderFinancialItemType> ordered = (field, ascending) switch
        {
            ("code", true) => query.OrderBy(x => x.Code),
            ("code", false) => query.OrderByDescending(x => x.Code),
            ("name", true) => query.OrderBy(x => x.Name),
            ("name", false) => query.OrderByDescending(x => x.Name),
            ("direction", true) => query.OrderBy(x => x.Direction),
            ("direction", false) => query.OrderByDescending(x => x.Direction),
            ("priority", true) => query.OrderBy(x => x.Priority),
            ("priority", false) => query.OrderByDescending(x => x.Priority),
            ("active", true) or ("isactive", true) => query.OrderBy(x => x.IsActive),
            ("active", false) or ("isactive", false) => query.OrderByDescending(x => x.IsActive),
            ("id", true) => query.OrderBy(x => x.Id),
            _ => query.OrderByDescending(x => x.Id)
        };
        return ordered.ThenBy(x => x.Id);
    }

    private static IOrderedQueryable<RiderFinancialItem> ApplyFinancialItemOrdering(IQueryable<RiderFinancialItem> query, string? sortBy, string? sortDirection)
    {
        var ascending = string.Equals(sortDirection?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);
        var field = sortBy?.Trim().ToLowerInvariant();
        IOrderedQueryable<RiderFinancialItem> ordered = (field, ascending) switch
        {
            ("reference", true) => query.OrderBy(x => x.Reference),
            ("reference", false) => query.OrderByDescending(x => x.Reference),
            ("description", true) => query.OrderBy(x => x.Description),
            ("description", false) => query.OrderByDescending(x => x.Description),
            ("effectivedate", true) => query.OrderBy(x => x.EffectiveDate),
            ("status", true) => query.OrderBy(x => x.Status),
            ("status", false) => query.OrderByDescending(x => x.Status),
            ("rideriqamano", true) => query.OrderBy(x => x.RiderIqamaNo),
            ("rideriqamano", false) => query.OrderByDescending(x => x.RiderIqamaNo),
            ("typeid", true) => query.OrderBy(x => x.RiderFinancialItemTypeId),
            ("typeid", false) => query.OrderByDescending(x => x.RiderFinancialItemTypeId),
            ("originalamount", true) => query.OrderBy(x => x.OriginalAmount),
            ("originalamount", false) => query.OrderByDescending(x => x.OriginalAmount),
            ("outstandingamount", true) => query.OrderBy(x => x.OutstandingAmount),
            ("outstandingamount", false) => query.OrderByDescending(x => x.OutstandingAmount),
            _ => query.OrderByDescending(x => x.EffectiveDate)
        };
        return ordered.ThenBy(x => x.Id);
    }

    private static IOrderedQueryable<RiderPaymentBatch> ApplyPaymentBatchOrdering(IQueryable<RiderPaymentBatch> query, string? sortBy, string? sortDirection)
    {
        var ascending = string.Equals(sortDirection?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);
        var field = sortBy?.Trim().ToLowerInvariant();
        IOrderedQueryable<RiderPaymentBatch> ordered = (field, ascending) switch
        {
            ("batchnumber", true) => query.OrderBy(x => x.BatchNumber),
            ("batchnumber", false) => query.OrderByDescending(x => x.BatchNumber),
            ("method", true) => query.OrderBy(x => x.Method),
            ("method", false) => query.OrderByDescending(x => x.Method),
            ("status", true) => query.OrderBy(x => x.Status),
            ("status", false) => query.OrderByDescending(x => x.Status),
            ("runid", true) or ("riderpayrollrunid", true) => query.OrderBy(x => x.RiderPayrollRunId),
            ("runid", false) or ("riderpayrollrunid", false) => query.OrderByDescending(x => x.RiderPayrollRunId),
            ("createdat", true) => query.OrderBy(x => x.CreatedAt),
            _ => query.OrderByDescending(x => x.CreatedAt)
        };
        return ordered.ThenBy(x => x.Id);
    }

    private static IOrderedQueryable<HousingCashUserAccess> ApplyHousingAccessOrdering(IQueryable<HousingCashUserAccess> query, string? sortBy, string? sortDirection)
    {
        var ascending = string.Equals(sortDirection?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);
        var field = sortBy?.Trim().ToLowerInvariant();
        IOrderedQueryable<HousingCashUserAccess> ordered = (field, ascending) switch
        {
            ("userid", true) => query.OrderBy(x => x.UserId),
            ("userid", false) => query.OrderByDescending(x => x.UserId),
            ("housingid", true) => query.OrderBy(x => x.HousingId),
            ("housingid", false) => query.OrderByDescending(x => x.HousingId),
            ("active", true) or ("isactive", true) => query.OrderBy(x => x.IsActive),
            ("active", false) or ("isactive", false) => query.OrderByDescending(x => x.IsActive),
            ("grantedat", true) => query.OrderBy(x => x.GrantedAt),
            _ => query.OrderByDescending(x => x.GrantedAt)
        };
        return ordered.ThenBy(x => x.Id);
    }

    private async Task AppendAuditAsync(int entityId, string eventType, string actorId, object payload, CancellationToken ct)
    {
        if (dbcontext.Database.IsSqlServer() && dbcontext.Database.CurrentTransaction is not null)
            await dbcontext.Database.ExecuteSqlInterpolatedAsync($"EXEC sp_getapplock @Resource={"Accounting:AuditChain:" + entityId}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000", ct);
        var head = await dbcontext.AccountingAuditChainHeads.SingleOrDefaultAsync(x => x.LegalEntityId == entityId, ct);
        if (head is null)
        {
            head = new AccountingAuditChainHead { LegalEntityId = entityId };
            dbcontext.AccountingAuditChainHeads.Add(head);
        }
        var json = JsonSerializer.Serialize(payload);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{head.LastHash}|{entityId}||{eventType}|{actorId}|{json}")));
        dbcontext.AccountingAuditEvents.Add(new AccountingAuditEvent { LegalEntityId = entityId, EventType = eventType, ActorId = actorId, PayloadJson = json, PreviousHash = head.LastHash, Hash = hash });
        dbcontext.AccountingOutboxMessages.Add(new AccountingOutboxMessage { LegalEntityId = entityId, Type = eventType, PayloadJson = json, CorrelationId = Guid.NewGuid().ToString("N") });
        head.LastHash = hash;
    }

    private sealed record DeductionCandidate(RiderPayrollComponentSource Source, string Code, string Description, decimal RequestedAmount, int Priority, int? PlatformAccountId, Guid? PolicyId, Guid? RuleId, Guid? SourceBatchId, Guid? FinancialItemId, Guid? CarryForwardId, string CalculationJson);
    private sealed class DimensionContext(IReadOnlyCollection<FinancialDimensionValue> values, IReadOnlyDictionary<int, string> platforms, IReadOnlyDictionary<long, int?> housing)
    {
        public IReadOnlyCollection<int> For(long riderIqamaNo, int? platformAccountId)
        {
            var codes = new List<(string Dimension, string Value)> { ("RIDER", riderIqamaNo.ToString()) };
            if (platformAccountId.HasValue && platforms.TryGetValue(platformAccountId.Value, out var platform)) codes.Add(("PLATFORM", platform));
            if (housing.TryGetValue(riderIqamaNo, out var housingId) && housingId.HasValue) codes.Add(("HOUSING", housingId.Value.ToString()));
            return values.Where(x => codes.Any(c => string.Equals(c.Dimension, x.FinancialDimension.Code, StringComparison.OrdinalIgnoreCase) && string.Equals(c.Value, x.Code, StringComparison.OrdinalIgnoreCase))).Select(x => x.Id).Distinct().ToArray();
        }
    }
}
