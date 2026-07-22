using Application.Contracts.AccountingFiles;
using Application.Contracts.Common;
using Application.Abstraction.Errors;
using Application.Extensions;
using Application.Service.AccountingFiles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/accounting/files")]
[ApiController]
[Authorize(Roles = "Master,Accountant")]
public class AccountingFilesController(IAccountingFileService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] AccountingFileListFilter filter,
        CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.GetAllAsync(pagination, filter, actor, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost]
    [RequestSizeLimit(104_857_600)]
    public async Task<IActionResult> Upload([FromForm] int legalEntityId, [FromForm] DateTime? retainUntil, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        if (file is null || file.Length == 0)
            return Application.Abstraction.Result.Failure(AccountingPlatformErrors.InvalidFile).ToProblem();
        await using var content = file.OpenReadStream();
        var result = await service.UploadAsync(new UploadAccountingFileRequest(legalEntityId, retainUntil), file.FileName, file.ContentType, content, actor, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("{fileId:guid}")]
    public async Task<IActionResult> Download(Guid fileId, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.DownloadAsync(fileId, actor, cancellationToken);
        return result.IsSuccess
            ? File(result.Value.Content, result.Value.File.ContentType, result.Value.File.OriginalFileName, enableRangeProcessing: false)
            : result.ToProblem();
    }
}
