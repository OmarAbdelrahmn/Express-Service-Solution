namespace Application.Service.AccountingStorage;

public interface IPrivateAccountingFileStorage
{
    Task<StoredAccountingFileResult> StoreAsync(int legalEntityId, Stream source, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string storageLocator, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageLocator, CancellationToken cancellationToken = default);
}
