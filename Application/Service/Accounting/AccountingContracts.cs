using Application.Abstraction;
using Domain.Entities.Accounting;
using Microsoft.AspNetCore.Http;

namespace Application.Service.Accounting;

public interface IAccountingImportService
{
    Task<Result<CompanyBillImportResponse>> ImportCompanyBillAsync(
        ImportCompanyBillRequest request,
        string uploadedBy,
        CancellationToken cancellationToken = default);

    Task<Result<List<CompanyBillImportListItemResponse>>> GetCompanyImportsAsync(
        CompanyBillImportQuery request,
        CancellationToken cancellationToken = default);

    Task<Result<CompanyBillImportResponse>> GetCompanyImportAsync(
        int companyId,
        int importId,
        CancellationToken cancellationToken = default);

    Task<Result<CompanyBillImportInfoResponse>> GetCompanyImportInfoAsync(
        int companyId,
        CancellationToken cancellationToken = default);

    Task<Result<CompanyBillImportResponse>> ApproveCompanyBillImportAsync(
        int importId,
        string approvedBy,
        CancellationToken cancellationToken = default);

    Task<Result<CompanyBillImportResponse>> ApproveCompanyBillImportAsync(
        int companyId,
        int importId,
        string approvedBy,
        CancellationToken cancellationToken = default);

    Task<Result<CompanyBillImportResponse>> ReverseCompanyBillImportAsync(
        int importId,
        string reversedBy,
        CancellationToken cancellationToken = default);

    Task<Result<CompanyBillImportResponse>> ReverseCompanyBillImportAsync(
        int companyId,
        int importId,
        string reversedBy,
        CancellationToken cancellationToken = default);

    Task<Result<CompanyBillImportResponse>> GetImportAsync(
        int importId,
        CancellationToken cancellationToken = default);
}

public interface IAccountingSalaryService
{
    Task<Result<List<SalaryResponse>>> GenerateMonthlySalariesAsync(
        GenerateSalaryRequest request,
        string generatedBy,
        CancellationToken cancellationToken = default);

    Task<Result<SalaryResponse>> GetSalaryAsync(
        int salaryId,
        CancellationToken cancellationToken = default);

    Task<Result<SalaryResponse>> GetCompanySalaryAsync(
        int companyId,
        int salaryId,
        CancellationToken cancellationToken = default);

    Task<Result<SalaryResponse>> ApproveSalaryAsync(
        int salaryId,
        string approvedBy,
        CancellationToken cancellationToken = default);

    Task<Result<SalaryResponse>> ReverseSalaryAsync(
        int salaryId,
        string reversedBy,
        CancellationToken cancellationToken = default);

