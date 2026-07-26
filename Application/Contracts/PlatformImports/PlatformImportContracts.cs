using Domain.Entities.AccountingPlatform;
using FluentValidation;

namespace Application.Contracts.PlatformImports;

public record CreatePlatformImportTemplateRequest(
    int LegalEntityId,
    int PlatformAccountId,
    string Code,
    string Name,
    string AdapterKey,
    string SchemaFingerprint,
    string ConfigurationJson,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

public record ActivatePlatformImportTemplateRequest(string? Comment);
public record RetirePlatformImportTemplateRequest(string? Comment);
public record UploadPlatformImportRequest(int LegalEntityId, int PlatformAccountId, Guid? TemplateId, string ExternalReference, DateOnly PeriodStart, DateOnly PeriodEnd, decimal? SourceControlTotal);
public record DirectPlatformImportRequest(int LegalEntityId, int PlatformAccountId, string ExternalReference, DateOnly PeriodStart, DateOnly PeriodEnd, decimal? SourceControlTotal);
public record ResolvePlatformImportIssueRequest(string Resolution, bool Waive);
public record ReviewPlatformImportRequest(string? Comment);
public record RemapPlatformWorkerRequest(string ExternalWorkerId, long RiderIqamaNo, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string Reason);
public record OverridePlatformValidityRequest(bool IsValid, string Reason);
public record ReprocessPlatformImportRequest(Guid TemplateId, string? RowVersion);
public record SupersedePlatformImportBatchRequest(Guid ReplacementBatchId, string Reason, string? RowVersion);

public sealed record PlatformImportTemplateListFilter
{
    public int LegalEntityId { get; init; }
    public int? PlatformAccountId { get; init; }
    public PlatformTemplateStatus? Status { get; init; }
    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public string? SortDirection { get; init; } = "desc";
}

public sealed record PlatformImportBatchListFilter
{
    public int LegalEntityId { get; init; }
    public int? PlatformAccountId { get; init; }
    public PlatformImportStatus? Status { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public string? SortDirection { get; init; } = "desc";
}

public sealed record PlatformNormalizedFactListFilter
{
    public PlatformFactCategory? Category { get; init; }
    public string? MetricCode { get; init; }
    public bool? IsResolved { get; init; }
    public string? SortBy { get; init; }
    public string? SortDirection { get; init; } = "desc";
}

public sealed record PlatformImportRawRowListFilter
{
    public long? SheetId { get; init; }
    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public string? SortDirection { get; init; } = "desc";
}

public record PlatformImportTemplateResponse(Guid Id, int LegalEntityId, int PlatformAccountId, string Code, int Version, string Name, string AdapterKey, string SchemaFingerprint, string ConfigurationJson, PlatformTemplateStatus Status, DateOnly EffectiveFrom, DateOnly? EffectiveTo)
{
    public string StatusNameAr => AccountingImportArabicText.TemplateStatus(Status);
}
public record PlatformImportIssueResponse(long Id, PlatformImportIssueSeverity Severity, PlatformImportIssueStatus Status, string Code, string Message, string? Resolution, long? SourceRawRowId)
{
    public string SeverityNameAr => AccountingImportArabicText.IssueSeverity(Severity);
    public string StatusNameAr => AccountingImportArabicText.IssueStatus(Status);
    public string CodeAr => AccountingImportArabicText.IssueCode(Code);
    public string MessageAr => AccountingImportArabicText.IssueMessage(Code, Message);
    public long? RiderIqamaNo { get; init; }
    public string? RiderNameAr { get; init; }
    public bool IsCompany { get; init; }
    public bool IsUnrecognizedRider => !IsCompany && !RiderIqamaNo.HasValue;
}
public record PlatformImportBatchResponse(Guid Id, int LegalEntityId, int PlatformAccountId, Guid StoredFileId, Guid? TemplateId, string? AdapterKey, string ExternalReference, DateOnly PeriodStart, DateOnly PeriodEnd, string ParserVersion, string SchemaFingerprint, PlatformImportStatus Status, decimal? SourceControlTotal, decimal? NormalizedControlTotal, int SheetCount, int RawRowCount, int RawCellCount, int FactCount, int OpenBlockingIssueCount, string RowVersion)
{
    public string StatusNameAr => AccountingImportArabicText.ImportStatus(Status);
}
public record PlatformFactOverrideResponse(long Id, bool BooleanValue, string Reason, string CreatedBy, DateTime CreatedAt);
public record PlatformNormalizedFactResponse(long Id, Guid PlatformImportBatchId, int LegalEntityId, int PlatformAccountId, string WorkerCategory, long? SourceRawRowId, long? RiderIqamaNo, string ExternalWorkerId, DateOnly FactDate, PlatformFactCategory Category, string MetricCode, decimal? NumericValue, string? TextValue, bool? BooleanValue, string CurrencyCode, bool IsResolved, string LineageJson, PlatformFactOverrideResponse? Override)
{
    public string WorkerCategoryAr => AccountingImportArabicText.WorkerCategory(WorkerCategory);
    public string CategoryNameAr => AccountingImportArabicText.FactCategory(Category);
    public string MetricNameAr => AccountingImportArabicText.Metric(MetricCode);
    public bool IsCompany => string.Equals(WorkerCategory, "Company", StringComparison.OrdinalIgnoreCase) || string.Equals(ExternalWorkerId, "COMPANY", StringComparison.OrdinalIgnoreCase);
    public bool IsUnrecognizedRider => !IsCompany && !RiderIqamaNo.HasValue;
    public string? RiderNameAr { get; init; }
}
public record PlatformImportRawCellResponse(long Id, int ColumnNumber, string CellReference, string? RawValue, string? DisplayValue, string? Formula, string DataType);
public record PlatformImportRawRowResponse(long Id, long SheetId, int SheetIndex, string SheetName, int RowNumber, string RowHash, IReadOnlyCollection<PlatformImportRawCellResponse> Cells)
{
    public long? RiderIqamaNo { get; init; }
    public string? RiderNameAr { get; init; }
}
public record AccountingFileDownloadResponse(Stream Content, string ContentType, string FileName);

public class CreatePlatformImportTemplateRequestValidator : AbstractValidator<CreatePlatformImportTemplateRequest>
{
    public CreatePlatformImportTemplateRequestValidator()
    {
        RuleFor(x => x.LegalEntityId).GreaterThan(0);
        RuleFor(x => x.PlatformAccountId).GreaterThan(0);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AdapterKey).NotEmpty().MaximumLength(128);
        RuleFor(x => x.SchemaFingerprint).Matches("^[A-Fa-f0-9]{64}$");
        RuleFor(x => x.ConfigurationJson).NotEmpty();
        RuleFor(x => x.EffectiveTo).GreaterThanOrEqualTo(x => x.EffectiveFrom).When(x => x.EffectiveTo.HasValue);
    }
}

public class RemapPlatformWorkerRequestValidator : AbstractValidator<RemapPlatformWorkerRequest>
{
    public RemapPlatformWorkerRequestValidator()
    {
        RuleFor(x => x.ExternalWorkerId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.RiderIqamaNo).GreaterThan(0);
        RuleFor(x => x.EffectiveTo).GreaterThanOrEqualTo(x => x.EffectiveFrom).When(x => x.EffectiveTo.HasValue);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public class DirectPlatformImportRequestValidator : AbstractValidator<DirectPlatformImportRequest>
{
    public DirectPlatformImportRequestValidator()
    {
        RuleFor(x => x.LegalEntityId).GreaterThan(0);
        RuleFor(x => x.PlatformAccountId).GreaterThan(0);
        RuleFor(x => x.ExternalReference).NotEmpty().MaximumLength(128);
        RuleFor(x => x.PeriodEnd).GreaterThanOrEqualTo(x => x.PeriodStart);
    }
}

public class ReprocessPlatformImportRequestValidator : AbstractValidator<ReprocessPlatformImportRequest>
{
    public ReprocessPlatformImportRequestValidator() => RuleFor(x => x.TemplateId).NotEmpty();
}

public class SupersedePlatformImportBatchRequestValidator : AbstractValidator<SupersedePlatformImportBatchRequest>
{
    public SupersedePlatformImportBatchRequestValidator()
    {
        RuleFor(x => x.ReplacementBatchId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}
