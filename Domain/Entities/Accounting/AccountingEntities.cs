using Domain.Entities;
using Domain.Entities.Spare;

namespace Domain.Entities.Accounting;

public enum AccountingPeriodStatus
{
    Open = 1,
    Closed = 2,
    Locked = 3
}

public enum AccountingRecordStatus
{
    Draft = 1,
    PendingReview = 2,
    Approved = 3,
    Posted = 4,
    Reversed = 5,
    Cancelled = 6
}

public enum AccountType
{
    Asset = 1,
    Liability = 2,
    Equity = 3,
    Revenue = 4,
    Expense = 5
}

public enum CostCenterType
{
    Company = 1,
    Rider = 2,
    Housing = 3,
    Vehicle = 4,
    Supplier = 5,
    General = 6
}

public enum CompanyBillTemplateType
{
    Generic = 1,
    Amazon = 2,
    KeetaPayPerOrder = 3,
    KeetaSegment = 4,
    FtrHunger = 5
}

public enum CompanyBillSheetRole
{
    Unknown = 1,
    PartnerSummary = 2,
    RiderSummary = 3,
    OrderDetail = 4,
    CostDetail = 5,
    DailyOrders = 6
}

public enum ImportResolutionStatus
{
    Pending = 1,
    Resolved = 2,
    NeedsAccountantReview = 3,
    Unresolved = 4,
    Ignored = 5
}

public enum RiderPaymentMethod
{
    BankTransfer = 1,
    Cash = 2,
    Hold = 3,
    Mixed = 4
}

public enum SalaryStatus
{
    Draft = 1,
    Reviewed = 2,
    Approved = 3,
    PartiallyPaid = 4,
    Paid = 5,
    Locked = 6,
    Cancelled = 7
}

public enum SalaryLineType
{
    Earning = 1,
    Bonus = 2,
    Allowance = 3,
    Deduction = 4,
    Reimbursement = 5,
    InformationOnly = 6
}

public enum FinancialItemCategory
{
    Earning = 1,
    Deduction = 2,
    Allowance = 3,
    Reimbursement = 4,
    CompanyCost = 5,
    InformationOnly = 6
}

public enum PaymentBatchStatus
{
    Draft = 1,
    Prepared = 2,
    Sent = 3,
    PartiallyConfirmed = 4,
    Confirmed = 5,
    Failed = 6,
    Cancelled = 7
}

public enum CashHandoverLineStatus
{
    Pending = 1,
    Delivered = 2,
    Rejected = 3,
    Absent = 4,
    NeedsReview = 5
}

public enum BankTransactionType
{
    Deposit = 1,
    Withdrawal = 2,
    Transfer = 3,
    Fee = 4,
    Adjustment = 5
}

public enum PurchaseInvoiceStatus
{
    Draft = 1,
    Approved = 2,
    PartiallyPaid = 3,
    Paid = 4,
    Cancelled = 5
}

public enum CheckCycleStatus
{
    Draft = 1,
    Issued = 2,
    Collected = 3,
    Paid = 4,
    Bounced = 5,
    Cancelled = 6
}

public class AccountingPeriod
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public AccountingPeriodStatus Status { get; set; } = AccountingPeriodStatus.Open;
    public string? ClosedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
}

public class CostCenter
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CostCenterType Type { get; set; }
    public int? CompanyId { get; set; }
    public int? RiderId { get; set; }
    public long? EmployeeIqamaNo { get; set; }
    public int? HousingId { get; set; }
    public string? VehicleNumber { get; set; }
    public int? SupplierId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);

    public Company? Company { get; set; }
    public RiderDetails? Rider { get; set; }
    public Employees? Employee { get; set; }
    public Housing? Housing { get; set; }
    public Vehicle? Vehicle { get; set; }
    public Supplier? Supplier { get; set; }
}

public class AccountingAccount
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public int? ParentAccountId { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;

    public AccountingAccount? ParentAccount { get; set; }
    public ICollection<AccountingAccount> Children { get; set; } = [];
}

