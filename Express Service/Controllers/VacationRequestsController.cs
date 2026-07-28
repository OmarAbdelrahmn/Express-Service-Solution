using Application.Contracts.Vacation;
using Application.Extensions;
using Application.Service.Vacation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/vacation-requests")]
[ApiController]
[Authorize]
public class VacationRequestsController(IVacationService service) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetAll([FromQuery] VacationRequestQuery query, CancellationToken cancellationToken)
    {
        var result = await service.GetAllAsync(query, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("inbox")]
    public async Task<IActionResult> GetInbox(CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.GetInboxAsync(actor, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.GetDetailAsync(actor, User.IsInRole("Master") || User.IsInRole("Admin"), id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("{id:guid}/decisions")]
    public async Task<IActionResult> Decide(Guid id, [FromBody] VacationDecisionRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.DecideAsync(actor, id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Master")]
    public async Task<IActionResult> DirectCancel(Guid id, [FromBody] DirectVacationCancellationRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.DirectCancelAsync(actor, id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("{id:guid}/documents/{documentId:guid}/stream")]
    public async Task<IActionResult> StreamDocument(Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.OpenHrDocumentAsync(
            actor,
            User.GetUserIqamaNo(),
            User.IsInRole("Master") || User.IsInRole("Admin"),
            id,
            documentId,
            cancellationToken);
        if (result.IsFailure) return result.ToProblem();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value.Content, result.Value.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/documents/{documentId:guid}/download")]
    public async Task<IActionResult> DownloadDocument(Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.OpenHrDocumentAsync(
            actor,
            User.GetUserIqamaNo(),
            User.IsInRole("Master") || User.IsInRole("Admin"),
            id,
            documentId,
            cancellationToken);
        if (result.IsFailure) return result.ToProblem();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName, enableRangeProcessing: true);
    }
}
