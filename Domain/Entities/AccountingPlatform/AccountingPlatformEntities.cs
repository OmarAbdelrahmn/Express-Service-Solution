using Domain.Entities.Organization;

namespace Domain.Entities.AccountingPlatform;

public enum StoredFileStatus { Active = 1, Quarantined = 2, Deleted = 3 }
public enum PlatformTemplateStatus { Draft = 1, Active = 2, Retired = 3 }
public enum PlatformImportStatus { Received = 1, Parsing = 2, NeedsResolution = 3, Reconciled = 4, Approved = 5, Rejected = 6, Superseded = 7, Failed = 8 }
public enum PlatformImportIssueStatus { Open = 1, Resolved = 2, Waived = 3 }
public enum PlatformImportIssueSeverity { Warning = 1, Blocking = 2 }
public enum PlatformFactCategory { Activity = 1, RiderPayout = 2, CompanyBilling = 3, Tax = 4, Payout = 5, Validity = 6, Penalty = 7, ControlTotal = 8 }
public enum CompensationPolicyStatus { Draft = 1, Active = 2, Retired = 3 }
public enum CompensationRuleTemplate { FixedAmount = 1, PerUnit = 2, Threshold = 3, TieredBasePlusExcess = 4, Percentage = 5, Range = 6, Cap = 7, Floor = 8, EligibilityCondition = 9 }
public enum CompensationComponentType { Earning = 1, Allowance = 2, Bonus = 3, Deduction = 4, Informational = 5 }
public enum CompensationStackingMode { ExclusiveHighest = 1, Cumulative = 2 }
public enum RiderPayrollStatus { Draft = 1, Calculated = 2, Approved = 3, PaymentPrepared = 4, PartiallyPaid = 5, Paid = 6, Held = 7, Reversed = 8 }
public enum RiderPayrollComponentSource { Policy = 1, FinancialItem = 2, Adjustment = 3, CarryForward = 4 }
public enum RiderFinancialItemStatus { Open = 1, Settled = 2, Reversed = 3 }
public enum RiderFinancialItemDirection { Earning = 1, Deduction = 2 }
public enum RiderPaymentMethod { Bank = 1, Cash = 2, Hold = 3, Mixed = 4 }
public enum RiderPaymentBatchStatus { Prepared = 1, Exported = 2, Sent = 3, Confirmed = 4, PartiallyRejected = 5, Rejected = 6, Reversed = 7 }

