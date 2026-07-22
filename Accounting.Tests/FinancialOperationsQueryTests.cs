using Application.Abstraction;
using Application.Contracts.Common;
using Application.Contracts.FinancialAccess;
using Application.Contracts.FinancialOperations;
using Application.Service.AccountingPosting;
using Application.Service.FinancialAccess;
using Application.Service.FinancialOperations;
using System.Text.Json;
using Domain;
using Domain.Entities.AccountingCore;
using Domain.Entities.FinancialOperations;
using Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public class FinancialOperationsQueryTests
{
    [Fact]
    public void InvoiceFinalizationRequests_AcceptEmptyJsonObjects()
    {
        var issue = JsonSerializer.Deserialize<IssueCustomerInvoiceRequest>("{}");
        var record = JsonSerializer.Deserialize<RecordSupplierInvoiceRequest>("{}");

        Assert.NotNull(issue);
        Assert.NotNull(record);
        Assert.Equal(string.Empty, issue.IdempotencyKey);
        Assert.Equal(string.Empty, record.IdempotencyKey);
    }

    [Fact]
    public async Task InvoiceAndReceiptDetails_ReturnLinesAllocationsAndOpenAmounts()
    {
        await using var db = CreateDbContext();
        SeedOrganization(db);
        var customer = new CustomerAccount { LegalEntityId = 1, Code = "CUST", Name = "Customer" };
        var invoice = new CustomerInvoice
        {
            LegalEntityId = 1,
            CustomerAccount = customer,
            InvoiceNumber = "INV-001",
            InvoiceDate = new DateOnly(2026, 7, 1),
            DueDate = new DateOnly(2026, 7, 31),
            ReceivableAccountId = 1,
            PostingProfileCode = "AR",
            NetAmount = 100m,
            GrossAmount = 115m,
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
                    NetAmount = 100m,
                    TaxAmount = 15m
                }
            ]
        };
        var receipt = new CustomerReceipt
        {
            LegalEntityId = 1,
            CustomerAccount = customer,
            ReceiptNumber = "RCT-001",
            ExternalReference = "BANK-001",
            ReceiptDate = new DateOnly(2026, 7, 5),
            Amount = 80m,
            CashAccountId = 3,
            ReceivableAccountId = 1,
            PostingProfileCode = "AR",
            CreatedBy = "accountant"
        };
        receipt.Allocations.Add(new CustomerReceiptAllocation
        {
            CustomerReceipt = receipt,
            CustomerInvoice = invoice,
            Amount = 45m,
            AllocatedBy = "accountant"
        });
        db.CustomerInvoices.Add(invoice);
        db.CustomerReceipts.Add(receipt);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var invoiceResult = await service.GetCustomerInvoiceAsync(invoice.Id, "accountant");
        var receiptResult = await service.GetCustomerReceiptAsync(receipt.Id, "accountant");

        Assert.True(invoiceResult.IsSuccess);
        Assert.Equal(70m, invoiceResult.Value.OpenAmount);
        Assert.Single(invoiceResult.Value.Lines!);
        Assert.Equal(15m, invoiceResult.Value.Lines!.Single().TaxAmount);
        Assert.True(receiptResult.IsSuccess);
        Assert.Equal(35m, receiptResult.Value.UnappliedAmount);
        Assert.Single(receiptResult.Value.Allocations!);
        Assert.Equal(invoice.Id, receiptResult.Value.Allocations!.Single().RelatedDocumentId);
    }

    [Fact]
    public async Task StockBalances_NetReceiptsTransfersAndIssuesByBin()
    {
        await using var db = CreateDbContext();
        SeedOrganization(db);
        var item = new InventoryItem { LegalEntityId = 1, Sku = "SKU-1", Name = "Part", UnitOfMeasure = "EA" };
        db.InventoryMovements.AddRange(
            Movement(item, InventoryMovementType.Receipt, "R-1", "", "A", 10m, 5m),
            Movement(item, InventoryMovementType.Transfer, "T-1", "A", "B", 4m, 5m),
            Movement(item, InventoryMovementType.Issue, "I-1", "B", "", 1m, 5m));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.GetInventoryStockBalancesAsync(
            new PaginationRequest { PageNumber = 1, PageSize = 25 },
            new InventoryStockBalanceListFilter { LegalEntityId = 1, SortDirection = "asc" },
            "accountant");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        var binA = Assert.Single(result.Value.Items, x => x.Bin == "A");
        var binB = Assert.Single(result.Value.Items, x => x.Bin == "B");
        Assert.Equal(6m, binA.Quantity);
        Assert.Equal(30m, binA.Value);
        Assert.Equal(3m, binB.Quantity);
        Assert.Equal(15m, binB.Value);
    }

    [Fact]
    public async Task ReconcileBankStatementLine_ReturnsUpdatedOperation()
    {
        await using var db = CreateDbContext();
        SeedOrganization(db);
        var bank = new BankAccount
        {
            LegalEntityId = 1,
            Code = "BANK",
            Name = "Bank",
            CurrencyCode = "SAR",
            LedgerAccountId = 1
        };
        var statementLine = new BankStatementLine
        {
            BankAccount = bank,
            ExternalReference = "BANK-001",
            TransactionDate = new DateOnly(2026, 7, 2),
            Amount = 100m,
            Description = "Deposit"
        };
        var document = new FinancialDocument
        {
            LegalEntityId = 1,
            DocumentType = "CustomerReceipt",
            DocumentNumber = "DOC-001",
            IdempotencyKey = "receipt-1",
            RequestHash = new string('A', 64),
            CorrelationId = "correlation-1",
            Description = "Receipt",
            TransactionDate = new DateOnly(2026, 7, 2),
            Status = FinancialDocumentStatus.Posted,
            CreatedBy = "accountant"
        };
        var postingBatch = new PostingBatch
        {
            LegalEntityId = 1,
            FinancialDocument = document,
            PostingKey = "CustomerReceipt:1",
            PostedBy = "accountant"
        };
        db.BankStatementLines.Add(statementLine);
        db.JournalEntries.Add(new JournalEntry
        {
            PostingBatch = postingBatch,
            LegalEntityId = 1,
            FiscalPeriodId = 1,
            EntryNumber = "JE-001",
            PostingDate = new DateOnly(2026, 7, 2),
            Description = "Receipt",
            IsFinalized = true,
            Lines = [new JournalLine { LineNumber = 1, AccountId = 1, Debit = 100m, BaseDebit = 100m }]
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.ReconcileBankStatementLineAsync(
            statementLine.Id,
            new ReconcileBankStatementLineRequest(document.Id),
            "accountant");

        Assert.True(result.IsSuccess);
        Assert.Equal("Reconciled", result.Value.Status);
        Assert.Equal(document.Id, result.Value.FinancialDocumentId);
        Assert.Equal("accountant", result.Value.ReconciledBy);
        Assert.NotNull(result.Value.ReconciledAt);
    }

    private static InventoryMovement Movement(
        InventoryItem item,
        InventoryMovementType type,
        string reference,
        string fromBin,
        string toBin,
        decimal quantity,
        decimal unitCost) => new()
        {
            LegalEntityId = 1,
            InventoryItem = item,
            MovementType = type,
            MovementDate = new DateOnly(2026, 7, 1),
            Reference = reference,
            FromBin = fromBin,
            ToBin = toBin,
            Quantity = quantity,
            UnitCost = unitCost,
            DebitAccountId = 1,
            CreditAccountId = 2,
            PostingProfileCode = "INVENTORY",
            CreatedBy = "accountant"
        };

    private static void SeedOrganization(ApplicationDbcontext db)
    {
        db.Tenants.Add(new Tenant { Id = 1, Code = "TENANT", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "ENTITY", LegalName = "Entity", BaseCurrencyCode = "SAR" });
    }

    private static FinancialOperationsService CreateService(ApplicationDbcontext db)
    {
        var access = new AllowAllAccess();
        return new FinancialOperationsService(db, access, new AccountingPostingService(db, access));
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
