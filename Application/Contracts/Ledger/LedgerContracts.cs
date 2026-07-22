using Domain.Entities.AccountingCore;
using FluentValidation;

namespace Application.Contracts.Ledger;

public record CreateAccountingAccountRequest(int LegalEntityId, int? ParentAccountId, string Code, string Name, AccountingAccountType Type, bool IsControlAccount, bool AllowManualPosting, bool IsCashEquivalent = false);
public record CreatePostingProfileRequest(int LegalEntityId, string Code, string Name, DateOnly EffectiveFrom, DateOnly? EffectiveTo, IReadOnlyCollection<PostingProfileLineRequest> Lines);
public record PostingProfileLineRequest(string EventCode, int DebitAccountId, int CreditAccountId);
public record CreateFiscalYearRequest(int LegalEntityId, string Name, DateOnly StartDate, DateOnly EndDate, IReadOnlyCollection<CreateFiscalPeriodRequest> Periods);
public record CreateFiscalPeriodRequest(int PeriodNumber, string Name, DateOnly StartDate, DateOnly EndDate);
public record ChangeFiscalPeriodStatusRequest(string Reason, bool TaxLocked = true, bool PayrollLocked = true);
public record CreateManualJournalRequest(int LegalEntityId, int? BranchId, DateOnly TransactionDate, string Description, string CurrencyCode, decimal ExchangeRate, string IdempotencyKey, IReadOnlyCollection<JournalLineRequest> Lines);
// This contract is intentionally not exposed by a controller. It is for trusted subledger services
// that resolve their own business event and then create a maker-checked accounting document.
public record CreateSourceJournalRequest(int LegalEntityId, int? BranchId, DateOnly TransactionDate, string DocumentType, string SourceReference, string PostingProfileCode, string Description, string CurrencyCode, decimal ExchangeRate, string IdempotencyKey, IReadOnlyCollection<JournalLineRequest> Lines);
public record JournalLineRequest(int AccountId, string? Description, decimal Debit, decimal Credit, IReadOnlyCollection<int>? DimensionValueIds = null);
public record ApproveDocumentRequest(string? Comment = null);
public record ReverseJournalRequest(DateOnly ReversalDate, string Reason, string IdempotencyKey);
public record CreateCurrencyRequest(string Code, string Name, int DecimalPlaces);
public record CreateExchangeRateRequest(int LegalEntityId, string FromCurrencyCode, string ToCurrencyCode, DateOnly EffectiveDate, decimal Rate);
public record CreateFinancialDimensionRequest(int LegalEntityId, string Code, string Name, bool IsRequired);
public record CreateFinancialDimensionValueRequest(int FinancialDimensionId, string Code, string Name);
public record CreateRecurringJournalScheduleRequest(int LegalEntityId, int? BranchId, string DocumentType, string Description, string CurrencyCode, int FrequencyMonths, DateOnly NextRunDate, DateOnly? EndDate, IReadOnlyCollection<JournalLineRequest> Lines);

