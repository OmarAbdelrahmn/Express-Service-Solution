using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.FinancialAccess;
using Application.Contracts.FinancialOperations;
using Application.Service.AccountingPosting;
using Application.Service.FinancialAccess;
using Application.Service.FinancialOperations;
using Domain;
using Domain.Entities.AccountingCore;
using Domain.Entities.FinancialOperations;
using Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public class FinancialOperationsIdempotencyTests
{
    [Fact]
    public async Task IssueCustomerInvoice_ReplaysAfterTheInvoiceReachedItsTerminalIssuedState()
    {
        await using var db = CreateDbContext();
        var customer = Seed(db);
        var invoice = new CustomerInvoice
        {
            LegalEntityId = 1,
            CustomerAccount = customer,
            InvoiceNumber = "INV-100",
            InvoiceDate = new DateOnly(2026, 7, 10),
            DueDate = new DateOnly(2026, 7, 31),
            CurrencyCode = "SAR",
            ExchangeRate = 1m,
            ReceivableAccountId = 1,
            PostingProfileCode = "AR",
            NetAmount = 100m,
            GrossAmount = 100m,
            CreatedBy = "accountant",
            Lines =
            [
                new CustomerInvoiceLine
                {
                    LineNumber = 1,
                    Description = "Service",
                    Quantity = 1m,
                    UnitPrice = 100m,
                    RevenueAccountId = 2,
                    NetAmount = 100m
                }
            ]
        };
        db.CustomerInvoices.Add(invoice);
        await db.SaveChangesAsync();
        var access = new AllowAllAccess();
        var service = new FinancialOperationsService(db, access, new AccountingPostingService(db, access));
        var request = new IssueCustomerInvoiceRequest("invoice-key");

        var first = await service.IssueCustomerInvoiceAsync(invoice.Id, request, "accountant");
        var replay = await service.IssueCustomerInvoiceAsync(invoice.Id, request, "accountant");

        Assert.True(first.IsSuccess);
        Assert.True(replay.IsSuccess);
        Assert.Equal(first.Value.FinancialDocumentId, replay.Value.FinancialDocumentId);
        Assert.Equal(ReceivableInvoiceStatus.Issued.ToString(), replay.Value.Status);
        Assert.Single(await db.FinancialDocuments.ToListAsync());
    }

    [Fact]
    public async Task RecordCustomerReceipt_ReplaysOriginalOperation_AndConflictsWhenPayloadChanges()
    {
        await using var db = CreateDbContext();
        var customer = Seed(db);
        await db.SaveChangesAsync();
        var access = new AllowAllAccess();
        var service = new FinancialOperationsService(db, access, new AccountingPostingService(db, access));
        var request = new RecordCustomerReceiptRequest(
            1, customer.Id, "RCT-100", "BANK-100", new DateOnly(2026, 7, 10), "SAR", 1m, 100m,
            1, 2, "AR", "receipt-key");

        var first = await service.RecordCustomerReceiptAsync(request, "accountant");
        var replay = await service.RecordCustomerReceiptAsync(request, "accountant");
        var conflict = await service.RecordCustomerReceiptAsync(request with { Amount = 101m }, "accountant");

        Assert.True(first.IsSuccess);
        Assert.True(replay.IsSuccess);
        Assert.Equal(first.Value.Id, replay.Value.Id);
        Assert.True(conflict.IsFailure);
        Assert.Equal(LedgerErrors.IdempotencyConflict.Code, conflict.Error.Code);
        Assert.Single(await db.CustomerReceipts.ToListAsync());
        Assert.Single(await db.FinancialDocuments.ToListAsync());
    }

    private static CustomerAccount Seed(ApplicationDbcontext db)
    {
        db.Tenants.Add(new Tenant { Id = 1, Code = "TENANT", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "ENTITY", LegalName = "Entity", BaseCurrencyCode = "SAR" });
        db.Currencies.Add(new Currency { Code = "SAR", Name = "Saudi Riyal" });
        db.AccountingAccounts.AddRange(
            new AccountingAccount { Id = 1, LegalEntityId = 1, Code = "1000", Name = "Cash", Type = AccountingAccountType.Asset },
            new AccountingAccount { Id = 2, LegalEntityId = 1, Code = "1100", Name = "Receivables", Type = AccountingAccountType.Asset });
        var year = new FiscalYear { Id = 1, LegalEntityId = 1, Name = "2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
        year.Periods.Add(new FiscalPeriod { Id = 1, Name = "July", PeriodNumber = 7, StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 7, 31) });
        db.FiscalYears.Add(year);
        var profile = new PostingProfile { Id = 1, LegalEntityId = 1, Code = "AR", Name = "Receivables", EffectiveFrom = new DateOnly(2026, 1, 1) };
        profile.Lines.Add(new PostingProfileLine { EventCode = "AR_RECEIPT", DebitAccountId = 1, CreditAccountId = 2 });
        profile.Lines.Add(new PostingProfileLine { EventCode = "AR_REVENUE", DebitAccountId = 1, CreditAccountId = 2 });
        db.PostingProfiles.Add(profile);
        var customer = new CustomerAccount { LegalEntityId = 1, Code = "CUST", Name = "Customer" };
        db.CustomerAccounts.Add(customer);
        return customer;
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
