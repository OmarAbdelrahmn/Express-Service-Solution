using Application.Abstraction;
using Application.Contracts.Common;
using Application.Contracts.Ledger;
using Domain.Entities.AccountingCore;

namespace Application.Service.Ledger;

public interface ILedgerService
{
    Task<Result<IReadOnlyCollection<CurrencyResponse>>> GetCurrenciesAsync(bool? active, string? search, CancellationToken cancellationToken = default);
    Task<Result<CurrencyResponse>> CreateCurrencyAsync(CreateCurrencyRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<ExchangeRateResponse>>> GetExchangeRatesAsync(int legalEntityId, PaginationRequest pagination, string? fromCurrencyCode, string? toCurrencyCode, DateOnly? fromDate, DateOnly? toDate, string? sortBy, string? sortDirection, string actorId, CancellationToken cancellationToken = default);
    Task<Result<ExchangeRateResponse>> CreateExchangeRateAsync(CreateExchangeRateRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<FinancialDimensionResponse>>> GetDimensionsAsync(int legalEntityId, bool? active, string? search, string actorId, CancellationToken cancellationToken = default);
    Task<Result<FinancialDimensionResponse>> CreateDimensionAsync(CreateFinancialDimensionRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<FinancialDimensionValueResponse>>> GetDimensionValuesAsync(int financialDimensionId, bool? active, string? search, string actorId, CancellationToken cancellationToken = default);
    Task<Result<FinancialDimensionValueResponse>> CreateDimensionValueAsync(CreateFinancialDimensionValueRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<RecurringJournalScheduleResponse>>> GetRecurringSchedulesAsync(int legalEntityId, PaginationRequest pagination, bool? active, DateOnly? fromDate, DateOnly? toDate, string? search, string? sortBy, string? sortDirection, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RecurringJournalScheduleResponse>> GetRecurringScheduleAsync(Guid id, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RecurringJournalScheduleResponse>> CreateRecurringScheduleAsync(CreateRecurringJournalScheduleRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<FinancialDocumentResponse>>> GenerateDueSchedulesAsync(DateOnly throughDate, string actorId, CancellationToken cancellationToken = default);
    Task<Result<AccountingAccountResponse>> CreateAccountAsync(CreateAccountingAccountRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<AccountingAccountResponse>>> GetAccountsAsync(int legalEntityId, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PostingProfileResponse>> CreatePostingProfileAsync(CreatePostingProfileRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<PostingProfileResponse>>> GetPostingProfilesAsync(int legalEntityId, PaginationRequest pagination, bool? active, DateOnly? fromDate, DateOnly? toDate, string? search, string? sortBy, string? sortDirection, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PostingProfileResponse>> GetPostingProfileAsync(int id, string actorId, CancellationToken cancellationToken = default);
    Task<Result<FiscalYearResponse>> CreateFiscalYearAsync(CreateFiscalYearRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<FiscalYearResponse>>> GetFiscalYearsAsync(int legalEntityId, PaginationRequest pagination, string? sortBy, string? sortDirection, string actorId, CancellationToken cancellationToken = default);
    Task<Result<FiscalYearResponse>> GetFiscalYearAsync(int fiscalYearId, string actorId, CancellationToken cancellationToken = default);
    Task<Result<FiscalPeriodResponse>> SoftClosePeriodAsync(int periodId, ChangeFiscalPeriodStatusRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<FiscalPeriodResponse>> ClosePeriodAsync(int periodId, ChangeFiscalPeriodStatusRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<FiscalPeriodResponse>> ReopenPeriodAsync(int periodId, ChangeFiscalPeriodStatusRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<FinancialDocumentResponse>> CreateManualJournalAsync(CreateManualJournalRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<FinancialDocumentResponse>> CreateSourceJournalAsync(CreateSourceJournalRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<FinancialDocumentResponse>> GetDocumentAsync(Guid documentId, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<FinancialDocumentResponse>>> GetDocumentsAsync(int legalEntityId, PaginationRequest pagination, FinancialDocumentStatus? status, string? documentType, DateOnly? fromDate, DateOnly? toDate, string? search, string? reference, string? sortBy, string? sortDirection, string actorId, CancellationToken cancellationToken = default);
    Task<Result<FinancialDocumentResponse>> SubmitDocumentAsync(Guid documentId, string actorId, CancellationToken cancellationToken = default);
    Task<Result<FinancialDocumentResponse>> ApproveDocumentAsync(Guid documentId, ApproveDocumentRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<JournalEntryResponse>> PostDocumentAsync(Guid documentId, string actorId, CancellationToken cancellationToken = default);
    Task<Result<FinancialDocumentResponse>> CreateReversalAsync(Guid documentId, ReverseJournalRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<JournalEntryResponse>>> GetJournalEntriesAsync(int legalEntityId, PaginationRequest pagination, DateOnly? fromDate, DateOnly? toDate, int? accountId, Guid? documentId, string? search, string? sortBy, string? sortDirection, string actorId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<ApprovalInboxItemResponse>>> GetApprovalInboxAsync(int legalEntityId, string actorId, CancellationToken cancellationToken = default);
    Task<Result<TrialBalanceResponse>> GetTrialBalanceAsync(int legalEntityId, DateOnly fromDate, DateOnly toDate, string actorId, CancellationToken cancellationToken = default);
    Task<Result<ProfitAndLossResponse>> GetProfitAndLossAsync(int legalEntityId, DateOnly fromDate, DateOnly toDate, string actorId, CancellationToken cancellationToken = default);
    Task<Result<BalanceSheetResponse>> GetBalanceSheetAsync(int legalEntityId, DateOnly asOfDate, string actorId, CancellationToken cancellationToken = default);
    Task<Result<CashMovementResponse>> GetCashMovementAsync(int legalEntityId, DateOnly fromDate, DateOnly toDate, string actorId, CancellationToken cancellationToken = default);
    Task<Result<DimensionBalanceResponse>> GetDimensionBalanceAsync(int legalEntityId, int financialDimensionId, DateOnly fromDate, DateOnly toDate, string actorId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<AccountingAuditEventResponse>>> GetAuditEventsAsync(int legalEntityId, int take, string actorId, CancellationToken cancellationToken = default);
}
