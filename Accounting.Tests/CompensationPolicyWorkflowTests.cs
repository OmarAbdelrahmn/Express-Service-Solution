using Application.Abstraction;
using Application.Contracts.Compensation;
using Application.Contracts.FinancialAccess;
using Application.Service.Compensation;
using Application.Service.FinancialAccess;
using Domain;
using Domain.Entities.AccountingCore;
using Domain.Entities.AccountingPlatform;
using Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public class CompensationPolicyWorkflowTests
{
    [Fact]
    public async Task CloneVersion_CopiesEveryPersistedRuleFieldIntoNewDraft()
    {
        await using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = 1, Code = "T", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "E", LegalName = "Entity", BaseCurrencyCode = "SAR" });
        db.PlatformAccounts.Add(new PlatformAccount { Id = 1, LegalEntityId = 1, Code = "P", PlatformName = "Platform" });
        var source = new CompensationPolicyVersion
        {
            LegalEntityId = 1,
            PlatformAccountId = 1,
            WorkerCategory = "Rider",
            Code = "POLICY",
            Version = 3,
            Name = "Policy",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Status = CompensationPolicyStatus.Active,
            CreatedBy = "maker",
            Rules =
            [
                new CompensationRule
                {
                    Code = "RULE",
                    Name = "Rule",
                    Template = CompensationRuleTemplate.Range,
                    ComponentType = CompensationComponentType.Bonus,
                    MetricCode = "ACCEPTED_ORDERS",
                    ConditionMetricCode = "WORK_DAYS",
                    ConditionOperator = ">=",
                    ConditionValue = 20m,
                    LowerBound = 100m,
                    UpperBound = 500m,
                    Rate = 4m,
                    BelowRate = 2m,
                    AboveRate = 6m,
                    FixedAmount = 25m,
                    BaseAmount = 100m,
                    TargetComponentCode = "BASE",
                    Priority = 7,
                    ExclusiveGroup = "BONUS",
                    StackingMode = CompensationStackingMode.Cumulative,
                    RoundingScale = 3,
                    IsActive = false
                }
            ]
        };
        db.CompensationPolicyVersions.Add(source);
        await db.SaveChangesAsync();
        var service = new CompensationService(db, new AllowAllAccess());

        var result = await service.CloneVersionAsync(
            source.Id,
            new CloneCompensationPolicyVersionRequest(new DateOnly(2026, 8, 1), null),
            "accountant");

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.Version);
        Assert.Equal(CompensationPolicyStatus.Draft, result.Value.Status);
        var rule = Assert.Single(result.Value.Rules);
        Assert.Equal("WORK_DAYS", rule.ConditionMetricCode);
        Assert.Equal(100m, rule.LowerBound);
        Assert.Equal(500m, rule.UpperBound);
        Assert.Equal(2m, rule.BelowRate);
        Assert.Equal(6m, rule.AboveRate);
        Assert.Equal("BASE", rule.TargetComponentCode);
        Assert.Equal(CompensationStackingMode.Cumulative, rule.StackingMode);
        Assert.Equal(3, rule.RoundingScale);
        Assert.False(rule.IsActive);
    }

    private static ApplicationDbcontext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbcontext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class AllowAllAccess : IFinancialAccessService
    {
        public Task<Result> EnsurePermissionAsync(string userId, int legalEntityId, FinancialPermission requiredPermission, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result<FinancialUserAccessResponse>> GrantAsync(GrantFinancialUserAccessRequest request, string grantedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> RevokeAsync(string userId, int legalEntityId, string revokedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyCollection<FinancialUserAccessResponse>>> GetForLegalEntityAsync(int legalEntityId, string requestedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
