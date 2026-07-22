using Application.Abstraction;
using Application.Contracts.AccountingFiles;
using Application.Contracts.Common;
using Application.Contracts.FinancialAccess;
using Application.Contracts.Ledger;
using Application.Contracts.RiderPayroll;
using Application.Service.AccountingFiles;
using Application.Service.AccountingPosting;
using Application.Service.FinancialAccess;
using Application.Service.RiderPayroll;
using Domain;
using Domain.Entities;
using Domain.Entities.AccountingCore;
using Domain.Entities.AccountingPlatform;
using Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public class RiderPayrollQueryWorkflowTests
{
    [Fact]
    public async Task RejectPaymentLine_UsesExistingReasonAndUpdatesBatchStatus()
    {
        await using var db = CreateDbContext();
        var batch = SeedPaymentBatch(db);
        await db.SaveChangesAsync();
        var service = Service(db);
        var line = batch.Lines.OrderBy(x => x.Id).First();

        var result = await service.RejectPaymentLineAsync(
            batch.Id,
            line.Id,
            new RejectRiderPaymentLineRequest("Bank rejected the IBAN"),
            "accountant");

        Assert.True(result.IsSuccess);
        Assert.Equal(RiderPaymentBatchStatus.PartiallyRejected, result.Value.Status);
        Assert.Equal("Bank rejected the IBAN", result.Value.Lines.Single(x => x.Id == line.Id).RejectionReason);
    }

    [Fact]
    public async Task RejectedPaymentLine_CanBeAllocatedToAReplacementBatch()
    {
        await using var db = CreateDbContext();
        var batch = SeedPaymentBatch(db);
        var rejectedLine = batch.Lines.OrderBy(x => x.RiderPayrollLine.RiderIqamaNo).First();
        db.Employees.Add(new Employees
        {
            IqamaNo = rejectedLine.RiderPayrollLine.RiderIqamaNo,
            NameEN = "Replacement rider",
            IBAN = "SA0000000000000000000000"
        });
        await db.SaveChangesAsync();
        var service = Service(db);

        var rejected = await service.RejectPaymentLineAsync(
            batch.Id,
            rejectedLine.Id,
            new RejectRiderPaymentLineRequest("Bank rejected the original instruction"),
            "accountant");
        Assert.True(rejected.IsSuccess);

        var replacement = await service.PreparePaymentBatchAsync(
            batch.RiderPayrollRunId,
            new PrepareRiderPaymentBatchRequest(
                RiderPaymentMethod.Bank,
                [rejectedLine.RiderPayrollLine.RiderIqamaNo]),
            "accountant");

        Assert.True(replacement.IsSuccess);
        var replacementLine = Assert.Single(replacement.Value.Lines);
        Assert.Equal(rejectedLine.RiderPayrollLine.RiderIqamaNo, replacementLine.RiderIqamaNo);
        Assert.Equal(rejectedLine.Amount, replacementLine.Amount);
    }

    [Fact]
    public async Task MemberCashDetail_ReturnsOnlyLinesForAssignedHousing()
    {
        await using var db = CreateDbContext();
        var batch = SeedPaymentBatch(db, RiderPaymentMethod.Cash);
        db.ApplicationUsers.Add(new ApplicationUser { Id = "member", UserName = "member", NormalizedUserName = "MEMBER" });
        db.Housings.AddRange(
            new Housing { Id = 10, Name = "Assigned", Address = "A" },
            new Housing { Id = 20, Name = "Other", Address = "B" });
        db.HousingCashUserAccesses.Add(new HousingCashUserAccess
        {
            UserId = "member",
            LegalEntityId = 1,
            HousingId = 10,
            GrantedBy = "accountant"
        });
        batch.Lines.ElementAt(0).HousingId = 10;
        batch.Lines.ElementAt(1).HousingId = 20;
        await db.SaveChangesAsync();

        var result = await Service(db).GetHousingCashPaymentBatchAsync(batch.Id, "member");

        Assert.True(result.IsSuccess);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(10, line.HousingId);
    }

    [Fact]
    public async Task ConfirmPaymentBatch_ReplaysTheOriginalLineSetAndRejectsAChangedSet()
    {
        await using var db = CreateDbContext();
        var batch = SeedPaymentBatch(db);
        var originalLine = batch.Lines.OrderBy(x => x.RiderPayrollLine.RiderIqamaNo).First();
        var otherLine = batch.Lines.OrderBy(x => x.RiderPayrollLine.RiderIqamaNo).Last();
        var document = new FinancialDocument
        {
            LegalEntityId = batch.LegalEntityId,
            DocumentType = "RiderPayrollPayment",
            DocumentNumber = "PAY-1",
            IdempotencyKey = "retry-key"
        };
        originalLine.IsConfirmed = true;
        originalLine.PaymentFinancialDocumentId = document.Id;
        batch.PaymentFinancialDocumentId = document.Id;
        batch.Status = RiderPaymentBatchStatus.Sent;
        db.FinancialDocuments.Add(document);
        await db.SaveChangesAsync();
        var posting = new ReplayPostingService(document.Id);
        var service = Service(db, posting);

        var replay = await service.ConfirmPaymentBatchAsync(
            batch.Id,
            new ConfirmRiderPaymentBatchRequest(
                new DateOnly(2026, 8, 1),
                "PAYROLL",
                "retry-key",
                "correlation",
                [originalLine.Id]),
            "accountant");

        Assert.True(replay.IsSuccess);
        Assert.True(replay.Value.Lines.Single(x => x.Id == originalLine.Id).IsConfirmed);
        Assert.Equal(1, posting.PostCalls);

        var conflict = await service.ConfirmPaymentBatchAsync(
            batch.Id,
            new ConfirmRiderPaymentBatchRequest(
                new DateOnly(2026, 8, 1),
                "PAYROLL",
                "retry-key",
                "correlation",
                [otherLine.Id]),
            "accountant");

        Assert.True(conflict.IsFailure);
        Assert.Equal("Ledger.IdempotencyConflict", conflict.Error.Code);
        Assert.Equal(1, posting.PostCalls);
    }

    private static RiderPaymentBatch SeedPaymentBatch(ApplicationDbcontext db, RiderPaymentMethod method = RiderPaymentMethod.Bank)
    {
        db.Tenants.Add(new Tenant { Id = 1, Code = "T", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "E", LegalName = "Entity", BaseCurrencyCode = "SAR" });
        var run = new RiderPayrollRun
        {
            LegalEntityId = 1,
            RunNumber = "RUN-1",
            PeriodStart = new DateOnly(2026, 7, 1),
            PeriodEnd = new DateOnly(2026, 7, 31),
            NetPay = 300m,
            Status = RiderPayrollStatus.PaymentPrepared,
            CreatedBy = "accountant",
            Lines =
            [
                new RiderPayrollLine { RiderIqamaNo = 1000000001, NetPay = 100m },
                new RiderPayrollLine { RiderIqamaNo = 1000000002, NetPay = 200m }
            ]
        };
        var batch = new RiderPaymentBatch
        {
            LegalEntityId = 1,
            RiderPayrollRunId = run.Id,
            BatchNumber = "PAY-1",
            Method = method,
            Status = RiderPaymentBatchStatus.Prepared,
            CreatedBy = "accountant",
            Lines =
            [
                new RiderPaymentBatchLine { RiderPayrollLine = run.Lines.ElementAt(0), Method = method, Amount = 100m },
                new RiderPaymentBatchLine { RiderPayrollLine = run.Lines.ElementAt(1), Method = method, Amount = 200m }
            ]
        };
        db.RiderPayrollRuns.Add(run);
        db.RiderPaymentBatches.Add(batch);
        return batch;
    }

    private static RiderPayrollService Service(ApplicationDbcontext db, IAccountingPostingService? postingService = null) => new(
        db,
        new AllowAllAccess(),
        postingService ?? new UnusedPostingService(),
        new UnusedFileService());

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

    private sealed class UnusedPostingService : IAccountingPostingService
    {
        public Task<Result<FinancialDocumentResponse>> PostAsync(PostSourceDocumentRequest request, string actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<FinancialDocumentResponse>> PostAfterScopeValidationAsync(PostSourceDocumentRequest request, string actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<FinancialDocumentResponse>> ReverseAsync(ReverseSourceDocumentRequest request, string actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ReplayPostingService(Guid documentId) : IAccountingPostingService
    {
        public int PostCalls { get; private set; }

        public Task<Result<FinancialDocumentResponse>> PostAsync(PostSourceDocumentRequest request, string actorId, CancellationToken cancellationToken = default)
        {
            PostCalls++;
            return Task.FromResult(Result.Success(Response(request)));
        }

        public Task<Result<FinancialDocumentResponse>> PostAfterScopeValidationAsync(PostSourceDocumentRequest request, string actorId, CancellationToken cancellationToken = default) =>
            PostAsync(request, actorId, cancellationToken);

        public Task<Result<FinancialDocumentResponse>> ReverseAsync(ReverseSourceDocumentRequest request, string actorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private FinancialDocumentResponse Response(PostSourceDocumentRequest request) => new(
            documentId,
            request.LegalEntityId,
            request.BranchId,
            request.DocumentType,
            "PAY-1",
            request.SourceReference,
            request.Description,
            request.TransactionDate,
            FinancialDocumentStatus.Posted,
            "accountant",
            "accountant",
            "accountant",
            "accountant",
            null,
            null,
            [],
            request.CorrelationId);
    }

    private sealed class UnusedFileService : IAccountingFileService
    {
        public Task<Result<PagedResponse<AccountingFileResponse>>> GetAllAsync(PaginationRequest pagination, AccountingFileListFilter filter, string actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AccountingFileResponse>> UploadAsync(UploadAccountingFileRequest request, string fileName, string contentType, Stream content, string actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AccountingFileDownload>> DownloadAsync(Guid fileId, string actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
