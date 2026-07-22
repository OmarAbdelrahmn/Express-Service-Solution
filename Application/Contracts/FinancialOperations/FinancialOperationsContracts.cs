using Domain.Entities.FinancialOperations;
using FluentValidation;

namespace Application.Contracts.FinancialOperations;

public record CreateSourceEvidenceRequest(int LegalEntityId, int? PlatformAccountId, string EvidenceType, string ExternalReference, string StorageLocator, string ContentHash, string MetadataJson);
public record CreatePrivateSourceEvidenceRequest(int LegalEntityId, int? PlatformAccountId, string EvidenceType, string ExternalReference, Guid StoredFileId, string MetadataJson);
public record ReviewSourceEvidenceRequest(bool Accept, string? Comment);
public record RecordPlatformSettlementRequest(int LegalEntityId, Guid SourceEvidenceId, string SettlementReference, DateOnly SettlementDate, decimal GrossRevenue, decimal CommissionAmount, decimal NetSettlementAmount, int PlatformClearingAccountId, int CommissionExpenseAccountId, int RevenueAccountId, string PostingProfileCode, string IdempotencyKey);
public record CreateCustomerAccountRequest(int LegalEntityId, string Code, string Name, string? TaxRegistrationNumber);
public record CreateCustomerInvoiceLineRequest(string Description, decimal Quantity, decimal UnitPrice, int RevenueAccountId, int? TaxCodeId);
public record CreateCustomerInvoiceRequest(int LegalEntityId, Guid CustomerAccountId, Guid? SourceEvidenceId, string InvoiceNumber, DateOnly InvoiceDate, DateOnly DueDate, string CurrencyCode, decimal ExchangeRate, int ReceivableAccountId, string PostingProfileCode, IReadOnlyCollection<CreateCustomerInvoiceLineRequest> Lines);
public record IssueCustomerInvoiceRequest(string IdempotencyKey = "");
public record RecordCustomerReceiptRequest(int LegalEntityId, Guid CustomerAccountId, string ReceiptNumber, string ExternalReference, DateOnly ReceiptDate, string CurrencyCode, decimal ExchangeRate, decimal Amount, int CashAccountId, int ReceivableAccountId, string PostingProfileCode, string IdempotencyKey);
public record AllocateCustomerReceiptRequest(Guid CustomerInvoiceId, decimal Amount);
public record CreateEmployeePayContractRequest(int LegalEntityId, long EmployeeIqamaNo, DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal GrossSalary, decimal FixedDeduction, string CurrencyCode);
public record CreatePayrollRunRequest(int LegalEntityId, string RunNumber, DateOnly PeriodStart, DateOnly PeriodEnd, string CurrencyCode, int PayrollExpenseAccountId, int PayrollPayableAccountId, int DeductionLiabilityAccountId, string PostingProfileCode);
public record PreparePayrollRunRequest(string IdempotencyKey);
public record PayPayrollRunRequest(DateOnly PaymentDate, int CashAccountId, string PostingProfileCode, string IdempotencyKey);
public record CreateSupplierAccountRequest(int LegalEntityId, string Code, string Name, string? TaxRegistrationNumber);
public record CreateSupplierInvoiceLineRequest(string Description, decimal Quantity, decimal UnitPrice, int ExpenseOrInventoryAccountId, int? TaxCodeId);
public record CreateSupplierInvoiceRequest(int LegalEntityId, Guid SupplierAccountId, Guid? SourceEvidenceId, string InvoiceNumber, DateOnly InvoiceDate, DateOnly DueDate, string CurrencyCode, decimal ExchangeRate, int PayableAccountId, string PostingProfileCode, IReadOnlyCollection<CreateSupplierInvoiceLineRequest> Lines);
public record RecordSupplierInvoiceRequest(string IdempotencyKey = "");
public record RecordSupplierPaymentRequest(int LegalEntityId, Guid SupplierAccountId, string PaymentNumber, string ExternalReference, DateOnly PaymentDate, decimal Amount, int CashAccountId, int PayableAccountId, string PostingProfileCode, string IdempotencyKey);
public record AllocateSupplierPaymentRequest(Guid SupplierInvoiceId, decimal Amount);
public record CreateInventoryItemRequest(int LegalEntityId, string Sku, string Name, string UnitOfMeasure);
public record RecordInventoryMovementRequest(int LegalEntityId, Guid InventoryItemId, InventoryMovementType MovementType, DateOnly MovementDate, string Reference, string FromBin, string ToBin, decimal Quantity, decimal UnitCost, int DebitAccountId, int CreditAccountId, string PostingProfileCode, string IdempotencyKey);
public record CreateExpenseClaimRequest(int LegalEntityId, long EmployeeIqamaNo, Guid? SourceEvidenceId, string ClaimNumber, DateOnly ClaimDate, string Description, decimal NetAmount, int ExpenseAccountId, int EmployeePayableAccountId, int? TaxCodeId, string PostingProfileCode, string IdempotencyKey);
public record CreateBankAccountRequest(int LegalEntityId, string Code, string Name, string CurrencyCode, int LedgerAccountId);
public record RecordBankStatementLineRequest(Guid BankAccountId, string ExternalReference, DateOnly TransactionDate, decimal Amount, string Description);
public record ReconcileBankStatementLineRequest(Guid FinancialDocumentId);
public record CreateTaxCodeRequest(int LegalEntityId, string Code, string Name, TaxDirection Direction, decimal Rate, int TaxAccountId, DateOnly EffectiveFrom, DateOnly? EffectiveTo);
public record PrepareTaxReturnRequest(int LegalEntityId, DateOnly PeriodStart, DateOnly PeriodEnd);
public record SubmitTaxReturnRequest(string SubmissionReference);
public record CreateFixedAssetRequest(int LegalEntityId, string AssetNumber, string Description, DateOnly AcquisitionDate, decimal AcquisitionCost, decimal ResidualValue, int UsefulLifeMonths, int AssetAccountId, int AccumulatedDepreciationAccountId, int DepreciationExpenseAccountId);
public record CreateBudgetLineRequest(int AccountId, int? FinancialDimensionValueId, decimal Amount);
public record CreateBudgetRequest(int LegalEntityId, string Name, DateOnly StartDate, DateOnly EndDate, IReadOnlyCollection<CreateBudgetLineRequest> Lines);

