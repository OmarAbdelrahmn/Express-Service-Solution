namespace Domain.Entities.AccountingCore;

public enum AccountingAccountType { Asset = 1, Liability = 2, Equity = 3, Revenue = 4, Expense = 5 }
public enum FinancialDocumentStatus { Draft = 1, Submitted = 2, Approved = 3, Posted = 4, Reversed = 5, Cancelled = 6 }
public enum FiscalPeriodStatus { Open = 1, Closed = 2, SoftClosed = 3 }

[Flags]
public enum FinancialPermission
{
    None = 0,
    View = 1,
    Prepare = 2,
    Approve = 4,
    Post = 8,
    ManagePeriods = 16,
    Configure = 32,
    All = View | Prepare | Approve | Post | ManagePeriods | Configure
}

public class FinancialUserAccess
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int LegalEntityId { get; set; }
    public FinancialPermission Permissions { get; set; }
    public string GrantedBy { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Domain.Entities.ApplicationUser User { get; set; } = null!;
    public Organization.LegalEntity LegalEntity { get; set; } = null!;
}

public class Currency
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DecimalPlaces { get; set; } = 2;
    public bool IsActive { get; set; } = true;
}

public class ExchangeRate
{
    public long Id { get; set; }
    public int LegalEntityId { get; set; }
    public string FromCurrencyCode { get; set; } = string.Empty;
    public string ToCurrencyCode { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }
    public decimal Rate { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Organization.LegalEntity LegalEntity { get; set; } = null!;
}

public class FinancialDimension
{
    public int Id { get; set; }
    public int LegalEntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; } = true;

    public Organization.LegalEntity LegalEntity { get; set; } = null!;
    public ICollection<FinancialDimensionValue> Values { get; set; } = [];
}

public class FinancialDimensionValue
{
    public int Id { get; set; }
    public int FinancialDimensionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public FinancialDimension FinancialDimension { get; set; } = null!;
}

public class AccountingAccount
{
    public int Id { get; set; }
    public int LegalEntityId { get; set; }
    public int? ParentAccountId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountingAccountType Type { get; set; }
    public bool IsControlAccount { get; set; }
    public bool AllowManualPosting { get; set; } = true;
    public bool IsCashEquivalent { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Organization.LegalEntity LegalEntity { get; set; } = null!;
    public AccountingAccount? ParentAccount { get; set; }
    public ICollection<AccountingAccount> ChildAccounts { get; set; } = [];
}

public class PostingProfile
{
    public int Id { get; set; }
    public int LegalEntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Organization.LegalEntity LegalEntity { get; set; } = null!;
    public ICollection<PostingProfileLine> Lines { get; set; } = [];
}

public class PostingProfileLine
{
    public int Id { get; set; }
    public int PostingProfileId { get; set; }
    public string EventCode { get; set; } = string.Empty;
    public int DebitAccountId { get; set; }
    public int CreditAccountId { get; set; }

    public PostingProfile PostingProfile { get; set; } = null!;
    public AccountingAccount DebitAccount { get; set; } = null!;
    public AccountingAccount CreditAccount { get; set; } = null!;
}

public class FiscalYear
{
    public int Id { get; set; }
    public int LegalEntityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsClosed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Organization.LegalEntity LegalEntity { get; set; } = null!;
    public ICollection<FiscalPeriod> Periods { get; set; } = [];
}

public class FiscalPeriod
{
    public int Id { get; set; }
    public int FiscalYearId { get; set; }
    public int PeriodNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public FiscalPeriodStatus Status { get; set; } = FiscalPeriodStatus.Open;
    public bool TaxLocked { get; set; }
    public bool PayrollLocked { get; set; }
    public string? CloseReason { get; set; }
    public string? ReopenReason { get; set; }
    public string? ReopenedBy { get; set; }
    public DateTime? ReopenedAt { get; set; }
    public string? ClosedBy { get; set; }
    public DateTime? ClosedAt { get; set; }