    Task<Result<BonusRuleResponse>> CreateBonusRuleAsync(
        BonusRuleRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<List<BonusRuleResponse>>> GetBonusRulesAsync(
        int? companyId = null,
        CancellationToken cancellationToken = default);

    Task<Result<FinancialItemTypeResponse>> CreateFinancialItemTypeAsync(
        FinancialItemTypeRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<RiderFinancialItemResponse>> CreateFinancialItemAsync(
        RiderFinancialItemRequest request,
        string createdBy,
        CancellationToken cancellationToken = default);

    Task<Result<List<RiderFinancialItemResponse>>> CreateBulkInternetReplacementAsync(
        BulkInternetReplacementRequest request,
        string createdBy,
        CancellationToken cancellationToken = default);

    Task<Result<List<RiderEarningResponse>>> CreateFixedMonthlyEarningsAsync(
        FixedMonthlyEarningRequest request,
        string createdBy,
        CancellationToken cancellationToken = default);

    Task<Result<RiderLoanResponse>> CreateLoanAsync(
        RiderLoanRequest request,
        string createdBy,
        CancellationToken cancellationToken = default);
}

public interface IAccountingPaymentService
{
    Task<Result<PaymentBatchResponse>> CreateBankPaymentBatchAsync(
        CreatePaymentBatchRequest request,
        string createdBy,
        CancellationToken cancellationToken = default);

    Task<Result<AccountingFileResponse>> ExportBankPaymentBatchAsync(
        int batchId,
        CancellationToken cancellationToken = default);

    Task<Result<PaymentBatchResponse>> MarkBankPaymentBatchSentAsync(
        int batchId,
        string sentBy,
        CancellationToken cancellationToken = default);

    Task<Result<PaymentBatchResponse>> ConfirmBankPaymentBatchAsync(
        int batchId,
        BankPaymentConfirmationRequest request,
        string confirmedBy,
        CancellationToken cancellationToken = default);

    Task<Result<PaymentLineResponse>> ReverseSalaryPaymentAsync(
        int paymentId,
        string reversedBy,
        CancellationToken cancellationToken = default);

    Task<Result<CashHandoverBatchResponse>> CreateCashHandoverBatchAsync(
        CreateCashHandoverBatchRequest request,
        string createdBy,
        CancellationToken cancellationToken = default);

    Task<Result<AccountingFileResponse>> ExportCashHandoverBatchAsync(
        int batchId,
        CancellationToken cancellationToken = default);

    Task<Result<List<CashHandoverBatchResponse>>> GetCashHandoverForHousingManagerAsync(
        long managerIqamaNo,
        int year,
        int month,
        CancellationToken cancellationToken = default);

    Task<Result<CashHandoverLineResponse>> SubmitCashHandoverLineAsync(
        int lineId,
        CashSalarySubmissionRequest request,
        long managerIqamaNo,
        string submittedBy,
        CancellationToken cancellationToken = default);

    Task<Result<CashHandoverBatchResponse>> SubmitCashHandoverBatchAsync(
        int batchId,
        CashSalarySubmissionRequest request,
        long managerIqamaNo,
        string submittedBy,
        CancellationToken cancellationToken = default);
}

public interface IRiderAccountingProfileService
{
    Task<Result<RiderAccountingProfileResponse>> GetRiderProfileAsync(
        int riderId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}

public interface ICompanyFinanceService
{
    Task<Result<CompanyFinanceSummaryResponse>> GetSummaryAsync(
        int year,
        int month,
        int? companyId,
        CancellationToken cancellationToken = default);

    Task<Result<List<CompanyIncomeResponse>>> GetIncomeAsync(
        DateOnly from,
        DateOnly to,
        int? companyId,
        CancellationToken cancellationToken = default);

    Task<Result<List<CompanyExpenseResponse>>> GetExpensesAsync(
        DateOnly from,
        DateOnly to,
        int? companyId,
        string? category,
        CancellationToken cancellationToken = default);

    Task<Result<CompanyExpenseResponse>> CreateExpenseAsync(
        CompanyExpenseRequest request,
        string createdBy,
        CancellationToken cancellationToken = default);

    Task<Result<CompanyPaymentReceiptResponse>> CreateReceiptAsync(
        CompanyPaymentReceiptRequest request,
        string receivedBy,
        CancellationToken cancellationToken = default);

    Task<Result<CompanyPaymentReceiptResponse>> ReverseReceiptAsync(
        int receiptId,
        string reversedBy,
        CancellationToken cancellationToken = default);

    Task<Result<ProfitLossResponse>> GetProfitLossAsync(
        DateOnly from,
        DateOnly to,
        int? companyId,
        CancellationToken cancellationToken = default);

    Task<Result<List<CostCenterFinanceResponse>>> GetCostCentersAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}

public interface IAccountingReportService
{
    Task<Result<TrialBalanceResponse>> GetTrialBalanceAsync(
        DateOnly from,
        DateOnly to,
        int? companyId = null,
        CancellationToken cancellationToken = default);

    Task<Result<GeneralLedgerResponse>> GetGeneralLedgerAsync(
        DateOnly from,
        DateOnly to,
        int? accountId,
        int? companyId = null,
        CancellationToken cancellationToken = default);
}

public record ImportCompanyBillRequest(
    IFormFile File,
    int Year,
    int Month,
    int CompanyId,
    CompanyBillTemplateType TemplateType,
    string? Notes);

public record CompanyBillImportResponse(
    int Id,
    int CompanyId,
    string CompanyName,
    CompanyBillTemplateType TemplateType,
    int Year,
    int Month,
    string SourceFileName,
    AccountingRecordStatus Status,
    decimal GrossAmount,
    decimal VatAmount,
    decimal NetAmount,
    decimal TotalDeductions,
    int SheetCount,
    int RawRowCount,
    int RawCellCount,
    int RiderSummaryCount,
    int TransactionLineCount,
    int DailyMetricCount,
    int IssueCount,
    IReadOnlyList<CompanyBillSheetResponse> Sheets,
    IReadOnlyList<CompanyBillResolutionIssueResponse> Issues);

public record CompanyBillImportQuery(
    int CompanyId,
    int? Year,
    int? Month,
    CompanyBillTemplateType? TemplateType,
    AccountingRecordStatus? Status);

public record CompanyBillImportListItemResponse(
    int Id,
    int CompanyId,
    string CompanyName,
    CompanyBillTemplateType TemplateType,
    int Year,
    int Month,
    string SourceFileName,
    AccountingRecordStatus Status,
    decimal GrossAmount,
    decimal VatAmount,
    decimal NetAmount,
    decimal TotalDeductions,
    int RiderSummaryCount,
    int TransactionLineCount,
    int IssueCount,
    DateTime UploadedAt,
    string UploadedBy);

public record CompanyBillImportInfoResponse(
    int CompanyId,
    string CompanyName,
    IReadOnlyList<CompanyBillTemplateInfoResponse> Templates);

public record CompanyBillTemplateInfoResponse(
    CompanyBillTemplateType TemplateType,
    string Code,
    string DisplayName,
    string UploadEndpoint,
    IReadOnlyList<string> RequiredColumns,
    IReadOnlyList<string> OptionalColumns,
    string Notes);

public record CompanyBillSheetResponse(
    int Id,
    string SheetName,
    CompanyBillSheetRole Role,
    int RowCount,
    int ColumnCount);

public record CompanyBillResolutionIssueResponse(
    int Id,
    string IssueType,
    string Message,
    int? SourceRowNumber,
    string? SourceRiderId,
    bool IsResolved);

public record GenerateSalaryRequest(
    int Year,
    int Month,
    int? CompanyId,
    bool ReplaceDraft = true);

public record SalaryResponse(
    int Id,
    int RiderId,
    string? WorkingId,
    string RiderName,
    int Year,
    int Month,
    RiderPaymentMethod PaymentMethod,
    SalaryStatus Status,
    decimal GrossEarnings,
    decimal TotalBonuses,
    decimal TotalAllowances,
    decimal TotalDeductions,
    decimal NetSalary,
    decimal PaidAmount,
    decimal RemainingAmount,
    string? IbanSnapshot,
    IReadOnlyList<SalaryLineResponse> Lines);

public record SalaryLineResponse(
    int Id,
    SalaryLineType Type,
    string Description,
    decimal Amount,
    string? SourceType,
    int? SourceId,
    bool IsEditable,
    string? Notes);

public record BonusRuleRequest(
    int? CompanyId,
    int MinimumAcceptedOrders,
    decimal BonusAmount,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    int Priority,
    string? Notes);

public record BonusRuleResponse(
    int Id,
    int? CompanyId,
    string? CompanyName,
    int MinimumAcceptedOrders,
    decimal BonusAmount,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    int Priority,
    bool IsActive,
    string? Notes);

public record FinancialItemTypeRequest(
    string Code,
    string Name,
    FinancialItemCategory Category);

public record FinancialItemTypeResponse(
    int Id,
    string Code,
    string Name,
    FinancialItemCategory Category,
    bool IsSystem,
    bool IsActive);

public record RiderFinancialItemRequest(
    int RiderFinancialItemTypeId,
    int RiderId,
    int? CompanyId,
    int? HousingId,
    string? VehicleNumber,
    int Year,
    int Month,
    DateOnly OccurredOn,
    decimal Amount,
    string? ReferenceNumber,
    string? Notes);

public record BulkInternetReplacementRequest(
    int Year,
    int Month,
    int CompanyId,
    decimal Amount,
    DateOnly OccurredOn,
    bool ReplaceExisting,
    string? ReferenceNumber,
    string? Notes);

public record FixedMonthlyEarningRequest(
    int Year,
    int Month,
    int CompanyId,
    decimal SalaryAmount,
    bool ReplaceExisting,
    string? Notes);

public record RiderFinancialItemResponse(
    int Id,
    int RiderId,
    string? WorkingId,
    string TypeCode,
    string TypeName,
    FinancialItemCategory Category,
    int Year,
    int Month,
    DateOnly OccurredOn,
    decimal Amount,
    decimal RemainingAmount,
    AccountingRecordStatus Status,
    string? ReferenceNumber,
    string? Notes);

public record RiderEarningResponse(
    int Id,
    int RiderId,
    string? WorkingId,
    int? CompanyId,
    int Year,
    int Month,
    int AcceptedOrders,
    decimal GrossAmount,
    decimal SalaryAmount,
    string SourceType,
    AccountingRecordStatus Status,
    string? Notes);

public record RiderLoanRequest(
    int RiderId,
    decimal PrincipalAmount,
    int FirstDeductionYear,
    int FirstDeductionMonth,
    int InstallmentCount,
    string? Notes);

public record RiderLoanResponse(
    int Id,
    int RiderId,
    decimal PrincipalAmount,
    decimal RemainingAmount,
    int FirstDeductionYear,
    int FirstDeductionMonth,
    int InstallmentCount,
    AccountingRecordStatus Status,
    IReadOnlyList<RiderLoanInstallmentResponse> Installments);

public record RiderLoanInstallmentResponse(
    int Id,
    int Year,
    int Month,
    decimal Amount,
    decimal PaidAmount,
    AccountingRecordStatus Status);

public record CreatePaymentBatchRequest(
    int Year,
    int Month,
    int? CompanyId,
    string? Notes);

public record BankPaymentConfirmationRequest(
    IReadOnlyList<BankPaymentConfirmationLine> ConfirmedPayments,
    IReadOnlyList<BankPaymentRejectionLine> RejectedPayments,
    string? Notes);

public record BankPaymentConfirmationLine(
    int PaymentId,
    string? ReferenceNumber,
    string? Notes);

public record BankPaymentRejectionLine(
    int PaymentId,
    string? Notes);

public record PaymentBatchResponse(
    int Id,
    int Year,
    int Month,
    RiderPaymentMethod PaymentMethod,
    PaymentBatchStatus Status,
    decimal TotalAmount,
    int PaymentCount,
    IReadOnlyList<PaymentLineResponse> Payments,
    string? Notes);

public record PaymentLineResponse(
    int Id,
    int RiderId,
    string? WorkingId,
    string RiderName,
    decimal Amount,
    string? IbanSnapshot,
    string? BankNameSnapshot,
    PaymentBatchStatus Status,
    string? ReferenceNumber,
    string? Notes);

public record CreateCashHandoverBatchRequest(
    int Year,
    int Month,
    int? HousingId,
    int? CompanyId,
    string? Notes);

public record CashHandoverBatchResponse(
    int Id,
    int Year,
    int Month,
    int? HousingId,
    string? HousingName,
    PaymentBatchStatus Status,
    decimal TotalAmount,
    IReadOnlyList<CashHandoverLineResponse> Lines,
    string? Notes);

public record CashHandoverLineResponse(
    int Id,
    int RiderId,
    string? WorkingId,
    string RiderName,
    decimal Amount,
    CashHandoverLineStatus Status,
    string? SubmittedBy,
    DateTime? SubmittedAt,
    string? MemberNotes);

public record CashSalarySubmissionRequest(
    CashHandoverLineStatus Status,
    string? ReferenceNumber,
    string? Notes);

public record AccountingFileResponse(
    string FileName,
    string ContentType,
    byte[] Content);

public record RiderAccountingProfileResponse(
    int RiderId,
    string? WorkingId,
    string RiderName,
    long EmployeeIqamaNo,
    string? CurrentCompany,
    string? Housing,
    string? VehicleNumber,
    DateOnly From,
    DateOnly To,
    decimal TotalCompanyEarnings,
    decimal TotalSalary,
    decimal TotalPaid,
    decimal CurrentBalance,
    IReadOnlyList<RiderAccountingPeriodSummary> Periods,
    IReadOnlyList<RiderStatementLineResponse> StatementLines);

public record RiderAccountingPeriodSummary(
    int Year,
    int Month,
    int AcceptedOrders,
    int RejectedOrders,
    decimal CompanyIncome,
    decimal GrossEarnings,
    decimal Bonuses,
    decimal Allowances,
    decimal Deductions,
    decimal NetSalary,
    decimal PaidAmount,
    decimal Balance);

public record RiderStatementLineResponse(
    DateOnly Date,
    int Year,
    int Month,
    string Type,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal Balance,
    string? SourceType,
    int? SourceId,
    string? Notes);

public record CompanyExpenseRequest(
    int CompanyExpenseCategoryId,
    int? CompanyId,
    int? CostCenterId,
    int? RiderId,
    int? HousingId,
    string? VehicleNumber,
    DateOnly ExpenseDate,
    decimal Amount,
    decimal VatAmount,
    string? ReferenceNumber,
    string? Description,
    bool AutoApprove);

public record CompanyExpenseResponse(
    int Id,
    int CompanyExpenseCategoryId,
    string Category,
    int? CompanyId,
    string? CompanyName,
    int? CostCenterId,
    int? RiderId,
    int? HousingId,
    string? VehicleNumber,
    DateOnly ExpenseDate,
    decimal Amount,
    decimal VatAmount,
    AccountingRecordStatus Status,
    string? ReferenceNumber,
    string? Description);

public record CompanyPaymentReceiptRequest(
    int? CompanyReceivableId,
    int? CompanyId,
    DateOnly ReceiptDate,
    decimal Amount,
    string? ReferenceNumber,
    string? BankAccount,
    string? Notes);

public record CompanyPaymentReceiptResponse(
    int Id,
    int? CompanyReceivableId,
    int? CompanyId,
    DateOnly ReceiptDate,
    decimal Amount,
    string? ReferenceNumber,
    string? BankAccount,
    string? Notes);

public record CompanyFinanceSummaryResponse(
    int Year,
    int Month,
    int? CompanyId,
    decimal GrossIncome,
    decimal VatAmount,
    decimal NetIncome,
    decimal CollectedAmount,
    decimal PendingReceivables,
    decimal RiderSalaries,
    decimal RiderBonuses,
    decimal CashPayouts,
    decimal BankPayouts,
    decimal DeductionsRecovered,
    decimal CompanyExpenses,
    decimal SupplierPayables,
    decimal Profit);

public record CompanyIncomeResponse(
    int Id,
    int? CompanyId,
    string? CompanyName,
    int Year,
    int Month,
    decimal GrossAmount,
    decimal VatAmount,
    decimal NetAmount,
    decimal CollectedAmount,
    decimal PendingAmount,
    AccountingRecordStatus Status,
    string? Notes);

public record ProfitLossResponse(
    DateOnly From,
    DateOnly To,
    int? CompanyId,
    decimal GrossIncome,
    decimal VatAmount,
    decimal NetIncome,
    decimal RiderSalaryExpense,
    decimal CompanyExpenses,
    decimal SupplierExpenses,
    decimal DeductionsRecovered,
    decimal Profit,
    IReadOnlyList<ProfitLossBreakdownLine> Breakdown);

public record ProfitLossBreakdownLine(
    string Dimension,
    string Name,
    decimal Income,
    decimal Expenses,
    decimal Profit);

public record CostCenterFinanceResponse(
    int? CostCenterId,
    string Code,
    string Name,
    CostCenterType Type,
    decimal Income,
    decimal Expenses,
    decimal RiderSalaries,
    decimal Profit);

public record TrialBalanceResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<TrialBalanceLineResponse> Lines,
    decimal TotalDebit,
    decimal TotalCredit);

public record TrialBalanceLineResponse(
    int AccountId,
    string AccountCode,
    string AccountName,
    AccountType AccountType,
    decimal Debit,
    decimal Credit,
    decimal Balance);

public record GeneralLedgerResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<GeneralLedgerLineResponse> Lines);

public record GeneralLedgerLineResponse(
    DateOnly EntryDate,
    string EntryNumber,
    int AccountId,
    string AccountCode,
    string AccountName,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance,
    string Description,
    string? SourceType,
    int? SourceId);
