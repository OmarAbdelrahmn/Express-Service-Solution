using System.Security.Cryptography;
using Application.Abstraction;
using Application.Contracts.AccountingFiles;
using Application.Contracts.Common;
using Application.Contracts.FinancialAccess;
using Application.Service.AccountingFiles;
using Application.Service.AccountingStorage;
using Application.Service.FinancialAccess;
using Domain;
using Domain.Entities.AccountingCore;
using Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public class AccountingFileServiceTests
{
    [Fact]
    public async Task Upload_UsesContentIdentity_AndDownloadReturnsPrivateFile()
    {
        await using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = 1, Code = "T", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "E", LegalName = "Entity", BaseCurrencyCode = "SAR" });
        await db.SaveChangesAsync();
        var storage = new MemoryPrivateStorage();
        var service = new AccountingFileService(db, storage, new AllowAllAccess());
        var bytes = "%PDF-1.7\nprivate evidence"u8.ToArray();

        var first = await service.UploadAsync(new UploadAccountingFileRequest(1, null), "receipt.pdf", "application/octet-stream", new MemoryStream(bytes), "accountant");
        var replay = await service.UploadAsync(new UploadAccountingFileRequest(1, null), "renamed.pdf", "application/pdf", new MemoryStream(bytes), "accountant");
        var download = await service.DownloadAsync(first.Value.Id, "accountant");

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value.Id, replay.Value.Id);
        Assert.Equal("application/pdf", first.Value.ContentType);
        Assert.Single(await db.AccountingStoredFiles.ToListAsync());
        Assert.Single(await db.AccountingAuditEvents.ToListAsync());
        await using var content = download.Value.Content;
        using var memory = new MemoryStream();
        await content.CopyToAsync(memory);
        Assert.Equal(bytes, memory.ToArray());
    }

    [Fact]
    public async Task Upload_RejectsMimeSpoofedPdf_AndDeletesOrphan()
    {
        await using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = 1, Code = "T", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "E", LegalName = "Entity", BaseCurrencyCode = "SAR" });
        await db.SaveChangesAsync();
        var storage = new MemoryPrivateStorage();
        var service = new AccountingFileService(db, storage, new AllowAllAccess());

        var result = await service.UploadAsync(new UploadAccountingFileRequest(1, null), "receipt.pdf", "application/pdf", new MemoryStream("not a pdf"u8.ToArray()), "accountant");

        Assert.True(result.IsFailure);
        Assert.Empty(storage.Files);
        Assert.Empty(await db.AccountingStoredFiles.ToListAsync());
    }

    [Fact]
    public async Task GetAll_IsLegalEntityScoped_Filtered_AndPaged()
    {
        await using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = 1, Code = "T", Name = "Tenant" });
        db.LegalEntities.AddRange(
            new LegalEntity { Id = 1, TenantId = 1, Code = "E1", LegalName = "Entity 1", BaseCurrencyCode = "SAR" },
            new LegalEntity { Id = 2, TenantId = 1, Code = "E2", LegalName = "Entity 2", BaseCurrencyCode = "SAR" });
        db.AccountingStoredFiles.AddRange(
            StoredFile(1, "older.pdf", "application/pdf", new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc)),
            StoredFile(1, "newer.pdf", "application/pdf", new DateTime(2026, 7, 2, 8, 0, 0, DateTimeKind.Utc)),
            StoredFile(1, "notes.txt", "text/plain", new DateTime(2026, 7, 3, 8, 0, 0, DateTimeKind.Utc)),
            StoredFile(2, "other-entity.pdf", "application/pdf", new DateTime(2026, 7, 4, 8, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var service = new AccountingFileService(db, new MemoryPrivateStorage(), new AllowAllAccess());

        var result = await service.GetAllAsync(
            new PaginationRequest { PageNumber = 1, PageSize = 1 },
            new AccountingFileListFilter { LegalEntityId = 1, ContentType = "application/pdf" },
            "accountant");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Single(result.Value.Items);
        Assert.Equal("newer.pdf", result.Value.Items[0].OriginalFileName);
        Assert.True(result.Value.HasNextPage);

        var searched = await service.GetAllAsync(
            new PaginationRequest { PageNumber = 1, PageSize = 10 },
            new AccountingFileListFilter
            {
                LegalEntityId = 1,
                Search = "PDF",
                SortBy = "originalFileName",
                SortDirection = "asc"
            },
            "accountant");

        Assert.Equal(new[] { "newer.pdf", "older.pdf" }, searched.Value.Items.Select(x => x.OriginalFileName));
    }

    private static Domain.Entities.AccountingPlatform.AccountingStoredFile StoredFile(
        int legalEntityId,
        string name,
        string contentType,
        DateTime createdAt) => new()
        {
            LegalEntityId = legalEntityId,
            OriginalFileName = name,
            ContentType = contentType,
            PlaintextLength = 10,
            Sha256 = Guid.NewGuid().ToString("N").PadRight(64, '0'),
            StorageLocator = Guid.NewGuid().ToString("N"),
            EncryptionKeyId = "test",
            CreatedBy = "tester",
            CreatedAt = createdAt
        };

    private static ApplicationDbcontext CreateDbContext() => new(new DbContextOptionsBuilder<ApplicationDbcontext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class MemoryPrivateStorage : IPrivateAccountingFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public async Task<StoredAccountingFileResult> StoreAsync(int legalEntityId, Stream source, CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream();
            await source.CopyToAsync(memory, cancellationToken);
            var bytes = memory.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var locator = $"{legalEntityId}/{hash}.acct";
            Files.TryAdd(locator, bytes);
            return new StoredAccountingFileResult(locator, hash, bytes.Length, "test");
        }

        public Task<Stream> OpenReadAsync(string storageLocator, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(Files[storageLocator], writable: false));

        public Task DeleteAsync(string storageLocator, CancellationToken cancellationToken = default)
        {
            Files.Remove(storageLocator);
            return Task.CompletedTask;
        }
    }

    private sealed class AllowAllAccess : IFinancialAccessService
    {
        public Task<Result> EnsurePermissionAsync(string userId, int legalEntityId, FinancialPermission requiredPermission, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result<FinancialUserAccessResponse>> GrantAsync(GrantFinancialUserAccessRequest request, string grantedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> RevokeAsync(string userId, int legalEntityId, string revokedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyCollection<FinancialUserAccessResponse>>> GetForLegalEntityAsync(int legalEntityId, string requestedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