public class AccountingStoredFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long PlaintextLength { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string StorageLocator { get; set; } = string.Empty;
    public string EncryptionKeyId { get; set; } = string.Empty;
    public StoredFileStatus Status { get; set; } = StoredFileStatus.Active;
    public DateTime? RetainUntil { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public LegalEntity LegalEntity { get; set; } = null!;
}

public class PlatformImportTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public int PlatformAccountId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public string AdapterKey { get; set; } = "generic-tabular-v1";
    public string SchemaFingerprint { get; set; } = string.Empty;
    public string ConfigurationJson { get; set; } = "{}";
    public PlatformTemplateStatus Status { get; set; } = PlatformTemplateStatus.Draft;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ActivatedBy { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public LegalEntity LegalEntity { get; set; } = null!;
    public PlatformAccount PlatformAccount { get; set; } = null!;
}

public class PlatformImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public int PlatformAccountId { get; set; }
    public Guid StoredFileId { get; set; }
    public Guid? TemplateId { get; set; }
    public string ExternalReference { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string ParserVersion { get; set; } = string.Empty;
    public string SchemaFingerprint { get; set; } = string.Empty;
    public PlatformImportStatus Status { get; set; } = PlatformImportStatus.Received;
    public decimal? SourceControlTotal { get; set; }
    public decimal? NormalizedControlTotal { get; set; }
    public string? FailureReason { get; set; }
    public Guid? SupersedesBatchId { get; set; }
    public Guid? SupersededByBatchId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public LegalEntity LegalEntity { get; set; } = null!;
    public PlatformAccount PlatformAccount { get; set; } = null!;
    public AccountingStoredFile StoredFile { get; set; } = null!;
    public PlatformImportTemplate? Template { get; set; }
    public PlatformImportBatch? SupersedesBatch { get; set; }
    public ICollection<PlatformImportSheet> Sheets { get; set; } = [];
    public ICollection<PlatformNormalizedFact> Facts { get; set; } = [];
    public ICollection<PlatformImportIssue> Issues { get; set; } = [];
}

public class PlatformImportSheet
{
    public long Id { get; set; }
    public Guid PlatformImportBatchId { get; set; }
    public int SheetIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsHidden { get; set; }
    public int MaxRowNumber { get; set; }
    public int MaxColumnNumber { get; set; }
    public PlatformImportBatch PlatformImportBatch { get; set; } = null!;
    public ICollection<PlatformImportRawRow> Rows { get; set; } = [];
}

public class PlatformImportRawRow
{
    public long Id { get; set; }
    public long PlatformImportSheetId { get; set; }
    public int RowNumber { get; set; }
    public string RowHash { get; set; } = string.Empty;
    public PlatformImportSheet PlatformImportSheet { get; set; } = null!;
    public ICollection<PlatformImportRawCell> Cells { get; set; } = [];
}

public class PlatformImportRawCell
{
    public long Id { get; set; }
    public long PlatformImportRawRowId { get; set; }
    public int ColumnNumber { get; set; }
    public string CellReference { get; set; } = string.Empty;
    public string? RawValue { get; set; }
    public string? DisplayValue { get; set; }
    public string? Formula { get; set; }
    public string DataType { get; set; } = string.Empty;
    public PlatformImportRawRow PlatformImportRawRow { get; set; } = null!;
}

public class PlatformNormalizedFact
{
    public long Id { get; set; }
    public Guid PlatformImportBatchId { get; set; }
    public int LegalEntityId { get; set; }
    public int PlatformAccountId { get; set; }
    public string WorkerCategory { get; set; } = "Rider";
    public long? SourceRawRowId { get; set; }
    public long? RiderIqamaNo { get; set; }
    public string ExternalWorkerId { get; set; } = string.Empty;
    public DateOnly FactDate { get; set; }
    public PlatformFactCategory Category { get; set; }
    public string MetricCode { get; set; } = string.Empty;
    public decimal? NumericValue { get; set; }
    public string? TextValue { get; set; }
    public bool? BooleanValue { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public bool IsResolved { get; set; }
    public string LineageJson { get; set; } = "{}";
    public PlatformImportBatch PlatformImportBatch { get; set; } = null!;
    public PlatformImportRawRow? SourceRawRow { get; set; }
    public PlatformFactOverride? Override { get; set; }
}

public class PlatformFactOverride
{
    public long Id { get; set; }
    public long PlatformNormalizedFactId { get; set; }
    public bool BooleanValue { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public PlatformNormalizedFact PlatformNormalizedFact { get; set; } = null!;
}

public class PlatformImportIssue
{
    public long Id { get; set; }
    public Guid PlatformImportBatchId { get; set; }
    public long? SourceRawRowId { get; set; }
    public PlatformImportIssueSeverity Severity { get; set; }
    public PlatformImportIssueStatus Status { get; set; } = PlatformImportIssueStatus.Open;
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public PlatformImportBatch PlatformImportBatch { get; set; } = null!;
    public PlatformImportRawRow? SourceRawRow { get; set; }
}

public class PlatformWorkerIdentity
{
    public long Id { get; set; }
    public int LegalEntityId { get; set; }
    public int PlatformAccountId { get; set; }
    public string ExternalWorkerId { get; set; } = string.Empty;
    public long RiderIqamaNo { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsSubstitution { get; set; }
    public string? Reason { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public LegalEntity LegalEntity { get; set; } = null!;
    public PlatformAccount PlatformAccount { get; set; } = null!;
}

public class CompensationPolicyVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public int PlatformAccountId { get; set; }
    public string WorkerCategory { get; set; } = "Rider";
    public string Code { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public CompensationPolicyStatus Status { get; set; } = CompensationPolicyStatus.Draft;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ActivatedBy { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public LegalEntity LegalEntity { get; set; } = null!;
    public PlatformAccount PlatformAccount { get; set; } = null!;
    public ICollection<CompensationRule> Rules { get; set; } = [];
}

public class CompensationRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompensationPolicyVersionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CompensationRuleTemplate Template { get; set; }
    public CompensationComponentType ComponentType { get; set; }
    public string MetricCode { get; set; } = string.Empty;
    public string? ConditionMetricCode { get; set; }
    public string? ConditionOperator { get; set; }
    public decimal? ConditionValue { get; set; }
    public decimal? LowerBound { get; set; }
    public decimal? UpperBound { get; set; }
    public decimal? Rate { get; set; }
    public decimal? BelowRate { get; set; }
    public decimal? AboveRate { get; set; }
    public decimal? FixedAmount { get; set; }
    public decimal? BaseAmount { get; set; }
    public string? TargetComponentCode { get; set; }
    public int Priority { get; set; }
    public string? ExclusiveGroup { get; set; }
    public CompensationStackingMode StackingMode { get; set; } = CompensationStackingMode.ExclusiveHighest;
    public int RoundingScale { get; set; } = 2;
    public bool IsActive { get; set; } = true;
    public CompensationPolicyVersion CompensationPolicyVersion { get; set; } = null!;
}

public class RiderPayrollRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public string RunNumber { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public RiderPayrollStatus Status { get; set; } = RiderPayrollStatus.Draft;
    public decimal GrossEarnings { get; set; }
    public decimal AppliedDeductions { get; set; }
    public decimal CarriedDeductions { get; set; }
    public decimal NetPay { get; set; }
    public Guid? AccrualFinancialDocumentId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public LegalEntity LegalEntity { get; set; } = null!;
    public AccountingCore.FinancialDocument? AccrualFinancialDocument { get; set; }
    public ICollection<RiderPayrollLine> Lines { get; set; } = [];
}

public class RiderPayrollLine
{
    public long Id { get; set; }
    public Guid RiderPayrollRunId { get; set; }
    public long RiderIqamaNo { get; set; }
    public decimal GrossEarnings { get; set; }
    public decimal AppliedDeductions { get; set; }
    public decimal CarriedDeductions { get; set; }
    public decimal NetPay { get; set; }
    public bool IsHeld { get; set; }
    public string? HoldReason { get; set; }
    public RiderPayrollRun RiderPayrollRun { get; set; } = null!;
    public ICollection<RiderPayrollComponent> Components { get; set; } = [];
}

public class RiderPayrollComponent
{
    public long Id { get; set; }
    public long RiderPayrollLineId { get; set; }
    public int? PlatformAccountId { get; set; }
    public Guid? CompensationPolicyVersionId { get; set; }
    public Guid? CompensationRuleId { get; set; }
    public Guid? SourceImportBatchId { get; set; }
    public Guid? RiderFinancialItemId { get; set; }
    public Guid? RiderPayrollCarryForwardId { get; set; }
    public RiderPayrollComponentSource Source { get; set; }
    public CompensationComponentType ComponentType { get; set; }
    public string ComponentCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
    public string CalculationJson { get; set; } = "{}";
    public bool IsAutomatic { get; set; }
    public RiderPayrollLine RiderPayrollLine { get; set; } = null!;
    public PlatformAccount? PlatformAccount { get; set; }
    public CompensationPolicyVersion? CompensationPolicyVersion { get; set; }
    public CompensationRule? CompensationRule { get; set; }
    public PlatformImportBatch? SourceImportBatch { get; set; }
    public RiderFinancialItem? RiderFinancialItem { get; set; }
    public RiderPayrollCarryForward? RiderPayrollCarryForward { get; set; }
}

public class RiderPayrollCarryForward
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public long RiderIqamaNo { get; set; }
    public Guid CreatedFromPayrollRunId { get; set; }
    public string SourceCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Priority { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public RiderFinancialItemStatus Status { get; set; } = RiderFinancialItemStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public LegalEntity LegalEntity { get; set; } = null!;
    public RiderPayrollRun CreatedFromPayrollRun { get; set; } = null!;
}

public class RiderPayrollAdjustment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long RiderPayrollLineId { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid? EvidenceFileId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public RiderPayrollLine RiderPayrollLine { get; set; } = null!;
    public AccountingStoredFile? EvidenceFile { get; set; }
}

public class RiderFinancialItemType
{
    public int Id { get; set; }
    public int LegalEntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public RiderFinancialItemDirection Direction { get; set; }
    public int Priority { get; set; }
    public int LedgerAccountId { get; set; }
    public bool IsActive { get; set; } = true;
    public LegalEntity LegalEntity { get; set; } = null!;
    public AccountingCore.AccountingAccount LedgerAccount { get; set; } = null!;
}

public class RiderFinancialItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public long RiderIqamaNo { get; set; }
    public int RiderFinancialItemTypeId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }
    public DateOnly? DeductionStartDate { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public int? InstallmentCount { get; set; }
    public RiderFinancialItemStatus Status { get; set; } = RiderFinancialItemStatus.Open;
    public Guid? EvidenceFileId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [];
    public LegalEntity LegalEntity { get; set; } = null!;
    public RiderFinancialItemType ItemType { get; set; } = null!;
    public AccountingStoredFile? EvidenceFile { get; set; }
    public ICollection<RiderFinancialInstallment> Installments { get; set; } = [];
}

public class RiderFinancialInstallment
{
    public long Id { get; set; }
    public Guid RiderFinancialItemId { get; set; }
    public int Sequence { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal ScheduledAmount { get; set; }
    public decimal AppliedAmount { get; set; }
    public bool IsSettled { get; set; }
    public RiderFinancialItem RiderFinancialItem { get; set; } = null!;
}

public class RiderPaymentBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public Guid RiderPayrollRunId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public RiderPaymentMethod Method { get; set; }
    public RiderPaymentBatchStatus Status { get; set; } = RiderPaymentBatchStatus.Prepared;
    public Guid? ExportFileId { get; set; }
    public Guid? PaymentFinancialDocumentId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public AccountingStoredFile? ExportFile { get; set; }
    public AccountingCore.FinancialDocument? PaymentFinancialDocument { get; set; }
    public ICollection<RiderPaymentBatchLine> Lines { get; set; } = [];
}

public class RiderPaymentBatchLine
{
    public long Id { get; set; }
    public Guid RiderPaymentBatchId { get; set; }
    public long RiderPayrollLineId { get; set; }
    public RiderPaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? IbanSnapshot { get; set; }
    public int? HousingId { get; set; }
    public bool IsConfirmed { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public Guid? PaymentFinancialDocumentId { get; set; }
    public RiderPaymentBatch RiderPaymentBatch { get; set; } = null!;
    public RiderPayrollLine RiderPayrollLine { get; set; } = null!;
    public AccountingCore.FinancialDocument? PaymentFinancialDocument { get; set; }
}

public class HousingCashUserAccess
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int LegalEntityId { get; set; }
    public int HousingId { get; set; }
    public bool IsActive { get; set; } = true;
    public string GrantedBy { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [];
    public Domain.Entities.ApplicationUser User { get; set; } = null!;
    public LegalEntity LegalEntity { get; set; } = null!;
    public Domain.Entities.Housing Housing { get; set; } = null!;
}
