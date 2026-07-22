using Application.Abstraction;
using Application.Contracts.AccountingFiles;
using Application.Contracts.Common;

namespace Application.Service.AccountingFiles;

public interface IAccountingFileService
{
    Task<Result<PagedResponse<AccountingFileResponse>>> GetAllAsync(PaginationRequest pagination, AccountingFileListFilter filter, string actorId, CancellationToken cancellationToken = default);
    Task<Result<AccountingFileResponse>> UploadAsync(UploadAccountingFileRequest request, string fileName, string contentType, Stream content, string actorId, CancellationToken cancellationToken = default);
    Task<Result<AccountingFileDownload>> DownloadAsync(Guid fileId, string actorId, CancellationToken cancellationToken = default);
}