public record AccountingAccountResponse(int Id, int LegalEntityId, int? ParentAccountId, string Code, string Name, AccountingAccountType Type, bool IsControlAccount, bool AllowManualPosting, bool IsCashEquivalent, bool IsActive);
public record PostingProfileResponse(int Id, int LegalEntityId, string Code, string Name, int Version, DateOnly EffectiveFrom, DateOnly? EffectiveTo, IReadOnlyCollection<PostingProfileLineResponse> Lines, bool IsActive = true);
public record PostingProfileLineResponse(string EventCode, int DebitAccountId, int CreditAccountId);
public record FiscalPeriodResponse(int Id, int PeriodNumber, string Name, DateOnly StartDate, DateOnly EndDate, FiscalPeriodStatus Status, bool TaxLocked, bool PayrollLocked, string? CloseReason, string? ReopenReason, string? ClosedBy, DateTime? ClosedAt, string? ReopenedBy, DateTime? ReopenedAt);
public record FiscalYearResponse(int Id, int LegalEntityId, string Name, DateOnly StartDate, DateOnly EndDate, bool IsClosed, IReadOnlyCollection<FiscalPeriodResponse> Periods);
public record FinancialDocumentResponse(Guid Id, int LegalEntityId, int? BranchId, string DocumentType, string DocumentNumber, string? SourceReference, string Description, DateOnly TransactionDate, FinancialDocumentStatus Status, string CreatedBy, string? SubmittedBy, string? ApprovedBy, string? PostedBy, Guid? ReversalOfDocumentId, Guid? ReversedByDocumentId, IReadOnlyCollection<FinancialDocumentLineResponse> Lines, string? CorrelationId = null, string? RequestHash = null);
public record FinancialDocumentLineResponse(int LineNumber, int AccountId, string? Description, decimal Debit, decimal Credit);
public record JournalEntryResponse(Guid Id, Guid PostingBatchId, int LegalEntityId, int FiscalPeriodId, string EntryNumber, DateOnly PostingDate, string Description, IReadOnlyCollection<JournalLineResponse> Lines);
public record JournalLineResponse(int LineNumber, int AccountId, string? Description, decimal Debit, decimal Credit);
public record TrialBalanceLineResponse(int AccountId, string AccountCode, string AccountName, AccountingAccountType AccountType, decimal OpeningDebit, decimal OpeningCredit, decimal MovementDebit, decimal MovementCredit, decimal ClosingDebit, decimal ClosingCredit, decimal ClosingBalance);
public record TrialBalanceResponse(int LegalEntityId, DateOnly FromDate, DateOnly ToDate, IReadOnlyCollection<TrialBalanceLineResponse> Lines, decimal TotalOpeningDebit, decimal TotalOpeningCredit, decimal TotalMovementDebit, decimal TotalMovementCredit, decimal TotalClosingDebit, decimal TotalClosingCredit);
public record ProfitAndLossLineResponse(int AccountId, string AccountCode, string AccountName, AccountingAccountType AccountType, decimal Debit, decimal Credit, decimal SignedAmount);
public record ProfitAndLossResponse(int LegalEntityId, DateOnly FromDate, DateOnly ToDate, IReadOnlyCollection<ProfitAndLossLineResponse> Lines, decimal TotalRevenue, decimal TotalExpense, decimal NetIncome);
public record BalanceSheetLineResponse(int AccountId, string AccountCode, string AccountName, AccountingAccountType AccountType, decimal Debit, decimal Credit, decimal Balance);
public record BalanceSheetResponse(int LegalEntityId, DateOnly AsOfDate, IReadOnlyCollection<BalanceSheetLineResponse> Lines, decimal TotalAssets, decimal TotalLiabilities, decimal TotalEquity, decimal NetPosition);
public record CashMovementResponse(int LegalEntityId, DateOnly FromDate, DateOnly ToDate, IReadOnlyCollection<int> CashAccountIds, decimal CashInflows, decimal CashOutflows, decimal NetCashMovement);
public record DimensionBalanceLineResponse(int DimensionValueId, string DimensionValueCode, string DimensionValueName, int AccountId, string AccountCode, string AccountName, AccountingAccountType AccountType, decimal Debit, decimal Credit, decimal Balance);
public record DimensionBalanceResponse(int LegalEntityId, int FinancialDimensionId, string DimensionCode, string DimensionName, DateOnly FromDate, DateOnly ToDate, IReadOnlyCollection<DimensionBalanceLineResponse> Lines);
public record ApprovalInboxItemResponse(Guid DocumentId, string DocumentNumber, string DocumentType, string Description, DateOnly TransactionDate, decimal Amount, string CreatedBy);
public record AccountingAuditEventResponse(long Id, string EventType, string ActorId, DateTime OccurredAt, string PayloadJson, string Hash);
public record CurrencyResponse(string Code, string Name, int DecimalPlaces, bool IsActive);
public record ExchangeRateResponse(long Id, int LegalEntityId, string FromCurrencyCode, string ToCurrencyCode, DateOnly EffectiveDate, decimal Rate);
public record FinancialDimensionResponse(int Id, int LegalEntityId, string Code, string Name, bool IsRequired, bool IsActive);
public record FinancialDimensionValueResponse(int Id, int FinancialDimensionId, string Code, string Name, bool IsActive);
public record RecurringJournalScheduleLineResponse(int LineNumber, int AccountId, string? Description, decimal Debit, decimal Credit);
public record RecurringJournalScheduleResponse(Guid Id, int LegalEntityId, string DocumentType, string Description, DateOnly NextRunDate, DateOnly? EndDate, bool IsActive, int? BranchId = null, string CurrencyCode = "SAR", int FrequencyMonths = 1, IReadOnlyCollection<RecurringJournalScheduleLineResponse>? Lines = null);

public class CreateAccountingAccountRequestValidator : AbstractValidator<CreateAccountingAccountRequest>
{
    public CreateAccountingAccountRequestValidator() { RuleFor(x => x.LegalEntityId).GreaterThan(0); RuleFor(x => x.Code).NotEmpty().MaximumLength(32); RuleFor(x => x.Name).NotEmpty().MaximumLength(200); }
}
public class CreatePostingProfileRequestValidator : AbstractValidator<CreatePostingProfileRequest>
{
    public CreatePostingProfileRequestValidator() { RuleFor(x => x.LegalEntityId).GreaterThan(0); RuleFor(x => x.Code).NotEmpty().MaximumLength(64); RuleFor(x => x.Name).NotEmpty().MaximumLength(200); RuleFor(x => x.Lines).NotEmpty(); RuleForEach(x => x.Lines).ChildRules(x => { x.RuleFor(y => y.EventCode).NotEmpty().MaximumLength(64); x.RuleFor(y => y.DebitAccountId).GreaterThan(0); x.RuleFor(y => y.CreditAccountId).GreaterThan(0).NotEqual(y => y.DebitAccountId); }); }
}
public class CreateFiscalYearRequestValidator : AbstractValidator<CreateFiscalYearRequest>
{
    public CreateFiscalYearRequestValidator() { RuleFor(x => x.LegalEntityId).GreaterThan(0); RuleFor(x => x.Name).NotEmpty().MaximumLength(64); RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate); RuleFor(x => x.Periods).NotEmpty(); }
}
public class CreateManualJournalRequestValidator : AbstractValidator<CreateManualJournalRequest>
{
    public CreateManualJournalRequestValidator() { RuleFor(x => x.LegalEntityId).GreaterThan(0); RuleFor(x => x.Description).NotEmpty().MaximumLength(500); RuleFor(x => x.CurrencyCode).Length(3).Matches("^[A-Za-z]{3}$"); RuleFor(x => x.ExchangeRate).GreaterThan(0); RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128); RuleFor(x => x.Lines).Must(x => x.Count >= 2); RuleForEach(x => x.Lines).ChildRules(x => { x.RuleFor(y => y.AccountId).GreaterThan(0); x.RuleFor(y => y.Debit).GreaterThanOrEqualTo(0); x.RuleFor(y => y.Credit).GreaterThanOrEqualTo(0); }); }
}
public class ReverseJournalRequestValidator : AbstractValidator<ReverseJournalRequest>
{
    public ReverseJournalRequestValidator() { RuleFor(x => x.Reason).NotEmpty().MaximumLength(500); RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128); }
}
