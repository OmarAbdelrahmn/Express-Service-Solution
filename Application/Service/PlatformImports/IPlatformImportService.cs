using Application.Abstraction;
using Application.Contracts.Common;
using Application.Contracts.PlatformImports;

namespace Application.Service.PlatformImports;

public interface IPlatformImportService
{
    Task<Result<PagedResponse<PlatformImportTemplateResponse>>> GetTemplatesAsync(PaginationRequest pagination, PlatformImportTemplateListFilter filter, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformImportTemplateResponse>> GetTemplateAsync(Guid id, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformImportTemplateResponse>> CreateTemplateAsync(CreatePlatformImportTemplateRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformImportTemplateResponse>> ActivateTemplateAsync(Guid id, ActivatePlatformImportTemplateRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformImportTemplateResponse>> RetireTemplateAsync(Guid id, RetirePlatformImportTemplateRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformImportBatchResponse>> UploadAsync(UploadPlatformImportRequest request, string fileName, string contentType, Stream content, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformImportBatchResponse>> UploadAmazonAsync(DirectPlatformImportRequest request, string fileName, string contentType, Stream content, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformImportBatchResponse>> UploadHungerAsync(DirectPlatformImportRequest request, string fileName, string contentType, Stream content, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformImportBatchResponse>> UploadKeetaPayPerOrderAsync(DirectPlatformImportRequest request, string fileName, string contentType, Stream content, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformImportBatchResponse>> UploadKeetaSegmentsAsync(DirectPlatformImportRequest request, string fileName, string contentType, Stream content, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<PlatformImportBatchResponse>>> GetBatchesAsync(PaginationRequest pagination, PlatformImportBatchListFilter filter, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformImportBatchResponse>> GetBatchAsync(Guid id, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<PlatformNormalizedFactResponse>>> GetFactsAsync(Guid batchId, PaginationRequest pagination, PlatformNormalizedFactListFilter filter, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<PlatformImportRawRowResponse>>> GetRowsAsync(Guid batchId, PaginationRequest pagination, PlatformImportRawRowListFilter filter, string actorId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<PlatformImportIssueResponse>>> GetIssuesAsync(Guid batchId, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformImportIssueResponse>> ResolveIssueAsync(long issueId, ResolvePlatformImportIssueRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformImportBatchResponse>> RemapWorkerAsync(Guid batchId, RemapPlatformWorkerRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformNormalizedFactResponse>> OverrideValidityAsync(long factId, OverridePlatformValidityRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformImportBatchResponse>> ReprocessAsync(Guid batchId, ReprocessPlatformImportRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformImportBatchResponse>> SupersedeAsync(Guid batchId, SupersedePlatformImportBatchRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformImportBatchResponse>> ApproveAsync(Guid id, ReviewPlatformImportRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<PlatformImportBatchResponse>> RejectAsync(Guid id, ReviewPlatformImportRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<AccountingFileDownloadResponse>> DownloadFileAsync(Guid batchId, string actorId, CancellationToken cancellationToken = default);
}
