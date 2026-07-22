namespace Application.Service.AccountingStorage;

public class AccountingStorageOptions
{
    public const string SectionName = "AccountingStorage";
    public string RootRelativePath { get; set; } = "uploads/accounting-private";
    public string EncryptionKeyBase64 { get; set; } = string.Empty;
    public string EncryptionKeyId { get; set; } = "primary";
    public long MaxFileBytes { get; set; } = 100L * 1024 * 1024;
    public int EncryptionChunkBytes { get; set; } = 1024 * 1024;
}

public record StoredAccountingFileResult(string StorageLocator, string Sha256, long PlaintextLength, string EncryptionKeyId);
