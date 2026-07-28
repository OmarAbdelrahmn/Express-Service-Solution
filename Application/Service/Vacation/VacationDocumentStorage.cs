using Microsoft.AspNetCore.Hosting;

namespace Application.Service.Vacation;

public sealed class VacationDocumentStorage(IWebHostEnvironment environment) : IVacationDocumentStorage
{
    public const long MaximumFileSize = 20L * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> AllowedExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp"
        };

    private readonly string root = Path.GetFullPath(Path.Combine(
        environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"),
        "vacation-documents"));

    public async Task<StoredVacationDocument> SaveAsync(
        Guid vacationRequestId,
        Guid documentId,
        string category,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(Path.GetFileName(fileName.Replace('\\', '/')));
        if (!AllowedExtensions.TryGetValue(extension, out var contentType))
            throw new InvalidDataException("Unsupported vacation document type.");

        var safeCategory = category switch
        {
            "ticket" => category,
            "exit-reentry-visa" => category,
            _ => throw new InvalidDataException("Invalid vacation document category.")
        };
        var relativePath = Path.Combine(vacationRequestId.ToString("N"), safeCategory, documentId.ToString("N") + extension.ToLowerInvariant());
        var fullPath = ResolveContainedPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            long total = 0;
            var buffer = new byte[81920];
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.Asynchronous))
            {
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaximumFileSize)
                        throw new InvalidDataException("Vacation document exceeds the maximum file size.");
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            if (total == 0)
                throw new InvalidDataException("Vacation document is empty.");

            File.Move(temporaryPath, fullPath);
            return new StoredVacationDocument(relativePath.Replace('\\', '/'), contentType, total);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveContainedPath(relativePath);
        Stream? stream = File.Exists(fullPath)
            ? new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveContainedPath(relativePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private string ResolveContainedPath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException("Absolute vacation document paths are not allowed.");

        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Vacation document path is outside its storage root.");
        return fullPath;
    }
}
