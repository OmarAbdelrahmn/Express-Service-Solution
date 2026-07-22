using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Common;
using Application.Contracts.RiderPayroll;
using Application.Extensions;
using Application.Service.RiderPayroll;
using Domain.Entities.AccountingPlatform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/accounting")]
[ApiController]
[Authorize(Roles = "Master,Accountant")]
public class RiderPayrollController(IRiderPayrollService service) : ControllerBase
{
    [HttpGet("payroll-runs")]
    public Task<IActionResult> GetRuns(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] int legalEntityId,
        [FromQuery] RiderPayrollStatus? status,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken ct) => WithActor(actor => service.GetRunsAsync(pagination, legalEntityId, status, fromDate, toDate, search, sortBy, sortDirection, actor, ct));

    [HttpPost("payroll-runs")]
    public Task<IActionResult> CreateRun([FromBody] CreateRiderPayrollRunRequest request, CancellationToken ct) => WithActor(actor => service.CreateRunAsync(request, actor, ct));

    [HttpGet("payroll-runs/{id:guid}")]
    public Task<IActionResult> GetRun(Guid id, CancellationToken ct) => WithActor(actor => service.GetRunAsync(id, actor, ct));

    [HttpPost("payroll-runs/{id:guid}/calculate")]
    public Task<IActionResult> Calculate(Guid id, [FromBody] CalculateRiderPayrollRunRequest request, CancellationToken ct) => WithActor(actor => service.CalculateAsync(id, request, actor, ct));

    [HttpPost("payroll-runs/{id:guid}/adjustments")]
    public Task<IActionResult> AddAdjustment(Guid id, [FromBody] AddRiderPayrollAdjustmentRequest request, CancellationToken ct) => WithActor(actor => service.AddAdjustmentAsync(id, request, actor, ct));

    [HttpPost("payroll-runs/{id:guid}/approve")]
    public Task<IActionResult> Approve(Guid id, [FromBody] ApproveRiderPayrollRunRequest request, CancellationToken ct)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key)) return Task.FromResult<IActionResult>(Result.Failure(AccountingPlatformErrors.IdempotencyKeyRequired).ToProblem());
        return WithActor(actor => service.ApproveAsync(id, request with { IdempotencyKey = key.Trim() }, actor, ct));
    }

    [HttpPost("payroll-runs/{id:guid}/reverse")]
    public Task<IActionResult> ReverseRun(Guid id, [FromBody] ReverseRiderPayrollRunRequest request, CancellationToken ct)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key)) return Task.FromResult<IActionResult>(Result.Failure(AccountingPlatformErrors.IdempotencyKeyRequired).ToProblem());
        return WithActor(actor => service.ReverseRunAsync(id, request with { IdempotencyKey = key.Trim() }, actor, ct));
    }

    [HttpPost("rider-financial-item-types")]
    public Task<IActionResult> CreateItemType([FromBody] CreateRiderFinancialItemTypeRequest request, CancellationToken ct) => WithActor(actor => service.CreateItemTypeAsync(request, actor, ct));

    [HttpGet("rider-financial-item-types")]
    public Task<IActionResult> GetItemTypes(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] int legalEntityId,
        [FromQuery] RiderFinancialItemDirection? direction,
        [FromQuery] bool? active,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken ct) => WithActor(actor => service.GetItemTypesAsync(pagination, legalEntityId, direction, active, search, sortBy, sortDirection, actor, ct));

    [HttpPost("rider-financial-items")]
    public Task<IActionResult> CreateFinancialItem([FromBody] CreateRiderFinancialItemRequest request, CancellationToken ct) => WithActor(actor => service.CreateFinancialItemAsync(request, actor, ct));

    [HttpGet("rider-financial-items")]
    public Task<IActionResult> GetFinancialItems(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] int legalEntityId,
        [FromQuery] long? riderIqamaNo,
        [FromQuery] RiderFinancialItemStatus? status,
        [FromQuery] int? typeId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken ct) => WithActor(actor => service.GetFinancialItemsAsync(pagination, legalEntityId, riderIqamaNo, status, typeId, fromDate, toDate, search, sortBy, sortDirection, actor, ct));

    [HttpGet("rider-financial-items/{id:guid}")]
    public Task<IActionResult> GetFinancialItem(Guid id, CancellationToken ct) => WithActor(actor => service.GetFinancialItemAsync(id, actor, ct));

    [HttpPost("payroll-runs/{id:guid}/payment-batches")]
    public Task<IActionResult> PreparePaymentBatch(Guid id, [FromBody] PrepareRiderPaymentBatchRequest request, CancellationToken ct) => WithActor(actor => service.PreparePaymentBatchAsync(id, request, actor, ct));

    [HttpGet("payment-batches")]
    public Task<IActionResult> GetPaymentBatches(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] int legalEntityId,
        [FromQuery] Guid? runId,
        [FromQuery] RiderPaymentMethod? method,
        [FromQuery] RiderPaymentBatchStatus? status,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken ct) => WithActor(actor => service.GetPaymentBatchesAsync(pagination, legalEntityId, runId, method, status, fromDate, toDate, search, sortBy, sortDirection, actor, ct));

    [HttpGet("payment-batches/{id:guid}")]
    public Task<IActionResult> GetPaymentBatch(Guid id, CancellationToken ct) => WithActor(actor => service.GetPaymentBatchAsync(id, actor, ct));

    [HttpPost("payment-batches/{id:guid}/export")]
    public Task<IActionResult> ExportPaymentBatch(Guid id, [FromBody] ExportRiderPaymentBatchRequest request, CancellationToken ct) => WithActor(actor => service.ExportPaymentBatchAsync(id, request, actor, ct));

    [HttpPost("payment-batches/{id:guid}/confirm")]
    public Task<IActionResult> ConfirmPaymentBatch(Guid id, [FromBody] ConfirmRiderPaymentBatchRequest request, CancellationToken ct)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key)) return Task.FromResult<IActionResult>(Result.Failure(AccountingPlatformErrors.IdempotencyKeyRequired).ToProblem());
        return WithActor(actor => service.ConfirmPaymentBatchAsync(id, request with { IdempotencyKey = key.Trim() }, actor, ct));
    }

    [HttpPost("payment-batches/{id:guid}/lines/{lineId:long}/reject")]
    public Task<IActionResult> RejectPaymentLine(Guid id, long lineId, [FromBody] RejectRiderPaymentLineRequest request, CancellationToken ct) =>
        WithActor(actor => service.RejectPaymentLineAsync(id, lineId, request, actor, ct));

    [HttpPost("payment-batches/{id:guid}/reverse")]
    public Task<IActionResult> ReversePaymentBatch(Guid id, [FromBody] ReverseRiderPaymentBatchRequest request, CancellationToken ct)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key)) return Task.FromResult<IActionResult>(Result.Failure(AccountingPlatformErrors.IdempotencyKeyRequired).ToProblem());
        return WithActor(actor => service.ReversePaymentBatchAsync(id, request with { IdempotencyKey = key.Trim() }, actor, ct));
    }

    [HttpPost("cash-delivery-access")]
    public Task<IActionResult> GrantCashDeliveryAccess([FromBody] GrantHousingCashAccessRequest request, CancellationToken ct) => WithActor(actor => service.GrantHousingCashAccessAsync(request, actor, ct));

    [HttpGet("cash-delivery-access")]
    public Task<IActionResult> GetCashDeliveryAccess(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] int legalEntityId,
        [FromQuery] string? userId,
        [FromQuery] int? housingId,
        [FromQuery] bool? active,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken ct) => WithActor(actor => service.GetHousingCashAccessesAsync(pagination, legalEntityId, userId, housingId, active, fromDate, toDate, sortBy, sortDirection, actor, ct));

    [HttpDelete("cash-delivery-access/{id:int}")]
    public async Task<IActionResult> RevokeCashDeliveryAccess(int id, CancellationToken ct)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.RevokeHousingCashAccessAsync(id, actor, ct);
        return result.IsSuccess ? NoContent() : result.ToProblem();
    }

    [HttpGet("riders/{riderIqamaNo:long}/financial-profile")]
    public Task<IActionResult> FinancialProfile(long riderIqamaNo, [FromQuery] int legalEntityId, CancellationToken ct) => WithActor(actor => service.GetFinancialProfileAsync(riderIqamaNo, legalEntityId, actor, ct));

    private async Task<IActionResult> WithActor<T>(Func<string, Task<Application.Abstraction.Result<T>>> action)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await action(actor);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
