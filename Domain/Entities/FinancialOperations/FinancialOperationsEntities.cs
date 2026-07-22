namespace Domain.Entities.FinancialOperations;

public enum SourceEvidenceStatus { Received = 1, Accepted = 2, Rejected = 3 }
public enum SettlementStatus { Draft = 1, Recorded = 2, Reversed = 3 }
public enum ReceivableInvoiceStatus { Draft = 1, Issued = 2, PartiallySettled = 3, Settled = 4, Cancelled = 5 }
public enum ReceiptStatus { Unapplied = 1, PartiallyApplied = 2, Applied = 3, Voided = 4 }
public enum PayrollRunStatus { Draft = 1, Prepared = 2, Paid = 3, Reversed = 4 }
public enum PayableInvoiceStatus { Draft = 1, Recorded = 2, PartiallyPaid = 3, Paid = 4, Cancelled = 5 }
public enum PaymentStatus { Unapplied = 1, PartiallyApplied = 2, Applied = 3, Voided = 4 }
public enum InventoryMovementType { Receipt = 1, Issue = 2, Transfer = 3, Adjustment = 4 }
public enum ExpenseClaimStatus { Draft = 1, Recorded = 2, Paid = 3, Rejected = 4 }
public enum BankStatementStatus { Unreconciled = 1, Reconciled = 2, Ignored = 3 }
public enum TaxDirection { Output = 1, Input = 2 }
public enum TaxReturnStatus { Draft = 1, Submitted = 2, Accepted = 3, Rejected = 4 }
public enum AssetStatus { Active = 1, FullyDepreciated = 2, Disposed = 3 }

public class SourceEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public int? PlatformAccountId { get; set; }
    public Guid? StoredFileId { get; set; }
    public string EvidenceType { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;
    public string StorageLocator { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
    public SourceEvidenceStatus Status { get; set; } = SourceEvidenceStatus.Received;
    public string ReceivedBy { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
    public Organization.LegalEntity LegalEntity { get; set; } = null!;
    public Organization.PlatformAccount? PlatformAccount { get; set; }
    public AccountingPlatform.AccountingStoredFile? StoredFile { get; set; }
}

public class PlatformSettlement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public Guid SourceEvidenceId { get; set; }
    public string SettlementReference { get; set; } = string.Empty;
    public DateOnly SettlementDate { get; set; }
    public decimal GrossRevenue { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal NetSettlementAmount { get; set; }
    public int PlatformClearingAccountId { get; set; }
    public int CommissionExpenseAccountId { get; set; }
    public int RevenueAccountId { get; set; }
    public string PostingProfileCode { get; set; } = string.Empty;
    public SettlementStatus Status { get; set; } = SettlementStatus.Draft;
    public Guid? FinancialDocumentId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public SourceEvidence SourceEvidence { get; set; } = null!;
    public AccountingCore.FinancialDocument? FinancialDocument { get; set; }
}

public class CustomerAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? TaxRegistrationNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Organization.LegalEntity LegalEntity { get; set; } = null!;
    public ICollection<CustomerInvoice> Invoices { get; set; } = [];
}

public class CustomerInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public Guid CustomerAccountId { get; set; }
    public Guid? SourceEvidenceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public decimal ExchangeRate { get; set; } = 1m;
    public int ReceivableAccountId { get; set; }
    public string PostingProfileCode { get; set; } = string.Empty;
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public ReceivableInvoiceStatus Status { get; set; } = ReceivableInvoiceStatus.Draft;
    public Guid? FinancialDocumentId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public CustomerAccount CustomerAccount { get; set; } = null!;
    public SourceEvidence? SourceEvidence { get; set; }
    public AccountingCore.FinancialDocument? FinancialDocument { get; set; }
    public ICollection<CustomerInvoiceLine> Lines { get; set; } = [];
}

