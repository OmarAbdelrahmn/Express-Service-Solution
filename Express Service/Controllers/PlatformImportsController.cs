using Application.Contracts.PlatformImports;
using Application.Contracts.Common;
using Application.Abstraction.Errors;
using Application.Extensions;
using Application.Service.PlatformImports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/accounting")]
[ApiController]
[Authorize(Roles = "Master,Accountant")]
public class PlatformImportsController(IPlatformImportService service) : ControllerBase
{
    [HttpGet("platform-templates")]
    public async Task<IActionResult> GetTemplates(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] PlatformImportTemplateListFilter filter,
        CancellationToken ct)
        => await WithActor(actor => service.GetTemplatesAsync(pagination, filter, actor, ct));

    [HttpGet("platform-templates/{id:guid}")]
    public async Task<IActionResult> GetTemplate(Guid id, CancellationToken ct)
        => await WithActor(actor => service.GetTemplateAsync(id, actor, ct));

    [HttpPost("platform-templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] CreatePlatformImportTemplateRequest request, CancellationToken ct)
        => await WithActor(actor => service.CreateTemplateAsync(request, actor, ct));

    [HttpPost("platform-templates/{id:guid}/activate")]
    public async Task<IActionResult> ActivateTemplate(Guid id, [FromBody] ActivatePlatformImportTemplateRequest request, CancellationToken ct)
        => await WithActor(actor => service.ActivateTemplateAsync(id, request, actor, ct));

    [HttpPost("platform-templates/{id:guid}/retire")]
    public async Task<IActionResult> RetireTemplate(Guid id, [FromBody] RetirePlatformImportTemplateRequest request, CancellationToken ct)
        => await WithActor(actor => service.RetireTemplateAsync(id, request, actor, ct));

    [HttpPost("platform-imports")]
    [RequestSizeLimit(104_857_600)]
    public async Task<IActionResult> Upload(
        [FromForm] int legalEntityId,
        [FromForm] int platformAccountId,
        [FromForm] Guid? templateId,
        [FromForm] string externalReference,
        [FromForm] DateOnly periodStart,
        [FromForm] DateOnly periodEnd,
        [FromForm] decimal? sourceControlTotal,
        [FromForm] IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Application.Abstraction.Result.Failure(AccountingPlatformErrors.InvalidFile).ToProblem();
        await using var content = file.OpenReadStream();
        var request = new UploadPlatformImportRequest(legalEntityId, platformAccountId, templateId, externalReference, periodStart, periodEnd, sourceControlTotal);
        return await WithActor(actor => service.UploadAsync(request, file.FileName, file.ContentType, content, actor, ct));
    }

    [HttpGet("platform-imports")]
    public async Task<IActionResult> GetBatches(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] PlatformImportBatchListFilter filter,
        CancellationToken ct)
        => await WithActor(actor => service.GetBatchesAsync(pagination, filter, actor, ct));

    [HttpGet("platform-imports/{id:guid}")]
    public async Task<IActionResult> GetBatch(Guid id, CancellationToken ct) => await WithActor(actor => service.GetBatchAsync(id, actor, ct));

    [HttpGet("platform-imports/{id:guid}/facts")]
    public async Task<IActionResult> GetFacts(
        Guid id,
        [FromQuery] PaginationRequest pagination,
        [FromQuery] PlatformNormalizedFactListFilter filter,
        CancellationToken ct)
        => await WithActor(actor => service.GetFactsAsync(id, pagination, filter, actor, ct));

    [HttpGet("platform-imports/{id:guid}/rows")]
    public async Task<IActionResult> GetRows(
        Guid id,
        [FromQuery] PaginationRequest pagination,
        [FromQuery] PlatformImportRawRowListFilter filter,
        CancellationToken ct)
        => await WithActor(actor => service.GetRowsAsync(id, pagination, filter, actor, ct));

    [HttpGet("platform-imports/{id:guid}/issues")]
    public async Task<IActionResult> GetIssues(Guid id, CancellationToken ct) => await WithActor(actor => service.GetIssuesAsync(id, actor, ct));

    [HttpPost("import-issues/{id:long}/resolve")]
    public async Task<IActionResult> ResolveIssue(long id, [FromBody] ResolvePlatformImportIssueRequest request, CancellationToken ct)
        => await WithActor(actor => service.ResolveIssueAsync(id, request, actor, ct));

    [HttpPost("platform-imports/{id:guid}/worker-remaps")]
    public async Task<IActionResult> RemapWorker(Guid id, [FromBody] RemapPlatformWorkerRequest request, CancellationToken ct)
        => await WithActor(actor => service.RemapWorkerAsync(id, request, actor, ct));

    [HttpPost("platform-facts/{id:long}/validity-override")]
    public async Task<IActionResult> OverrideValidity(long id, [FromBody] OverridePlatformValidityRequest request, CancellationToken ct)
        => await WithActor(actor => service.OverrideValidityAsync(id, request, actor, ct));

    [HttpPost("platform-imports/{id:guid}/reprocess")]
    public async Task<IActionResult> Reprocess(Guid id, [FromBody] ReprocessPlatformImportRequest request, CancellationToken ct)
        => await WithActor(actor => service.ReprocessAsync(id, request, actor, ct));

    [HttpPost("platform-imports/{id:guid}/supersede")]
    public async Task<IActionResult> Supersede(Guid id, [FromBody] SupersedePlatformImportBatchRequest request, CancellationToken ct)
        => await WithActor(actor => service.SupersedeAsync(id, request, actor, ct));

    [HttpPost("platform-imports/{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ReviewPlatformImportRequest request, CancellationToken ct)
        => await WithActor(actor => service.ApproveAsync(id, request, actor, ct));

    [HttpPost("platform-imports/{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ReviewPlatformImportRequest request, CancellationToken ct)
        => await WithActor(actor => service.RejectAsync(id, request, actor, ct));

    [HttpGet("platform-imports/{id:guid}/file")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.DownloadFileAsync(id, actor, ct);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.ToProblem();
    }

    private async Task<IActionResult> WithActor<T>(Func<string, Task<Application.Abstraction.Result<T>>> action)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await action(actor);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    private async Task<IActionResult> WithActor(Func<string, Task<Application.Abstraction.Result>> action)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await action(actor);
        return result.IsSuccess ? Ok() : result.ToProblem();
    }
}
