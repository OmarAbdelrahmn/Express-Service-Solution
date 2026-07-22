using Application.Abstraction;
using Application.Contracts.Ledger;

namespace Application.Service.AccountingPosting;

public enum AccountingModule { GeneralLedger = 1, Receivables = 2, Payables = 3, Payroll = 4, Inventory = 5, Treasury = 6, Tax = 7, Assets = 8 }

public record PostingEventAmount(string EventCode, decimal Amount, string? Description, IReadOnlyCollection<int>? DimensionValueIds = null);

public record PostSourceDocumentRequest(
    int LegalEntityId,
    int? BranchId,
    DateOnly TransactionDate,
    string DocumentType,
    string SourceReference,
    string PostingProfileCode,
    string Description,
    string CurrencyCode,
    string IdempotencyKey,
    string CorrelationId,
    AccountingModule Module,
    IReadOnlyCollection<PostingEventAmount> Events,
    string? IdempotencyPayload = null);

public record ReverseSourceDocumentRequest(
    Guid FinancialDocumentId,
    DateOnly ReversalDate,
    string Reason,
    string IdempotencyKey,
    string CorrelationId,
    AccountingModule Module,
    string? IdempotencyPayload = null);

public interface IAccountingPostingService
{
    Task<Result<FinancialDocumentResponse>> PostAsync(PostSourceDocumentRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<FinancialDocumentResponse>> PostAfterScopeValidationAsync(PostSourceDocumentRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<FinancialDocumentResponse>> ReverseAsync(ReverseSourceDocumentRequest request, string actorId, CancellationToken cancellationToken = default);
}
