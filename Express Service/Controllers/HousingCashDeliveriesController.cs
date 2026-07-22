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

[Route("api/accounting/cash-deliveries")]
[ApiController]
[Authorize(Roles = "Member")]
public class HousingCashDeliveriesController(IRiderPayrollService service) : ControllerBase
{
    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] int? legalEntityId,
        [FromQuery] RiderPaymentBatchStatus? status,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.GetHousingCashInboxAsync(pagination, legalEntityId, status, sortBy, sortDirection, actor, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("payment-batches/{batchId:guid}")]
    public async Task<IActionResult> GetPaymentBatch(Guid batchId, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.GetHousingCashPaymentBatchAsync(batchId, actor, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("payment-batches/{batchId:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid batchId, [FromBody] ConfirmHousingCashDeliveryRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key)) return Result.Failure(AccountingPlatformErrors.IdempotencyKeyRequired).ToProblem();
        var result = await service.ConfirmHousingCashDeliveryAsync(batchId, request with { IdempotencyKey = key.Trim() }, actor, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