public class CustomerInvoiceLine
{
    public int Id { get; set; }
    public Guid CustomerInvoiceId { get; set; }
    public int LineNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public int RevenueAccountId { get; set; }
    public int? TaxCodeId { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public CustomerInvoice CustomerInvoice { get; set; } = null!;
    public TaxCode? TaxCode { get; set; }
}

public class CustomerReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public Guid CustomerAccountId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;
    public DateOnly ReceiptDate { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal Amount { get; set; }
    public int CashAccountId { get; set; }
    public int ReceivableAccountId { get; set; }
    public string PostingProfileCode { get; set; } = string.Empty;
    public ReceiptStatus Status { get; set; } = ReceiptStatus.Unapplied;
    public Guid? FinancialDocumentId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public CustomerAccount CustomerAccount { get; set; } = null!;
    public AccountingCore.FinancialDocument? FinancialDocument { get; set; }
    public ICollection<CustomerReceiptAllocation> Allocations { get; set; } = [];
}

public class CustomerReceiptAllocation
{
    public int Id { get; set; }
    public Guid CustomerReceiptId { get; set; }
    public Guid CustomerInvoiceId { get; set; }
    public decimal Amount { get; set; }
    public DateTime AllocatedAt { get; set; } = DateTime.UtcNow;
    public string AllocatedBy { get; set; } = string.Empty;
    public CustomerReceipt CustomerReceipt { get; set; } = null!;
    public CustomerInvoice CustomerInvoice { get; set; } = null!;
}

public class EmployeePayContract
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public long EmployeeIqamaNo { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public decimal GrossSalary { get; set; }
    public decimal FixedDeduction { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public bool IsActive { get; set; } = true;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PayrollRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public string RunNumber { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public int PayrollExpenseAccountId { get; set; }
    public int PayrollPayableAccountId { get; set; }
    public int DeductionLiabilityAccountId { get; set; }
    public string PostingProfileCode { get; set; } = string.Empty;
    public PayrollRunStatus Status { get; set; } = PayrollRunStatus.Draft;
    public Guid? AccrualFinancialDocumentId { get; set; }
    public Guid? PaymentFinancialDocumentId { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DeductionAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public AccountingCore.FinancialDocument? AccrualFinancialDocument { get; set; }
    public AccountingCore.FinancialDocument? PaymentFinancialDocument { get; set; }
    public ICollection<PayrollRunLine> Lines { get; set; } = [];
}

public class PayrollRunLine
{
    public int Id { get; set; }
    public Guid PayrollRunId { get; set; }
    public long EmployeeIqamaNo { get; set; }
    public Guid EmployeePayContractId { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DeductionAmount { get; set; }
    public decimal NetAmount { get; set; }
    public PayrollRun PayrollRun { get; set; } = null!;
}

public class SupplierAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? TaxRegistrationNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Organization.LegalEntity LegalEntity { get; set; } = null!;
}

public class SupplierInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public Guid SupplierAccountId { get; set; }
    public Guid? SourceEvidenceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public decimal ExchangeRate { get; set; } = 1m;
    public int PayableAccountId { get; set; }
    public string PostingProfileCode { get; set; } = string.Empty;
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public PayableInvoiceStatus Status { get; set; } = PayableInvoiceStatus.Draft;
    public Guid? FinancialDocumentId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public SupplierAccount SupplierAccount { get; set; } = null!;
    public SourceEvidence? SourceEvidence { get; set; }
    public AccountingCore.FinancialDocument? FinancialDocument { get; set; }
    public ICollection<SupplierInvoiceLine> Lines { get; set; } = [];
}

public class SupplierInvoiceLine
{
    public int Id { get; set; }
    public Guid SupplierInvoiceId { get; set; }
    public int LineNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public int ExpenseOrInventoryAccountId { get; set; }
    public int? TaxCodeId { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public SupplierInvoice SupplierInvoice { get; set; } = null!;
    public TaxCode? TaxCode { get; set; }
}

public class SupplierPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public Guid SupplierAccountId { get; set; }
    public string PaymentNumber { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public int CashAccountId { get; set; }
    public int PayableAccountId { get; set; }
    public string PostingProfileCode { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; } = PaymentStatus.Unapplied;
    public Guid? FinancialDocumentId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public SupplierAccount SupplierAccount { get; set; } = null!;
    public AccountingCore.FinancialDocument? FinancialDocument { get; set; }
    public ICollection<SupplierPaymentAllocation> Allocations { get; set; } = [];
}

public class SupplierPaymentAllocation
{
    public int Id { get; set; }
    public Guid SupplierPaymentId { get; set; }
    public Guid SupplierInvoiceId { get; set; }
    public decimal Amount { get; set; }
    public DateTime AllocatedAt { get; set; } = DateTime.UtcNow;
    public string AllocatedBy { get; set; } = string.Empty;
    public SupplierPayment SupplierPayment { get; set; } = null!;
    public SupplierInvoice SupplierInvoice { get; set; } = null!;
}

public class InventoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string UnitOfMeasure { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Organization.LegalEntity LegalEntity { get; set; } = null!;
}

public class InventoryMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public Guid InventoryItemId { get; set; }
    public InventoryMovementType MovementType { get; set; }
    public DateOnly MovementDate { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string FromBin { get; set; } = string.Empty;
    public string ToBin { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public int DebitAccountId { get; set; }
    public int CreditAccountId { get; set; }
    public string PostingProfileCode { get; set; } = string.Empty;
    public Guid? FinancialDocumentId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public InventoryItem InventoryItem { get; set; } = null!;
    public AccountingCore.FinancialDocument? FinancialDocument { get; set; }
}

public class ExpenseClaim
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public long EmployeeIqamaNo { get; set; }
    public Guid? SourceEvidenceId { get; set; }
    public string ClaimNumber { get; set; } = string.Empty;
    public DateOnly ClaimDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public int ExpenseAccountId { get; set; }
    public int EmployeePayableAccountId { get; set; }
    public int? TaxCodeId { get; set; }
    public string PostingProfileCode { get; set; } = string.Empty;
    public ExpenseClaimStatus Status { get; set; } = ExpenseClaimStatus.Draft;
    public Guid? FinancialDocumentId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public SourceEvidence? SourceEvidence { get; set; }
    public TaxCode? TaxCode { get; set; }
    public AccountingCore.FinancialDocument? FinancialDocument { get; set; }
}

public class BankAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "SAR";
    public int LedgerAccountId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Organization.LegalEntity LegalEntity { get; set; } = null!;
}

public class BankStatementLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BankAccountId { get; set; }
    public string ExternalReference { get; set; } = string.Empty;
    public DateOnly TransactionDate { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public BankStatementStatus Status { get; set; } = BankStatementStatus.Unreconciled;
    public Guid? MatchedFinancialDocumentId { get; set; }
    public string? ReconciledBy { get; set; }
    public DateTime? ReconciledAt { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public BankAccount BankAccount { get; set; } = null!;
    public AccountingCore.FinancialDocument? MatchedFinancialDocument { get; set; }
}

public class TaxCode
{
    public int Id { get; set; }
    public int LegalEntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TaxDirection Direction { get; set; }
    public decimal Rate { get; set; }
    public int TaxAccountId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public Organization.LegalEntity LegalEntity { get; set; } = null!;
}

public class TaxTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public int TaxCodeId { get; set; }
    public Guid? FinancialDocumentId { get; set; }
    public Guid? TaxReturnId { get; set; }
    public string SourceReference { get; set; } = string.Empty;
    public DateOnly TransactionDate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public TaxDirection Direction { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public TaxCode TaxCode { get; set; } = null!;
    public AccountingCore.FinancialDocument? FinancialDocument { get; set; }
    public TaxReturn? TaxReturn { get; set; }
}

public class TaxReturn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal OutputTaxAmount { get; set; }
    public decimal InputTaxAmount { get; set; }
    public decimal NetTaxPayableAmount { get; set; }
    public TaxReturnStatus Status { get; set; } = TaxReturnStatus.Draft;
    public string? SubmissionReference { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public Organization.LegalEntity LegalEntity { get; set; } = null!;
    public ICollection<TaxTransaction> TaxTransactions { get; set; } = [];
}

public class FixedAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public string AssetNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateOnly AcquisitionDate { get; set; }
    public decimal AcquisitionCost { get; set; }
    public decimal ResidualValue { get; set; }
    public int UsefulLifeMonths { get; set; }
    public int AssetAccountId { get; set; }
    public int AccumulatedDepreciationAccountId { get; set; }
    public int DepreciationExpenseAccountId { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Organization.LegalEntity LegalEntity { get; set; } = null!;
}

public class Budget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsApproved { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Organization.LegalEntity LegalEntity { get; set; } = null!;
    public ICollection<BudgetLine> Lines { get; set; } = [];
}

public class BudgetLine
{
    public int Id { get; set; }
    public Guid BudgetId { get; set; }
    public int AccountId { get; set; }
    public int? FinancialDimensionValueId { get; set; }
    public decimal Amount { get; set; }
    public Budget Budget { get; set; } = null!;
    public AccountingCore.AccountingAccount Account { get; set; } = null!;
    public AccountingCore.FinancialDimensionValue? FinancialDimensionValue { get; set; }
}
