using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.FinancialAccess;
using Application.Contracts.FinancialOperations;
using Application.Contracts.Ledger;
using Application.Service.FinancialAccess;
using Application.Service.FinancialOperations;
using Application.Service.AccountingPosting;
using Application.Service.Ledger;
using Domain;
using Domain.Entities;
using Domain.Entities.AccountingCore;
using Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public class LedgerServiceTests
{
    [Fact]
    public async Task ProfitAndLoss_UsesOnlyFinalizedJournalLines()
    {
        await using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = 1, Code = "TENANT", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "ENTITY", LegalName = "Entity" });
        db.AccountingAccounts.AddRange(
            new AccountingAccount { Id = 1, LegalEntityId = 1, Code = "4000", Name = "Revenue", Type = AccountingAccountType.Revenue },
            new AccountingAccount { Id = 2, LegalEntityId = 1, Code = "5000", Name = "Expense", Type = AccountingAccountType.Expense });
        db.JournalEntries.AddRange(
            new JournalEntry
            {
                Id = Guid.NewGuid(),
                LegalEntityId = 1,
                FiscalPeriodId = 1,
                PostingBatchId = Guid.NewGuid(),
                EntryNumber = "JE-1",
                PostingDate = new DateOnly(2026, 7, 1),
                Description = "Finalized",
                IsFinalized = true,
                Lines =
                [
                    new JournalLine { AccountId = 1, LineNumber = 1, Credit = 100m, BaseCredit = 100m },
                    new JournalLine { AccountId = 2, LineNumber = 2, Debit = 40m, BaseDebit = 40m }
                ]
            },
            new JournalEntry
            {
                Id = Guid.NewGuid(),
                LegalEntityId = 1,
                FiscalPeriodId = 1,
                PostingBatchId = Guid.NewGuid(),
                EntryNumber = "JE-2",
                PostingDate = new DateOnly(2026, 7, 1),
                Description = "Draft posting",
                IsFinalized = false,
                Lines =
                [new JournalLine { AccountId = 1, LineNumber = 1, Credit = 999m, BaseCredit = 999m }]
            });
        await db.SaveChangesAsync();

        var service = new LedgerService(db, new AllowAllFinancialAccessService());
        var result = await service.GetProfitAndLossAsync(1, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), "tester");

        Assert.True(result.IsSuccess);
        Assert.Equal(100m, result.Value.TotalRevenue);
        Assert.Equal(40m, result.Value.TotalExpense);
        Assert.Equal(60m, result.Value.NetIncome);
        Assert.DoesNotContain(result.Value.Lines, x => x.Credit == 999m);
    }

    [Fact]
    public async Task FinancialAccess_GrantAddsViewAndRequiredPermissions()
    {
        await using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = 1, Code = "TENANT", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "ENTITY", LegalName = "Entity" });
        db.ApplicationUsers.AddRange(
            new ApplicationUser { Id = "master", UserName = "master", NormalizedUserName = "MASTER" },
            new ApplicationUser { Id = "preparer", UserName = "preparer", NormalizedUserName = "PREPARER" });
        db.ApplicationRoles.Add(new ApplicationRole { Id = "role-master", Name = "Master", NormalizedName = "MASTER" });
        db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<string> { UserId = "master", RoleId = "role-master" });
        await db.SaveChangesAsync();

        var service = new FinancialAccessService(db);
        var grant = await service.GrantAsync(new GrantFinancialUserAccessRequest("preparer", 1, FinancialPermission.Prepare), "master");
        var allowed = await service.EnsurePermissionAsync("preparer", 1, FinancialPermission.Prepare);
        var denied = await service.EnsurePermissionAsync("preparer", 1, FinancialPermission.Post);

        Assert.True(grant.IsSuccess);
        Assert.Equal(FinancialPermission.View | FinancialPermission.Prepare, grant.Value.Permissions);
        Assert.True(allowed.IsSuccess);
        Assert.True(denied.IsFailure);
    }

    [Fact]
    public async Task SourceJournal_AllowsControlAccounts_WhileManualJournalDoesNot()
    {
        await using var db = CreateDbContext();
        SeedFinancialSetup(db);
        await db.SaveChangesAsync();
        var service = new LedgerService(db, new AllowAllFinancialAccessService());

        var source = await service.CreateSourceJournalAsync(
            new CreateSourceJournalRequest(1, null, new DateOnly(2026, 7, 1), "CustomerReceipt", "RCPT:1", "OPERATIONS", "Receipt", "SAR", 1m, "source-1", [new JournalLineRequest(1, "Cash", 100m, 0), new JournalLineRequest(2, "Receivable", 0, 100m)]),
            "tester");
        var manual = await service.CreateManualJournalAsync(
            new CreateManualJournalRequest(1, null, new DateOnly(2026, 7, 1), "Manual", "SAR", 1m, "manual-1", [new JournalLineRequest(1, "Cash", 100m, 0), new JournalLineRequest(2, "Receivable", 0, 100m)]),
            "tester");

        Assert.True(source.IsSuccess);
        Assert.Equal("RCPT:1", source.Value.SourceReference);
        Assert.True(manual.IsFailure);
    }

    [Fact]
    public async Task CustomerReceipt_IsCreatedAsUnappliedCashUntilAllocated()
    {
        await using var db = CreateDbContext();
        SeedFinancialSetup(db);
        var customer = new Domain.Entities.FinancialOperations.CustomerAccount { LegalEntityId = 1, Code = "CUST", Name = "Customer" };
        db.CustomerAccounts.Add(customer);
        await db.SaveChangesAsync();
        var access = new AllowAllFinancialAccessService();
        var operations = new FinancialOperationsService(db, access, new AccountingPostingService(db, access));

        var result = await operations.RecordCustomerReceiptAsync(
            new RecordCustomerReceiptRequest(1, customer.Id, "RCPT-001", "BANK-001", new DateOnly(2026, 7, 1), "SAR", 1m, 250m, 0, 0, "OPERATIONS", "receipt-1"),
            "tester");

        Assert.True(result.IsSuccess);
        Assert.Equal("Unapplied", result.Value.Status);
        Assert.NotNull(result.Value.FinancialDocumentId);
        Assert.Equal(0, await db.CustomerReceiptAllocations.CountAsync());
        var receipt = await db.CustomerReceipts.SingleAsync();
        Assert.Equal(1, receipt.CashAccountId);
        Assert.Equal(2, receipt.ReceivableAccountId);
    }

    [Fact]
    public async Task ManualJournal_CreatorCannotApproveOwnDocument()
    {
        await using var db = CreateDbContext();
        SeedFinancialSetup(db);
        db.AccountingAccounts.AddRange(
            new AccountingAccount { Id = 3, LegalEntityId = 1, Code = "6100", Name = "Manual debit", Type = AccountingAccountType.Expense, AllowManualPosting = true },
            new AccountingAccount { Id = 4, LegalEntityId = 1, Code = "3100", Name = "Manual credit", Type = AccountingAccountType.Equity, AllowManualPosting = true });
        await db.SaveChangesAsync();
        var service = new LedgerService(db, new AllowAllFinancialAccessService());

        var created = await service.CreateManualJournalAsync(
            new CreateManualJournalRequest(
                1,
                null,
                new DateOnly(2026, 7, 10),
                "Maker-checker",
                "SAR",
                1m,
                "manual-maker-checker",
                [new JournalLineRequest(3, "Debit", 100m, 0m), new JournalLineRequest(4, "Credit", 0m, 100m)]),
            "maker");
        var submitted = await service.SubmitDocumentAsync(created.Value.Id, "maker");
        var approval = await service.ApproveDocumentAsync(created.Value.Id, new ApproveDocumentRequest("self approval"), "maker");

        Assert.True(created.IsSuccess);
        Assert.True(submitted.IsSuccess);
        Assert.True(approval.IsFailure);
        Assert.Equal(LedgerErrors.MakerCheckerViolation.Code, approval.Error.Code);
    }

    private static void SeedFinancialSetup(ApplicationDbcontext db)
    {
        db.Tenants.Add(new Tenant { Id = 1, Code = "TENANT", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "ENTITY", LegalName = "Entity", BaseCurrencyCode = "SAR" });
        db.Currencies.Add(new Currency { Code = "SAR", Name = "Saudi Riyal" });
        db.AccountingAccounts.AddRange(
            new AccountingAccount { Id = 1, LegalEntityId = 1, Code = "1100", Name = "Cash", Type = AccountingAccountType.Asset, IsControlAccount = true, AllowManualPosting = false },
            new AccountingAccount { Id = 2, LegalEntityId = 1, Code = "1200", Name = "Receivable", Type = AccountingAccountType.Asset, IsControlAccount = true, AllowManualPosting = false });
        var profile = new PostingProfile { Id = 1, LegalEntityId = 1, Code = "OPERATIONS", Name = "Operations", EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true };
        profile.Lines.Add(new PostingProfileLine { EventCode = "AR_RECEIPT", DebitAccountId = 1, CreditAccountId = 2 });
        db.PostingProfiles.Add(profile);
        var year = new FiscalYear { Id = 1, LegalEntityId = 1, Name = "2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
        year.Periods.Add(new FiscalPeriod { Id = 1, Name = "July", PeriodNumber = 7, StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 7, 31) });
        db.FiscalYears.Add(year);
    }

    private static ApplicationDbcontext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbcontext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class AllowAllFinancialAccessService : IFinancialAccessService
    {
        public Task<Result> EnsurePermissionAsync(string userId, int legalEntityId, FinancialPermission requiredPermission, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result<FinancialUserAccessResponse>> GrantAsync(GrantFinancialUserAccessRequest request, string grantedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> RevokeAsync(string userId, int legalEntityId, string revokedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyCollection<FinancialUserAccessResponse>>> GetForLegalEntityAsync(int legalEntityId, string requestedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
