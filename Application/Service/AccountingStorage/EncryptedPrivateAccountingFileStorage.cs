using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace Application.Service.AccountingStorage;

public sealed class EncryptedPrivateAccountingFileStorage : IPrivateAccountingFileStorage
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ACCTENC1");
    private readonly string? rootPath;
    private readonly byte[]? key;
    private readonly AccountingStorageOptions options;

    public EncryptedPrivateAccountingFileStorage(IWebHostEnvironment environment, IOptions<AccountingStorageOptions> optionsAccessor)
    {
        options = optionsAccessor.Value;
        rootPath = string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? null
            : Path.GetFullPath(Path.Combine(environment.WebRootPath, options.RootRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        try { key = Convert.FromBase64String(options.EncryptionKeyBase64); }
        catch (FormatException) { key = null; }
    }

    public async Task<StoredAccountingFileResult> StoreAsync(int legalEntityId, Stream source, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (legalEntityId <= 0 || !source.CanRead) throw new ArgumentException("A readable source and legal entity are required.");
        var tempDirectory = ResolveContainedPath(Path.Combine(".tmp", legalEntityId.ToString("D10")));
        Directory.CreateDirectory(tempDirectory);
        var tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.tmp");
        var baseNonce = RandomNumberGenerator.GetBytes(8);
        long total = 0;
        var index = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        try
        {
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await output.WriteAsync(Magic, cancellationToken);
                var intBuffer = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(intBuffer, options.EncryptionChunkBytes);
                await output.WriteAsync(intBuffer, cancellationToken);
                await output.WriteAsync(baseNonce, cancellationToken);

                var plain = new byte[options.EncryptionChunkBytes];
                using var aes = new AesGcm(key!, 16);
                while (true)
                {
                    var read = await ReadUpToAsync(source, plain, cancellationToken);
                    if (read == 0) break;
                    total += read;
                    if (total > options.MaxFileBytes) throw new InvalidDataException("The file exceeds the configured accounting upload limit.");
                    hash.AppendData(plain, 0, read);
                    var cipher = new byte[read];
                    var tag = new byte[16];
                    var nonce = BuildNonce(baseNonce, index);
                    aes.Encrypt(nonce, plain.AsSpan(0, read), cipher, tag, BuildAad(index));
                    BinaryPrimitives.WriteInt32LittleEndian(intBuffer, read);
                    await output.WriteAsync(intBuffer, cancellationToken);
                    await output.WriteAsync(cipher, cancellationToken);
                    await output.WriteAsync(tag, cancellationToken);
                    index++;
                }
                BinaryPrimitives.WriteInt32LittleEndian(intBuffer, 0);
                await output.WriteAsync(intBuffer, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            var sha256 = Convert.ToHexString(hash.GetHashAndReset());
            var relative = Path.Combine(legalEntityId.ToString("D10"), sha256[..2], $"{sha256}.acct");
            var destination = ResolveContainedPath(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!File.Exists(destination)) File.Move(tempPath, destination);
            else File.Delete(tempPath);
            return new StoredAccountingFileResult(relative.Replace(Path.DirectorySeparatorChar, '/'), sha256, total, options.EncryptionKeyId);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    public async Task<Stream> OpenReadAsync(string storageLocator, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var sourcePath = ResolveContainedPath(storageLocator.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The private accounting file was not found.");
        var tempDirectory = ResolveContainedPath(".read");
        Directory.CreateDirectory(tempDirectory);
        var tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.tmp");

        try
        {
            await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var magic = new byte[Magic.Length];
                await ReadExactlyAsync(input, magic, cancellationToken);
                if (!magic.SequenceEqual(Magic)) throw new InvalidDataException("The encrypted accounting file header is invalid.");
                var intBuffer = new byte[4];
                await ReadExactlyAsync(input, intBuffer, cancellationToken);
                var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(intBuffer);
                if (chunkSize is < 4096 or > 8 * 1024 * 1024) throw new InvalidDataException("The encrypted accounting chunk size is invalid.");
                var baseNonce = new byte[8];
                await ReadExactlyAsync(input, baseNonce, cancellationToken);
                using var aes = new AesGcm(key!, 16);
                var index = 0;
                while (true)
                {
                    await ReadExactlyAsync(input, intBuffer, cancellationToken);
                    var length = BinaryPrimitives.ReadInt32LittleEndian(intBuffer);
                    if (length == 0) break;
                    if (length < 0 || length > chunkSize) throw new InvalidDataException("The encrypted accounting file contains an invalid chunk.");
                    var cipher = new byte[length];
                    var tag = new byte[16];
                    await ReadExactlyAsync(input, cipher, cancellationToken);
                    await ReadExactlyAsync(input, tag, cancellationToken);
                    var plain = new byte[length];
                    aes.Decrypt(BuildNonce(baseNonce, index), cipher, tag, plain, BuildAad(index));
                    await output.WriteAsync(plain, cancellationToken);
                    index++;
                }
                if (input.Position != input.Length) throw new InvalidDataException("The encrypted accounting file has trailing content.");
                await output.FlushAsync(cancellationToken);
            }
            return new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    public Task DeleteAsync(string storageLocator, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureConfigured();
        var path = ResolveContainedPath(storageLocator.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string ResolveContainedPath(string relativePath)
    {
        EnsureConfigured();
        if (Path.IsPathRooted(relativePath)) throw new InvalidDataException("Absolute accounting storage paths are not allowed.");
        var configuredRootPath = rootPath!;
        var full = Path.GetFullPath(Path.Combine(configuredRootPath, relativePath));
        var prefix = configuredRootPath.EndsWith(Path.DirectorySeparatorChar) ? configuredRootPath : configuredRootPath + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The accounting storage path escapes the private root.");
        return full;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new InvalidOperationException("The web root is not configured for private accounting storage.");
        if (key is null)
            throw new InvalidOperationException("AccountingStorage:EncryptionKeyBase64 must be valid base64.");
        if (key.Length != 32)
            throw new InvalidOperationException("AccountingStorage:EncryptionKeyBase64 must contain exactly 32 bytes.");
        if (options.EncryptionChunkBytes is < 4096 or > 8 * 1024 * 1024)
            throw new InvalidOperationException("Accounting storage chunk size is invalid.");
        if (options.MaxFileBytes <= 0)
            throw new InvalidOperationException("Accounting storage maximum file size is invalid.");

        Directory.CreateDirectory(rootPath);
    }

    private static byte[] BuildNonce(byte[] baseNonce, int index)
    {
        var nonce = new byte[12];
        baseNonce.CopyTo(nonce, 0);
        BinaryPrimitives.WriteInt32BigEndian(nonce.AsSpan(8), index);
        return nonce;
    }

    private static byte[] BuildAad(int index)
    {
        var aad = new byte[Magic.Length + 4];
        Magic.CopyTo(aad, 0);
        BinaryPrimitives.WriteInt32BigEndian(aad.AsSpan(Magic.Length), index);
        return aad;
    }

    private static async Task<int> ReadUpToAsync(Stream source, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await source.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}
