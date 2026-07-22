using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.FinancialAccess;
using Application.Service.AccountingPosting;
using Application.Service.FinancialAccess;
using Domain;
using Domain.Entities.AccountingCore;
using Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public class AccountingPostingServiceTests
{
    [Fact]
    public async Task Post_IsIdempotentForSameCanonicalRequest_AndConflictsForDifferentRequest()
    {
        await using var db = CreateDbContext();
        Seed(db);
        await db.SaveChangesAsync();
        var service = new AccountingPostingService(db, new AllowAllAccess());
        var command = Command(100m);

        var first = await service.PostAsync(command, "accountant");
        var replay = await service.PostAsync(command, "accountant");
        var conflict = await service.PostAsync(Command(101m), "accountant");
        var invalidPayloadConflict = await service.PostAsync(Command(0m), "accountant");
        var correlationConflict = await service.PostAsync(command with { CorrelationId = "different-correlation" }, "accountant");
        var missingCorrelationConflict = await service.PostAsync(command with { CorrelationId = "" }, "accountant");
        var payloadConflict = await service.PostAsync(command with { IdempotencyPayload = "{\"notes\":\"changed\"}" }, "accountant");

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value.Id, replay.Value.Id);
        Assert.Equal(FinancialDocumentStatus.Posted, first.Value.Status);
        Assert.True(conflict.IsFailure);
        Assert.Equal(LedgerErrors.IdempotencyConflict.Code, conflict.Error.Code);
        Assert.Equal(LedgerErrors.IdempotencyConflict.Code, invalidPayloadConflict.Error.Code);
        Assert.Equal(LedgerErrors.IdempotencyConflict.Code, correlationConflict.Error.Code);
        Assert.Equal(LedgerErrors.IdempotencyConflict.Code, missingCorrelationConflict.Error.Code);
        Assert.Equal(LedgerErrors.IdempotencyConflict.Code, payloadConflict.Error.Code);
        Assert.Single(await db.FinancialDocuments.ToListAsync());
        Assert.True(await db.JournalEntries.AllAsync(x => x.IsFinalized));
    }

    [Fact]
    public async Task Reverse_CopiesDimensions_SwapsBothCurrencies_AndIsIdempotent()
    {
        await using var db = CreateDbContext();
        Seed(db);
        await db.SaveChangesAsync();
        var service = new AccountingPostingService(db, new AllowAllAccess());
        var posted = await service.PostAsync(Command(100m), "accountant");
        var request = new ReverseSourceDocumentRequest(posted.Value.Id, new DateOnly(2026, 7, 15), "Correction", "reverse-1", "corr-reverse", AccountingModule.Payroll);

        var reversed = await service.ReverseAsync(request, "accountant");
        var replay = await service.ReverseAsync(request, "accountant");
        var conflict = await service.ReverseAsync(request with { CorrelationId = "different-correlation" }, "accountant");
        var invalidPayloadConflict = await service.ReverseAsync(request with { Reason = "" }, "accountant");
        var missingCorrelationConflict = await service.ReverseAsync(request with { CorrelationId = "" }, "accountant");

        Assert.True(reversed.IsSuccess);
        Assert.Equal(reversed.Value.Id, replay.Value.Id);
        Assert.Equal(LedgerErrors.IdempotencyConflict.Code, conflict.Error.Code);
        Assert.Equal(LedgerErrors.IdempotencyConflict.Code, invalidPayloadConflict.Error.Code);
        Assert.Equal(LedgerErrors.IdempotencyConflict.Code, missingCorrelationConflict.Error.Code);
        Assert.Equal(posted.Value.Id, reversed.Value.ReversalOfDocumentId);
        var original = await db.FinancialDocuments.SingleAsync(x => x.Id == posted.Value.Id);
        Assert.Equal(FinancialDocumentStatus.Reversed, original.Status);
        Assert.Equal(reversed.Value.Id, original.ReversedByDocumentId);
        var reversalLines = await db.FinancialDocumentLines.Where(x => x.FinancialDocumentId == reversed.Value.Id).OrderBy(x => x.LineNumber).ToListAsync();
        Assert.Equal(100m, reversalLines[0].Credit);
        Assert.Equal(100m, reversalLines[0].BaseCredit);
        Assert.Equal(2, await db.FinancialDocumentLineDimensions.CountAsync(x => reversalLines.Select(l => l.Id).Contains(x.FinancialDocumentLineId)));
        Assert.Equal(2, await db.JournalEntries.CountAsync());
        Assert.True(await db.JournalEntries.AllAsync(x => x.IsFinalized));
    }

    private static PostSourceDocumentRequest Command(decimal amount) => new(
        1, null, new DateOnly(2026, 7, 10), "Payroll", "RUN-1", "PAYROLL", "Monthly payroll", "SAR",
        "post-1", "corr-post", AccountingModule.Payroll,
        [new PostingEventAmount("PAYROLL_EARNING", amount, "Earning", [1])]);

    private static void Seed(ApplicationDbcontext db)
    {
        db.Tenants.Add(new Tenant { Id = 1, Code = "T", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "E", LegalName = "Entity", BaseCurrencyCode = "SAR" });
        db.Currencies.Add(new Currency { Code = "SAR", Name = "Saudi Riyal" });
        db.AccountingAccounts.AddRange(
            new AccountingAccount { Id = 1, LegalEntityId = 1, Code = "5000", Name = "Salary expense", Type = AccountingAccountType.Expense },
            new AccountingAccount { Id = 2, LegalEntityId = 1, Code = "2100", Name = "Salary payable", Type = AccountingAccountType.Liability });
        var dimension = new FinancialDimension { Id = 1, LegalEntityId = 1, Code = "RIDER", Name = "Rider", IsRequired = true };
        dimension.Values.Add(new FinancialDimensionValue { Id = 1, Code = "1000000001", Name = "Rider" });
        db.FinancialDimensions.Add(dimension);
        var year = new FiscalYear { Id = 1, LegalEntityId = 1, Name = "2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
        year.Periods.Add(new FiscalPeriod { Id = 1, Name = "July", PeriodNumber = 7, StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 7, 31) });
        db.FiscalYears.Add(year);
        var profile = new PostingProfile { Id = 1, LegalEntityId = 1, Code = "PAYROLL", Name = "Payroll", EffectiveFrom = new DateOnly(2026, 1, 1) };
        profile.Lines.Add(new PostingProfileLine { EventCode = "PAYROLL_EARNING", DebitAccountId = 1, CreditAccountId = 2 });
        db.PostingProfiles.Add(profile);
    }

    private static ApplicationDbcontext CreateDbContext() => new(new DbContextOptionsBuilder<ApplicationDbcontext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class AllowAllAccess : IFinancialAccessService
    {
        public Task<Result> EnsurePermissionAsync(string userId, int legalEntityId, FinancialPermission requiredPermission, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result<FinancialUserAccessResponse>> GrantAsync(GrantFinancialUserAccessRequest request, string grantedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> RevokeAsync(string userId, int legalEntityId, string revokedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyCollection<FinancialUserAccessResponse>>> GetForLegalEntityAsync(int legalEntityId, string requestedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