// Bounded /api/accounting contracts deliberately omit GL control-account IDs.
// The service resolves those accounts from the effective posting profile/event.
public record AccountingPlatformSettlementRequest(int LegalEntityId, Guid SourceEvidenceId, string SettlementReference, DateOnly SettlementDate, decimal GrossRevenue, decimal CommissionAmount, decimal NetSettlementAmount, string PostingProfileCode);
public record AccountingCustomerInvoiceLineRequest(string Description, decimal Quantity, decimal UnitPrice, int? TaxCodeId);
public record AccountingCustomerInvoiceRequest(int LegalEntityId, Guid CustomerAccountId, Guid? SourceEvidenceId, string InvoiceNumber, DateOnly InvoiceDate, DateOnly DueDate, string CurrencyCode, decimal ExchangeRate, string PostingProfileCode, IReadOnlyCollection<AccountingCustomerInvoiceLineRequest> Lines);
public record AccountingCustomerReceiptRequest(int LegalEntityId, Guid CustomerAccountId, string ReceiptNumber, string ExternalReference, DateOnly ReceiptDate, string CurrencyCode, decimal ExchangeRate, decimal Amount, string PostingProfileCode);
public record AccountingSupplierInvoiceLineRequest(string Description, decimal Quantity, decimal UnitPrice, int? TaxCodeId);
public record AccountingSupplierInvoiceRequest(int LegalEntityId, Guid SupplierAccountId, Guid? SourceEvidenceId, string InvoiceNumber, DateOnly InvoiceDate, DateOnly DueDate, string CurrencyCode, decimal ExchangeRate, string PostingProfileCode, IReadOnlyCollection<AccountingSupplierInvoiceLineRequest> Lines);
public record AccountingSupplierPaymentRequest(int LegalEntityId, Guid SupplierAccountId, string PaymentNumber, string ExternalReference, DateOnly PaymentDate, decimal Amount, string PostingProfileCode);
public record AccountingInventoryMovementRequest(int LegalEntityId, Guid InventoryItemId, InventoryMovementType MovementType, DateOnly MovementDate, string Reference, string FromBin, string ToBin, decimal Quantity, decimal UnitCost, string PostingProfileCode);
public record AccountingExpenseClaimRequest(int LegalEntityId, long EmployeeIqamaNo, Guid? SourceEvidenceId, string ClaimNumber, DateOnly ClaimDate, string Description, decimal NetAmount, int? TaxCodeId, string PostingProfileCode);

