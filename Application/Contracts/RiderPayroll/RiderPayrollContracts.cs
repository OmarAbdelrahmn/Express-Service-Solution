using Domain.Entities.AccountingPlatform;
using FluentValidation;

namespace Application.Contracts.RiderPayroll;

public record CreateRiderPayrollRunRequest(int LegalEntityId, DateOnly PeriodStart, DateOnly PeriodEnd, string CurrencyCode = "SAR");
public record CalculateRiderPayrollRunRequest(string? RowVersion);
public record ApproveRiderPayrollRunRequest(string PostingProfileCode, string? IdempotencyKey, string CorrelationId, string? RowVersion);
public record ReverseRiderPayrollRunRequest(DateOnly ReversalDate, string Reason, string? IdempotencyKey, string CorrelationId, string? RowVersion);
public record AddRiderPayrollAdjustmentRequest(long RiderIqamaNo, decimal Amount, string Reason, string? Notes, Guid? EvidenceFileId);
public record CreateRiderFinancialItemTypeRequest(int LegalEntityId, string Code, string Name, RiderFinancialItemDirection Direction, int Priority, int LedgerAccountId);
public record CreateRiderFinancialItemRequest(int LegalEntityId, long RiderIqamaNo, int RiderFinancialItemTypeId, string Reference, string Description, DateOnly EffectiveDate, DateOnly? DeductionStartDate, decimal Amount, int? InstallmentCount, DateOnly? FirstInstallmentDate, Guid? EvidenceFileId);
public record RiderPaymentAllocationRequest(long RiderIqamaNo, decimal Amount, RiderPaymentMethod Method);
public record PrepareRiderPaymentBatchRequest(RiderPaymentMethod Method, IReadOnlyCollection<long>? RiderIqamaNumbers = null, IReadOnlyCollection<RiderPaymentAllocationRequest>? Allocations = null);
public record ExportRiderPaymentBatchRequest(string Format = "xlsx");
public record ConfirmRiderPaymentBatchRequest(DateOnly SettlementDate, string PostingProfileCode, string? IdempotencyKey, string CorrelationId, IReadOnlyCollection<long>? LineIds = null, string? Notes = null);
public record ReverseRiderPaymentBatchRequest(DateOnly ReversalDate, string Reason, string? IdempotencyKey, string CorrelationId);
public record RejectRiderPaymentLineRequest(string Reason);
public record GrantHousingCashAccessRequest(int LegalEntityId, string UserId, int HousingId);
public record ConfirmHousingCashDeliveryRequest(DateOnly SettlementDate, string PostingProfileCode, string? IdempotencyKey, string CorrelationId, IReadOnlyCollection<long> LineIds, string? Notes = null);

public record RiderPayrollComponentResponse(long Id, int? PlatformAccountId, Guid? PolicyVersionId, Guid? SourceImportBatchId, Guid? FinancialItemId, RiderPayrollComponentSource Source, CompensationComponentType ComponentType, string Code, string Description, decimal Quantity, decimal Rate, decimal Amount, bool IsAutomatic, string CalculationJson);
public record RiderPayrollLineResponse(long Id, long RiderIqamaNo, string RiderName, decimal GrossEarnings, decimal AppliedDeductions, decimal CarriedDeductions, decimal NetPay, bool IsHeld, string? HoldReason, IReadOnlyCollection<RiderPayrollComponentResponse> Components);
public record RiderPayrollRunResponse(Guid Id, int LegalEntityId, string RunNumber, DateOnly PeriodStart, DateOnly PeriodEnd, string CurrencyCode, RiderPayrollStatus Status, decimal GrossEarnings, decimal AppliedDeductions, decimal CarriedDeductions, decimal NetPay, Guid? AccrualFinancialDocumentId, string RowVersion, IReadOnlyCollection<RiderPayrollLineResponse> Lines);
public record RiderFinancialItemTypeResponse(int Id, int LegalEntityId, string Code, string Name, RiderFinancialItemDirection Direction, int Priority, int LedgerAccountId, bool IsActive);
public record RiderFinancialInstallmentResponse(long Id, int Sequence, DateOnly DueDate, decimal ScheduledAmount, decimal AppliedAmount, bool IsSettled);
public record RiderFinancialItemResponse(Guid Id, int LegalEntityId, long RiderIqamaNo, int TypeId, string TypeCode, string Reference, string Description, DateOnly EffectiveDate, DateOnly? DeductionStartDate, decimal OriginalAmount, decimal OutstandingAmount, RiderFinancialItemStatus Status, string RowVersion, IReadOnlyCollection<RiderFinancialInstallmentResponse> Installments);
public record RiderPaymentBatchLineResponse(long Id, long RiderPayrollLineId, long RiderIqamaNo, RiderPaymentMethod Method, decimal Amount, string? IbanSnapshot, int? HousingId, bool IsConfirmed, string? RejectionReason, DateTime? ConfirmedAt, string? ConfirmedBy, Guid? PaymentFinancialDocumentId);
public record RiderPaymentBatchResponse(Guid Id, int LegalEntityId, Guid RiderPayrollRunId, string BatchNumber, RiderPaymentMethod Method, RiderPaymentBatchStatus Status, Guid? ExportFileId, Guid? PaymentFinancialDocumentId, IReadOnlyCollection<RiderPaymentBatchLineResponse> Lines);
public record HousingCashAccessResponse(int Id, string UserId, int LegalEntityId, int HousingId, bool IsActive, string GrantedBy, DateTime GrantedAt, string RowVersion);
public record RiderPlatformFinancialSummary(int PlatformAccountId, string WorkerCategory, decimal AcceptedOrders, decimal CompanyBilling, decimal Vat, decimal RiderPolicyEarnings);
public record RiderFinancialProfileResponse(long RiderIqamaNo, string Name, string? Iban, int? HousingId, IReadOnlyCollection<RiderPlatformFinancialSummary> Platforms, IReadOnlyCollection<RiderFinancialItemResponse> FinancialItems, IReadOnlyCollection<RiderPayrollLineResponse> PayrollLines, decimal OutstandingDeductions, decimal UnpaidPayroll);

public class CreateRiderPayrollRunRequestValidator : AbstractValidator<CreateRiderPayrollRunRequest>
{
    public CreateRiderPayrollRunRequestValidator()
    {
        RuleFor(x => x.LegalEntityId).GreaterThan(0);
        RuleFor(x => x.PeriodEnd).GreaterThanOrEqualTo(x => x.PeriodStart);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
    }
}

public class ApproveRiderPayrollRunRequestValidator : AbstractValidator<ApproveRiderPayrollRunRequest>
{
    public ApproveRiderPayrollRunRequestValidator()
    {
        RuleFor(x => x.PostingProfileCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.CorrelationId).NotEmpty().MaximumLength(128);
    }
}

public class CreateRiderFinancialItemRequestValidator : AbstractValidator<CreateRiderFinancialItemRequest>
{
    public CreateRiderFinancialItemRequestValidator()
    {
        RuleFor(x => x.LegalEntityId).GreaterThan(0);
        RuleFor(x => x.RiderIqamaNo).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.InstallmentCount).GreaterThan(0).When(x => x.InstallmentCount.HasValue);
        RuleFor(x => x.FirstInstallmentDate).NotNull().When(x => x.InstallmentCount.HasValue);
    }
}

public class RejectRiderPaymentLineRequestValidator : AbstractValidator<RejectRiderPaymentLineRequest>
{
    public RejectRiderPaymentLineRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}
