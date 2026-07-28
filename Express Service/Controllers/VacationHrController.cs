using Application.Contracts.Vacation;
using Application.Extensions;
using Application.Service.Vacation;
using Domain.Entities.Vacation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/vacation-hr")]
[ApiController]
[Authorize]
public class VacationHrController(IVacationService service) : ControllerBase
{
    [HttpGet("inbox")]
    public async Task<IActionResult> GetInbox(CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.GetHrInboxAsync(actor, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("{id:guid}/ticket")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(VacationDocumentStorage.MaximumFileSize + 1024 * 1024)]
    public Task<IActionResult> UploadTicket(
        Guid id,
        [FromForm] IFormFile file,
        [FromForm] bool completed,
        CancellationToken cancellationToken) =>
        Upload(id, VacationHrDocumentType.Ticket, file, completed, cancellationToken);

    [HttpPost("{id:guid}/exit-reentry-visa")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(VacationDocumentStorage.MaximumFileSize + 1024 * 1024)]
    public Task<IActionResult> UploadExitReentryVisa(
        Guid id,
        [FromForm] IFormFile file,
        [FromForm] bool completed,
        CancellationToken cancellationToken) =>
        Upload(id, VacationHrDocumentType.ExitReentryVisa, file, completed, cancellationToken);

    private async Task<IActionResult> Upload(
        Guid id,
        VacationHrDocumentType type,
        IFormFile file,
        bool completed,
        CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        if (file is null || file.Length == 0)
            return BadRequest(new { code = "Vacation.InvalidDocument", message = "A non-empty HR document is required." });

        await using var stream = file.OpenReadStream();
        var result = await service.UploadHrDocumentAsync(
            actor,
            id,
            type,
            completed,
            file.FileName,
            file.ContentType,
            file.Length,
            stream,
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
