using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Common;
using Application.Contracts.PlatformImports;
using Application.Service.AccountingStorage;
using Application.Service.Compensation;
using Application.Service.FinancialAccess;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Domain;
using Domain.Entities;
using Domain.Entities.AccountingCore;
using Domain.Entities.AccountingPlatform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Application.Service.PlatformImports;

public class PlatformImportService(
    ApplicationDbcontext dbcontext,
    IFinancialAccessService financialAccessService,
    IPrivateAccountingFileStorage storage) : IPlatformImportService
{
    private const string ParserVersion = "openxml-stream-v1";

    public async Task<Result<PagedResponse<PlatformImportTemplateResponse>>> GetTemplatesAsync(
        PaginationRequest pagination,
        PlatformImportTemplateListFilter filter,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (filter.LegalEntityId <= 0)
            return Result.Failure<PagedResponse<PlatformImportTemplateResponse>>(AccountingPlatformErrors.InvalidRequest);

        var access = await financialAccessService.EnsurePermissionAsync(actorId, filter.LegalEntityId, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<PagedResponse<PlatformImportTemplateResponse>>(access.Error);

        var query = dbcontext.PlatformImportTemplates
            .AsNoTracking()
            .Where(x => x.LegalEntityId == filter.LegalEntityId);

        if (filter.PlatformAccountId.HasValue)
            query = query.Where(x => x.PlatformAccountId == filter.PlatformAccountId.Value);
        if (filter.Status.HasValue)
            query = query.Where(x => x.Status == filter.Status.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToUpperInvariant();
            query = query.Where(x => x.Code.ToUpper().Contains(search) || x.Name.ToUpper().Contains(search) || x.AdapterKey.ToUpper().Contains(search) || x.SchemaFingerprint.ToUpper().Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = pagination.NormalizedPageNumber;
        var pageSize = pagination.NormalizedPageSize;
        var ascending = string.Equals(filter.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        var sortBy = filter.SortBy?.Trim().ToLowerInvariant();
        var ordered = (sortBy, ascending) switch
        {
            ("code", true) => query.OrderBy(x => x.Code).ThenBy(x => x.Id),
            ("code", false) => query.OrderByDescending(x => x.Code).ThenByDescending(x => x.Id),
            ("name", true) => query.OrderBy(x => x.Name).ThenBy(x => x.Id),
            ("name", false) => query.OrderByDescending(x => x.Name).ThenByDescending(x => x.Id),
            ("version", true) => query.OrderBy(x => x.Version).ThenBy(x => x.Id),
            ("version", false) => query.OrderByDescending(x => x.Version).ThenByDescending(x => x.Id),
            ("status", true) => query.OrderBy(x => x.Status).ThenBy(x => x.Id),
            ("status", false) => query.OrderByDescending(x => x.Status).ThenByDescending(x => x.Id),
            ("effectivefrom", true) => query.OrderBy(x => x.EffectiveFrom).ThenBy(x => x.Id),
            ("effectivefrom", false) => query.OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id),
            ("effectiveto", true) => query.OrderBy(x => x.EffectiveTo).ThenBy(x => x.Id),
            ("effectiveto", false) => query.OrderByDescending(x => x.EffectiveTo).ThenByDescending(x => x.Id),
            ("id", true) => query.OrderBy(x => x.Id),
            ("id", false) => query.OrderByDescending(x => x.Id),
            ("createdat", true) => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        };

        var items = await ordered
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new PlatformImportTemplateResponse(x.Id, x.LegalEntityId, x.PlatformAccountId, x.Code, x.Version, x.Name, x.AdapterKey, x.SchemaFingerprint, x.ConfigurationJson, x.Status, x.EffectiveFrom, x.EffectiveTo))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResponse<PlatformImportTemplateResponse>(items, pageNumber, pageSize, totalCount));
    }

    public async Task<Result<PlatformImportTemplateResponse>> GetTemplateAsync(Guid id, string actorId, CancellationToken cancellationToken = default)
    {
        var template = await dbcontext.PlatformImportTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (template is null) return Result.Failure<PlatformImportTemplateResponse>(AccountingPlatformErrors.NotFound);

        var access = await financialAccessService.EnsurePermissionAsync(actorId, template.LegalEntityId, FinancialPermission.View, cancellationToken);
        return access.IsFailure
            ? Result.Failure<PlatformImportTemplateResponse>(access.Error)
            : Result.Success(ToResponse(template));
    }

    public async Task<Result<PlatformImportTemplateResponse>> CreateTemplateAsync(CreatePlatformImportTemplateRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var access = await financialAccessService.EnsurePermissionAsync(actorId, request.LegalEntityId, FinancialPermission.Configure, cancellationToken);
        if (access.IsFailure) return Result.Failure<PlatformImportTemplateResponse>(access.Error);
        if (request.EffectiveTo < request.EffectiveFrom ||
            !await dbcontext.PlatformAccounts.AnyAsync(x => x.Id == request.PlatformAccountId && x.LegalEntityId == request.LegalEntityId && x.IsActive, cancellationToken))
            return Result.Failure<PlatformImportTemplateResponse>(AccountingPlatformErrors.InvalidRequest);

        GenericTemplateConfiguration? configuration;
        try { configuration = JsonSerializer.Deserialize<GenericTemplateConfiguration>(request.ConfigurationJson, JsonOptions); }
        catch (JsonException) { return Result.Failure<PlatformImportTemplateResponse>(AccountingPlatformErrors.InvalidRequest); }
        if (!IsConfigurationValid(request.AdapterKey, configuration)) return Result.Failure<PlatformImportTemplateResponse>(AccountingPlatformErrors.InvalidRequest);

        var code = NormalizeCode(request.Code);
        var version = (await dbcontext.PlatformImportTemplates
            .Where(x => x.LegalEntityId == request.LegalEntityId && x.PlatformAccountId == request.PlatformAccountId && x.Code == code)
            .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var template = new PlatformImportTemplate
        {
            LegalEntityId = request.LegalEntityId, PlatformAccountId = request.PlatformAccountId, Code = code, Version = version,
            Name = request.Name.Trim(), AdapterKey = request.AdapterKey.Trim(), SchemaFingerprint = request.SchemaFingerprint.Trim().ToUpperInvariant(),
            ConfigurationJson = JsonSerializer.Serialize(configuration, JsonOptions), EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, CreatedBy = actorId
        };
        dbcontext.PlatformImportTemplates.Add(template);
        await AppendAuditAsync(template.LegalEntityId, "PlatformImport.TemplateCreated", actorId, new { template.Id, template.Code, template.Version, template.SchemaFingerprint }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToResponse(template));
    }

    public async Task<Result<PlatformImportTemplateResponse>> ActivateTemplateAsync(Guid id, ActivatePlatformImportTemplateRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var template = await dbcontext.PlatformImportTemplates.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (template is null) return Result.Failure<PlatformImportTemplateResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, template.LegalEntityId, FinancialPermission.Configure, cancellationToken);
        if (access.IsFailure) return Result.Failure<PlatformImportTemplateResponse>(access.Error);
        if (template.Status != PlatformTemplateStatus.Draft) return Result.Failure<PlatformImportTemplateResponse>(AccountingPlatformErrors.InvalidState);
        var end = template.EffectiveTo ?? DateOnly.MaxValue;
        if (await dbcontext.PlatformImportTemplates.AnyAsync(x => x.Id != template.Id && x.LegalEntityId == template.LegalEntityId && x.PlatformAccountId == template.PlatformAccountId && x.Code == template.Code && x.Status == PlatformTemplateStatus.Active && x.EffectiveFrom <= end && (x.EffectiveTo == null || x.EffectiveTo >= template.EffectiveFrom), cancellationToken))
            return Result.Failure<PlatformImportTemplateResponse>(AccountingPlatformErrors.PolicyOverlap);
        template.Status = PlatformTemplateStatus.Active;
        template.ActivatedBy = actorId;
        template.ActivatedAt = DateTime.UtcNow;
        await AppendAuditAsync(template.LegalEntityId, "PlatformImport.TemplateActivated", actorId, new { template.Id, template.Code, template.Version, Comment = request.Comment?.Trim() }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToResponse(template));
    }

    public async Task<Result<PlatformImportTemplateResponse>> RetireTemplateAsync(Guid id, RetirePlatformImportTemplateRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var template = await dbcontext.PlatformImportTemplates.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (template is null) return Result.Failure<PlatformImportTemplateResponse>(AccountingPlatformErrors.NotFound);

        var access = await financialAccessService.EnsurePermissionAsync(actorId, template.LegalEntityId, FinancialPermission.Configure, cancellationToken);
        if (access.IsFailure) return Result.Failure<PlatformImportTemplateResponse>(access.Error);
        if (template.Status == PlatformTemplateStatus.Retired)
            return Result.Failure<PlatformImportTemplateResponse>(AccountingPlatformErrors.InvalidState);

        template.Status = PlatformTemplateStatus.Retired;
        await AppendAuditAsync(template.LegalEntityId, "PlatformImport.TemplateRetired", actorId, new { template.Id, template.Code, template.Version, Comment = request.Comment?.Trim() }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return Result.Success(ToResponse(template));
    }

    public Task<Result<PlatformImportBatchResponse>> UploadAsync(
        UploadPlatformImportRequest request,
        string fileName,
        string contentType,
        Stream content,
        string actorId,
        CancellationToken cancellationToken = default)
        => UploadInternalAsync(request, fileName, contentType, content, actorId, null, cancellationToken);

    public Task<Result<PlatformImportBatchResponse>> UploadAmazonAsync(
        DirectPlatformImportRequest request,
        string fileName,
        string contentType,
        Stream content,
        string actorId,
        CancellationToken cancellationToken = default)
        => UploadDirectAsync(request, fileName, contentType, content, actorId, DirectImportProfiles.Amazon, cancellationToken);

    public Task<Result<PlatformImportBatchResponse>> UploadHungerAsync(
        DirectPlatformImportRequest request,
        string fileName,
        string contentType,
        Stream content,
        string actorId,
        CancellationToken cancellationToken = default)
        => UploadDirectAsync(request, fileName, contentType, content, actorId, DirectImportProfiles.Hunger, cancellationToken);

    public Task<Result<PlatformImportBatchResponse>> UploadKeetaPayPerOrderAsync(
        DirectPlatformImportRequest request,
        string fileName,
        string contentType,
        Stream content,
        string actorId,
        CancellationToken cancellationToken = default)
        => UploadDirectAsync(request, fileName, contentType, content, actorId, DirectImportProfiles.KeetaPayPerOrder, cancellationToken);

    public Task<Result<PlatformImportBatchResponse>> UploadKeetaSegmentsAsync(
        DirectPlatformImportRequest request,
        string fileName,
        string contentType,
        Stream content,
        string actorId,
        CancellationToken cancellationToken = default)
        => UploadDirectAsync(request, fileName, contentType, content, actorId, DirectImportProfiles.KeetaSegments, cancellationToken);

    private Task<Result<PlatformImportBatchResponse>> UploadDirectAsync(
        DirectPlatformImportRequest request,
        string fileName,
        string contentType,
        Stream content,
        string actorId,
        DirectImportProfile profile,
        CancellationToken cancellationToken)
        => UploadInternalAsync(
            new UploadPlatformImportRequest(
                request.LegalEntityId,
                request.PlatformAccountId,
                null,
                request.ExternalReference,
                request.PeriodStart,
                request.PeriodEnd,
                request.SourceControlTotal),
            fileName,
            contentType,
            content,
            actorId,
            profile,
            cancellationToken);

    private async Task<Result<PlatformImportBatchResponse>> UploadInternalAsync(
        UploadPlatformImportRequest request,
        string fileName,
        string contentType,
        Stream content,
        string actorId,
        DirectImportProfile? directProfile,
        CancellationToken cancellationToken)
    {
        var access = await financialAccessService.EnsurePermissionAsync(actorId, request.LegalEntityId, FinancialPermission.Prepare, cancellationToken);
        if (access.IsFailure) return Result.Failure<PlatformImportBatchResponse>(access.Error);
        if (request.PeriodEnd < request.PeriodStart || !IsSpreadsheetName(fileName) ||
            !await dbcontext.PlatformAccounts.AnyAsync(x => x.Id == request.PlatformAccountId && x.LegalEntityId == request.LegalEntityId && x.IsActive, cancellationToken))
            return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.InvalidFile);

        StoredAccountingFileResult stored;
        try { stored = await storage.StoreAsync(request.LegalEntityId, content, cancellationToken); }
        catch (Exception ex) when (ex is InvalidDataException or CryptographicException or IOException or InvalidOperationException)
        { return Result.Failure<PlatformImportBatchResponse>(ex is InvalidDataException ? AccountingPlatformErrors.InvalidFile : AccountingPlatformErrors.StorageUnavailable); }

        try
        {
            await using var signatureStream = await storage.OpenReadAsync(stored.StorageLocator, cancellationToken);
            var signature = new byte[4];
            if (await signatureStream.ReadAsync(signature, cancellationToken) < 4 || signature[0] != (byte)'P' || signature[1] != (byte)'K')
            {
                await storage.DeleteAsync(stored.StorageLocator, cancellationToken);
                return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.InvalidFile);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or CryptographicException or IOException)
        {
            await storage.DeleteAsync(stored.StorageLocator, cancellationToken);
            return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.InvalidFile);
        }

        var file = await dbcontext.AccountingStoredFiles.SingleOrDefaultAsync(x => x.LegalEntityId == request.LegalEntityId && x.Sha256 == stored.Sha256, cancellationToken);
        if (file is null)
        {
            file = new AccountingStoredFile
            {
                LegalEntityId = request.LegalEntityId, OriginalFileName = SafeFileName(fileName), ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : contentType.Trim(),
                PlaintextLength = stored.PlaintextLength, Sha256 = stored.Sha256, StorageLocator = stored.StorageLocator, EncryptionKeyId = stored.EncryptionKeyId,
                RetainUntil = DateTime.UtcNow.AddYears(7), CreatedBy = actorId
            };
            dbcontext.AccountingStoredFiles.Add(file);
            await dbcontext.SaveChangesAsync(cancellationToken);
        }
        else if (await dbcontext.PlatformImportBatches.AnyAsync(x => x.LegalEntityId == request.LegalEntityId && x.PlatformAccountId == request.PlatformAccountId && x.StoredFileId == file.Id && x.Status != PlatformImportStatus.Rejected, cancellationToken))
        {
            return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.Duplicate);
        }

        PlatformImportTemplate? requestedTemplate = null;
        if (request.TemplateId.HasValue)
        {
            requestedTemplate = await dbcontext.PlatformImportTemplates.SingleOrDefaultAsync(x => x.Id == request.TemplateId && x.LegalEntityId == request.LegalEntityId && x.PlatformAccountId == request.PlatformAccountId && x.Status == PlatformTemplateStatus.Active, cancellationToken);
            if (requestedTemplate is null) return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.NotFound);
        }

        var batch = new PlatformImportBatch
        {
            LegalEntityId = request.LegalEntityId, PlatformAccountId = request.PlatformAccountId, StoredFileId = file.Id, TemplateId = requestedTemplate?.Id,
            ExternalReference = request.ExternalReference.Trim(), PeriodStart = request.PeriodStart, PeriodEnd = request.PeriodEnd, ParserVersion = ParserVersion,
            SourceControlTotal = request.SourceControlTotal, Status = PlatformImportStatus.Parsing, CreatedBy = actorId
        };
        dbcontext.PlatformImportBatches.Add(batch);
        await AppendAuditAsync(batch.LegalEntityId, "PlatformImport.Received", actorId, new { BatchId = batch.Id, FileId = file.Id, file.Sha256, batch.ExternalReference }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);

        try
        {
            await using var workbookStream = await storage.OpenReadAsync(file.StorageLocator, cancellationToken);
            var selectedProfile = directProfile ?? GetDirectProfile(requestedTemplate?.AdapterKey);
            batch.SchemaFingerprint = await PreserveWorkbookAsync(batch, workbookStream, selectedProfile?.IncludedSheetNames, cancellationToken);

            PlatformImportTemplate? template;
            if (directProfile is not null)
            {
                var mismatch = await GetProfileMismatchAsync(batch.Id, directProfile, cancellationToken);
                if (mismatch is not null)
                {
                    batch.Issues.Add(new PlatformImportIssue
                    {
                        Severity = PlatformImportIssueSeverity.Blocking,
                        Code = "WORKBOOK_PROFILE_MISMATCH",
                        Message = mismatch
                    });
                    batch.Status = PlatformImportStatus.NeedsResolution;
                    template = null;
                }
                else
                {
                    template = await GetOrCreateSystemTemplateAsync(batch, directProfile, actorId, cancellationToken);
                    batch.TemplateId = template.Id;
                    await NormalizeAsync(batch, template, cancellationToken);
                    await RefreshReconciliationStateAsync(batch, cancellationToken);
                }
            }
            else
            {
                template = requestedTemplate ?? await dbcontext.PlatformImportTemplates
                    .AsNoTracking()
                    .Where(x => x.LegalEntityId == batch.LegalEntityId &&
                                x.PlatformAccountId == batch.PlatformAccountId &&
                                x.Status == PlatformTemplateStatus.Active &&
                                x.SchemaFingerprint == batch.SchemaFingerprint &&
                                x.EffectiveFrom <= batch.PeriodEnd &&
                                (x.EffectiveTo == null || x.EffectiveTo >= batch.PeriodStart))
                    .OrderByDescending(x => x.EffectiveFrom)
                    .ThenByDescending(x => x.Version)
                    .ThenBy(x => x.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (template is null || !string.Equals(template.SchemaFingerprint, batch.SchemaFingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    batch.Issues.Add(new PlatformImportIssue { Severity = PlatformImportIssueSeverity.Blocking, Code = "SCHEMA_DRIFT", Message = $"بصمة الملف {batch.SchemaFingerprint} لا تطابق قالبًا نشطًا ومعتمدًا." });
                    batch.Status = PlatformImportStatus.NeedsResolution;
                }
                else
                {
                    batch.TemplateId = template.Id;
                    await NormalizeAsync(batch, template, cancellationToken);
                    await RefreshReconciliationStateAsync(batch, cancellationToken);
                }
            }
            await AppendAuditAsync(batch.LegalEntityId, "PlatformImport.Parsed", actorId, new { batch.Id, batch.SchemaFingerprint, batch.Status }, cancellationToken);
            await dbcontext.SaveChangesAsync(cancellationToken);
            return await GetBatchAsync(batch.Id, actorId, cancellationToken);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or JsonException or IOException)
        {
            batch.Status = PlatformImportStatus.Failed;
            batch.FailureReason = ex.Message[..Math.Min(ex.Message.Length, 2000)];
            await AppendAuditAsync(batch.LegalEntityId, "PlatformImport.Failed", actorId, new { batch.Id, Error = batch.FailureReason }, cancellationToken);
            await dbcontext.SaveChangesAsync(cancellationToken);
            return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.InvalidFile);
        }
    }

    public async Task<Result<PagedResponse<PlatformImportBatchResponse>>> GetBatchesAsync(
        PaginationRequest pagination,
        PlatformImportBatchListFilter filter,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (filter.LegalEntityId <= 0 || (filter.FromDate.HasValue && filter.ToDate.HasValue && filter.ToDate < filter.FromDate))
            return Result.Failure<PagedResponse<PlatformImportBatchResponse>>(AccountingPlatformErrors.InvalidRequest);

        var access = await financialAccessService.EnsurePermissionAsync(actorId, filter.LegalEntityId, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<PagedResponse<PlatformImportBatchResponse>>(access.Error);

        var query = dbcontext.PlatformImportBatches
            .AsNoTracking()
            .Where(x => x.LegalEntityId == filter.LegalEntityId);

        if (filter.PlatformAccountId.HasValue)
            query = query.Where(x => x.PlatformAccountId == filter.PlatformAccountId.Value);
        if (filter.Status.HasValue)
            query = query.Where(x => x.Status == filter.Status.Value);
        if (filter.FromDate.HasValue)
            query = query.Where(x => x.PeriodEnd >= filter.FromDate.Value);
        if (filter.ToDate.HasValue)
            query = query.Where(x => x.PeriodStart <= filter.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToUpperInvariant();
            query = query.Where(x => x.ExternalReference.ToUpper().Contains(search) || x.SchemaFingerprint.ToUpper().Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = pagination.NormalizedPageNumber;
        var pageSize = pagination.NormalizedPageSize;
        var ascending = string.Equals(filter.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        var sortBy = filter.SortBy?.Trim().ToLowerInvariant();
        var ordered = (sortBy, ascending) switch
        {
            ("externalreference", true) => query.OrderBy(x => x.ExternalReference).ThenBy(x => x.Id),
            ("externalreference", false) => query.OrderByDescending(x => x.ExternalReference).ThenByDescending(x => x.Id),
            ("periodstart", true) => query.OrderBy(x => x.PeriodStart).ThenBy(x => x.Id),
            ("periodstart", false) => query.OrderByDescending(x => x.PeriodStart).ThenByDescending(x => x.Id),
            ("periodend", true) => query.OrderBy(x => x.PeriodEnd).ThenBy(x => x.Id),
            ("periodend", false) => query.OrderByDescending(x => x.PeriodEnd).ThenByDescending(x => x.Id),
            ("status", true) => query.OrderBy(x => x.Status).ThenBy(x => x.Id),
            ("status", false) => query.OrderByDescending(x => x.Status).ThenByDescending(x => x.Id),
            ("id", true) => query.OrderBy(x => x.Id),
            ("id", false) => query.OrderByDescending(x => x.Id),
            ("createdat", true) => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        };

        var projections = await ordered
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new PlatformImportBatchProjection(
                x.Id, x.LegalEntityId, x.PlatformAccountId, x.StoredFileId, x.TemplateId, x.Template == null ? null : x.Template.AdapterKey, x.ExternalReference, x.PeriodStart, x.PeriodEnd, x.ParserVersion, x.SchemaFingerprint, x.Status,
                x.SourceControlTotal, x.NormalizedControlTotal, x.Sheets.Count, x.Sheets.SelectMany(s => s.Rows).Count(), x.Sheets.SelectMany(s => s.Rows).SelectMany(r => r.Cells).Count(), x.Facts.Count,
                x.Issues.Count(i => i.Status == PlatformImportIssueStatus.Open && i.Severity == PlatformImportIssueSeverity.Blocking), x.RowVersion))
            .ToListAsync(cancellationToken);
        var items = projections.Select(ToResponse).ToArray();

        return Result.Success(new PagedResponse<PlatformImportBatchResponse>(items, pageNumber, pageSize, totalCount));
    }

    public async Task<Result<PlatformImportBatchResponse>> GetBatchAsync(Guid id, string actorId, CancellationToken cancellationToken = default)
    {
        var entityId = await dbcontext.PlatformImportBatches.AsNoTracking().Where(x => x.Id == id).Select(x => (int?)x.LegalEntityId).SingleOrDefaultAsync(cancellationToken);
        if (entityId is null) return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, entityId.Value, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<PlatformImportBatchResponse>(access.Error);
        var projection = await dbcontext.PlatformImportBatches
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new PlatformImportBatchProjection(
                x.Id, x.LegalEntityId, x.PlatformAccountId, x.StoredFileId, x.TemplateId, x.Template == null ? null : x.Template.AdapterKey, x.ExternalReference, x.PeriodStart, x.PeriodEnd, x.ParserVersion, x.SchemaFingerprint, x.Status,
                x.SourceControlTotal, x.NormalizedControlTotal, x.Sheets.Count, x.Sheets.SelectMany(s => s.Rows).Count(), x.Sheets.SelectMany(s => s.Rows).SelectMany(r => r.Cells).Count(), x.Facts.Count,
                x.Issues.Count(i => i.Status == PlatformImportIssueStatus.Open && i.Severity == PlatformImportIssueSeverity.Blocking), x.RowVersion))
            .SingleAsync(cancellationToken);
        return Result.Success(ToResponse(projection));
    }

    public async Task<Result<PagedResponse<PlatformNormalizedFactResponse>>> GetFactsAsync(
        Guid batchId,
        PaginationRequest pagination,
        PlatformNormalizedFactListFilter filter,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var entityId = await dbcontext.PlatformImportBatches
            .AsNoTracking()
            .Where(x => x.Id == batchId)
            .Select(x => (int?)x.LegalEntityId)
            .SingleOrDefaultAsync(cancellationToken);
        if (entityId is null) return Result.Failure<PagedResponse<PlatformNormalizedFactResponse>>(AccountingPlatformErrors.NotFound);

        var access = await financialAccessService.EnsurePermissionAsync(actorId, entityId.Value, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<PagedResponse<PlatformNormalizedFactResponse>>(access.Error);

        var query = dbcontext.PlatformNormalizedFacts
            .AsNoTracking()
            .Where(x => x.PlatformImportBatchId == batchId);
        if (filter.Category.HasValue)
            query = query.Where(x => x.Category == filter.Category.Value);
        if (!string.IsNullOrWhiteSpace(filter.MetricCode))
        {
            var metricCode = NormalizeCode(filter.MetricCode);
            query = query.Where(x => x.MetricCode.ToUpper() == metricCode);
        }
        if (filter.IsResolved.HasValue)
            query = query.Where(x => x.IsResolved == filter.IsResolved.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = pagination.NormalizedPageNumber;
        var pageSize = pagination.NormalizedPageSize;
        var ascending = string.Equals(filter.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        var sortBy = filter.SortBy?.Trim().ToLowerInvariant();
        var ordered = (sortBy, ascending) switch
        {
            ("factdate", true) => query.OrderBy(x => x.FactDate).ThenBy(x => x.Id),
            ("factdate", false) => query.OrderByDescending(x => x.FactDate).ThenByDescending(x => x.Id),
            ("category", true) => query.OrderBy(x => x.Category).ThenBy(x => x.Id),
            ("category", false) => query.OrderByDescending(x => x.Category).ThenByDescending(x => x.Id),
            ("metriccode", true) => query.OrderBy(x => x.MetricCode).ThenBy(x => x.Id),
            ("metriccode", false) => query.OrderByDescending(x => x.MetricCode).ThenByDescending(x => x.Id),
            ("externalworkerid", true) => query.OrderBy(x => x.ExternalWorkerId).ThenBy(x => x.Id),
            ("externalworkerid", false) => query.OrderByDescending(x => x.ExternalWorkerId).ThenByDescending(x => x.Id),
            ("rideriqamano", true) => query.OrderBy(x => x.RiderIqamaNo).ThenBy(x => x.Id),
            ("rideriqamano", false) => query.OrderByDescending(x => x.RiderIqamaNo).ThenByDescending(x => x.Id),
            ("id", true) => query.OrderBy(x => x.Id),
            _ => query.OrderByDescending(x => x.Id)
        };

        var items = await ordered
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new PlatformNormalizedFactResponse(
                x.Id, x.PlatformImportBatchId, x.LegalEntityId, x.PlatformAccountId, x.WorkerCategory, x.SourceRawRowId, x.RiderIqamaNo,
                x.ExternalWorkerId, x.FactDate, x.Category, x.MetricCode, x.NumericValue, x.TextValue, x.BooleanValue, x.CurrencyCode, x.IsResolved, x.LineageJson,
                x.Override == null ? null : new PlatformFactOverrideResponse(x.Override.Id, x.Override.BooleanValue, x.Override.Reason, x.Override.CreatedBy, x.Override.CreatedAt)))
            .ToListAsync(cancellationToken);

        var itemsWithRiders = await AttachRiderNamesAsync(items, cancellationToken);
        return Result.Success(new PagedResponse<PlatformNormalizedFactResponse>(itemsWithRiders, pageNumber, pageSize, totalCount));
    }

    public async Task<Result<PagedResponse<PlatformImportRawRowResponse>>> GetRowsAsync(
        Guid batchId,
        PaginationRequest pagination,
        PlatformImportRawRowListFilter filter,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var entityId = await dbcontext.PlatformImportBatches
            .AsNoTracking()
            .Where(x => x.Id == batchId)
            .Select(x => (int?)x.LegalEntityId)
            .SingleOrDefaultAsync(cancellationToken);
        if (entityId is null) return Result.Failure<PagedResponse<PlatformImportRawRowResponse>>(AccountingPlatformErrors.NotFound);

        var access = await financialAccessService.EnsurePermissionAsync(actorId, entityId.Value, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<PagedResponse<PlatformImportRawRowResponse>>(access.Error);

        var query = dbcontext.PlatformImportRawRows
            .AsNoTracking()
            .Where(x => x.PlatformImportSheet.PlatformImportBatchId == batchId);
        if (filter.SheetId.HasValue)
            query = query.Where(x => x.PlatformImportSheetId == filter.SheetId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToUpperInvariant();
            query = query.Where(x => x.RowHash.ToUpper().Contains(search) || x.Cells.Any(cell =>
                (cell.DisplayValue != null && cell.DisplayValue.ToUpper().Contains(search)) ||
                (cell.RawValue != null && cell.RawValue.ToUpper().Contains(search))));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = pagination.NormalizedPageNumber;
        var pageSize = pagination.NormalizedPageSize;
        var ascending = string.Equals(filter.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        var sortBy = filter.SortBy?.Trim().ToLowerInvariant();
        var ordered = (sortBy, ascending) switch
        {
            ("sheet", true) => query.OrderBy(x => x.PlatformImportSheet.SheetIndex).ThenBy(x => x.RowNumber).ThenBy(x => x.Id),
            ("sheet", false) => query.OrderByDescending(x => x.PlatformImportSheet.SheetIndex).ThenByDescending(x => x.RowNumber).ThenByDescending(x => x.Id),
            ("rownumber", true) => query.OrderBy(x => x.RowNumber).ThenBy(x => x.Id),
            ("rownumber", false) => query.OrderByDescending(x => x.RowNumber).ThenByDescending(x => x.Id),
            ("rowhash", true) => query.OrderBy(x => x.RowHash).ThenBy(x => x.Id),
            ("rowhash", false) => query.OrderByDescending(x => x.RowHash).ThenByDescending(x => x.Id),
            ("id", true) => query.OrderBy(x => x.Id),
            _ => query.OrderByDescending(x => x.Id)
        };

        var rows = await ordered
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new PlatformImportRawRowProjection(
                x.Id,
                x.PlatformImportSheetId,
                x.PlatformImportSheet.SheetIndex,
                x.PlatformImportSheet.Name,
                x.RowNumber,
                x.RowHash))
            .ToListAsync(cancellationToken);

        var rowIds = rows.Select(x => x.Id).ToArray();
        var cells = rowIds.Length == 0
            ? []
            : await dbcontext.PlatformImportRawCells
                .AsNoTracking()
                .Where(x => rowIds.Contains(x.PlatformImportRawRowId))
                .OrderBy(x => x.PlatformImportRawRowId)
                .ThenBy(x => x.ColumnNumber)
                .ThenBy(x => x.Id)
                .Select(x => new PlatformImportRawCellProjection(
                    x.PlatformImportRawRowId,
                    x.Id,
                    x.ColumnNumber,
                    x.CellReference,
                    x.RawValue,
                    x.DisplayValue,
                    x.Formula,
                    x.DataType))
                .ToArrayAsync(cancellationToken);
        var cellsByRow = cells.ToLookup(x => x.PlatformImportRawRowId);
        var ridersByRow = await GetRidersBySourceRowAsync(batchId, rowIds, cancellationToken);
        var items = rows
            .Select(row => new PlatformImportRawRowResponse(
                row.Id,
                row.SheetId,
                row.SheetIndex,
                row.SheetName,
                row.RowNumber,
                row.RowHash,
                cellsByRow[row.Id]
                    .Select(cell => new PlatformImportRawCellResponse(cell.Id, cell.ColumnNumber, cell.CellReference, cell.RawValue, cell.DisplayValue, cell.Formula, cell.DataType))
                    .ToArray()) with
            {
                RiderIqamaNo = ridersByRow.GetValueOrDefault(row.Id)?.IqamaNo,
                RiderNameAr = ridersByRow.GetValueOrDefault(row.Id)?.NameAr
            })
            .ToArray();

        return Result.Success(new PagedResponse<PlatformImportRawRowResponse>(items, pageNumber, pageSize, totalCount));
    }

    public async Task<Result<IReadOnlyCollection<PlatformImportIssueResponse>>> GetIssuesAsync(Guid batchId, string actorId, CancellationToken cancellationToken = default)
    {
        var entityId = await dbcontext.PlatformImportBatches.AsNoTracking().Where(x => x.Id == batchId).Select(x => (int?)x.LegalEntityId).SingleOrDefaultAsync(cancellationToken);
        if (entityId is null) return Result.Failure<IReadOnlyCollection<PlatformImportIssueResponse>>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, entityId.Value, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<IReadOnlyCollection<PlatformImportIssueResponse>>(access.Error);
        var issues = await dbcontext.PlatformImportIssues.AsNoTracking().Where(x => x.PlatformImportBatchId == batchId).OrderByDescending(x => x.Severity).ThenBy(x => x.Id)
            .Select(x => new PlatformImportIssueResponse(x.Id, x.Severity, x.Status, x.Code, x.Message, x.Resolution, x.SourceRawRowId)).ToListAsync(cancellationToken);
        var issueRowIds = issues.Where(x => x.SourceRawRowId.HasValue).Select(x => x.SourceRawRowId!.Value).ToArray();
        var companyRowIds = issueRowIds.Length == 0
            ? []
            : (await dbcontext.PlatformNormalizedFacts.AsNoTracking()
                .Where(x => x.PlatformImportBatchId == batchId && x.SourceRawRowId.HasValue && issueRowIds.Contains(x.SourceRawRowId.Value) &&
                    (x.WorkerCategory == "Company" || x.ExternalWorkerId == "COMPANY"))
                .Select(x => x.SourceRawRowId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken)).ToHashSet();
        issues = issues.Select(x => x with { IsCompany = x.SourceRawRowId.HasValue && companyRowIds.Contains(x.SourceRawRowId.Value) }).ToList();
        var issuesWithRiders = await AttachIssueRidersAsync(batchId, issues, cancellationToken);
        return Result.Success<IReadOnlyCollection<PlatformImportIssueResponse>>(issuesWithRiders);
    }

    public async Task<Result<PlatformImportIssueResponse>> ResolveIssueAsync(long issueId, ResolvePlatformImportIssueRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var issue = await dbcontext.PlatformImportIssues.Include(x => x.PlatformImportBatch).SingleOrDefaultAsync(x => x.Id == issueId, cancellationToken);
        if (issue is null) return Result.Failure<PlatformImportIssueResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, issue.PlatformImportBatch.LegalEntityId, FinancialPermission.Approve, cancellationToken);
        if (access.IsFailure) return Result.Failure<PlatformImportIssueResponse>(access.Error);
        if (issue.Status != PlatformImportIssueStatus.Open || issue.PlatformImportBatch.Status is PlatformImportStatus.Approved or PlatformImportStatus.Rejected or PlatformImportStatus.Superseded)
            return Result.Failure<PlatformImportIssueResponse>(AccountingPlatformErrors.InvalidState);
        if (string.IsNullOrWhiteSpace(request.Resolution) || request.Resolution.Trim().Length > 2000)
            return Result.Failure<PlatformImportIssueResponse>(AccountingPlatformErrors.InvalidRequest);
        issue.Status = request.Waive ? PlatformImportIssueStatus.Waived : PlatformImportIssueStatus.Resolved;
        issue.Resolution = request.Resolution.Trim();
        issue.ResolvedBy = actorId;
        issue.ResolvedAt = DateTime.UtcNow;
        await dbcontext.SaveChangesAsync(cancellationToken);
        await RefreshReconciliationStateAsync(issue.PlatformImportBatch, cancellationToken);
        await AppendAuditAsync(issue.PlatformImportBatch.LegalEntityId, "PlatformImport.IssueResolved", actorId, new { issue.Id, issue.Code, issue.Status, issue.Resolution }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);
        var response = await AttachIssueRidersAsync(issue.PlatformImportBatchId, [ToResponse(issue)], cancellationToken);
        return Result.Success(response.Single());
    }

    public async Task<Result<PlatformImportBatchResponse>> RemapWorkerAsync(Guid batchId, RemapPlatformWorkerRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var batch = await dbcontext.PlatformImportBatches.Include(x => x.Issues).SingleOrDefaultAsync(x => x.Id == batchId, cancellationToken);
        if (batch is null) return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, batch.LegalEntityId, FinancialPermission.Approve, cancellationToken);
        if (access.IsFailure) return Result.Failure<PlatformImportBatchResponse>(access.Error);
        if (batch.Status is PlatformImportStatus.Approved or PlatformImportStatus.Rejected or PlatformImportStatus.Superseded or PlatformImportStatus.Failed)
            return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.InvalidState);
        if (request.EffectiveTo < request.EffectiveFrom || !await dbcontext.Employees.AnyAsync(x => x.IqamaNo == request.RiderIqamaNo && !x.IsDeleted, cancellationToken))
            return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.InvalidRequest);

        var externalId = request.ExternalWorkerId.Trim();
        if (IsCompanySummaryWorkerId(externalId))
            return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.InvalidRequest);
        var end = request.EffectiveTo ?? DateOnly.MaxValue;
        var overlaps = await dbcontext.PlatformWorkerIdentities.AnyAsync(x =>
            x.LegalEntityId == batch.LegalEntityId && x.PlatformAccountId == batch.PlatformAccountId && x.ExternalWorkerId == externalId &&
            x.EffectiveFrom <= end && (x.EffectiveTo == null || x.EffectiveTo >= request.EffectiveFrom), cancellationToken);
        if (overlaps) return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.PolicyOverlap);

        dbcontext.PlatformWorkerIdentities.Add(new PlatformWorkerIdentity
        {
            LegalEntityId = batch.LegalEntityId, PlatformAccountId = batch.PlatformAccountId, ExternalWorkerId = externalId,
            RiderIqamaNo = request.RiderIqamaNo, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo,
            IsSubstitution = true, Reason = request.Reason.Trim(), CreatedBy = actorId
        });

        var affectedFacts = await dbcontext.PlatformNormalizedFacts.Where(x =>
            x.PlatformImportBatchId == batch.Id && x.ExternalWorkerId == externalId && x.FactDate >= request.EffectiveFrom && x.FactDate <= end).ToListAsync(cancellationToken);
        if (affectedFacts.Count == 0) return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.NotFound);
        foreach (var fact in affectedFacts)
        {
            fact.RiderIqamaNo = request.RiderIqamaNo;
            fact.IsResolved = true;
            fact.LineageJson = AddResolutionToLineage(fact.LineageJson, actorId, request.Reason, request.RiderIqamaNo);
        }

        var affectedRows = affectedFacts.Where(x => x.SourceRawRowId.HasValue).Select(x => x.SourceRawRowId!.Value).Distinct().ToArray();
        var identityIssues = batch.Issues.Where(x => x.Status == PlatformImportIssueStatus.Open && affectedRows.Contains(x.SourceRawRowId ?? 0) && x.Code is "IDENTITY_MISSING" or "IDENTITY_AMBIGUOUS").ToArray();
        foreach (var issue in identityIssues)
        {
            issue.Status = PlatformImportIssueStatus.Resolved;
            issue.Resolution = $"تم ربط المندوب الخارجي {externalId} بالمندوب ذي الإقامة {request.RiderIqamaNo} من {request.EffectiveFrom:yyyy-MM-dd} إلى {(request.EffectiveTo?.ToString("yyyy-MM-dd") ?? "مفتوح")}: {request.Reason.Trim()}";
            issue.ResolvedBy = actorId;
            issue.ResolvedAt = DateTime.UtcNow;
        }

        await dbcontext.SaveChangesAsync(cancellationToken);
        await RefreshReconciliationStateAsync(batch, cancellationToken);
        await AppendAuditAsync(batch.LegalEntityId, "PlatformImport.WorkerRemapped", actorId, new { batch.Id, ExternalWorkerId = externalId, request.RiderIqamaNo, request.EffectiveFrom, request.EffectiveTo, request.Reason, AffectedFacts = affectedFacts.Count }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);
        return await GetBatchAsync(batch.Id, actorId, cancellationToken);
    }

    public async Task<Result<PlatformNormalizedFactResponse>> OverrideValidityAsync(long factId, OverridePlatformValidityRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var fact = await dbcontext.PlatformNormalizedFacts.Include(x => x.PlatformImportBatch).Include(x => x.Override).SingleOrDefaultAsync(x => x.Id == factId, cancellationToken);
        if (fact is null) return Result.Failure<PlatformNormalizedFactResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, fact.LegalEntityId, FinancialPermission.Approve, cancellationToken);
        if (access.IsFailure) return Result.Failure<PlatformNormalizedFactResponse>(access.Error);
        if (!string.Equals(fact.MetricCode, "VALIDITY", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            return Result.Failure<PlatformNormalizedFactResponse>(AccountingPlatformErrors.InvalidRequest);
        if (fact.PlatformImportBatch.Status is PlatformImportStatus.Rejected or PlatformImportStatus.Superseded or PlatformImportStatus.Failed)
            return Result.Failure<PlatformNormalizedFactResponse>(AccountingPlatformErrors.InvalidState);
        if (fact.Override is not null) return Result.Failure<PlatformNormalizedFactResponse>(AccountingPlatformErrors.InvalidState);

        var factOverride = new PlatformFactOverride
        {
            PlatformNormalizedFactId = fact.Id,
            BooleanValue = request.IsValid,
            Reason = request.Reason.Trim(),
            CreatedBy = actorId
        };
        fact.Override = factOverride;
        dbcontext.PlatformFactOverrides.Add(factOverride);
        await AppendAuditAsync(fact.LegalEntityId, "PlatformImport.ValidityOverridden", actorId, new { fact.Id, fact.PlatformImportBatchId, fact.RiderIqamaNo, fact.ExternalWorkerId, request.IsValid, Reason = request.Reason.Trim() }, cancellationToken);
        await dbcontext.SaveChangesAsync(cancellationToken);
        var response = await AttachRiderNamesAsync([ToResponse(fact)], cancellationToken);
        return Result.Success(response.Single());
    }

    public async Task<Result<PlatformImportBatchResponse>> ReprocessAsync(Guid batchId, ReprocessPlatformImportRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var batch = await dbcontext.PlatformImportBatches
            .Include(x => x.StoredFile)
            .SingleOrDefaultAsync(x => x.Id == batchId, cancellationToken);
        if (batch is null) return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.NotFound);

        var access = await financialAccessService.EnsurePermissionAsync(actorId, batch.LegalEntityId, FinancialPermission.Prepare, cancellationToken);
        if (access.IsFailure) return Result.Failure<PlatformImportBatchResponse>(access.Error);
        if (batch.Status is PlatformImportStatus.Approved or PlatformImportStatus.Superseded or PlatformImportStatus.Parsing ||
            batch.StoredFile.Status != StoredFileStatus.Active)
            return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.InvalidState);
        if (!MatchesRowVersion(request.RowVersion, batch.RowVersion))
            return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.ConcurrencyConflict);

        var template = await dbcontext.PlatformImportTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.TemplateId &&
                                       x.LegalEntityId == batch.LegalEntityId &&
                                       x.PlatformAccountId == batch.PlatformAccountId &&
                                       x.Status != PlatformTemplateStatus.Retired,
                cancellationToken);
        if (template is null) return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.NotFound);
        if (!string.IsNullOrWhiteSpace(batch.SchemaFingerprint) &&
            !string.Equals(batch.SchemaFingerprint, template.SchemaFingerprint, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.SchemaDrift);

        IDbContextTransaction? transaction = null;
        try
        {
            if (dbcontext.Database.IsRelational())
                transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);

            await ClearBatchImportDataAsync(batch.Id, cancellationToken);
            batch.TemplateId = template.Id;
            batch.ParserVersion = ParserVersion;
            batch.Status = PlatformImportStatus.Parsing;
            batch.NormalizedControlTotal = null;
            batch.FailureReason = null;
            batch.ReviewedBy = null;
            batch.ReviewedAt = null;

            await using var workbookStream = await storage.OpenReadAsync(batch.StoredFile.StorageLocator, cancellationToken);
            var directProfile = GetDirectProfile(template.AdapterKey);
            batch.SchemaFingerprint = await PreserveWorkbookAsync(batch, workbookStream, directProfile?.IncludedSheetNames, cancellationToken);
            if (!string.Equals(batch.SchemaFingerprint, template.SchemaFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                batch.Issues.Add(new PlatformImportIssue
                {
                    Severity = PlatformImportIssueSeverity.Blocking,
                    Code = "SCHEMA_DRIFT",
                    Message = $"Workbook fingerprint {batch.SchemaFingerprint} does not match template fingerprint {template.SchemaFingerprint}."
                });
                batch.Status = PlatformImportStatus.NeedsResolution;
            }
            else
            {
                await NormalizeAsync(batch, template, cancellationToken);
                await RefreshReconciliationStateAsync(batch, cancellationToken);
            }

            await AppendAuditAsync(batch.LegalEntityId, "PlatformImport.Reprocessed", actorId, new { batch.Id, TemplateId = template.Id, batch.SchemaFingerprint, batch.Status }, cancellationToken);
            await dbcontext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.ConcurrencyConflict);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or JsonException or IOException or CryptographicException or InvalidOperationException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<PlatformImportBatchResponse>(ex is IOException or CryptographicException or InvalidOperationException
                ? AccountingPlatformErrors.StorageUnavailable
                : AccountingPlatformErrors.InvalidFile);
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }

        return await GetBatchAsync(batch.Id, actorId, cancellationToken);
    }

    public async Task<Result<PlatformImportBatchResponse>> SupersedeAsync(Guid batchId, SupersedePlatformImportBatchRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        if (request.ReplacementBatchId == Guid.Empty || request.ReplacementBatchId == batchId || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.InvalidRequest);

        var batches = await dbcontext.PlatformImportBatches
            .Where(x => x.Id == batchId || x.Id == request.ReplacementBatchId)
            .ToListAsync(cancellationToken);
        var batch = batches.SingleOrDefault(x => x.Id == batchId);
        var replacement = batches.SingleOrDefault(x => x.Id == request.ReplacementBatchId);
        if (batch is null || replacement is null) return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.NotFound);

        var access = await financialAccessService.EnsurePermissionAsync(actorId, batch.LegalEntityId, FinancialPermission.Approve, cancellationToken);
        if (access.IsFailure) return Result.Failure<PlatformImportBatchResponse>(access.Error);
        if (!MatchesRowVersion(request.RowVersion, batch.RowVersion))
            return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.ConcurrencyConflict);
        if (batch.Status == PlatformImportStatus.Superseded || batch.SupersededByBatchId.HasValue ||
            replacement.LegalEntityId != batch.LegalEntityId || replacement.PlatformAccountId != batch.PlatformAccountId ||
            replacement.Status is PlatformImportStatus.Rejected or PlatformImportStatus.Superseded or PlatformImportStatus.Failed ||
            replacement.SupersedesBatchId.HasValue)
            return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.InvalidState);

        batch.Status = PlatformImportStatus.Superseded;
        batch.SupersededByBatchId = replacement.Id;
        batch.ReviewedBy = actorId;
        batch.ReviewedAt = DateTime.UtcNow;
        replacement.SupersedesBatchId = batch.Id;
        await AppendAuditAsync(batch.LegalEntityId, "PlatformImport.Superseded", actorId, new { batch.Id, ReplacementBatchId = replacement.Id, Reason = request.Reason.Trim() }, cancellationToken);
        try
        {
            await dbcontext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.ConcurrencyConflict);
        }

        return await GetBatchAsync(batch.Id, actorId, cancellationToken);
    }

    public Task<Result<PlatformImportBatchResponse>> ApproveAsync(Guid id, ReviewPlatformImportRequest request, string actorId, CancellationToken cancellationToken = default)
        => ReviewAsync(id, true, request.Comment, actorId, cancellationToken);

    public Task<Result<PlatformImportBatchResponse>> RejectAsync(Guid id, ReviewPlatformImportRequest request, string actorId, CancellationToken cancellationToken = default)
        => ReviewAsync(id, false, request.Comment, actorId, cancellationToken);

    public async Task<Result<AccountingFileDownloadResponse>> DownloadFileAsync(Guid batchId, string actorId, CancellationToken cancellationToken = default)
    {
        var batch = await dbcontext.PlatformImportBatches
            .AsNoTracking()
            .Where(x => x.Id == batchId)
            .Select(x => new { x.LegalEntityId, x.StoredFileId })
            .SingleOrDefaultAsync(cancellationToken);
        if (batch is null) return Result.Failure<AccountingFileDownloadResponse>(AccountingPlatformErrors.NotFound);

        var access = await financialAccessService.EnsurePermissionAsync(actorId, batch.LegalEntityId, FinancialPermission.View, cancellationToken);
        if (access.IsFailure) return Result.Failure<AccountingFileDownloadResponse>(access.Error);
        var file = await dbcontext.AccountingStoredFiles
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == batch.StoredFileId && x.Status == StoredFileStatus.Active, cancellationToken);
        if (file is null) return Result.Failure<AccountingFileDownloadResponse>(AccountingPlatformErrors.NotFound);
        try { return Result.Success(new AccountingFileDownloadResponse(await storage.OpenReadAsync(file.StorageLocator, cancellationToken), file.ContentType, file.OriginalFileName)); }
        catch (Exception ex) when (ex is IOException or CryptographicException or InvalidDataException or InvalidOperationException)
        { return Result.Failure<AccountingFileDownloadResponse>(AccountingPlatformErrors.StorageUnavailable); }
    }

    private async Task<Result<PlatformImportBatchResponse>> ReviewAsync(Guid id, bool approve, string? comment, string actorId, CancellationToken ct)
    {
        var batch = await dbcontext.PlatformImportBatches.Include(x => x.Issues).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (batch is null) return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.NotFound);
        var access = await financialAccessService.EnsurePermissionAsync(actorId, batch.LegalEntityId, FinancialPermission.Approve, ct);
        if (access.IsFailure) return Result.Failure<PlatformImportBatchResponse>(access.Error);
        if (approve)
        {
            if (batch.Status != PlatformImportStatus.Reconciled || batch.Issues.Any(x => x.Status == PlatformImportIssueStatus.Open && x.Severity == PlatformImportIssueSeverity.Blocking))
                return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.BlockingImportIssues);
            if (!batch.TemplateId.HasValue || !await dbcontext.PlatformImportTemplates.AsNoTracking().AnyAsync(x => x.Id == batch.TemplateId && x.Status == PlatformTemplateStatus.Active, ct))
                return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.InvalidState);
            batch.Status = PlatformImportStatus.Approved;
        }
        else
        {
            if (batch.Status is PlatformImportStatus.Approved or PlatformImportStatus.Superseded) return Result.Failure<PlatformImportBatchResponse>(AccountingPlatformErrors.InvalidState);
            batch.Status = PlatformImportStatus.Rejected;
        }
        batch.ReviewedBy = actorId;
        batch.ReviewedAt = DateTime.UtcNow;
        await AppendAuditAsync(batch.LegalEntityId, approve ? "PlatformImport.Approved" : "PlatformImport.Rejected", actorId, new { batch.Id, Comment = comment?.Trim() }, ct);
        await dbcontext.SaveChangesAsync(ct);
        return await GetBatchAsync(id, actorId, ct);
    }

    private async Task<string> PreserveWorkbookAsync(
        PlatformImportBatch batch,
        Stream content,
        IReadOnlySet<string>? includedSheetNames,
        CancellationToken ct)
    {
        using var document = SpreadsheetDocument.Open(content, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidDataException("Workbook part is missing.");
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable.Elements<SharedStringItem>().Select(x => x.InnerText).ToArray() ?? [];
        var fingerprintParts = new List<string>();
        var sheetIndex = 0;
        var preservedSheetCount = 0;
        foreach (var sheet in workbookPart.Workbook.Sheets?.Elements<Sheet>() ?? [])
        {
            sheetIndex++;
            var sheetName = sheet.Name?.Value ?? $"Sheet{sheetIndex}";
            if (includedSheetNames is not null && !includedSheetNames.Contains(sheetName)) continue;

            preservedSheetCount++;
            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
            var state = sheet.State?.Value;
            var sheetEntity = new PlatformImportSheet { PlatformImportBatchId = batch.Id, SheetIndex = sheetIndex, Name = sheetName, IsHidden = state == SheetStateValues.Hidden || state == SheetStateValues.VeryHidden };
            dbcontext.PlatformImportSheets.Add(sheetEntity);
            await dbcontext.SaveChangesAsync(ct);
            fingerprintParts.Add($"S:{sheetEntity.Name.Trim().ToUpperInvariant()}:{sheetEntity.IsHidden}");
            var firstNonEmptyCaptured = false;
            var buffered = 0;
            using var reader = OpenXmlReader.Create(worksheetPart);
            while (reader.Read())
            {
                if (reader.ElementType != typeof(Row) || !reader.IsStartElement) continue;
                if (reader.LoadCurrentElement() is not Row rowElement) continue;
                var cells = rowElement.Elements<Cell>().Select(cell => ToRawCell(cell, sharedStrings)).Where(x => x is not null).Cast<PlatformImportRawCell>().ToList();
                if (cells.Count == 0) continue;
                var rowNumber = checked((int)(rowElement.RowIndex?.Value ?? 0));
                var rowText = string.Join('|', cells.OrderBy(x => x.ColumnNumber).Select(x => $"{x.ColumnNumber}:{x.DisplayValue}"));
                var rawRow = new PlatformImportRawRow { PlatformImportSheetId = sheetEntity.Id, RowNumber = rowNumber, RowHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rowText))), Cells = cells };
                dbcontext.PlatformImportRawRows.Add(rawRow);
                sheetEntity.MaxRowNumber = Math.Max(sheetEntity.MaxRowNumber, rowNumber);
                sheetEntity.MaxColumnNumber = Math.Max(sheetEntity.MaxColumnNumber, cells.Max(x => x.ColumnNumber));
                if (!firstNonEmptyCaptured)
                {
                    fingerprintParts.Add($"H:{string.Join('|', cells.OrderBy(x => x.ColumnNumber).Select(x => NormalizeHeader(x.DisplayValue)))}");
                    firstNonEmptyCaptured = true;
                }
                if (++buffered >= 500)
                {
                    await dbcontext.SaveChangesAsync(ct);
                    buffered = 0;
                }
            }
            await dbcontext.SaveChangesAsync(ct);
        }
        if (preservedSheetCount == 0) throw new InvalidDataException("Workbook does not contain the required sheets.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', fingerprintParts))));
    }

    private async Task<string?> GetProfileMismatchAsync(
        Guid batchId,
        DirectImportProfile profile,
        CancellationToken cancellationToken)
    {
        var configuration = ExactConfigurations[profile.AdapterKey];
        var sheets = await dbcontext.PlatformImportSheets
            .AsNoTracking()
            .Where(x => x.PlatformImportBatchId == batchId)
            .ToListAsync(cancellationToken);

        foreach (var requiredSheetName in configuration.SheetNames)
        {
            var sheet = sheets.SingleOrDefault(x => string.Equals(x.Name, requiredSheetName, StringComparison.Ordinal));
            if (sheet is null) return $"This is not a {profile.DisplayName} workbook. Required sheet '{requiredSheetName}' is missing.";

            var header = await dbcontext.PlatformImportRawRows
                .AsNoTracking()
                .Include(x => x.Cells)
                .SingleOrDefaultAsync(x => x.PlatformImportSheetId == sheet.Id && x.RowNumber == configuration.HeaderRow, cancellationToken);
            if (header is null) return $"This is not a {profile.DisplayName} workbook. Header row {configuration.HeaderRow} is missing from '{requiredSheetName}'.";

            var headers = header.Cells
                .Select(x => NormalizeHeader(x.DisplayValue))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var requiredHeaders = configuration.Columns.Select(x => x.Header).Append(configuration.ExternalWorkerIdHeader);
            var missingHeaders = requiredHeaders
                .Where(x => !headers.Contains(NormalizeHeader(x)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (missingHeaders.Length > 0)
                return $"This is not a {profile.DisplayName} workbook. Missing columns in '{requiredSheetName}': {string.Join(", ", missingHeaders)}.";
        }

        if (ExactCompanyConfigurations.TryGetValue(profile.AdapterKey, out var companyConfiguration))
        {
            var companySheet = sheets.SingleOrDefault(x => string.Equals(x.Name, companyConfiguration.SheetName, StringComparison.Ordinal));
            if (companySheet is null) return $"This is not a {profile.DisplayName} workbook. Required sheet '{companyConfiguration.SheetName}' is missing.";

            var header = await dbcontext.PlatformImportRawRows
                .AsNoTracking()
                .Include(x => x.Cells)
                .SingleOrDefaultAsync(x => x.PlatformImportSheetId == companySheet.Id && x.RowNumber == 1, cancellationToken);
            var headers = header?.Cells
                .Select(x => NormalizeHeader(x.DisplayValue))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            var missingHeaders = companyConfiguration.Columns
                .Select(x => x.Header)
                .Where(x => !headers.Contains(NormalizeHeader(x)))
                .ToArray();
            if (missingHeaders.Length > 0)
                return $"This is not a {profile.DisplayName} workbook. Missing columns in '{companyConfiguration.SheetName}': {string.Join(", ", missingHeaders)}.";
        }

        return null;
    }

    private async Task<PlatformImportTemplate> GetOrCreateSystemTemplateAsync(
        PlatformImportBatch batch,
        DirectImportProfile profile,
        string actorId,
        CancellationToken cancellationToken)
    {
        var existing = await dbcontext.PlatformImportTemplates
            .SingleOrDefaultAsync(x =>
                x.LegalEntityId == batch.LegalEntityId &&
                x.PlatformAccountId == batch.PlatformAccountId &&
                x.AdapterKey == profile.AdapterKey &&
                x.SchemaFingerprint == batch.SchemaFingerprint &&
                x.Status == PlatformTemplateStatus.Active,
                cancellationToken);
        if (existing is not null) return existing;

        var code = $"{profile.TemplateCode}-{batch.SchemaFingerprint[..12]}";
        var version = (await dbcontext.PlatformImportTemplates
            .Where(x => x.LegalEntityId == batch.LegalEntityId && x.PlatformAccountId == batch.PlatformAccountId && x.Code == code)
            .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var template = new PlatformImportTemplate
        {
            LegalEntityId = batch.LegalEntityId,
            PlatformAccountId = batch.PlatformAccountId,
            Code = code,
            Version = version,
            Name = profile.DisplayName,
            AdapterKey = profile.AdapterKey,
            SchemaFingerprint = batch.SchemaFingerprint,
            ConfigurationJson = "{}",
            Status = PlatformTemplateStatus.Active,
            EffectiveFrom = DateOnly.MinValue,
            CreatedBy = actorId,
            ActivatedBy = actorId,
            ActivatedAt = DateTime.UtcNow
        };
        dbcontext.PlatformImportTemplates.Add(template);
        await AppendAuditAsync(batch.LegalEntityId, "PlatformImport.SystemTemplateCreated", actorId, new
        {
            template.Id,
            template.Code,
            template.AdapterKey,
            template.SchemaFingerprint
        }, cancellationToken);
        return template;
    }

    private static DirectImportProfile? GetDirectProfile(string? adapterKey) =>
        string.IsNullOrWhiteSpace(adapterKey)
            ? null
            : DirectImportProfiles.ByAdapter.GetValueOrDefault(adapterKey.Trim());

    private async Task NormalizeAsync(PlatformImportBatch batch, PlatformImportTemplate template, CancellationToken ct)
    {
        var exactConfiguration = ExactConfigurations.GetValueOrDefault(template.AdapterKey.Trim());
        if (!string.Equals(template.AdapterKey, "generic-tabular-v1", StringComparison.OrdinalIgnoreCase) && exactConfiguration is null)
        {
            batch.Issues.Add(new PlatformImportIssue { Severity = PlatformImportIssueSeverity.Blocking, Code = "ADAPTER_NOT_INSTALLED", Message = $"معالج الإصدار {template.AdapterKey} غير مثبت." });
            return;
        }
        var configuration = exactConfiguration ?? JsonSerializer.Deserialize<GenericTemplateConfiguration>(template.ConfigurationJson, JsonOptions) ?? throw new JsonException("Template configuration is empty.");
        var sheets = await dbcontext.PlatformImportSheets.AsNoTracking().Where(x => x.PlatformImportBatchId == batch.Id && (configuration.SheetNames.Count == 0 || configuration.SheetNames.Contains(x.Name))).OrderBy(x => x.SheetIndex).ToListAsync(ct);
        if (sheets.Count == 0)
        {
            batch.Issues.Add(new PlatformImportIssue { Severity = PlatformImportIssueSeverity.Blocking, Code = "SHEET_MISSING", Message = "لم يتم العثور على ورقة مصدر مطابقة للإعدادات." });
            return;
        }

        var identities = await dbcontext.PlatformWorkerIdentities.AsNoTracking().Where(x => x.LegalEntityId == batch.LegalEntityId && x.PlatformAccountId == batch.PlatformAccountId).ToListAsync(ct);
        var companyIds = await dbcontext.LegacyCompanyPlatformMappings.AsNoTracking()
            .Where(x => x.PlatformAccountId == batch.PlatformAccountId && x.EffectiveFrom.Date <= batch.PeriodEnd.ToDateTime(TimeOnly.MaxValue) && (x.EffectiveTo == null || x.EffectiveTo.Value.Date >= batch.PeriodStart.ToDateTime(TimeOnly.MinValue)))
            .Select(x => x.CompanyId).Distinct().ToArrayAsync(ct);
        var currentRiders = await dbcontext.RiderDetails.AsNoTracking().Where(x => companyIds.Contains(x.CompanyId) && x.WorkingId != null)
            .Select(x => new LegacyIdentityCandidate(x.WorkingId!, x.EmployeeIqamaNo, null, null, "CurrentRider")).ToListAsync(ct);
        var workingHistory = await dbcontext.RiderWorkingIdHistories.AsNoTracking().Where(x => companyIds.Contains(x.CompanyId))
            .Select(x => new LegacyIdentityCandidate(x.WorkingId, x.RiderIqamaNo, x.StartDate, x.EndDate, "WorkingIdHistory")).ToListAsync(ct);
        var shiftSubstitutions = await dbcontext.RiderShiftSubstitutions.AsNoTracking().Where(x => x.IsActive && x.StartDate.Date <= batch.PeriodEnd.ToDateTime(TimeOnly.MaxValue) && (x.EndDate == null || x.EndDate.Value.Date >= batch.PeriodStart.ToDateTime(TimeOnly.MinValue)))
            .Select(x => new LegacyIdentityCandidate(x.ActualRiderWorkingId, x.SubstituteRider.EmployeeIqamaNo, x.StartDate, x.EndDate, "ShiftSubstitution")).ToListAsync(ct);
        var hungerSubstitutions = await dbcontext.Set<HungerDisability>().AsNoTracking().Where(x => companyIds.Contains(x.CompanyId) && x.SubstituteRiderId != null && x.ShiftDate >= batch.PeriodStart && x.ShiftDate <= batch.PeriodEnd)
            .Join(dbcontext.RiderDetails.AsNoTracking(), x => x.SubstituteRiderId, rider => (int?)rider.Id, (x, rider) => new LegacyIdentityCandidate(x.ActualWorkingId, rider.EmployeeIqamaNo, x.ShiftDate.ToDateTime(TimeOnly.MinValue), x.ShiftDate.ToDateTime(TimeOnly.MaxValue), "HungerSubstitution"))
            .ToListAsync(ct);
        var activeEmployeeIqamas = (await dbcontext.Employees.AsNoTracking().Where(x => !x.IsDeleted).Select(x => x.IqamaNo).ToListAsync(ct)).ToHashSet();
        decimal normalizedTotal = 0m;
        foreach (var sheet in sheets)
        {
            var header = await dbcontext.PlatformImportRawRows.AsNoTracking().Include(x => x.Cells).SingleOrDefaultAsync(x => x.PlatformImportSheetId == sheet.Id && x.RowNumber == configuration.HeaderRow, ct);
            if (header is null)
            {
                batch.Issues.Add(new PlatformImportIssue { Severity = PlatformImportIssueSeverity.Blocking, Code = "HEADER_MISSING", Message = $"صف العناوين رقم {configuration.HeaderRow} غير موجود في ورقة {sheet.Name}." });
                continue;
            }
            var columns = header.Cells.ToDictionary(x => NormalizeHeader(x.DisplayValue), x => x.ColumnNumber, StringComparer.OrdinalIgnoreCase);
            if (!columns.TryGetValue(NormalizeHeader(configuration.ExternalWorkerIdHeader), out var workerColumn))
            {
                batch.Issues.Add(new PlatformImportIssue { Severity = PlatformImportIssueSeverity.Blocking, Code = "WORKER_COLUMN_MISSING", Message = $"عمود المندوب {configuration.ExternalWorkerIdHeader} غير موجود في ورقة {sheet.Name}." });
                continue;
            }
            var directIqamaColumn = !string.IsNullOrWhiteSpace(configuration.RiderIqamaHeader) && columns.TryGetValue(NormalizeHeader(configuration.RiderIqamaHeader), out var parsedIqamaColumn)
                ? parsedIqamaColumn
                : (int?)null;
            var missingColumns = configuration.Columns.Where(x => !columns.ContainsKey(NormalizeHeader(x.Header))).Select(x => x.Header).ToArray();
            if (missingColumns.Length > 0)
            {
                batch.Issues.Add(new PlatformImportIssue { Severity = PlatformImportIssueSeverity.Blocking, Code = "METRIC_COLUMN_MISSING", Message = $"الأعمدة المطلوبة غير موجودة في ورقة {sheet.Name}: {string.Join(", ", missingColumns)}." });
                continue;
            }

            const int pageSize = 500;
            var lastId = 0L;
            while (true)
            {
                var rows = await dbcontext.PlatformImportRawRows.AsNoTracking().Include(x => x.Cells).Where(x => x.PlatformImportSheetId == sheet.Id && x.RowNumber > configuration.HeaderRow && x.Id > lastId).OrderBy(x => x.Id).Take(pageSize).ToListAsync(ct);
                if (rows.Count == 0) break;
                foreach (var row in rows)
                {
                    lastId = row.Id;
                    var byColumn = row.Cells.ToDictionary(x => x.ColumnNumber);
                    var workerId = byColumn.GetValueOrDefault(workerColumn)?.DisplayValue?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(workerId)) continue;
                    var factDate = ParseFactDate(configuration, columns, byColumn, batch.PeriodEnd);
                    var isCompanySummary = IsCompanySummaryWorkerId(workerId);
                    var resolution = isCompanySummary
                        ? new IdentityResolution(null, 1, "CompanySummary")
                        : ResolveIdentity(workerId, factDate, identities, shiftSubstitutions, hungerSubstitutions, workingHistory, currentRiders);
                    long? riderIqama = resolution.RiderIqamaNo;
                    if (!isCompanySummary && !riderIqama.HasValue && resolution.MatchCount == 0 && directIqamaColumn.HasValue &&
                        long.TryParse(byColumn.GetValueOrDefault(directIqamaColumn.Value)?.DisplayValue?.Trim(), out var directIqama) && activeEmployeeIqamas.Contains(directIqama))
                    {
                        riderIqama = directIqama;
                        resolution = new IdentityResolution(directIqama, 1, "WorkbookIqama");
                    }
                    if (!isCompanySummary && !riderIqama.HasValue)
                        dbcontext.PlatformImportIssues.Add(new PlatformImportIssue { PlatformImportBatchId = batch.Id, SourceRawRowId = row.Id, Severity = PlatformImportIssueSeverity.Blocking, Code = resolution.MatchCount == 0 ? "IDENTITY_MISSING" : "IDENTITY_AMBIGUOUS", Message = AccountingImportArabicText.IdentityIssueMessage(workerId, resolution.MatchCount, factDate, resolution.Source) });

                    foreach (var mapping in configuration.Columns)
                    {
                        var cell = byColumn.GetValueOrDefault(columns[NormalizeHeader(mapping.Header)]);
                        if (cell is null || string.IsNullOrWhiteSpace(cell.DisplayValue)) continue;
                        var metricCode = NormalizeCode(mapping.MetricCode);
                        if (!CompensationService.AllowedMetrics.Contains(metricCode))
                        {
                            dbcontext.PlatformImportIssues.Add(new PlatformImportIssue { PlatformImportBatchId = batch.Id, SourceRawRowId = row.Id, Severity = PlatformImportIssueSeverity.Blocking, Code = "METRIC_NOT_ALLOWED", Message = $"القيمة المحاسبية {AccountingImportArabicText.Metric(metricCode)} ({metricCode}) غير مسموح بها." });
                            continue;
                        }
                        var fact = new PlatformNormalizedFact
                        {
                            PlatformImportBatchId = batch.Id, LegalEntityId = batch.LegalEntityId, PlatformAccountId = batch.PlatformAccountId, SourceRawRowId = row.Id,
                            WorkerCategory = isCompanySummary ? "Company" : configuration.WorkerCategory, RiderIqamaNo = riderIqama, ExternalWorkerId = isCompanySummary ? "COMPANY" : workerId, FactDate = factDate, Category = mapping.Category, MetricCode = metricCode,
                            CurrencyCode = string.IsNullOrWhiteSpace(mapping.CurrencyCode) ? "SAR" : NormalizeCode(mapping.CurrencyCode), IsResolved = isCompanySummary || riderIqama.HasValue,
                            LineageJson = JsonSerializer.Serialize(new { Sheet = sheet.Name, row.RowNumber, cell.CellReference, IdentitySource = resolution.Source }, JsonOptions)
                        };
                        if (string.Equals(mapping.DataType, "boolean", StringComparison.OrdinalIgnoreCase)) fact.BooleanValue = ParseBoolean(cell.DisplayValue);
                        else if (string.Equals(mapping.DataType, "text", StringComparison.OrdinalIgnoreCase)) fact.TextValue = cell.DisplayValue;
                        else if (TryParseDecimal(cell.DisplayValue, out var numeric)) fact.NumericValue = numeric * (mapping.Multiplier ?? 1m);
                        else
                        {
                            dbcontext.PlatformImportIssues.Add(new PlatformImportIssue { PlatformImportBatchId = batch.Id, SourceRawRowId = row.Id, Severity = PlatformImportIssueSeverity.Blocking, Code = "VALUE_INVALID", Message = $"الخلية {cell.CellReference} لا تحتوي على قيمة صالحة من النوع {mapping.DataType}." });
                            continue;
                        }
                        dbcontext.PlatformNormalizedFacts.Add(fact);
                        if (string.Equals(metricCode, NormalizeCode(configuration.ControlTotalMetricCode ?? string.Empty), StringComparison.OrdinalIgnoreCase)) normalizedTotal += fact.NumericValue ?? 0m;
                    }
                }
                await dbcontext.SaveChangesAsync(ct);
            }
        }
        batch.NormalizedControlTotal = normalizedTotal;
        var companyControlTotal = await NormalizeExactCompanySummaryAsync(batch, template.AdapterKey, ct);
        if (companyControlTotal.HasValue) batch.NormalizedControlTotal = companyControlTotal.Value;
        if (batch.SourceControlTotal.HasValue && decimal.Round(batch.SourceControlTotal.Value, 2) != decimal.Round(batch.NormalizedControlTotal ?? 0m, 2))
            batch.Issues.Add(new PlatformImportIssue { Severity = PlatformImportIssueSeverity.Blocking, Code = "CONTROL_TOTAL_MISMATCH", Message = $"إجمالي المصدر {batch.SourceControlTotal:0.00} يختلف عن الإجمالي المحسوب {batch.NormalizedControlTotal:0.00}." });
    }

    private async Task RefreshReconciliationStateAsync(PlatformImportBatch batch, CancellationToken ct)
    {
        var hasOpenBlocking = batch.Issues.Any(x => x.Status == PlatformImportIssueStatus.Open && x.Severity == PlatformImportIssueSeverity.Blocking) ||
            await dbcontext.PlatformImportIssues.AnyAsync(x => x.PlatformImportBatchId == batch.Id && x.Status == PlatformImportIssueStatus.Open && x.Severity == PlatformImportIssueSeverity.Blocking, ct);
        var totalsMatch = !batch.SourceControlTotal.HasValue || decimal.Round(batch.SourceControlTotal.Value, 2) == decimal.Round(batch.NormalizedControlTotal ?? 0m, 2);
        batch.Status = !hasOpenBlocking && totalsMatch ? PlatformImportStatus.Reconciled : PlatformImportStatus.NeedsResolution;
    }

    private async Task ClearBatchImportDataAsync(Guid batchId, CancellationToken cancellationToken)
    {
        if (dbcontext.Database.IsRelational())
        {
            await dbcontext.PlatformFactOverrides
                .Where(x => x.PlatformNormalizedFact.PlatformImportBatchId == batchId)
                .ExecuteDeleteAsync(cancellationToken);
            await dbcontext.PlatformNormalizedFacts
                .Where(x => x.PlatformImportBatchId == batchId)
                .ExecuteDeleteAsync(cancellationToken);
            await dbcontext.PlatformImportIssues
                .Where(x => x.PlatformImportBatchId == batchId)
                .ExecuteDeleteAsync(cancellationToken);
            await dbcontext.PlatformImportRawCells
                .Where(x => x.PlatformImportRawRow.PlatformImportSheet.PlatformImportBatchId == batchId)
                .ExecuteDeleteAsync(cancellationToken);
            await dbcontext.PlatformImportRawRows
                .Where(x => x.PlatformImportSheet.PlatformImportBatchId == batchId)
                .ExecuteDeleteAsync(cancellationToken);
            await dbcontext.PlatformImportSheets
                .Where(x => x.PlatformImportBatchId == batchId)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var overrides = await dbcontext.PlatformFactOverrides
            .Where(x => x.PlatformNormalizedFact.PlatformImportBatchId == batchId)
            .ToListAsync(cancellationToken);
        var facts = await dbcontext.PlatformNormalizedFacts
            .Where(x => x.PlatformImportBatchId == batchId)
            .ToListAsync(cancellationToken);
        var issues = await dbcontext.PlatformImportIssues
            .Where(x => x.PlatformImportBatchId == batchId)
            .ToListAsync(cancellationToken);
        var cells = await dbcontext.PlatformImportRawCells
            .Where(x => x.PlatformImportRawRow.PlatformImportSheet.PlatformImportBatchId == batchId)
            .ToListAsync(cancellationToken);
        var rows = await dbcontext.PlatformImportRawRows
            .Where(x => x.PlatformImportSheet.PlatformImportBatchId == batchId)
            .ToListAsync(cancellationToken);
        var sheets = await dbcontext.PlatformImportSheets
            .Where(x => x.PlatformImportBatchId == batchId)
            .ToListAsync(cancellationToken);

        dbcontext.PlatformFactOverrides.RemoveRange(overrides);
        dbcontext.PlatformNormalizedFacts.RemoveRange(facts);
        dbcontext.PlatformImportIssues.RemoveRange(issues);
        dbcontext.PlatformImportRawCells.RemoveRange(cells);
        dbcontext.PlatformImportRawRows.RemoveRange(rows);
        dbcontext.PlatformImportSheets.RemoveRange(sheets);
        await dbcontext.SaveChangesAsync(cancellationToken);
    }

    private static PlatformImportRawCell? ToRawCell(Cell cell, IReadOnlyList<string> sharedStrings)
    {
        var reference = cell.CellReference?.Value;
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var raw = cell.CellValue?.Text ?? cell.InlineString?.InnerText;
        var display = raw;
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(raw, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count) display = sharedStrings[sharedIndex];
        if (string.IsNullOrWhiteSpace(display) && cell.CellFormula is null) return null;
        return new PlatformImportRawCell { ColumnNumber = ColumnNumber(reference), CellReference = reference, RawValue = raw, DisplayValue = display, Formula = cell.CellFormula?.Text, DataType = cell.DataType?.Value.ToString() ?? "Number" };
    }

    private static int ColumnNumber(string reference)
    {
        var value = 0;
        foreach (var c in reference.TakeWhile(char.IsLetter)) value = checked(value * 26 + (char.ToUpperInvariant(c) - 'A' + 1));
        return value;
    }

    private static DateOnly ParseFactDate(GenericTemplateConfiguration config, IReadOnlyDictionary<string, int> columns, IReadOnlyDictionary<int, PlatformImportRawCell> cells, DateOnly fallback)
    {
        if (string.IsNullOrWhiteSpace(config.DateHeader) || !columns.TryGetValue(NormalizeHeader(config.DateHeader), out var column)) return fallback;
        var value = cells.GetValueOrDefault(column)?.DisplayValue;
        if (DateOnly.TryParse(value, out var date)) return date;
        if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var serial)) return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        return fallback;
    }

    private static bool IsConfigurationValid(string adapterKey, GenericTemplateConfiguration? configuration) =>
        ExactConfigurations.ContainsKey(adapterKey.Trim()) ||
        (string.Equals(adapterKey, "generic-tabular-v1", StringComparison.OrdinalIgnoreCase) && configuration is not null && configuration.HeaderRow > 0 &&
        !string.IsNullOrWhiteSpace(configuration.ExternalWorkerIdHeader) && configuration.Columns.Count > 0 &&
        configuration.Columns.All(x => !string.IsNullOrWhiteSpace(x.Header) && CompensationService.AllowedMetrics.Contains(x.MetricCode)));

    private static bool TryParseDecimal(string? value, out decimal result)
    {
        var normalized = NormalizeDigits(value);
        if (normalized.Trim() is "-" or "–" or "—")
        {
            result = 0m;
            return true;
        }
        var match = Regex.Match(normalized, @"[-+]?\d[\d,]*(?:\.\d+)?");
        if (match.Success && decimal.TryParse(match.Value.Replace(",", string.Empty), System.Globalization.NumberStyles.Number | System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out result))
            return true;
        return decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result) || decimal.TryParse(value, out result);
    }
    private static string NormalizeDigits(string? value) => new((value ?? string.Empty).Select(c => c switch
    {
        >= '\u0660' and <= '\u0669' => (char)('0' + c - '\u0660'),
        >= '\u06F0' and <= '\u06F9' => (char)('0' + c - '\u06F0'),
        _ => c
    }).ToArray());
    private static bool? ParseBoolean(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "1" or "TRUE" or "YES" or "VALID" or "صالح" => true,
        "0" or "FALSE" or "NO" or "INVALID" or "غير صالح" => false,
        _ => null
    };
    private static bool IsSpreadsheetName(string fileName) => new[] { ".xlsx", ".xlsm" }.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);
    private static string SafeFileName(string value) => Path.GetFileName(value).Length <= 260 ? Path.GetFileName(value) : Path.GetFileName(value)[..260];
    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
    private static string NormalizeHeader(string? value) => string.Join(' ', (value ?? string.Empty).Trim().ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static bool IsCompanySummaryWorkerId(string value)
    {
        var normalized = NormalizeHeader(value);
        return normalized is "COMPANY" or "COMPANY TOTAL" or "TOTAL COMPANY" or "TOTAL" or "GRAND TOTAL" or
            "الشركة" or "إجمالي الشركة" or "الإجمالي" or "إجمالي" or "المجموع" or "المجموع الكلي";
    }
    private static IdentityResolution ResolveIdentity(
        string workerId,
        DateOnly factDate,
        IReadOnlyCollection<PlatformWorkerIdentity> explicitMappings,
        IReadOnlyCollection<LegacyIdentityCandidate> shiftSubstitutions,
        IReadOnlyCollection<LegacyIdentityCandidate> hungerSubstitutions,
        IReadOnlyCollection<LegacyIdentityCandidate> histories,
        IReadOnlyCollection<LegacyIdentityCandidate> currentRiders)
    {
        var explicitMatches = explicitMappings.Where(x => string.Equals(x.ExternalWorkerId, workerId, StringComparison.OrdinalIgnoreCase) && x.EffectiveFrom <= factDate && (x.EffectiveTo == null || x.EffectiveTo >= factDate)).Select(x => x.RiderIqamaNo).Distinct().ToArray();
        if (explicitMatches.Length > 0) return explicitMatches.Length == 1 ? new(explicitMatches[0], 1, "PlatformWorkerIdentity") : new(null, explicitMatches.Length, "PlatformWorkerIdentity");
        foreach (var source in new[] { shiftSubstitutions, hungerSubstitutions, histories, currentRiders })
        {
            var matches = source.Where(x => string.Equals(x.WorkingId, workerId, StringComparison.OrdinalIgnoreCase) && x.Covers(factDate)).Select(x => x.RiderIqamaNo).Distinct().ToArray();
            if (matches.Length > 0) return matches.Length == 1 ? new(matches[0], 1, source.First(x => string.Equals(x.WorkingId, workerId, StringComparison.OrdinalIgnoreCase)).Source) : new(null, matches.Length, string.Join(',', source.Where(x => string.Equals(x.WorkingId, workerId, StringComparison.OrdinalIgnoreCase)).Select(x => x.Source).Distinct()));
        }
        return new(null, 0, "None");
    }
    private static string AddResolutionToLineage(string lineageJson, string actorId, string reason, long riderIqamaNo)
    {
        using var existing = JsonDocument.Parse(lineageJson);
        var data = new Dictionary<string, object?>();
        foreach (var property in existing.RootElement.EnumerateObject()) data[property.Name] = property.Value.Clone();
        data["identityResolution"] = new { riderIqamaNo, reason = reason.Trim(), actorId, resolvedAt = DateTime.UtcNow };
        return JsonSerializer.Serialize(data, JsonOptions);
    }

    private async Task AppendAuditAsync(int entityId, string eventType, string actorId, object payload, CancellationToken ct)
    {
        if (dbcontext.Database.IsSqlServer() && dbcontext.Database.CurrentTransaction is not null)
            await dbcontext.Database.ExecuteSqlInterpolatedAsync($"EXEC sp_getapplock @Resource={"Accounting:AuditChain:" + entityId}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000", ct);
        var head = await dbcontext.AccountingAuditChainHeads.SingleOrDefaultAsync(x => x.LegalEntityId == entityId, ct);
        if (head is null) { head = new AccountingAuditChainHead { LegalEntityId = entityId }; dbcontext.AccountingAuditChainHeads.Add(head); }
        var json = JsonSerializer.Serialize(payload);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{head.LastHash}|{entityId}||{eventType}|{actorId}|{json}")));
        dbcontext.AccountingAuditEvents.Add(new AccountingAuditEvent { LegalEntityId = entityId, EventType = eventType, ActorId = actorId, PayloadJson = json, PreviousHash = head.LastHash, Hash = hash });
        dbcontext.AccountingOutboxMessages.Add(new AccountingOutboxMessage { LegalEntityId = entityId, Type = eventType, PayloadJson = json, CorrelationId = hash[..32] });
        head.LastHash = hash;
    }

    private static PlatformImportTemplateResponse ToResponse(PlatformImportTemplate x) => new(x.Id, x.LegalEntityId, x.PlatformAccountId, x.Code, x.Version, x.Name, x.AdapterKey, x.SchemaFingerprint, x.ConfigurationJson, x.Status, x.EffectiveFrom, x.EffectiveTo);
    private static PlatformImportIssueResponse ToResponse(PlatformImportIssue x) => new(x.Id, x.Severity, x.Status, x.Code, x.Message, x.Resolution, x.SourceRawRowId);
    private static PlatformImportBatchResponse ToResponse(PlatformImportBatchProjection x) => new(
        x.Id, x.LegalEntityId, x.PlatformAccountId, x.StoredFileId, x.TemplateId, x.AdapterKey, x.ExternalReference, x.PeriodStart, x.PeriodEnd, x.ParserVersion, x.SchemaFingerprint, x.Status,
        x.SourceControlTotal, x.NormalizedControlTotal, x.SheetCount, x.RawRowCount, x.RawCellCount, x.FactCount, x.OpenBlockingIssueCount,
        Convert.ToBase64String(x.RowVersion));
    private static PlatformNormalizedFactResponse ToResponse(PlatformNormalizedFact x) => new(
        x.Id, x.PlatformImportBatchId, x.LegalEntityId, x.PlatformAccountId, x.WorkerCategory, x.SourceRawRowId, x.RiderIqamaNo, x.ExternalWorkerId,
        x.FactDate, x.Category, x.MetricCode, x.NumericValue, x.TextValue, x.BooleanValue, x.CurrencyCode, x.IsResolved, x.LineageJson,
        x.Override is null ? null : new PlatformFactOverrideResponse(x.Override.Id, x.Override.BooleanValue, x.Override.Reason, x.Override.CreatedBy, x.Override.CreatedAt));

    private async Task<IReadOnlyList<PlatformNormalizedFactResponse>> AttachRiderNamesAsync(
        IEnumerable<PlatformNormalizedFactResponse> facts,
        CancellationToken cancellationToken)
    {
        var items = facts.ToArray();
        var riders = await GetRiderDisplaysAsync(items.Where(x => x.RiderIqamaNo.HasValue).Select(x => x.RiderIqamaNo!.Value), cancellationToken);
        return items.Select(fact => fact with
        {
            RiderNameAr = fact.RiderIqamaNo.HasValue && riders.TryGetValue(fact.RiderIqamaNo.Value, out var rider) ? rider.NameAr : null
        }).ToArray();
    }

    private async Task<IReadOnlyCollection<PlatformImportIssueResponse>> AttachIssueRidersAsync(
        Guid batchId,
        IEnumerable<PlatformImportIssueResponse> issues,
        CancellationToken cancellationToken)
    {
        var items = issues.ToArray();
        var ridersByRow = await GetRidersBySourceRowAsync(batchId, items.Where(x => x.SourceRawRowId.HasValue).Select(x => x.SourceRawRowId!.Value).ToArray(), cancellationToken);
        return items.Select(issue => issue.SourceRawRowId.HasValue && ridersByRow.TryGetValue(issue.SourceRawRowId.Value, out var rider)
            ? issue with { RiderIqamaNo = rider.IqamaNo, RiderNameAr = rider.NameAr }
            : issue).ToArray();
    }

    private async Task<Dictionary<long, RiderDisplay>> GetRidersBySourceRowAsync(
        Guid batchId,
        IReadOnlyCollection<long> sourceRowIds,
        CancellationToken cancellationToken)
    {
        if (sourceRowIds.Count == 0) return [];

        var matches = await dbcontext.PlatformNormalizedFacts
            .AsNoTracking()
            .Where(x => x.PlatformImportBatchId == batchId && x.SourceRawRowId.HasValue && sourceRowIds.Contains(x.SourceRawRowId.Value) && x.RiderIqamaNo.HasValue)
            .Select(x => new { SourceRawRowId = x.SourceRawRowId!.Value, RiderIqamaNo = x.RiderIqamaNo!.Value })
            .ToListAsync(cancellationToken);
        var riderByRow = matches
            .GroupBy(x => x.SourceRawRowId)
            .ToDictionary(x => x.Key, x =>
            {
                var iqamas = x.Select(y => y.RiderIqamaNo).Distinct().ToArray();
                return iqamas.Length == 1 ? iqamas[0] : 0;
            });
        var riders = await GetRiderDisplaysAsync(riderByRow.Values.Where(x => x > 0), cancellationToken);
        return riderByRow
            .Where(x => x.Value > 0)
            .ToDictionary(x => x.Key, x => new RiderDisplay(x.Value, riders.GetValueOrDefault(x.Value)?.NameAr));
    }

    private async Task<Dictionary<long, RiderDisplay>> GetRiderDisplaysAsync(
        IEnumerable<long> iqamas,
        CancellationToken cancellationToken)
    {
        var values = iqamas.Distinct().ToArray();
        if (values.Length == 0) return [];

        var employees = await dbcontext.Employees
            .AsNoTracking()
            .Where(x => values.Contains(x.IqamaNo) && !x.IsDeleted)
            .Select(x => new RiderDisplay(x.IqamaNo, x.NameAR))
            .ToListAsync(cancellationToken);
        return employees.ToDictionary(x => x.IqamaNo);
    }

    private static bool MatchesRowVersion(string? supplied, byte[] actual)
    {
        if (supplied is null) return true;
        try { return Convert.FromBase64String(supplied).SequenceEqual(actual); }
        catch (FormatException) { return false; }
    }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    private sealed record PlatformImportBatchProjection(
        Guid Id,
        int LegalEntityId,
        int PlatformAccountId,
        Guid StoredFileId,
        Guid? TemplateId,
        string? AdapterKey,
        string ExternalReference,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        string ParserVersion,
        string SchemaFingerprint,
        PlatformImportStatus Status,
        decimal? SourceControlTotal,
        decimal? NormalizedControlTotal,
        int SheetCount,
        int RawRowCount,
        int RawCellCount,
        int FactCount,
        int OpenBlockingIssueCount,
        byte[] RowVersion);

    private sealed record PlatformImportRawRowProjection(long Id, long SheetId, int SheetIndex, string SheetName, int RowNumber, string RowHash);
    private sealed record PlatformImportRawCellProjection(long PlatformImportRawRowId, long Id, int ColumnNumber, string CellReference, string? RawValue, string? DisplayValue, string? Formula, string DataType);
    private sealed record RiderDisplay(long IqamaNo, string? NameAr);

    private async Task<decimal?> NormalizeExactCompanySummaryAsync(PlatformImportBatch batch, string adapterKey, CancellationToken ct)
    {
        if (!ExactCompanyConfigurations.TryGetValue(adapterKey.Trim(), out var configuration)) return null;
        var sheet = await dbcontext.PlatformImportSheets.AsNoTracking().SingleOrDefaultAsync(x => x.PlatformImportBatchId == batch.Id && x.Name == configuration.SheetName, ct);
        if (sheet is null)
        {
            batch.Issues.Add(new PlatformImportIssue { Severity = PlatformImportIssueSeverity.Blocking, Code = "COMPANY_SHEET_MISSING", Message = $"ورقة ملخص الشركة المطلوبة {configuration.SheetName} غير موجودة." });
            return null;
        }
        var rows = await dbcontext.PlatformImportRawRows.AsNoTracking().Include(x => x.Cells).Where(x => x.PlatformImportSheetId == sheet.Id && (x.RowNumber == 1 || x.RowNumber == 2)).OrderBy(x => x.RowNumber).ToListAsync(ct);
        var header = rows.FirstOrDefault(x => x.RowNumber == 1);
        var data = rows.FirstOrDefault(x => x.RowNumber == 2);
        if (header is null || data is null) return null;
        var columns = header.Cells.ToDictionary(x => NormalizeHeader(x.DisplayValue), x => x.ColumnNumber, StringComparer.OrdinalIgnoreCase);
        var values = data.Cells.ToDictionary(x => x.ColumnNumber);
        decimal? controlTotal = null;
        foreach (var mapping in configuration.Columns)
        {
            if (!columns.TryGetValue(NormalizeHeader(mapping.Header), out var column) || !values.TryGetValue(column, out var cell) || !TryParseDecimal(cell.DisplayValue, out var amount))
            {
                batch.Issues.Add(new PlatformImportIssue { Severity = PlatformImportIssueSeverity.Blocking, Code = "COMPANY_VALUE_MISSING", Message = $"قيمة الشركة المطلوبة {mapping.Header} غير موجودة أو غير صالحة في ورقة {configuration.SheetName}." });
                continue;
            }
            dbcontext.PlatformNormalizedFacts.Add(new PlatformNormalizedFact
            {
                PlatformImportBatchId = batch.Id, LegalEntityId = batch.LegalEntityId, PlatformAccountId = batch.PlatformAccountId,
                WorkerCategory = "Company", SourceRawRowId = data.Id, ExternalWorkerId = "COMPANY", FactDate = batch.PeriodEnd,
                Category = mapping.Category, MetricCode = mapping.MetricCode, NumericValue = amount, IsResolved = true,
                LineageJson = JsonSerializer.Serialize(new { Sheet = configuration.SheetName, data.RowNumber, cell.CellReference }, JsonOptions)
            });
            if (mapping.IsControlTotal) controlTotal = amount;
        }
        await dbcontext.SaveChangesAsync(ct);
        return controlTotal;
    }

    private sealed record GenericTemplateConfiguration(int HeaderRow, string ExternalWorkerIdHeader, string? DateHeader, IReadOnlyCollection<string> SheetNames, string? ControlTotalMetricCode, IReadOnlyCollection<GenericColumnMapping> Columns, string WorkerCategory = "Rider", string? RiderIqamaHeader = null);
    private sealed record GenericColumnMapping(string Header, string MetricCode, PlatformFactCategory Category, string DataType, string? CurrencyCode, decimal? Multiplier);
    private sealed record CompanyColumnMapping(string Header, string MetricCode, PlatformFactCategory Category, bool IsControlTotal = false);
    private sealed record ExactCompanyConfiguration(string SheetName, IReadOnlyCollection<CompanyColumnMapping> Columns);
    private sealed record LegacyIdentityCandidate(string WorkingId, long RiderIqamaNo, DateTime? EffectiveFrom, DateTime? EffectiveTo, string Source)
    {
        public bool Covers(DateOnly date) => (!EffectiveFrom.HasValue || DateOnly.FromDateTime(EffectiveFrom.Value) <= date) && (!EffectiveTo.HasValue || DateOnly.FromDateTime(EffectiveTo.Value) >= date);
    }
    private sealed record IdentityResolution(long? RiderIqamaNo, int MatchCount, string Source);
    private sealed record DirectImportProfile(
        string AdapterKey,
        string TemplateCode,
        string DisplayName,
        IReadOnlySet<string> IncludedSheetNames);

    private static class DirectImportProfiles
    {
        public static readonly DirectImportProfile Amazon = new(
            "amazon-anow-v1",
            "SYSTEM-AMAZON-ANOW",
            "Amazon ANOW monthly payment",
            new HashSet<string>(["Sheet1"], StringComparer.Ordinal));

        public static readonly DirectImportProfile Hunger = new(
            "hunger-ftr-v1",
            "SYSTEM-HUNGER-FTR",
            "HungerStation FTR invoice",
            new HashSet<string>(["WR", "RLVL"], StringComparer.Ordinal));

        public static readonly DirectImportProfile KeetaPayPerOrder = new(
            "keeta-pay-per-order-v1",
            "SYSTEM-KEETA-PAY-PER-ORDER",
            "Keeta pay-per-order invoice",
            new HashSet<string>(["تفاصيل الشركاء", "تفاصيل سائق التوصيل"], StringComparer.Ordinal));

        public static readonly DirectImportProfile KeetaSegments = new(
            "keeta-segments-v1",
            "SYSTEM-KEETA-SEGMENTS",
            "Keeta segments invoice",
            new HashSet<string>(["تفاصيل الشركاء", "تفاصيل سائق التوصيل"], StringComparer.Ordinal));

        public static readonly IReadOnlyDictionary<string, DirectImportProfile> ByAdapter =
            new Dictionary<string, DirectImportProfile>(StringComparer.OrdinalIgnoreCase)
            {
                [Amazon.AdapterKey] = Amazon,
                [Hunger.AdapterKey] = Hunger,
                [KeetaPayPerOrder.AdapterKey] = KeetaPayPerOrder,
                [KeetaSegments.AdapterKey] = KeetaSegments
            };
    }

    private static readonly IReadOnlyDictionary<string, GenericTemplateConfiguration> ExactConfigurations = new Dictionary<string, GenericTemplateConfiguration>(StringComparer.OrdinalIgnoreCase)
    {
        ["amazon-anow-v1"] = new(1, "Row Labels", null, ["Sheet1"], "COMPANY_TOTAL",
        [
            new("Grand Total", "ACCEPTED_ORDERS", PlatformFactCategory.Activity, "number", null, null),
            new("Working Days", "WORK_DAYS", PlatformFactCategory.Activity, "number", null, null),
            new("Amount", "COMPANY_TOTAL", PlatformFactCategory.CompanyBilling, "number", "SAR", null),
            new("Incentive Amount", "INCENTIVES", PlatformFactCategory.CompanyBilling, "number", "SAR", null),
            new("EID", "EID_DAYS", PlatformFactCategory.Activity, "number", null, null),
            new("EID OT Amount", "EID_OVERTIME_AMOUNT", PlatformFactCategory.CompanyBilling, "number", "SAR", null)
        ], "Amazon", "الإقامة"),
        ["keeta-pay-per-order-v1"] = new(1, "معرّف سائق التوصيل", null, ["تفاصيل سائق التوصيل"], "COMPANY_TOTAL",
        [
            new("الطلبات المُسلمة", "ACCEPTED_ORDERS", PlatformFactCategory.Activity, "number", null, null),
            new("رسوم خدمة التوصيل", "BASE_AMOUNT", PlatformFactCategory.CompanyBilling, "number", "SAR", null),
            new("دعم", "INCENTIVES", PlatformFactCategory.CompanyBilling, "number", "SAR", null),
            new("غرامة مُخالفة", "PENALTIES", PlatformFactCategory.Penalty, "number", "SAR", null),
            new("إجمالي المبلغ المستحق", "COMPANY_TOTAL", PlatformFactCategory.CompanyBilling, "number", "SAR", null)
        ], "KeetaPayPerOrder"),
        ["keeta-segments-v1"] = new(1, "معرّف سائق التوصيل", null, ["تفاصيل سائق التوصيل"], "COMPANY_TOTAL",
        [
            new("صالح", "VALIDITY", PlatformFactCategory.Validity, "boolean", null, null),
            new("أيام الاتصال-صالحة", "WORK_DAYS", PlatformFactCategory.Activity, "number", null, null),
            new("ساعات الاتصال اليومي-صالحة", "CONNECTION_HOURS", PlatformFactCategory.Activity, "number", null, null),
            new("الطلبات المُسلمة", "ACCEPTED_ORDERS", PlatformFactCategory.Activity, "number", null, null),
            new("مسافة التوصيل", "DISTANCE_KM", PlatformFactCategory.Activity, "number", null, null),
            new("التسعير حسب الطلب", "BASE_AMOUNT", PlatformFactCategory.CompanyBilling, "number", "SAR", null),
            new("حوافز سعة الطلب المتاحة الصالحة (زيادة)", "INCENTIVES", PlatformFactCategory.CompanyBilling, "number", "SAR", null),
            new("إجمالي المبلغ المستحق", "COMPANY_TOTAL", PlatformFactCategory.CompanyBilling, "number", "SAR", null)
        ], "KeetaSegments"),
        ["hunger-ftr-v1"] = new(1, "rider_id - معرف المندوب", null, ["RLVL"], null,
        [
            new("Sum of completed_orders - إجمالي الطلبات المكتملة", "ACCEPTED_ORDERS", PlatformFactCategory.Activity, "number", null, null),
            new("Sum of basic_payment - إجمالي الدفعة الأساسية", "BASE_AMOUNT", PlatformFactCategory.CompanyBilling, "number", "SAR", null),
            new("Sum of distance_payment - إجمالي مدفوعات المسافة", "INCENTIVES", PlatformFactCategory.CompanyBilling, "number", "SAR", null),
            new("Sum of declined_penalties_day_logic - إجمالي غرامات الرفض", "PENALTIES", PlatformFactCategory.Penalty, "number", "SAR", null),
            new("Rider Balance - رصيد محفظة المندوب", "RIDER_PAYOUT", PlatformFactCategory.Payout, "number", "SAR", null)
        ], "Hunger")
    };

    private static readonly IReadOnlyDictionary<string, ExactCompanyConfiguration> ExactCompanyConfigurations = new Dictionary<string, ExactCompanyConfiguration>(StringComparer.OrdinalIgnoreCase)
    {
        ["keeta-pay-per-order-v1"] = new("تفاصيل الشركاء",
        [
            new("رسوم خدمة التوصيل", "BASE_AMOUNT", PlatformFactCategory.CompanyBilling),
            new("مبلغ ضريبة القيمة المضافة", "VAT", PlatformFactCategory.Tax),
            new("مبلغ الفاتورة", "INVOICE_AMOUNT", PlatformFactCategory.CompanyBilling),
            new("إجمالي المبلغ المستحق", "COMPANY_TOTAL", PlatformFactCategory.ControlTotal, true)
        ]),
        ["keeta-segments-v1"] = new("تفاصيل الشركاء",
        [
            new("التسعير حسب الطلب", "BASE_AMOUNT", PlatformFactCategory.CompanyBilling),
            new("مبلغ الضريبة", "VAT", PlatformFactCategory.Tax),
            new("مبلغ الفاتورة", "INVOICE_AMOUNT", PlatformFactCategory.CompanyBilling),
            new("إجمالي المبلغ المستحق", "COMPANY_TOTAL", PlatformFactCategory.ControlTotal, true)
        ]),
        ["hunger-ftr-v1"] = new("WR",
        [
            new("completed_orders - الطلبات المكتملة", "ACCEPTED_ORDERS", PlatformFactCategory.Activity),
            new("Amount Excl. VAT - المبلغ بدون ضريبة", "BASE_AMOUNT", PlatformFactCategory.CompanyBilling),
            new("VAT - الضريبة", "VAT", PlatformFactCategory.Tax),
            new("Amount Incl. VAT - المبلغ شامل الضريبة", "COMPANY_TOTAL", PlatformFactCategory.ControlTotal, true),
            new("Net FTR - صافي المستحق", "NET_SETTLEMENT", PlatformFactCategory.Payout)
        ])
    };
}
