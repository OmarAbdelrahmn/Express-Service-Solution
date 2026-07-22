using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.AccountingFiles;
using Application.Contracts.Common;
using Application.Service.AccountingStorage;
using Application.Service.FinancialAccess;
using Domain;
using Domain.Entities.AccountingCore;
using Domain.Entities.AccountingPlatform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Application.Service.AccountingFiles;

public sealed class AccountingFileService(
    ApplicationDbcontext dbcontext,
    IPrivateAccountingFileStorage storage,
    IFinancialAccessService financialAccessService) : IAccountingFileService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".pdf", ".png", ".jpg", ".jpeg", ".xlsx", ".csv", ".txt" };

    public async Task<Result<PagedResponse<AccountingFileResponse>>> GetAllAsync(
        PaginationRequest pagination,
        AccountingFileListFilter filter,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (filter.LegalEntityId <= 0 || (filter.FromDate.HasValue && filter.ToDate.HasValue && filter.ToDate < filter.FromDate))
            return Result.Failure<PagedResponse<AccountingFileResponse>>(AccountingPlatformErrors.InvalidRequest);

        var access = await financialAccessService.EnsurePermissionAsync(actorId, filter.LegalEntityId, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<PagedResponse<AccountingFileResponse>>(access.Error);

        var query = dbcontext.AccountingStoredFiles
            .AsNoTracking()
            .Where(x => x.LegalEntityId == filter.LegalEntityId && x.Status == StoredFileStatus.Active);

        if (!string.IsNullOrWhiteSpace(filter.ContentType))
        {
            var contentType = filter.ContentType.Trim().ToUpperInvariant();
            query = query.Where(x => x.ContentType.ToUpper() == contentType);
        }

        if (filter.FromDate.HasValue)
        {
            var from = filter.FromDate.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(x => x.CreatedAt >= from);
        }

        if (filter.ToDate.HasValue)
        {
            var to = filter.ToDate.Value.ToDateTime(TimeOnly.MaxValue);
            query = query.Where(x => x.CreatedAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToUpperInvariant();
            query = query.Where(x => x.OriginalFileName.ToUpper().Contains(search) || x.Sha256.ToUpper().Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = pagination.NormalizedPageNumber;
        var pageSize = pagination.NormalizedPageSize;
        var ascending = string.Equals(filter.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        var sortBy = filter.SortBy?.Trim().ToLowerInvariant();

        var ordered = (sortBy, ascending) switch
        {
            ("filename" or "originalfilename", true) => query.OrderBy(x => x.OriginalFileName).ThenBy(x => x.Id),
            ("filename" or "originalfilename", false) => query.OrderByDescending(x => x.OriginalFileName).ThenByDescending(x => x.Id),
            ("contenttype", true) => query.OrderBy(x => x.ContentType).ThenBy(x => x.Id),
            ("contenttype", false) => query.OrderByDescending(x => x.ContentType).ThenByDescending(x => x.Id),
            ("length", true) => query.OrderBy(x => x.PlaintextLength).ThenBy(x => x.Id),
            ("length", false) => query.OrderByDescending(x => x.PlaintextLength).ThenByDescending(x => x.Id),
            ("retainuntil", true) => query.OrderBy(x => x.RetainUntil).ThenBy(x => x.Id),
            ("retainuntil", false) => query.OrderByDescending(x => x.RetainUntil).ThenByDescending(x => x.Id),
            ("id", true) => query.OrderBy(x => x.Id),
            ("id", false) => query.OrderByDescending(x => x.Id),
            ("createdat", true) => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        };

        var files = await ordered
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AccountingFileResponse(x.Id, x.LegalEntityId, x.OriginalFileName, x.ContentType, x.PlaintextLength, x.Sha256, x.RetainUntil, x.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResponse<AccountingFileResponse>(files, pageNumber, pageSize, totalCount));
    }

    public async Task<Result<AccountingFileResponse>> UploadAsync(UploadAccountingFileRequest request, string fileName, string contentType, Stream content, string actorId, CancellationToken cancellationToken = default)
    {
        var access = await financialAccessService.EnsurePermissionAsync(actorId, request.LegalEntityId, FinancialPermission.Prepare, cancellationToken);
        if (access.IsFailure) return Result.Failure<AccountingFileResponse>(access.Error);
        if (!await dbcontext.LegalEntities.AnyAsync(x => x.Id == request.LegalEntityId && x.IsActive, cancellationToken))
            return Result.Failure<AccountingFileResponse>(AccountingPlatformErrors.NotFound);

        var safeName = Path.GetFileName(fileName ?? string.Empty).Trim();
        var extension = Path.GetExtension(safeName);
        if (string.IsNullOrWhiteSpace(safeName) || safeName.Length > 260 || !AllowedExtensions.Contains(extension))
            return Result.Failure<AccountingFileResponse>(AccountingPlatformErrors.InvalidFile);

        StoredAccountingFileResult stored;
        try { stored = await storage.StoreAsync(request.LegalEntityId, content, cancellationToken); }
        catch (Exception ex) when (ex is InvalidDataException or CryptographicException or IOException or InvalidOperationException)
        { return Result.Failure<AccountingFileResponse>(ex is InvalidDataException ? AccountingPlatformErrors.InvalidFile : AccountingPlatformErrors.StorageUnavailable); }

        if (!await HasValidSignatureAsync(extension, stored.StorageLocator, cancellationToken))
        {
            // Content-addressed files may already be referenced by another record.
            // Remove only a genuinely orphaned invalid upload.
            if (!await dbcontext.AccountingStoredFiles.AnyAsync(x => x.LegalEntityId == request.LegalEntityId && x.Sha256 == stored.Sha256, cancellationToken))
                await storage.DeleteAsync(stored.StorageLocator, cancellationToken);
            return Result.Failure<AccountingFileResponse>(AccountingPlatformErrors.InvalidFile);
        }

        var existing = await dbcontext.AccountingStoredFiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.LegalEntityId == request.LegalEntityId && x.Sha256 == stored.Sha256 && x.Status == StoredFileStatus.Active, cancellationToken);
        if (existing is not null) return Result.Success(ToResponse(existing));

        IDbContextTransaction? transaction = null;
        try
        {
            if (dbcontext.Database.IsRelational())
                transaction = await dbcontext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (dbcontext.Database.IsSqlServer())
                await dbcontext.Database.ExecuteSqlInterpolatedAsync($"EXEC sp_getapplock @Resource={"Accounting:AuditChain:" + request.LegalEntityId}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000", cancellationToken);

            var raced = await dbcontext.AccountingStoredFiles.AsNoTracking()
                .SingleOrDefaultAsync(x => x.LegalEntityId == request.LegalEntityId && x.Sha256 == stored.Sha256 && x.Status == StoredFileStatus.Active, cancellationToken);
            if (raced is not null)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return Result.Success(ToResponse(raced));
            }

            var file = new AccountingStoredFile
            {
                LegalEntityId = request.LegalEntityId,
                OriginalFileName = safeName,
                ContentType = CanonicalContentType(extension, contentType),
                PlaintextLength = stored.PlaintextLength,
                Sha256 = stored.Sha256,
                StorageLocator = stored.StorageLocator,
                EncryptionKeyId = stored.EncryptionKeyId,
                RetainUntil = request.RetainUntil ?? DateTime.UtcNow.AddYears(7),
                CreatedBy = actorId
            };
            dbcontext.AccountingStoredFiles.Add(file);
            await AppendAuditAsync(file, actorId, cancellationToken);
            await dbcontext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Result.Success(ToResponse(file));
        }
        catch (DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            dbcontext.ChangeTracker.Clear();
            var raced = await dbcontext.AccountingStoredFiles.AsNoTracking()
                .SingleOrDefaultAsync(x => x.LegalEntityId == request.LegalEntityId && x.Sha256 == stored.Sha256 && x.Status == StoredFileStatus.Active, cancellationToken);
            return raced is null ? Result.Failure<AccountingFileResponse>(AccountingPlatformErrors.StorageUnavailable) : Result.Success(ToResponse(raced));
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    public async Task<Result<AccountingFileDownload>> DownloadAsync(Guid fileId, string actorId, CancellationToken cancellationToken = default)
    {
        var file = await dbcontext.AccountingStoredFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fileId && x.Status == StoredFileStatus.Active, cancellationToken);
        if (file is null) return Result.Failure<AccountingFileDownload>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, file.LegalEntityId, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<AccountingFileDownload>(access.Error);
        try
        {
            var content = await storage.OpenReadAsync(file.StorageLocator, cancellationToken);
            return Result.Success(new AccountingFileDownload(ToResponse(file), content));
        }
        catch (Exception ex) when (ex is InvalidDataException or CryptographicException or IOException or InvalidOperationException)
        { return Result.Failure<AccountingFileDownload>(AccountingPlatformErrors.StorageUnavailable); }
    }

    private async Task<bool> HasValidSignatureAsync(string extension, string locator, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await storage.OpenReadAsync(locator, cancellationToken);
            var header = new byte[8];
            var read = await stream.ReadAsync(header, cancellationToken);
            return extension.ToLowerInvariant() switch
            {
                ".pdf" => read >= 5 && header.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
                ".png" => read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                ".jpg" or ".jpeg" => read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
                ".xlsx" => read >= 4 && header[0] == (byte)'P' && header[1] == (byte)'K' && header[2] == 3 && header[3] == 4,
                ".csv" or ".txt" => read > 0 && !header.AsSpan(0, read).Contains((byte)0),
                _ => false
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or CryptographicException or IOException or InvalidOperationException) { return false; }
    }

    private async Task AppendAuditAsync(AccountingStoredFile file, string actorId, CancellationToken cancellationToken)
    {
        var head = await dbcontext.AccountingAuditChainHeads.SingleOrDefaultAsync(x => x.LegalEntityId == file.LegalEntityId, cancellationToken);
        if (head is null)
        {
            head = new AccountingAuditChainHead { LegalEntityId = file.LegalEntityId };
            dbcontext.AccountingAuditChainHeads.Add(head);
        }
        var payload = JsonSerializer.Serialize(new { file.Id, file.OriginalFileName, file.ContentType, file.PlaintextLength, file.Sha256, file.RetainUntil });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{head.LastHash}|{file.LegalEntityId}|{file.Id}|AccountingFile.Uploaded|{actorId}|{payload}")));
        dbcontext.AccountingAuditEvents.Add(new AccountingAuditEvent { LegalEntityId = file.LegalEntityId, EventType = "AccountingFile.Uploaded", ActorId = actorId, PayloadJson = payload, PreviousHash = head.LastHash, Hash = hash });
        dbcontext.AccountingOutboxMessages.Add(new AccountingOutboxMessage { LegalEntityId = file.LegalEntityId, Type = "AccountingFile.Uploaded", PayloadJson = payload, CorrelationId = file.Id.ToString("N") });
        head.LastHash = hash;
    }

    private static string CanonicalContentType(string extension, string supplied) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".csv" => "text/csv",
        ".txt" => "text/plain",
        _ => string.IsNullOrWhiteSpace(supplied) ? "application/octet-stream" : supplied.Trim()
    };

    private static AccountingFileResponse ToResponse(AccountingStoredFile file) =>
        new(file.Id, file.LegalEntityId, file.OriginalFileName, file.ContentType, file.PlaintextLength, file.Sha256, file.RetainUntil, file.CreatedAt);
}
