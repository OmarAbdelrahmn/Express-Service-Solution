using Application.Contracts.Vacation;
using Application.Extensions;
using Application.Service.Vacation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[ApiController]
[Authorize(Roles = "Master,Admin")]
public class VacationAmendmentsController(IVacationService service) : ControllerBase
{
    [HttpGet("api/vacation-date-changes")]
    public async Task<IActionResult> GetDateChanges(CancellationToken cancellationToken)
    {
        var result = await service.GetDateChangesAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("api/vacation-date-changes/{id:guid}/decision")]
    [Authorize(Roles = "Master")]
    public async Task<IActionResult> ResolveDateChange(Guid id, [FromBody] ResolveVacationAmendmentRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.ResolveDateChangeAsync(actor, id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("api/vacation-cancellations")]
    public async Task<IActionResult> GetCancellations(CancellationToken cancellationToken)
    {
        var result = await service.GetCancellationsAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("api/vacation-cancellations/{id:guid}/decision")]
    [Authorize(Roles = "Master")]
    public async Task<IActionResult> ResolveCancellation(Guid id, [FromBody] ResolveVacationAmendmentRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.ResolveCancellationAsync(actor, id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
