using Application.Abstraction;
using Application.Contracts.RiderPayroll;
using Application.Contracts.AccountingFiles;
using Application.Contracts.Common;
using Domain.Entities.AccountingPlatform;

namespace Application.Service.RiderPayroll;

public interface IRiderPayrollService
{
    Task<Result<PagedResponse<RiderPayrollRunResponse>>> GetRunsAsync(PaginationRequest pagination, int legalEntityId, RiderPayrollStatus? status, DateOnly? fromDate, DateOnly? toDate, string? search, string? sortBy, string? sortDirection, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderPayrollRunResponse>> CreateRunAsync(CreateRiderPayrollRunRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderPayrollRunResponse>> CalculateAsync(Guid runId, CalculateRiderPayrollRunRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderPayrollRunResponse>> GetRunAsync(Guid runId, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderPayrollRunResponse>> AddAdjustmentAsync(Guid runId, AddRiderPayrollAdjustmentRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderPayrollRunResponse>> ApproveAsync(Guid runId, ApproveRiderPayrollRunRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderPayrollRunResponse>> ReverseRunAsync(Guid runId, ReverseRiderPayrollRunRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<RiderFinancialItemTypeResponse>>> GetItemTypesAsync(PaginationRequest pagination, int legalEntityId, RiderFinancialItemDirection? direction, bool? active, string? search, string? sortBy, string? sortDirection, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderFinancialItemTypeResponse>> CreateItemTypeAsync(CreateRiderFinancialItemTypeRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<RiderFinancialItemResponse>>> GetFinancialItemsAsync(PaginationRequest pagination, int legalEntityId, long? riderIqamaNo, RiderFinancialItemStatus? status, int? typeId, DateOnly? fromDate, DateOnly? toDate, string? search, string? sortBy, string? sortDirection, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderFinancialItemResponse>> GetFinancialItemAsync(Guid id, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderFinancialItemResponse>> CreateFinancialItemAsync(CreateRiderFinancialItemRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<RiderPaymentBatchResponse>>> GetPaymentBatchesAsync(PaginationRequest pagination, int legalEntityId, Guid? runId, RiderPaymentMethod? method, RiderPaymentBatchStatus? status, DateOnly? fromDate, DateOnly? toDate, string? search, string? sortBy, string? sortDirection, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderPaymentBatchResponse>> GetPaymentBatchAsync(Guid id, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderPaymentBatchResponse>> PreparePaymentBatchAsync(Guid runId, PrepareRiderPaymentBatchRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<AccountingFileResponse>> ExportPaymentBatchAsync(Guid batchId, ExportRiderPaymentBatchRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderPaymentBatchResponse>> ConfirmPaymentBatchAsync(Guid batchId, ConfirmRiderPaymentBatchRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderPaymentBatchResponse>> RejectPaymentLineAsync(Guid batchId, long lineId, RejectRiderPaymentLineRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderPaymentBatchResponse>> ReversePaymentBatchAsync(Guid batchId, ReverseRiderPaymentBatchRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<HousingCashAccessResponse>>> GetHousingCashAccessesAsync(PaginationRequest pagination, int legalEntityId, string? userId, int? housingId, bool? active, DateOnly? fromDate, DateOnly? toDate, string? sortBy, string? sortDirection, string actorId, CancellationToken cancellationToken = default);
    Task<Result<HousingCashAccessResponse>> GrantHousingCashAccessAsync(GrantHousingCashAccessRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result> RevokeHousingCashAccessAsync(int id, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<RiderPaymentBatchResponse>>> GetHousingCashInboxAsync(PaginationRequest pagination, int? legalEntityId, RiderPaymentBatchStatus? status, string? sortBy, string? sortDirection, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderPaymentBatchResponse>> GetHousingCashPaymentBatchAsync(Guid batchId, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderPaymentBatchResponse>> ConfirmHousingCashDeliveryAsync(Guid batchId, ConfirmHousingCashDeliveryRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<RiderFinancialProfileResponse>> GetFinancialProfileAsync(long riderIqamaNo, int legalEntityId, string actorId, CancellationToken cancellationToken = default);
}
