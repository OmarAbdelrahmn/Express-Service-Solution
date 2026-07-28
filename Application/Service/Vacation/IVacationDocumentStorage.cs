namespace Application.Service.Vacation;

public record StoredVacationDocument(string RelativePath, string ContentType, long Length);

public interface IVacationDocumentStorage
{
    Task<StoredVacationDocument> SaveAsync(
        Guid vacationRequestId,
        Guid documentId,
        string category,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}
