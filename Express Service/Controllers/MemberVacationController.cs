using Application.Contracts.Vacation;
using Application.Extensions;
using Application.Service.Vacation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/member")]
[ApiController]
[Authorize(Roles = "Member")]
public class MemberVacationController(IVacationService service) : ControllerBase
{
    [HttpPost("vacation-requests")]
    public async Task<IActionResult> Create([FromBody] CreateVacationRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        var iqama = User.GetUserIqamaNo();
        if (string.IsNullOrWhiteSpace(actor) || iqama == 0) return Unauthorized();
        var result = await service.CreateForMemberAsync(actor, iqama, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("vacation-requests")]
    public async Task<IActionResult> GetRequests(CancellationToken cancellationToken)
    {
        var iqama = User.GetUserIqamaNo();
        if (iqama == 0) return Unauthorized();
        var result = await service.GetMemberRequestsAsync(iqama, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("vacation-riders")]
    public async Task<IActionResult> GetVacationRiders([FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate, CancellationToken cancellationToken)
    {
        var iqama = User.GetUserIqamaNo();
        if (iqama == 0) return Unauthorized();
        var result = await service.GetMemberVacationRidersAsync(iqama, fromDate, toDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("vacation-requests/{id:guid}/date-change")]
    public async Task<IActionResult> RequestDateChange(Guid id, [FromBody] CreateVacationDateChangeRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        var iqama = User.GetUserIqamaNo();
        if (string.IsNullOrWhiteSpace(actor) || iqama == 0) return Unauthorized();
        var result = await service.RequestDateChangeAsync(actor, iqama, id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("vacation-requests/{id:guid}/cancellation")]
    public async Task<IActionResult> RequestCancellation(Guid id, [FromBody] CreateVacationCancellationRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        var iqama = User.GetUserIqamaNo();
        if (string.IsNullOrWhiteSpace(actor) || iqama == 0) return Unauthorized();
        var result = await service.RequestCancellationAsync(actor, iqama, id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