    public FiscalYear FiscalYear { get; set; } = null!;
}

public class FinancialDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public int? BranchId { get; set; }
    public string DocumentType { get; set; } = "ManualJournal";
    public string DocumentNumber { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? SourceReference { get; set; }
    public string? PostingProfileCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateOnly TransactionDate { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public string BaseCurrencyCode { get; set; } = "SAR";
    public decimal ExchangeRate { get; set; } = 1m;
    public long? ExchangeRateId { get; set; }
    public string RoundingTraceJson { get; set; } = "{}";
    public FinancialDocumentStatus Status { get; set; } = FinancialDocumentStatus.Draft;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? PostedBy { get; set; }
    public DateTime? PostedAt { get; set; }
    public Guid? ReversalOfDocumentId { get; set; }
    public Guid? ReversedByDocumentId { get; set; }
    public string? ReversalReason { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Organization.LegalEntity LegalEntity { get; set; } = null!;
    public Organization.Branch? Branch { get; set; }
    public FinancialDocument? ReversalOfDocument { get; set; }
    public ICollection<FinancialDocumentLine> Lines { get; set; } = [];
    public ICollection<DocumentApproval> Approvals { get; set; } = [];
}

public class LegalEntityDocumentSequence
{
    public int Id { get; set; }
    public int LegalEntityId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public long NextNumber { get; set; } = 1;
    public byte[] RowVersion { get; set; } = [];

    public Organization.LegalEntity LegalEntity { get; set; } = null!;
}

public class FinancialDocumentLine
{
    public int Id { get; set; }
    public Guid FinancialDocumentId { get; set; }
    public int LineNumber { get; set; }
    public int AccountId { get; set; }
    public string? Description { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal BaseDebit { get; set; }
    public decimal BaseCredit { get; set; }

    public FinancialDocument FinancialDocument { get; set; } = null!;
    public AccountingAccount Account { get; set; } = null!;
    public ICollection<FinancialDocumentLineDimension> Dimensions { get; set; } = [];
}

public class FinancialDocumentLineDimension
{
    public int FinancialDocumentLineId { get; set; }
    public int FinancialDimensionValueId { get; set; }
    public FinancialDocumentLine FinancialDocumentLine { get; set; } = null!;
    public FinancialDimensionValue FinancialDimensionValue { get; set; } = null!;
}

public class DocumentApproval
{
    public int Id { get; set; }
    public Guid FinancialDocumentId { get; set; }
    public int StepNumber { get; set; } = 1;
    public string ApprovedBy { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;

    public FinancialDocument FinancialDocument { get; set; } = null!;
}

public class PostingBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public Guid FinancialDocumentId { get; set; }
    public string PostingKey { get; set; } = string.Empty;
    public DateTime PostedAt { get; set; } = DateTime.UtcNow;
    public string PostedBy { get; set; } = string.Empty;
    public Guid? ReversalOfPostingBatchId { get; set; }

    public FinancialDocument FinancialDocument { get; set; } = null!;
    public PostingBatch? ReversalOfPostingBatch { get; set; }
    public ICollection<JournalEntry> JournalEntries { get; set; } = [];
}

public class JournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostingBatchId { get; set; }
    public int LegalEntityId { get; set; }
    public int FiscalPeriodId { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public DateOnly PostingDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsFinalized { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public PostingBatch PostingBatch { get; set; } = null!;
    public FiscalPeriod FiscalPeriod { get; set; } = null!;
    public ICollection<JournalLine> Lines { get; set; } = [];
}

public class JournalLine
{
    public int Id { get; set; }
    public Guid JournalEntryId { get; set; }
    public int LineNumber { get; set; }
    public int AccountId { get; set; }
    public string? Description { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal BaseDebit { get; set; }
    public decimal BaseCredit { get; set; }

    public JournalEntry JournalEntry { get; set; } = null!;
    public AccountingAccount Account { get; set; } = null!;
    public ICollection<JournalLineDimension> Dimensions { get; set; } = [];
}

public class JournalLineDimension
{
    public int JournalLineId { get; set; }
    public int FinancialDimensionValueId { get; set; }
    public JournalLine JournalLine { get; set; } = null!;
    public FinancialDimensionValue FinancialDimensionValue { get; set; } = null!;
}

public class RecurringJournalSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public int? BranchId { get; set; }
    public string DocumentType { get; set; } = "RecurringJournal";
    public string Description { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "SAR";
    public int FrequencyMonths { get; set; } = 1;
    public DateOnly NextRunDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<RecurringJournalScheduleLine> Lines { get; set; } = [];
    public Organization.LegalEntity LegalEntity { get; set; } = null!;
    public Organization.Branch? Branch { get; set; }
}

public class RecurringJournalScheduleLine
{
    public int Id { get; set; }
    public Guid RecurringJournalScheduleId { get; set; }
    public int LineNumber { get; set; }
    public int AccountId { get; set; }
    public string? Description { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public RecurringJournalSchedule RecurringJournalSchedule { get; set; } = null!;
    public AccountingAccount Account { get; set; } = null!;
}

public class AccountingAuditEvent
{
    public long Id { get; set; }
    public int LegalEntityId { get; set; }
    public Guid? FinancialDocumentId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string PreviousHash { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public class AccountingOutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int LegalEntityId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? DeadLetteredAt { get; set; }
    public string? LockedBy { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

public class AccountingAuditChainHead
{
    public int LegalEntityId { get; set; }
    public string LastHash { get; set; } = string.Empty;
    public long LastEventId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Organization.LegalEntity LegalEntity { get; set; } = null!;
}
