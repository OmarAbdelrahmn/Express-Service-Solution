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
using Domain.Entities;
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

    [Fact]
    public async Task DirectKeetaPayPerOrderUpload_AutoCertifiesAndSkipsOrderDetailSheet()
    {
        await using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = 1, Code = "T", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "E", LegalName = "Entity", BaseCurrencyCode = "SAR" });
        db.PlatformAccounts.Add(new PlatformAccount { Id = 1, LegalEntityId = 1, Code = "KEETA", PlatformName = "Keeta" });
        await db.SaveChangesAsync();
        var service = new PlatformImportService(db, new AllowAllAccess(), new MemoryStorage());

        var upload = await service.UploadKeetaPayPerOrderAsync(
            new DirectPlatformImportRequest(1, 1, "KEETA-2026-05-PPO", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 100m),
            "keeta-pay-per-order.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            new MemoryStream(CreateKeetaPayPerOrderWorkbook()),
            "accountant");

        Assert.True(upload.IsSuccess);
        Assert.Equal("keeta-pay-per-order-v1", upload.Value.AdapterKey);
        Assert.NotNull(upload.Value.TemplateId);
        Assert.Equal(2, upload.Value.SheetCount);
        Assert.Equal(4, upload.Value.RawRowCount);
        Assert.Equal(9, upload.Value.FactCount);
        Assert.Equal(100m, upload.Value.NormalizedControlTotal);
        Assert.Equal("تحتاج إلى معالجة", upload.Value.StatusNameAr);
        Assert.DoesNotContain(await db.PlatformImportSheets.ToListAsync(), x => x.Name == "تفاصيل طلب السائق");

        var template = await db.PlatformImportTemplates.SingleAsync();
        Assert.Equal(PlatformTemplateStatus.Active, template.Status);
        Assert.Equal("keeta-pay-per-order-v1", template.AdapterKey);
        Assert.Equal(upload.Value.SchemaFingerprint, template.SchemaFingerprint);

        var facts = await service.GetFactsAsync(
            upload.Value.Id,
            new PaginationRequest { PageNumber = 1, PageSize = 25 },
            new PlatformNormalizedFactListFilter { SortBy = "id", SortDirection = "asc" },
            "accountant");
        var companyTotal = Assert.Single(facts.Value.Items, x => x.MetricCode == "COMPANY_TOTAL" && x.WorkerCategory == "Company");
        Assert.Equal("إجمالي مستحقات الشركة", companyTotal.MetricNameAr);
        Assert.Equal("إجمالي المطابقة", companyTotal.CategoryNameAr);

        var issues = await service.GetIssuesAsync(upload.Value.Id, "accountant");
        var identityIssue = Assert.Single(issues.Value, x => x.Code == "IDENTITY_MISSING");
        Assert.Equal("هوية المندوب غير مرتبطة", identityIssue.CodeAr);
        Assert.Equal("المندوب KEETA-RIDER-1 لديه 0 تطابق فعّال للهوية بتاريخ 2026-05-31. المصادر: لا يوجد.", identityIssue.MessageAr);
    }

    [Fact]
    public async Task DirectAmazonUpload_TreatsCompanySummaryRowAsCompanyFact()
    {
        await using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = 1, Code = "T", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "E", LegalName = "Entity", BaseCurrencyCode = "SAR" });
        db.PlatformAccounts.Add(new PlatformAccount { Id = 1, LegalEntityId = 1, Code = "AMAZON", PlatformName = "Amazon" });
        await db.SaveChangesAsync();
        var service = new PlatformImportService(db, new AllowAllAccess(), new MemoryStorage());

        var upload = await service.UploadAmazonAsync(
            new DirectPlatformImportRequest(1, 1, "AMAZON-2026-07", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 100m),
            "amazon.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            new MemoryStream(CreateAmazonCompanyWorkbook()),
            "accountant");

        Assert.True(upload.IsSuccess);
        Assert.Equal(PlatformImportStatus.Reconciled, upload.Value.Status);
        Assert.Equal(0, upload.Value.OpenBlockingIssueCount);

        var facts = await service.GetFactsAsync(
            upload.Value.Id,
            new PaginationRequest { PageNumber = 1, PageSize = 20 },
            new PlatformNormalizedFactListFilter { SortBy = "id", SortDirection = "asc" },
            "accountant");
        Assert.All(facts.Value.Items, fact => Assert.Equal("Company", fact.WorkerCategory));
        Assert.All(facts.Value.Items, fact => Assert.True(fact.IsResolved));
        Assert.All(facts.Value.Items, fact => Assert.Equal("COMPANY", fact.ExternalWorkerId));

        var issues = await service.GetIssuesAsync(upload.Value.Id, "accountant");
        Assert.DoesNotContain(issues.Value, issue => issue.Code is "IDENTITY_MISSING" or "IDENTITY_AMBIGUOUS");
    }

    [Fact]
    public async Task RejectedImport_CanBeUploadedAgainWithTheSameFileAndReference()
    {
        await using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = 1, Code = "T", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "E", LegalName = "Entity", BaseCurrencyCode = "SAR" });
        db.PlatformAccounts.Add(new PlatformAccount { Id = 1, LegalEntityId = 1, Code = "AMAZON", PlatformName = "Amazon" });
        await db.SaveChangesAsync();
        var service = new PlatformImportService(db, new AllowAllAccess(), new MemoryStorage());
        var workbook = CreateAmazonCompanyWorkbook();
        var request = new DirectPlatformImportRequest(1, 1, "AMAZON-2026-07", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 100m);

        var first = await service.UploadAmazonAsync(request, "amazon.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", new MemoryStream(workbook), "accountant");
        Assert.True(first.IsSuccess);
        var rejected = await service.RejectAsync(first.Value.Id, new ReviewPlatformImportRequest("Incorrect source file"), "accountant");
        Assert.True(rejected.IsSuccess);
        Assert.Equal(PlatformImportStatus.Rejected, rejected.Value.Status);

        var second = await service.UploadAmazonAsync(request, "amazon.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", new MemoryStream(workbook), "accountant");

        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value.Id, second.Value.Id);
        Assert.Equal(PlatformImportStatus.Reconciled, second.Value.Status);
        Assert.Equal(2, await db.PlatformImportBatches.CountAsync());
    }

    [Fact]
    public async Task FactAndIssueResponses_ReturnRiderIqamaAndArabicName()
    {
        await using var db = CreateDbContext();
        var batchId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = 1, Code = "T", Name = "Tenant" });
        db.LegalEntities.Add(new LegalEntity { Id = 1, TenantId = 1, Code = "E", LegalName = "Entity", BaseCurrencyCode = "SAR" });
        db.PlatformAccounts.Add(new PlatformAccount { Id = 1, LegalEntityId = 1, Code = "P", PlatformName = "Platform" });
        db.Employees.Add(new Employees { IqamaNo = 2039796, NameAR = "أحمد علي" });
        db.PlatformImportBatches.Add(new PlatformImportBatch { Id = batchId, LegalEntityId = 1, PlatformAccountId = 1, ExternalReference = "TEST", PeriodStart = new DateOnly(2026, 7, 1), PeriodEnd = new DateOnly(2026, 7, 31) });
        db.PlatformNormalizedFacts.Add(new PlatformNormalizedFact
        {
            PlatformImportBatchId = batchId, LegalEntityId = 1, PlatformAccountId = 1, SourceRawRowId = 77,
            RiderIqamaNo = 2039796, ExternalWorkerId = "WORKER-1", FactDate = new DateOnly(2026, 7, 31),
            Category = PlatformFactCategory.RiderPayout, MetricCode = "NET_SETTLEMENT", NumericValue = 123m, IsResolved = true
        });
        db.PlatformImportIssues.Add(new PlatformImportIssue
        {
            PlatformImportBatchId = batchId, SourceRawRowId = 77, Severity = PlatformImportIssueSeverity.Warning,
            Code = "VALUE_INVALID", Message = "الخلية B2 لا تحتوي على قيمة صالحة من النوع number."
        });
        await db.SaveChangesAsync();
        var service = new PlatformImportService(db, new AllowAllAccess(), new MemoryStorage());

        var facts = await service.GetFactsAsync(batchId, new PaginationRequest { PageNumber = 1, PageSize = 10 }, new PlatformNormalizedFactListFilter(), "accountant");
        var fact = Assert.Single(facts.Value.Items);
        Assert.Equal(2039796, fact.RiderIqamaNo);
        Assert.Equal("أحمد علي", fact.RiderNameAr);
        Assert.Equal("صافي التسوية", fact.MetricNameAr);

        var issues = await service.GetIssuesAsync(batchId, "accountant");
        var issue = Assert.Single(issues.Value);
        Assert.Equal(2039796, issue.RiderIqamaNo);
        Assert.Equal("أحمد علي", issue.RiderNameAr);
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

    private static byte[] CreateKeetaPayPerOrderWorkbook()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());

            AddSheet(workbookPart, sheets, 1, "تفاصيل الشركاء",
                ["رسوم خدمة التوصيل", "مبلغ ضريبة القيمة المضافة", "مبلغ الفاتورة", "إجمالي المبلغ المستحق"],
                ["80", "15", "100 ر.س.", "100 ر.س."]);
            AddSheet(workbookPart, sheets, 2, "تفاصيل سائق التوصيل",
                ["معرّف سائق التوصيل", "الطلبات المُسلمة", "رسوم خدمة التوصيل", "دعم", "غرامة مُخالفة", "إجمالي المبلغ المستحق"],
                ["KEETA-RIDER-1", "-", "80", "10", "-5", "85 ر.س."]);
            AddSheet(workbookPart, sheets, 3, "تفاصيل طلب السائق",
                ["معرّف سائق التوصيل", "معرّف العمل", "المبلغ التفصيلي"],
                ["KEETA-RIDER-1", "ORDER-1", "8.5"]);
            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static byte[] CreateAmazonCompanyWorkbook()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            AddSheet(workbookPart, sheets, 1, "Sheet1",
                ["Row Labels", "Grand Total", "Working Days", "Amount", "Incentive Amount", "EID", "EID OT Amount"],
                ["COMPANY", "10", "20", "100", "0", "0", "0"]);
            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static void AddSheet(
        WorkbookPart workbookPart,
        Sheets sheets,
        uint sheetId,
        string name,
        IReadOnlyList<string> headers,
        IReadOnlyList<string> values)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        worksheetPart.Worksheet = new Worksheet(new SheetData(
            new Row(headers.Select((value, index) => InlineCell($"{ColumnName(index + 1)}1", value))) { RowIndex = 1 },
            new Row(values.Select((value, index) => InlineCell($"{ColumnName(index + 1)}2", value))) { RowIndex = 2 }));
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = sheetId,
            Name = name
        });
    }

    private static string ColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0)
        {
            column--;
            name = (char)('A' + column % 26) + name;
            column /= 26;
        }
        return name;
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