public class JournalEntry
{
    public int Id { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public DateOnly EntryDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public AccountingRecordStatus Status { get; set; } = AccountingRecordStatus.Draft;
    public string? SourceType { get; set; }
    public int? SourceId { get; set; }
    public int? ReversedEntryId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string? PostedBy { get; set; }
    public DateTime? PostedAt { get; set; }
    public string? Notes { get; set; }

    public ICollection<JournalEntryLine> Lines { get; set; } = [];
}

public class JournalEntryLine
{
    public int Id { get; set; }
    public int JournalEntryId { get; set; }
    public int AccountId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public int? CostCenterId { get; set; }
    public int? CompanyId { get; set; }
    public int? RiderId { get; set; }
    public long? EmployeeIqamaNo { get; set; }
    public int? HousingId { get; set; }
    public string? VehicleNumber { get; set; }
    public int? SupplierId { get; set; }
    public int? BankAccountId { get; set; }
    public string? Notes { get; set; }

    public JournalEntry JournalEntry { get; set; } = default!;
    public AccountingAccount Account { get; set; } = default!;
    public CostCenter? CostCenter { get; set; }
    public BankAccount? BankAccount { get; set; }
}

public class AccountingAuditLog
{
    public int Id { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
    public DateTime PerformedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string? Notes { get; set; }
}

public class AccountingNote
{
    public int Id { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Note { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
}

public class AccountingAttachment
{
    public int Id { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow.AddHours(3);
}

public class CompanyBillImport
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public string CompanyNameSnapshot { get; set; } = string.Empty;
    public CompanyBillTemplateType TemplateType { get; set; } = CompanyBillTemplateType.Generic;
    public int Year { get; set; }
    public int Month { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public AccountingRecordStatus Status { get; set; } = AccountingRecordStatus.Draft;
    public decimal GrossAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TotalDeductions { get; set; }
    public string? Notes { get; set; }

    public Company? Company { get; set; }
    public ICollection<CompanyBillSheet> Sheets { get; set; } = [];
    public ICollection<CompanyBillRiderSummary> RiderSummaries { get; set; } = [];
    public ICollection<CompanyBillTransactionLine> TransactionLines { get; set; } = [];
    public ICollection<CompanyBillDailyMetric> DailyMetrics { get; set; } = [];
}

public class CompanyBillSheet
{
    public int Id { get; set; }
    public int CompanyBillImportId { get; set; }
    public string SheetName { get; set; } = string.Empty;
    public CompanyBillSheetRole Role { get; set; } = CompanyBillSheetRole.Unknown;
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }

    public CompanyBillImport CompanyBillImport { get; set; } = default!;
    public ICollection<CompanyBillRawRow> RawRows { get; set; } = [];
}

public class CompanyBillRawRow
{
    public int Id { get; set; }
    public int CompanyBillSheetId { get; set; }
    public int RowNumber { get; set; }
    public bool IsHeader { get; set; }

    public CompanyBillSheet Sheet { get; set; } = default!;
    public ICollection<CompanyBillRawCell> Cells { get; set; } = [];
}

public class CompanyBillRawCell
{
    public int Id { get; set; }
    public int CompanyBillRawRowId { get; set; }
    public int ColumnNumber { get; set; }
    public string? Header { get; set; }
    public string? OriginalValue { get; set; }
    public string? NormalizedField { get; set; }

    public CompanyBillRawRow Row { get; set; } = default!;
}

public class CompanyBillRiderSummary
{
    public int Id { get; set; }
    public int CompanyBillImportId { get; set; }
    public int? CompanyBillSheetId { get; set; }
    public int SourceRowNumber { get; set; }
    public string SourceRiderId { get; set; } = string.Empty;
    public string? SourceRiderName { get; set; }
    public int? OriginalRiderId { get; set; }
    public int? PaidRiderId { get; set; }
    public ImportResolutionStatus ResolutionStatus { get; set; } = ImportResolutionStatus.Pending;
    public string? ResolutionNotes { get; set; }
    public int AcceptedOrders { get; set; }
    public int RejectedOrders { get; set; }
    public decimal DistanceAmount { get; set; }
    public decimal BasicPayment { get; set; }
    public decimal BonusAmount { get; set; }
    public decimal PenaltyAmount { get; set; }
    public decimal RiderBalance { get; set; }
    public decimal VatAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal WorkingHours { get; set; }
    public int WorkingDays { get; set; }
    public string? ValidityStatus { get; set; }
    public string? ValidityReason { get; set; }
    public string? RawJson { get; set; }

    public CompanyBillImport CompanyBillImport { get; set; } = default!;
    public CompanyBillSheet? Sheet { get; set; }
    public RiderDetails? OriginalRider { get; set; }
    public RiderDetails? PaidRider { get; set; }
}

public class CompanyBillTransactionLine
{
    public int Id { get; set; }
    public int CompanyBillImportId { get; set; }
    public int? CompanyBillSheetId { get; set; }
    public int SourceRowNumber { get; set; }
    public DateOnly? ServiceDate { get; set; }
    public string SourceRiderId { get; set; } = string.Empty;
    public string? SourceRiderName { get; set; }
    public int? OriginalRiderId { get; set; }
    public int? PaidRiderId { get; set; }
    public ImportResolutionStatus ResolutionStatus { get; set; } = ImportResolutionStatus.Pending;
    public string? TransactionType { get; set; }
    public string? WorkId { get; set; }
    public string? FeeType { get; set; }
    public string? AmountDetail { get; set; }
    public decimal Amount { get; set; }
    public decimal DistanceKm { get; set; }
    public string? TicketId { get; set; }
    public string? ViolationId { get; set; }
    public string? ViolationType { get; set; }
    public string? PunishmentMethod { get; set; }
    public DateTime? FaceVerificationTime { get; set; }
    public string? FaceVerificationResult { get; set; }
    public string? Notes { get; set; }
    public string? RawJson { get; set; }

    public CompanyBillImport CompanyBillImport { get; set; } = default!;
    public CompanyBillSheet? Sheet { get; set; }
    public RiderDetails? OriginalRider { get; set; }
    public RiderDetails? PaidRider { get; set; }
}

public class CompanyBillDailyMetric
{
    public int Id { get; set; }
    public int CompanyBillImportId { get; set; }
    public string SourceRiderId { get; set; } = string.Empty;
    public int? RiderId { get; set; }
    public DateOnly MetricDate { get; set; }
    public int AcceptedOrders { get; set; }
    public int RejectedOrders { get; set; }
    public decimal Amount { get; set; }
    public string? RawValue { get; set; }

    public CompanyBillImport CompanyBillImport { get; set; } = default!;
    public RiderDetails? Rider { get; set; }
}

public class CompanyBillResolutionIssue
{
    public int Id { get; set; }
    public int CompanyBillImportId { get; set; }
    public string IssueType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? SourceRowNumber { get; set; }
    public string? SourceRiderId { get; set; }
    public bool IsResolved { get; set; }

    public CompanyBillImport CompanyBillImport { get; set; } = default!;
}

public class RiderEarning
{
    public int Id { get; set; }
    public int? CompanyBillImportId { get; set; }
    public int? CompanyBillRiderSummaryId { get; set; }
    public int? CompanyBillTransactionLineId { get; set; }
    public int? CompanyId { get; set; }
    public int? OriginalRiderId { get; set; }
    public int PaidRiderId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly? ServiceDate { get; set; }
    public int AcceptedOrders { get; set; }
    public int RejectedOrders { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DistanceAmount { get; set; }
    public decimal SalaryAmount { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public AccountingRecordStatus Status { get; set; } = AccountingRecordStatus.Draft;
    public string? Notes { get; set; }

    public CompanyBillImport? CompanyBillImport { get; set; }
    public Company? Company { get; set; }
    public RiderDetails? OriginalRider { get; set; }
    public RiderDetails PaidRider { get; set; } = default!;
}

public class RiderBonusRule
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int MinimumAcceptedOrders { get; set; }
    public decimal BonusAmount { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public Company? Company { get; set; }
}

public class RiderSalaryRule
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public CompanyBillTemplateType? TemplateType { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MinimumAcceptedOrders { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal ExtraOrderAmount { get; set; }
    public decimal BelowThresholdOrderAmount { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public Company? Company { get; set; }
}

public class RiderBonusAward
{
    public int Id { get; set; }
    public int RiderBonusRuleId { get; set; }
    public int RiderId { get; set; }
    public int? CompanyId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int AcceptedOrders { get; set; }
    public decimal Amount { get; set; }
    public bool IsManualOverride { get; set; }
    public string? Notes { get; set; }

    public RiderBonusRule Rule { get; set; } = default!;
    public RiderDetails Rider { get; set; } = default!;
    public Company? Company { get; set; }
}

public class RiderFinancialItemType
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public FinancialItemCategory Category { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
}

public class RiderFinancialItem
{
    public int Id { get; set; }
    public int RiderFinancialItemTypeId { get; set; }
    public int RiderId { get; set; }
    public long? EmployeeIqamaNo { get; set; }
    public int? CompanyId { get; set; }
    public int? HousingId { get; set; }
    public string? VehicleNumber { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly OccurredOn { get; set; }
    public decimal Amount { get; set; }
    public decimal RemainingAmount { get; set; }
    public AccountingRecordStatus Status { get; set; } = AccountingRecordStatus.Draft;
    public bool IsWaived { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);

    public RiderFinancialItemType Type { get; set; } = default!;
    public RiderDetails Rider { get; set; } = default!;
    public Company? Company { get; set; }
    public Housing? Housing { get; set; }
    public Vehicle? Vehicle { get; set; }
}

public class RiderLoan
{
    public int Id { get; set; }
    public int RiderId { get; set; }
    public decimal PrincipalAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public int FirstDeductionYear { get; set; }
    public int FirstDeductionMonth { get; set; }
    public int InstallmentCount { get; set; }
    public AccountingRecordStatus Status { get; set; } = AccountingRecordStatus.Draft;
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);

    public RiderDetails Rider { get; set; } = default!;
    public ICollection<RiderLoanInstallment> Installments { get; set; } = [];
}

public class RiderLoanInstallment
{
    public int Id { get; set; }
    public int RiderLoanId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public AccountingRecordStatus Status { get; set; } = AccountingRecordStatus.Draft;

    public RiderLoan RiderLoan { get; set; } = default!;
}

public class RiderFinalSettlement
{
    public int Id { get; set; }
    public int RiderId { get; set; }
    public DateOnly SettlementDate { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal FinalSalaryAmount { get; set; }
    public decimal ReimbursementAmount { get; set; }
    public decimal ManualDeductionAmount { get; set; }
    public decimal OutstandingLoanBalance { get; set; }
    public decimal LoanWriteOffAmount { get; set; }
    public decimal LoanFinalDeductionAmount { get; set; }
    public decimal NetSettlementAmount { get; set; }
    public AccountingRecordStatus Status { get; set; } = AccountingRecordStatus.Approved;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string? Notes { get; set; }

    public RiderDetails Rider { get; set; } = default!;
}

public class RiderMonthlySalary
{
    public int Id { get; set; }
    public int RiderId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public RiderPaymentMethod PaymentMethod { get; set; } = RiderPaymentMethod.BankTransfer;
    public SalaryStatus Status { get; set; } = SalaryStatus.Draft;
    public decimal GrossEarnings { get; set; }
    public decimal TotalBonuses { get; set; }
    public decimal TotalAllowances { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetSalary { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string? IbanSnapshot { get; set; }
    public string? Notes { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string GeneratedBy { get; set; } = string.Empty;
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }

    public RiderDetails Rider { get; set; } = default!;
    public ICollection<RiderMonthlySalaryLine> Lines { get; set; } = [];
}

public class RiderMonthlySalaryLine
{
    public int Id { get; set; }
    public int RiderMonthlySalaryId { get; set; }
    public SalaryLineType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? SourceType { get; set; }
    public int? SourceId { get; set; }
    public bool IsEditable { get; set; } = true;
    public string? Notes { get; set; }

    public RiderMonthlySalary RiderMonthlySalary { get; set; } = default!;
}

public class RiderSalaryPaymentBatch
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public RiderPaymentMethod PaymentMethod { get; set; }
    public PaymentBatchStatus Status { get; set; } = PaymentBatchStatus.Draft;
    public decimal TotalAmount { get; set; }
    public int PaymentCount { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public DateTime? SentAt { get; set; }
    public string? SentBy { get; set; }
    public string? Notes { get; set; }

    public ICollection<RiderSalaryPayment> Payments { get; set; } = [];
}

public class RiderSalaryPayment
{
    public int Id { get; set; }
    public int RiderSalaryPaymentBatchId { get; set; }
    public int RiderMonthlySalaryId { get; set; }
    public int RiderId { get; set; }
    public decimal Amount { get; set; }
    public string? IbanSnapshot { get; set; }
    public string? BankNameSnapshot { get; set; }
    public PaymentBatchStatus Status { get; set; } = PaymentBatchStatus.Prepared;
    public string? ReferenceNumber { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public string? Notes { get; set; }

    public RiderSalaryPaymentBatch Batch { get; set; } = default!;
    public RiderMonthlySalary Salary { get; set; } = default!;
    public RiderDetails Rider { get; set; } = default!;
}

public class CashSalaryHandoverBatch
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int? HousingId { get; set; }
    public PaymentBatchStatus Status { get; set; } = PaymentBatchStatus.Draft;
    public decimal TotalAmount { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? Notes { get; set; }

    public Housing? Housing { get; set; }
    public ICollection<CashSalaryHandoverLine> Lines { get; set; } = [];
}

public class CashSalaryHandoverLine
{
    public int Id { get; set; }
    public int CashSalaryHandoverBatchId { get; set; }
    public int RiderMonthlySalaryId { get; set; }
    public int RiderId { get; set; }
    public decimal Amount { get; set; }
    public CashHandoverLineStatus Status { get; set; } = CashHandoverLineStatus.Pending;
    public string? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? MemberNotes { get; set; }

    public CashSalaryHandoverBatch Batch { get; set; } = default!;
    public RiderMonthlySalary Salary { get; set; } = default!;
    public RiderDetails Rider { get; set; } = default!;
}

public class CompanyReceivable
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int? CompanyBillImportId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal CollectedAmount { get; set; }
    public decimal PendingAmount { get; set; }
    public AccountingRecordStatus Status { get; set; } = AccountingRecordStatus.Draft;
    public string? Notes { get; set; }

    public Company? Company { get; set; }
    public CompanyBillImport? CompanyBillImport { get; set; }
}

public class CompanyPaymentReceipt
{
    public int Id { get; set; }
    public int? CompanyReceivableId { get; set; }
    public int? CompanyId { get; set; }
    public int? BankAccountId { get; set; }
    public DateOnly ReceiptDate { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? BankAccount { get; set; }
    public string ReceivedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string? Notes { get; set; }

    public CompanyReceivable? CompanyReceivable { get; set; }
    public Company? Company { get; set; }
    public BankAccount? LinkedBankAccount { get; set; }
}

public class CompanyExpenseCategory
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CompanyExpense
{
    public int Id { get; set; }
    public int CompanyExpenseCategoryId { get; set; }
    public int? CompanyId { get; set; }
    public int? CostCenterId { get; set; }
    public int? RiderId { get; set; }
    public int? HousingId { get; set; }
    public string? VehicleNumber { get; set; }
    public int? BankAccountId { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Description { get; set; }
    public AccountingRecordStatus Status { get; set; } = AccountingRecordStatus.Draft;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);

    public CompanyExpenseCategory Category { get; set; } = default!;
    public Company? Company { get; set; }
    public CostCenter? CostCenter { get; set; }
    public RiderDetails? Rider { get; set; }
    public Housing? Housing { get; set; }
    public Vehicle? Vehicle { get; set; }
    public BankAccount? BankAccount { get; set; }
}

public class SupplierPayable
{
    public int Id { get; set; }
    public int? SupplierId { get; set; }
    public int? PurchaseInvoiceId { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public AccountingRecordStatus Status { get; set; } = AccountingRecordStatus.Draft;
    public string? Notes { get; set; }

    public Supplier? Supplier { get; set; }
}

public class SupplierPayment
{
    public int Id { get; set; }
    public int? SupplierPayableId { get; set; }
    public int? SupplierId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string PaidBy { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public SupplierPayable? SupplierPayable { get; set; }
    public Supplier? Supplier { get; set; }
}

public class CompanyProfitSnapshot
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal GrossIncome { get; set; }
    public decimal VatAmount { get; set; }
    public decimal NetIncome { get; set; }
    public decimal RiderSalaryExpense { get; set; }
    public decimal CompanyExpenses { get; set; }
    public decimal DeductionsRecovered { get; set; }
    public decimal Profit { get; set; }
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow.AddHours(3);

    public Company? Company { get; set; }
}

public class FixedAsset
{
    public int Id { get; set; }
    public string AssetCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public int? CompanyId { get; set; }
    public string? VehicleNumber { get; set; }
    public DateOnly PurchaseDate { get; set; }
    public decimal PurchaseCost { get; set; }
    public decimal SalvageValue { get; set; }
    public int UsefulLifeMonths { get; set; }
    public bool IsActive { get; set; } = true;

    public Company? Company { get; set; }
    public Vehicle? Vehicle { get; set; }
}

public class AssetDepreciationEntry
{
    public int Id { get; set; }
    public int FixedAssetId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public AccountingRecordStatus Status { get; set; } = AccountingRecordStatus.Draft;

    public FixedAsset FixedAsset { get; set; } = default!;
}

public class BankAccount
{
    public int Id { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string? Iban { get; set; }
    public string? BankName { get; set; }
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TreasuryAccount
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; } = true;
}

public class BankTransaction
{
    public int Id { get; set; }
    public int? BankAccountId { get; set; }
    public BankTransactionType Type { get; set; }
    public DateOnly TransactionDate { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Description { get; set; }
    public bool IsReconciled { get; set; }

    public BankAccount? BankAccount { get; set; }
}

public class BankReconciliation
{
    public int Id { get; set; }
    public int BankAccountId { get; set; }
    public DateOnly StatementDate { get; set; }
    public decimal StatementBalance { get; set; }
    public decimal SystemBalance { get; set; }
    public decimal Difference { get; set; }
    public AccountingRecordStatus Status { get; set; } = AccountingRecordStatus.Draft;

    public BankAccount BankAccount { get; set; } = default!;
}

public class CheckCycle
{
    public int Id { get; set; }
    public string CheckNumber { get; set; } = string.Empty;
    public DateOnly CheckDate { get; set; }
    public decimal Amount { get; set; }
    public string PayeeName { get; set; } = string.Empty;
    public CheckCycleStatus Status { get; set; } = CheckCycleStatus.Draft;
    public string? Notes { get; set; }
}

public class PurchaseInvoice
{
    public int Id { get; set; }
    public int? SupplierId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public PurchaseInvoiceStatus Status { get; set; } = PurchaseInvoiceStatus.Draft;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string? Notes { get; set; }

    public Supplier? Supplier { get; set; }
}
