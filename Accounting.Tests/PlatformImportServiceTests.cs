using Application.Abstraction;
using Application.Contracts.Common;
using Application.Contracts.FinancialAccess;
using Application.Contracts.PlatformImports;
using Application.Service.AccountingStorage;
using Application.Service.FinancialAccess;
using Application.Service.PlatformImports;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Domain;
using Domain.Entities.AccountingCore;
using Domain.Entities.AccountingPlatform;
using Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public class PlatformImportServiceTests
{
    [Fact]
    public async Task DownloadFile_ResolvesBatchStoredFileId()
    {
        await using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = 1, Code = "T", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "E", LegalName = "Entity", BaseCurrencyCode = "SAR" });
        db.PlatformAccounts.Add(new PlatformAccount { Id = 1, LegalEntityId = 1, Code = "P", PlatformName = "Platform" });
        var storedFile = new AccountingStoredFile
        {
            LegalEntityId = 1,
            OriginalFileName = "source.xlsx",
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            PlaintextLength = 4,
            Sha256 = new string('A', 64),
            StorageLocator = "stored-target",
            EncryptionKeyId = "test",
            CreatedBy = "accountant"
        };
        var batch = new PlatformImportBatch
        {
            LegalEntityId = 1,
            PlatformAccountId = 1,
            StoredFile = storedFile,
            ExternalReference = "IMPORT-1",
            PeriodStart = new DateOnly(2026, 7, 1),
            PeriodEnd = new DateOnly(2026, 7, 31),
            CreatedBy = "accountant"
        };
        db.PlatformImportBatches.Add(batch);
        await db.SaveChangesAsync();
        var storage = new MemoryStorage(new Dictionary<string, byte[]> { ["stored-target"] = [1, 2, 3, 4] });
        var service = new PlatformImportService(db, new AllowAllAccess(), storage);

        var result = await service.DownloadFileAsync(batch.Id, "accountant");

        Assert.True(result.IsSuccess);
        Assert.Equal("source.xlsx", result.Value.FileName);
        await using var content = result.Value.Content;
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, copy.ToArray());
    }

    [Fact]
    public async Task UntemplatedUpload_CanCreateTemplateAndReprocessSameBatch()
    {
        await using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = 1, Code = "T", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "E", LegalName = "Entity", BaseCurrencyCode = "SAR" });
        db.PlatformAccounts.Add(new PlatformAccount { Id = 1, LegalEntityId = 1, Code = "P", PlatformName = "Platform" });
        await db.SaveChangesAsync();
        var storage = new MemoryStorage();
        var service = new PlatformImportService(db, new AllowAllAccess(), storage);

        var upload = await service.UploadAsync(
            new UploadPlatformImportRequest(1, 1, null, "IMPORT-BOOTSTRAP", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), null),
            "bootstrap.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            new MemoryStream(CreateWorkbook()),
            "accountant");

        Assert.True(upload.IsSuccess);
        Assert.Equal(PlatformImportStatus.NeedsResolution, upload.Value.Status);
        Assert.Null(upload.Value.TemplateId);
        Assert.Equal(64, upload.Value.SchemaFingerprint.Length);

        var configuration = """
            {
              "headerRow": 1,
              "externalWorkerIdHeader": "Worker",
              "dateHeader": null,
              "sheetNames": ["Data"],
              "controlTotalMetricCode": "ACCEPTED_ORDERS",
              "columns": [
                {
                  "header": "Orders",
                  "metricCode": "ACCEPTED_ORDERS",
                  "category": 1,
                  "dataType": "number",
                  "currencyCode": "SAR",
                  "multiplier": 1
                }
              ],
              "workerCategory": "Rider",
              "riderIqamaHeader": null
            }
            """;
        var template = await service.CreateTemplateAsync(
            new CreatePlatformImportTemplateRequest(
                1,
                1,
                "bootstrap",
                "Bootstrap template",
                "generic-tabular-v1",
                upload.Value.SchemaFingerprint,
                configuration,
                new DateOnly(2026, 7, 1),
                null),
            "accountant");

        Assert.True(template.IsSuccess);
        Assert.Equal(PlatformTemplateStatus.Draft, template.Value.Status);

        var staleReprocess = await service.ReprocessAsync(
            upload.Value.Id,
            new ReprocessPlatformImportRequest(template.Value.Id, "not-base64"),
            "accountant");
        Assert.True(staleReprocess.IsFailure);
        Assert.Equal("Accounting.ConcurrencyConflict", staleReprocess.Error.Code);

        var reprocessed = await service.ReprocessAsync(
            upload.Value.Id,
            new ReprocessPlatformImportRequest(template.Value.Id, upload.Value.RowVersion),
            "accountant");

        Assert.True(reprocessed.IsSuccess);
        Assert.Equal(upload.Value.Id, reprocessed.Value.Id);
        Assert.Equal(template.Value.Id, reprocessed.Value.TemplateId);
        Assert.Equal(upload.Value.SchemaFingerprint, reprocessed.Value.SchemaFingerprint);
        Assert.Equal(1, reprocessed.Value.SheetCount);
        Assert.Equal(2, reprocessed.Value.RawRowCount);
        Assert.Equal(4, reprocessed.Value.RawCellCount);
        Assert.Equal(1, reprocessed.Value.FactCount);

        var rows = await service.GetRowsAsync(
            upload.Value.Id,
            new PaginationRequest { PageNumber = 1, PageSize = 10 },
            new PlatformImportRawRowListFilter { Search = "RIDER-1", SortBy = "rowNumber", SortDirection = "asc" },
            "accountant");
        var rawRow = Assert.Single(rows.Value.Items);
        Assert.Equal(2, rawRow.RowNumber);
        Assert.Equal(new[] { "RIDER-1", "5" }, rawRow.Cells.Select(x => x.DisplayValue));

        var facts = await service.GetFactsAsync(
            upload.Value.Id,
            new PaginationRequest { PageNumber = 1, PageSize = 10 },
            new PlatformNormalizedFactListFilter { MetricCode = "accepted_orders", IsResolved = false },
            "accountant");
        var fact = Assert.Single(facts.Value.Items);
        Assert.Equal(5m, fact.NumericValue);
        Assert.Equal(rawRow.Id, fact.SourceRawRowId);

        var templates = await service.GetTemplatesAsync(
            new PaginationRequest { PageNumber = 1, PageSize = 10 },
            new PlatformImportTemplateListFilter { LegalEntityId = 1, Search = "BOOT", SortBy = "code", SortDirection = "asc" },
            "accountant");
        Assert.Equal(template.Value.Id, Assert.Single(templates.Value.Items).Id);

        var batches = await service.GetBatchesAsync(
            new PaginationRequest { PageNumber = 1, PageSize = 10 },
            new PlatformImportBatchListFilter { LegalEntityId = 1, Search = "bootstrap", SortBy = "periodStart", SortDirection = "asc" },
            "accountant");
        Assert.Equal(upload.Value.Id, Assert.Single(batches.Value.Items).Id);
    }

    private static ApplicationDbcontext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbcontext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class AllowAllAccess : IFinancialAccessService
    {
        public Task<Result> EnsurePermissionAsync(string userId, int legalEntityId, FinancialPermission requiredPermission, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result<FinancialUserAccessResponse>> GrantAsync(GrantFinancialUserAccessRequest request, string grantedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> RevokeAsync(string userId, int legalEntityId, string revokedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyCollection<FinancialUserAccessResponse>>> GetForLegalEntityAsync(int legalEntityId, string requestedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static byte[] CreateWorkbook()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(new SheetData(
                new Row(
                    InlineCell("A1", "Worker"),
                    InlineCell("B1", "Orders")) { RowIndex = 1 },
                new Row(
                    InlineCell("A2", "RIDER-1"),
                    NumberCell("B2", "5")) { RowIndex = 2 }));
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Data"
            });
            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static Cell InlineCell(string reference, string value) => new()
    {
        CellReference = reference,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(value))
    };

    private static Cell NumberCell(string reference, string value) => new()
    {
        CellReference = reference,
        DataType = CellValues.Number,
        CellValue = new CellValue(value)
    };

    private sealed class MemoryStorage : IPrivateAccountingFileStorage
    {
        private readonly Dictionary<string, byte[]> files;

        public MemoryStorage(IReadOnlyDictionary<string, byte[]>? files = null)
        {
            this.files = files?.ToDictionary() ?? [];
        }

        public async Task<StoredAccountingFileResult> StoreAsync(int legalEntityId, Stream source, CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream();
            await source.CopyToAsync(memory, cancellationToken);
            var bytes = memory.ToArray();
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            var locator = $"{legalEntityId}/{hash}.acct";
            files[locator] = bytes;
            return new StoredAccountingFileResult(locator, hash, bytes.LongLength, "test");
        }

        public Task<Stream> OpenReadAsync(string storageLocator, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(files[storageLocator], writable: false));

        public Task DeleteAsync(string storageLocator, CancellationToken cancellationToken = default)
        {
            files.Remove(storageLocator);
            return Task.CompletedTask;
        }
    }
}