public abstract record AccountingRegisterFilter
{
    public int LegalEntityId { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public string SortDirection { get; init; } = "desc";
}

public sealed record MasterRecordListFilter : AccountingRegisterFilter { public bool? Active { get; init; } }
public sealed record CustomerInvoiceListFilter : AccountingRegisterFilter { public ReceivableInvoiceStatus? Status { get; init; } public Guid? CustomerAccountId { get; init; } }
public sealed record CustomerReceiptListFilter : AccountingRegisterFilter { public ReceiptStatus? Status { get; init; } public Guid? CustomerAccountId { get; init; } }
public sealed record PlatformSettlementListFilter : AccountingRegisterFilter { public SettlementStatus? Status { get; init; } }
public sealed record SupplierInvoiceListFilter : AccountingRegisterFilter { public PayableInvoiceStatus? Status { get; init; } public Guid? SupplierAccountId { get; init; } }
public sealed record SupplierPaymentListFilter : AccountingRegisterFilter { public PaymentStatus? Status { get; init; } public Guid? SupplierAccountId { get; init; } }
public sealed record SourceEvidenceListFilter : AccountingRegisterFilter { public SourceEvidenceStatus? Status { get; init; } public int? PlatformAccountId { get; init; } }
public sealed record ExpenseClaimListFilter : AccountingRegisterFilter { public ExpenseClaimStatus? Status { get; init; } public long? EmployeeIqamaNo { get; init; } }
public sealed record InventoryMovementListFilter : AccountingRegisterFilter { public InventoryMovementType? Status { get; init; } public Guid? InventoryItemId { get; init; } }
public sealed record BankStatementLineListFilter : AccountingRegisterFilter { public BankStatementStatus? Status { get; init; } public Guid? BankAccountId { get; init; } }
public sealed record TaxCodeListFilter : AccountingRegisterFilter { public bool? Active { get; init; } public TaxDirection? Direction { get; init; } }
public sealed record TaxReturnListFilter : AccountingRegisterFilter { public TaxReturnStatus? Status { get; init; } }
public sealed record FixedAssetListFilter : AccountingRegisterFilter { public AssetStatus? Status { get; init; } }
public sealed record BudgetListFilter : AccountingRegisterFilter { public bool? Approved { get; init; } }
public sealed record InventoryStockBalanceListFilter : AccountingRegisterFilter { public Guid? InventoryItemId { get; init; } public string? Bin { get; init; } }

public record MasterRecordResponse(Guid Id, int LegalEntityId, string Code, string Name, bool IsActive, string? TaxRegistrationNumber = null, string? UnitOfMeasure = null, string? CurrencyCode = null, int? LedgerAccountId = null, DateTime? CreatedAt = null);
public record SourceEvidenceResponse(Guid Id, int LegalEntityId, int? PlatformAccountId, Guid? StoredFileId, string EvidenceType, string ExternalReference, string ContentHash, SourceEvidenceStatus Status, DateTime ReceivedAt, string? ReviewedBy, string MetadataJson = "{}", string ReceivedBy = "", DateTime? ReviewedAt = null, string? ReviewComment = null);
public record FinancialOperationLineResponse(int LineNumber, string Description, decimal Quantity, decimal UnitPrice, decimal NetAmount, decimal TaxAmount, int AccountId, int? TaxCodeId);
public record FinancialOperationAllocationResponse(Guid RelatedDocumentId, decimal Amount, DateTime AllocatedAt, string AllocatedBy);
public record FinancialOperationResponse(
    Guid Id, int LegalEntityId, string Number, string Status, decimal Amount, Guid? FinancialDocumentId,
    Guid? CounterpartyId = null, Guid? SourceEvidenceId = null, DateOnly? TransactionDate = null, DateOnly? DueDate = null,
    string? CurrencyCode = null, decimal? ExchangeRate = null, decimal? NetAmount = null, decimal? TaxAmount = null,
    decimal? OpenAmount = null, decimal? UnappliedAmount = null, string? ExternalReference = null, string? Description = null,
    string? PostingProfileCode = null, decimal? GrossAmount = null, decimal? CommissionAmount = null, long? EmployeeIqamaNo = null,
    Guid? InventoryItemId = null, InventoryMovementType? MovementType = null, string? FromBin = null, string? ToBin = null,
    decimal? Quantity = null, decimal? UnitCost = null, Guid? BankAccountId = null, string? ReconciledBy = null,
    DateTime? ReconciledAt = null, IReadOnlyCollection<FinancialOperationLineResponse>? Lines = null,
    IReadOnlyCollection<FinancialOperationAllocationResponse>? Allocations = null, int? ReceivableAccountId = null,
    int? CashAccountId = null, int? PayableAccountId = null, int? PlatformClearingAccountId = null,
    int? CommissionExpenseAccountId = null, int? RevenueAccountId = null, int? ExpenseAccountId = null,
    int? EmployeePayableAccountId = null, int? TaxCodeId = null, int? DebitAccountId = null, int? CreditAccountId = null,
    string? CreatedBy = null, DateTime? CreatedAt = null, DateTime? ImportedAt = null);
public record PayrollRunResponse(Guid Id, int LegalEntityId, string RunNumber, DateOnly PeriodStart, DateOnly PeriodEnd, PayrollRunStatus Status, decimal GrossAmount, decimal DeductionAmount, decimal NetAmount, Guid? AccrualFinancialDocumentId, Guid? PaymentFinancialDocumentId);
public record TaxCodeResponse(int Id, int LegalEntityId, string Code, string Name, TaxDirection Direction, decimal Rate, int TaxAccountId, bool IsActive, DateOnly? EffectiveFrom = null, DateOnly? EffectiveTo = null);
public record TaxTransactionResponse(Guid Id, int TaxCodeId, Guid? FinancialDocumentId, string SourceReference, DateOnly TransactionDate, decimal NetAmount, decimal TaxAmount, TaxDirection Direction);
public record TaxReturnResponse(Guid Id, int LegalEntityId, DateOnly PeriodStart, DateOnly PeriodEnd, decimal OutputTaxAmount, decimal InputTaxAmount, decimal NetTaxPayableAmount, TaxReturnStatus Status, string? SubmissionReference, string? CreatedBy = null, DateTime? CreatedAt = null, string? SubmittedBy = null, DateTime? SubmittedAt = null, IReadOnlyCollection<TaxTransactionResponse>? Transactions = null);
public record FixedAssetResponse(Guid Id, int LegalEntityId, string AssetNumber, AssetStatus Status, decimal AcquisitionCost, decimal ResidualValue, int UsefulLifeMonths, string? Description = null, DateOnly? AcquisitionDate = null, int? AssetAccountId = null, int? AccumulatedDepreciationAccountId = null, int? DepreciationExpenseAccountId = null, DateTime? CreatedAt = null);
public record BudgetLineResponse(int AccountId, int? FinancialDimensionValueId, decimal Amount);
public record BudgetResponse(Guid Id, int LegalEntityId, string Name, DateOnly StartDate, DateOnly EndDate, bool IsApproved, decimal TotalAmount, string? CreatedBy = null, DateTime? CreatedAt = null, IReadOnlyCollection<BudgetLineResponse>? Lines = null);
public record InventoryStockBalanceResponse(Guid InventoryItemId, int LegalEntityId, string Sku, string ItemName, string UnitOfMeasure, string Bin, decimal Quantity, decimal Value);

public class CreateSourceEvidenceRequestValidator : AbstractValidator<CreateSourceEvidenceRequest>
{ public CreateSourceEvidenceRequestValidator() { RuleFor(x => x.LegalEntityId).GreaterThan(0); RuleFor(x => x.EvidenceType).NotEmpty().MaximumLength(64); RuleFor(x => x.ExternalReference).NotEmpty().MaximumLength(128); RuleFor(x => x.StorageLocator).NotEmpty().MaximumLength(1024); RuleFor(x => x.ContentHash).Matches("^[A-Fa-f0-9]{64,128}$"); RuleFor(x => x.MetadataJson).NotEmpty(); } }
public class CreatePrivateSourceEvidenceRequestValidator : AbstractValidator<CreatePrivateSourceEvidenceRequest>
{ public CreatePrivateSourceEvidenceRequestValidator() { RuleFor(x => x.LegalEntityId).GreaterThan(0); RuleFor(x => x.EvidenceType).NotEmpty().MaximumLength(64); RuleFor(x => x.ExternalReference).NotEmpty().MaximumLength(128); RuleFor(x => x.StoredFileId).NotEmpty(); RuleFor(x => x.MetadataJson).NotEmpty(); } }
public class CreateCustomerInvoiceRequestValidator : AbstractValidator<CreateCustomerInvoiceRequest>
{ public CreateCustomerInvoiceRequestValidator() { RuleFor(x => x.LegalEntityId).GreaterThan(0); RuleFor(x => x.CustomerAccountId).NotEmpty(); RuleFor(x => x.InvoiceNumber).NotEmpty().MaximumLength(64); RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.InvoiceDate); RuleFor(x => x.CurrencyCode).Length(3); RuleFor(x => x.ExchangeRate).GreaterThan(0); RuleFor(x => x.ReceivableAccountId).GreaterThan(0); RuleFor(x => x.PostingProfileCode).NotEmpty().MaximumLength(64); RuleFor(x => x.Lines).NotEmpty(); RuleForEach(x => x.Lines).ChildRules(y => { y.RuleFor(x => x.Description).NotEmpty().MaximumLength(500); y.RuleFor(x => x.Quantity).GreaterThan(0); y.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0); y.RuleFor(x => x.RevenueAccountId).GreaterThan(0); }); } }
public class CreateSupplierInvoiceRequestValidator : AbstractValidator<CreateSupplierInvoiceRequest>
{ public CreateSupplierInvoiceRequestValidator() { RuleFor(x => x.LegalEntityId).GreaterThan(0); RuleFor(x => x.SupplierAccountId).NotEmpty(); RuleFor(x => x.InvoiceNumber).NotEmpty().MaximumLength(64); RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.InvoiceDate); RuleFor(x => x.CurrencyCode).Length(3); RuleFor(x => x.ExchangeRate).GreaterThan(0); RuleFor(x => x.PayableAccountId).GreaterThan(0); RuleFor(x => x.PostingProfileCode).NotEmpty().MaximumLength(64); RuleFor(x => x.Lines).NotEmpty(); RuleForEach(x => x.Lines).ChildRules(y => { y.RuleFor(x => x.Description).NotEmpty().MaximumLength(500); y.RuleFor(x => x.Quantity).GreaterThan(0); y.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0); y.RuleFor(x => x.ExpenseOrInventoryAccountId).GreaterThan(0); }); } }
