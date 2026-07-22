using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Common;
using Application.Contracts.Compensation;
using Application.Service.FinancialAccess;
using Domain;
using Domain.Entities.AccountingCore;
using Domain.Entities.AccountingPlatform;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.Compensation;

public class CompensationService(ApplicationDbcontext dbcontext, IFinancialAccessService financialAccessService) : ICompensationService
{
    public static readonly IReadOnlySet<string> AllowedMetrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ACCEPTED_ORDERS", "REJECTED_ORDERS", "TOTAL_ORDERS", "WORK_DAYS", "CONNECTION_HOURS", "DISTANCE_KM",
        "BASE_AMOUNT", "INCENTIVES", "PENALTIES", "FEES", "VAT", "COMPANY_TOTAL", "VALIDITY", "RIDER_PAYOUT",
        "NET_SETTLEMENT", "INVOICE_AMOUNT", "EID_DAYS", "EID_OVERTIME_AMOUNT"
    };

    public async Task<Result<PagedResponse<CompensationPolicyResponse>>> GetPoliciesAsync(
        PaginationRequest pagination,
        int legalEntityId,
        int? platformAccountId,
        string? category,
        CompensationPolicyStatus? status,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? search,
        string? sortBy,
        string? sortDirection,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var access = await financialAccessService.EnsurePermissionAsync(actorId, legalEntityId, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<PagedResponse<CompensationPolicyResponse>>(access.Error);
        if (toDate < fromDate) return Result.Failure<PagedResponse<CompensationPolicyResponse>>(AccountingPlatformErrors.InvalidRequest);

        var query = dbcontext.CompensationPolicyVersions
            .AsNoTracking()
            .Where(x => x.LegalEntityId == legalEntityId);

        if (platformAccountId.HasValue) query = query.Where(x => x.PlatformAccountId == platformAccountId.Value);
        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalizedCategory = category.Trim().ToUpperInvariant();
            query = query.Where(x => x.WorkerCategory.ToUpper() == normalizedCategory);
        }
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (fromDate.HasValue) query = query.Where(x => x.EffectiveTo == null || x.EffectiveTo >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(x => x.EffectiveFrom <= toDate.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToUpperInvariant();
            query = query.Where(x => x.Code.ToUpper().Contains(normalizedSearch) || x.Name.ToUpper().Contains(normalizedSearch));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = pagination.NormalizedPageNumber;
        var pageSize = pagination.NormalizedPageSize;
        var policies = await ApplyPolicyOrdering(query, sortBy, sortDirection)
            .Include(x => x.Rules)
            .AsSplitQuery()
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResponse<CompensationPolicyResponse>(policies.Select(ToResponse).ToArray(), pageNumber, pageSize, totalCount));
    }

    public async Task<Result<CompensationPolicyResponse>> CreatePolicyAsync(CreateCompensationPolicyRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var access = await financialAccessService.EnsurePermissionAsync(actorId, request.LegalEntityId, FinancialPermission.Configure, cancellationToken);
        if (access.IsFailure) return Result.Failure<CompensationPolicyResponse>(access.Error);

        if (request.EffectiveTo < request.EffectiveFrom || request.Rules.Count == 0 ||
            !await dbcontext.PlatformAccounts.AnyAsync(x => x.Id == request.PlatformAccountId && x.LegalEntityId == request.LegalEntityId && x.IsActive, cancellationToken))
            return Result.Failure<CompensationPolicyResponse>(AccountingPlatformErrors.InvalidRequest);

        foreach (var rule in request.Rules)
        {
            if (!AllowedMetrics.Contains(rule.MetricCode) ||
                (!string.IsNullOrWhiteSpace(rule.ConditionMetricCode) && !AllowedMetrics.Contains(rule.ConditionMetricCode)))
                return Result.Failure<CompensationPolicyResponse>(AccountingPlatformErrors.UnsupportedMetric);
            if (!IsRuleValid(rule)) return Result.Failure<CompensationPolicyResponse>(AccountingPlatformErrors.InvalidRequest);
        }

        var code = NormalizeCode(request.Code);
        var workerCategory = request.WorkerCategory.Trim();
        var version = (await dbcontext.CompensationPolicyVersions
            .Where(x => x.LegalEntityId == request.LegalEntityId && x.PlatformAccountId == request.PlatformAccountId && x.WorkerCategory == workerCategory && x.Code == code)
            .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;

        var policy = new CompensationPolicyVersion
        {
            LegalEntityId = request.LegalEntityId,
            PlatformAccountId = request.PlatformAccountId,
            WorkerCategory = workerCategory,
            Code = code,
            Version = version,
            Name = request.Name.Trim(),
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            CreatedBy = actorId,
            Rules = request.Rules.Select(ToEntity).ToList()
        };

        dbcontext.CompensationPolicyVersions.Add(policy);
        await AppendAuditAsync(policy.LegalEntityId, "Compensation.PolicyCreated", actorId, new { policy.Id, policy.Code, policy.Version, policy.EffectiveFrom, policy.EffectiveTo }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToResponse(policy));
    }

    public async Task<Result<CompensationPolicyResponse>> CloneVersionAsync(Guid id, CloneCompensationPolicyVersionRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var source = await dbcontext.CompensationPolicyVersions
            .AsNoTracking()
            .Include(x => x.Rules)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (source is null) return Result.Failure<CompensationPolicyResponse>(AccountingPlatformErrors.NotFound);

        var access = await financialAccessService.EnsurePermissionAsync(actorId, source.LegalEntityId, FinancialPermission.Configure, cancellationToken);
        if (access.IsFailure) return Result.Failure<CompensationPolicyResponse>(access.Error);
        if (request.EffectiveTo < request.EffectiveFrom)
            return Result.Failure<CompensationPolicyResponse>(AccountingPlatformErrors.InvalidRequest);

        var version = (await dbcontext.CompensationPolicyVersions
            .Where(x => x.LegalEntityId == source.LegalEntityId && x.PlatformAccountId == source.PlatformAccountId &&
                x.WorkerCategory == source.WorkerCategory && x.Code == source.Code)
            .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;

        var clone = new CompensationPolicyVersion
        {
            LegalEntityId = source.LegalEntityId,
            PlatformAccountId = source.PlatformAccountId,
            WorkerCategory = source.WorkerCategory,
            Code = source.Code,
            Version = version,
            Name = source.Name,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            Status = CompensationPolicyStatus.Draft,
            CreatedBy = actorId,
            Rules = source.Rules.Select(rule => new CompensationRule
            {
                Code = rule.Code,
                Name = rule.Name,
                Template = rule.Template,
                ComponentType = rule.ComponentType,
                MetricCode = rule.MetricCode,
                ConditionMetricCode = rule.ConditionMetricCode,
                ConditionOperator = rule.ConditionOperator,
                ConditionValue = rule.ConditionValue,
                LowerBound = rule.LowerBound,
                UpperBound = rule.UpperBound,
                Rate = rule.Rate,
                BelowRate = rule.BelowRate,
                AboveRate = rule.AboveRate,
                FixedAmount = rule.FixedAmount,
                BaseAmount = rule.BaseAmount,
                TargetComponentCode = rule.TargetComponentCode,
                Priority = rule.Priority,
                ExclusiveGroup = rule.ExclusiveGroup,
                StackingMode = rule.StackingMode,
                RoundingScale = rule.RoundingScale,
                IsActive = rule.IsActive
            }).ToList()
        };

        dbcontext.CompensationPolicyVersions.Add(clone);
        await AppendAuditAsync(clone.LegalEntityId, "Compensation.PolicyVersionCloned", actorId, new
        {
            SourcePolicyId = source.Id,
            clone.Id,
            clone.Code,
            clone.Version,
            clone.EffectiveFrom,
            clone.EffectiveTo
        }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToResponse(clone));
    }

    public async Task<Result<CompensationPolicyResponse>> ActivatePolicyAsync(Guid id, ActivateCompensationPolicyRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var policy = await dbcontext.CompensationPolicyVersions.Include(x => x.Rules).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (policy is null) return Result.Failure<CompensationPolicyResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, policy.LegalEntityId, FinancialPermission.Configure, cancellationToken);
        if (access.IsFailure) return Result.Failure<CompensationPolicyResponse>(access.Error);
        if (policy.Status != CompensationPolicyStatus.Draft) return Result.Failure<CompensationPolicyResponse>(AccountingPlatformErrors.InvalidState);

        if (!string.IsNullOrWhiteSpace(request.RowVersion))
        {
            byte[] supplied;
            try { supplied = Convert.FromBase64String(request.RowVersion); }
            catch (FormatException) { return Result.Failure<CompensationPolicyResponse>(AccountingPlatformErrors.ConcurrencyConflict); }
            if (!supplied.SequenceEqual(policy.RowVersion)) return Result.Failure<CompensationPolicyResponse>(AccountingPlatformErrors.ConcurrencyConflict);
        }

        var end = policy.EffectiveTo ?? DateOnly.MaxValue;
        var overlaps = await dbcontext.CompensationPolicyVersions.AnyAsync(x =>
            x.Id != policy.Id && x.LegalEntityId == policy.LegalEntityId && x.PlatformAccountId == policy.PlatformAccountId &&
            x.WorkerCategory == policy.WorkerCategory && x.Status == CompensationPolicyStatus.Active &&
            x.EffectiveFrom <= end && (x.EffectiveTo == null || x.EffectiveTo >= policy.EffectiveFrom), cancellationToken);
        if (overlaps) return Result.Failure<CompensationPolicyResponse>(AccountingPlatformErrors.PolicyOverlap);

        policy.Status = CompensationPolicyStatus.Active;
        policy.ActivatedBy = actorId;
        policy.ActivatedAt = DateTime.UtcNow;
        await AppendAuditAsync(policy.LegalEntityId, "Compensation.PolicyActivated", actorId, new { policy.Id, policy.Code, policy.Version }, cancellationToken);
        try
        {
            await dbcontext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<CompensationPolicyResponse>(AccountingPlatformErrors.ConcurrencyConflict);
        }
        return Result.Success(ToResponse(policy));
    }

    public async Task<Result<CompensationPolicyResponse>> RetirePolicyAsync(Guid id, RetireCompensationPolicyRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var policy = await dbcontext.CompensationPolicyVersions
            .Include(x => x.Rules)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (policy is null) return Result.Failure<CompensationPolicyResponse>(AccountingPlatformErrors.NotFound);

        var access = await financialAccessService.EnsurePermissionAsync(actorId, policy.LegalEntityId, FinancialPermission.Configure, cancellationToken);
        if (access.IsFailure) return Result.Failure<CompensationPolicyResponse>(access.Error);
        if (!MatchesRowVersion(request.RowVersion, policy.RowVersion))
            return Result.Failure<CompensationPolicyResponse>(AccountingPlatformErrors.ConcurrencyConflict);
        if (policy.Status == CompensationPolicyStatus.Retired)
            return Result.Success(ToResponse(policy));

        policy.Status = CompensationPolicyStatus.Retired;
        await AppendAuditAsync(policy.LegalEntityId, "Compensation.PolicyRetired", actorId, new
        {
            policy.Id,
            policy.Code,
            policy.Version,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim()
        }, cancellationToken);
        try
        {
            await dbcontext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<CompensationPolicyResponse>(AccountingPlatformErrors.ConcurrencyConflict);
        }
        return Result.Success(ToResponse(policy));
    }

    public async Task<Result<CompensationPolicyResponse>> GetPolicyAsync(Guid id, string actorId, CancellationToken cancellationToken = default)
    {
        var policy = await dbcontext.CompensationPolicyVersions.AsNoTracking().Include(x => x.Rules).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (policy is null) return Result.Failure<CompensationPolicyResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, policy.LegalEntityId, FinancialPermission.View, cancellationToken);
        return access.IsFailure ? Result.Failure<CompensationPolicyResponse>(access.Error) : Result.Success(ToResponse(policy));
    }

    public async Task<Result<CompensationSimulationResponse>> SimulateAsync(Guid id, SimulateCompensationPolicyRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var policy = await dbcontext.CompensationPolicyVersions.AsNoTracking().Include(x => x.Rules).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (policy is null) return Result.Failure<CompensationSimulationResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, policy.LegalEntityId, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<CompensationSimulationResponse>(access.Error);
        if (request.Metrics.Keys.Any(x => !AllowedMetrics.Contains(x))) return Result.Failure<CompensationSimulationResponse>(AccountingPlatformErrors.UnsupportedMetric);
        return Result.Success(Evaluate(policy, request.Metrics));
    }

    public static CompensationSimulationResponse Evaluate(CompensationPolicyVersion policy, IReadOnlyDictionary<string, decimal> suppliedMetrics)
    {
        var metrics = suppliedMetrics.ToDictionary(x => NormalizeCode(x.Key), x => x.Value, StringComparer.OrdinalIgnoreCase);
        var candidates = new List<CompensationSimulationComponentResponse>();
        var conflicts = new List<string>();

        foreach (var rule in policy.Rules.Where(x => x.IsActive).OrderBy(x => x.Priority).ThenBy(x => x.Code))
        {
            if (!ConditionMatches(rule, metrics)) continue;
            var quantity = metrics.GetValueOrDefault(NormalizeCode(rule.MetricCode));
            var amount = Calculate(rule, quantity, candidates.Where(x => x.Selected).ToArray());
            if (amount is null) continue;
            amount = Math.Round(amount.Value, rule.RoundingScale, MidpointRounding.AwayFromZero);
            candidates.Add(new CompensationSimulationComponentResponse(rule.Id, rule.Code, rule.Name, rule.ComponentType, quantity, RuleRate(rule), amount.Value, true, Explain(rule, quantity, amount.Value)));
        }

        foreach (var group in candidates.Where(x => policy.Rules.Single(r => r.Id == x.RuleId).StackingMode == CompensationStackingMode.ExclusiveHighest && !string.IsNullOrWhiteSpace(policy.Rules.Single(r => r.Id == x.RuleId).ExclusiveGroup)).GroupBy(x => policy.Rules.Single(r => r.Id == x.RuleId).ExclusiveGroup!, StringComparer.OrdinalIgnoreCase))
        {
            var winner = group.OrderByDescending(x => x.Amount).ThenBy(x => policy.Rules.Single(r => r.Id == x.RuleId).Priority).First();
            for (var i = 0; i < candidates.Count; i++)
                if (group.Any(x => x.RuleId == candidates[i].RuleId) && candidates[i].RuleId != winner.RuleId)
                    candidates[i] = candidates[i] with { Selected = false, Explanation = $"Excluded by exclusive group; {winner.RuleCode} produced the highest eligible amount." };
        }

        foreach (var group in policy.Rules.Where(x => x.IsActive && !string.IsNullOrWhiteSpace(x.ExclusiveGroup)).GroupBy(x => x.ExclusiveGroup!, StringComparer.OrdinalIgnoreCase))
            if (group.Any(x => x.StackingMode == CompensationStackingMode.Cumulative) && group.Any(x => x.StackingMode == CompensationStackingMode.ExclusiveHighest))
                conflicts.Add($"Exclusive group {group.Key} mixes cumulative and exclusive stacking modes.");

        var selected = candidates.Where(x => x.Selected).ToArray();
        var earnings = selected.Where(x => x.ComponentType is CompensationComponentType.Earning or CompensationComponentType.Allowance or CompensationComponentType.Bonus).Sum(x => x.Amount);
        var deductions = selected.Where(x => x.ComponentType == CompensationComponentType.Deduction).Sum(x => Math.Abs(x.Amount));
        return new CompensationSimulationResponse(policy.Id, metrics, candidates, earnings, deductions, earnings - deductions, conflicts);
    }

    private static CompensationRule ToEntity(CreateCompensationRuleRequest x) => new()
    {
        Code = NormalizeCode(x.Code), Name = x.Name.Trim(), Template = x.Template, ComponentType = x.ComponentType,
        MetricCode = NormalizeCode(x.MetricCode), ConditionMetricCode = NormalizeOptionalCode(x.ConditionMetricCode), ConditionOperator = x.ConditionOperator?.Trim(),
        ConditionValue = x.ConditionValue, LowerBound = x.LowerBound, UpperBound = x.UpperBound, Rate = x.Rate, BelowRate = x.BelowRate,
        AboveRate = x.AboveRate, FixedAmount = x.FixedAmount, BaseAmount = x.BaseAmount, TargetComponentCode = NormalizeOptionalCode(x.TargetComponentCode),
        Priority = x.Priority, ExclusiveGroup = string.IsNullOrWhiteSpace(x.ExclusiveGroup) ? null : x.ExclusiveGroup.Trim(), StackingMode = x.StackingMode, RoundingScale = x.RoundingScale
    };

    private static bool IsRuleValid(CreateCompensationRuleRequest rule) => rule.Template switch
    {
        CompensationRuleTemplate.FixedAmount => rule.FixedAmount.HasValue,
        CompensationRuleTemplate.PerUnit => rule.Rate.HasValue,
        CompensationRuleTemplate.Threshold => rule.ConditionValue.HasValue && rule.FixedAmount.HasValue,
        CompensationRuleTemplate.TieredBasePlusExcess => rule.ConditionValue.HasValue && rule.BelowRate.HasValue && rule.AboveRate.HasValue && rule.BaseAmount.HasValue,
        CompensationRuleTemplate.Percentage => rule.Rate.HasValue,
        CompensationRuleTemplate.Range => rule.LowerBound.HasValue && rule.UpperBound.HasValue && rule.LowerBound <= rule.UpperBound && (rule.FixedAmount.HasValue || rule.Rate.HasValue),
        CompensationRuleTemplate.Cap or CompensationRuleTemplate.Floor => rule.FixedAmount.HasValue && !string.IsNullOrWhiteSpace(rule.TargetComponentCode),
        CompensationRuleTemplate.EligibilityCondition => rule.ConditionValue.HasValue && !string.IsNullOrWhiteSpace(rule.ConditionOperator),
        _ => false
    };

    private static bool ConditionMatches(CompensationRule rule, IReadOnlyDictionary<string, decimal> metrics)
    {
        if (string.IsNullOrWhiteSpace(rule.ConditionMetricCode)) return true;
        var actual = metrics.GetValueOrDefault(NormalizeCode(rule.ConditionMetricCode));
        var expected = rule.ConditionValue ?? 0m;
        return Compare(actual, rule.ConditionOperator ?? ">=", expected);
    }

    private static decimal? Calculate(CompensationRule rule, decimal quantity, IReadOnlyCollection<CompensationSimulationComponentResponse> selected) => rule.Template switch
    {
        CompensationRuleTemplate.FixedAmount => rule.FixedAmount,
        CompensationRuleTemplate.PerUnit => quantity * rule.Rate!.Value,
        CompensationRuleTemplate.Threshold => Compare(quantity, rule.ConditionOperator ?? ">=", rule.ConditionValue!.Value) ? rule.FixedAmount : null,
        CompensationRuleTemplate.TieredBasePlusExcess => quantity < rule.ConditionValue!.Value
            ? quantity * rule.BelowRate!.Value
            : rule.BaseAmount!.Value + ((quantity - rule.ConditionValue.Value) * rule.AboveRate!.Value),
        CompensationRuleTemplate.Percentage => quantity * rule.Rate!.Value / 100m,
        CompensationRuleTemplate.Range => quantity >= rule.LowerBound && quantity <= rule.UpperBound ? rule.FixedAmount ?? quantity * rule.Rate!.Value : null,
        CompensationRuleTemplate.Cap => Math.Min(selected.Where(x => string.Equals(x.RuleCode, rule.TargetComponentCode, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount), rule.FixedAmount!.Value),
        CompensationRuleTemplate.Floor => Math.Max(selected.Where(x => string.Equals(x.RuleCode, rule.TargetComponentCode, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount), rule.FixedAmount!.Value),
        CompensationRuleTemplate.EligibilityCondition => Compare(quantity, rule.ConditionOperator ?? ">=", rule.ConditionValue!.Value) ? 0m : null,
        _ => null
    };

    private static bool Compare(decimal actual, string op, decimal expected) => op.Trim() switch
    {
        ">" => actual > expected, ">=" => actual >= expected, "<" => actual < expected, "<=" => actual <= expected,
        "=" or "==" => actual == expected, "!=" or "<>" => actual != expected, _ => false
    };

    private static decimal RuleRate(CompensationRule rule) => rule.Rate ?? rule.AboveRate ?? rule.BelowRate ?? 0m;
    private static string Explain(CompensationRule rule, decimal quantity, decimal amount) => $"{rule.Template}: metric {rule.MetricCode}={quantity:0.####}; result={amount:0.00}.";
    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
    private static string? NormalizeOptionalCode(string? value) => string.IsNullOrWhiteSpace(value) ? null : NormalizeCode(value);
    private static IOrderedQueryable<CompensationPolicyVersion> ApplyPolicyOrdering(
        IQueryable<CompensationPolicyVersion> query,
        string? sortBy,
        string? sortDirection)
    {
        var ascending = string.Equals(sortDirection?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);
        var field = sortBy?.Trim().ToLowerInvariant();
        IOrderedQueryable<CompensationPolicyVersion> ordered = (field, ascending) switch
        {
            ("code", true) => query.OrderBy(x => x.Code),
            ("code", false) => query.OrderByDescending(x => x.Code),
            ("name", true) => query.OrderBy(x => x.Name),
            ("name", false) => query.OrderByDescending(x => x.Name),
            ("version", true) => query.OrderBy(x => x.Version),
            ("version", false) => query.OrderByDescending(x => x.Version),
            ("status", true) => query.OrderBy(x => x.Status),
            ("status", false) => query.OrderByDescending(x => x.Status),
            ("platformaccountid", true) => query.OrderBy(x => x.PlatformAccountId),
            ("platformaccountid", false) => query.OrderByDescending(x => x.PlatformAccountId),
            ("category", true) or ("workercategory", true) => query.OrderBy(x => x.WorkerCategory),
            ("category", false) or ("workercategory", false) => query.OrderByDescending(x => x.WorkerCategory),
            ("effectiveto", true) => query.OrderBy(x => x.EffectiveTo),
            ("effectiveto", false) => query.OrderByDescending(x => x.EffectiveTo),
            ("effectivefrom", true) => query.OrderBy(x => x.EffectiveFrom),
            _ => query.OrderByDescending(x => x.EffectiveFrom)
        };
        return ordered.ThenBy(x => x.Id);
    }
    private static bool MatchesRowVersion(string? supplied, byte[] actual)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return true;
        try { return Convert.FromBase64String(supplied).SequenceEqual(actual); }
        catch (FormatException) { return false; }
    }

    private async Task AppendAuditAsync(int entityId, string eventType, string actorId, object payload, CancellationToken ct)
    {
        if (dbcontext.Database.IsSqlServer() && dbcontext.Database.CurrentTransaction is not null)
            await dbcontext.Database.ExecuteSqlInterpolatedAsync($"EXEC sp_getapplock @Resource={"Accounting:AuditChain:" + entityId}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000", ct);
        var head = await dbcontext.AccountingAuditChainHeads.SingleOrDefaultAsync(x => x.LegalEntityId == entityId, ct);
        if (head is null) { head = new AccountingAuditChainHead { LegalEntityId = entityId }; dbcontext.AccountingAuditChainHeads.Add(head); }
        var json = JsonSerializer.Serialize(payload);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{head.LastHash}|{entityId}||{eventType}|{actorId}|{json}")));
        dbcontext.AccountingAuditEvents.Add(new AccountingAuditEvent { LegalEntityId = entityId, EventType = eventType, ActorId = actorId, PayloadJson = json, PreviousHash = head.LastHash, Hash = hash });
        dbcontext.AccountingOutboxMessages.Add(new AccountingOutboxMessage { LegalEntityId = entityId, Type = eventType, PayloadJson = json, CorrelationId = hash[..32] });
        head.LastHash = hash;
    }

    private static CompensationPolicyResponse ToResponse(CompensationPolicyVersion x) => new(
        x.Id, x.LegalEntityId, x.PlatformAccountId, x.WorkerCategory, x.Code, x.Version, x.Name, x.EffectiveFrom, x.EffectiveTo, x.Status,
        Convert.ToBase64String(x.RowVersion), x.Rules.OrderBy(r => r.Priority).ThenBy(r => r.Code).Select(r => new CompensationRuleResponse(
            r.Id,
            r.CompensationPolicyVersionId,
            r.Code,
            r.Name,
            r.Template,
            r.ComponentType,
            r.MetricCode,
            r.ConditionMetricCode,
            r.ConditionOperator,
            r.ConditionValue,
            r.LowerBound,
            r.UpperBound,
            r.Rate,
            r.BelowRate,
            r.AboveRate,
            r.FixedAmount,
            r.BaseAmount,
            r.TargetComponentCode,
            r.Priority,
            r.ExclusiveGroup,
            r.StackingMode,
            r.RoundingScale,
            r.IsActive)).ToArray());
}
