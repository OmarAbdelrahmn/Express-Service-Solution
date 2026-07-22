namespace Application.Contracts.AccountingFiles;

public record UploadAccountingFileRequest(int LegalEntityId, DateTime? RetainUntil);

public sealed record AccountingFileListFilter
{
    public int LegalEntityId { get; init; }
    public string? ContentType { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public string? SortDirection { get; init; } = "desc";
}

public record AccountingFileResponse(
    Guid Id,
    int LegalEntityId,
    string OriginalFileName,
    string ContentType,
    long Length,
    string Sha256,
    DateTime? RetainUntil,
    DateTime CreatedAt);

public record AccountingFileDownload(AccountingFileResponse File, Stream Content);
