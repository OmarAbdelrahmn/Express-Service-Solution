using Application.Abstraction;
using Domain;
using Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.EmployeesFiles;

public class EmployeeDocumentsService(
    ApplicationDbcontext db,
    IWebHostEnvironment env) : IEmployeeDocumentsService
{
    private static readonly HashSet<string> _allowedExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".pdf"];

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    // ═════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates and saves the file to wwwroot/uploads/{iqamaNo}/{Guid}{ext}.
    /// Returns the relative URL to store in the DB column.
    /// </summary>
    private async Task<Result<string>> SaveFileAsync(
        long iqamaNo,
        IFormFile file,
        CancellationToken ct)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!_allowedExtensions.Contains(ext))
            return Result.Failure<string>(new Error(
                "InvalidFileType",
                $"Allowed types: {string.Join(", ", _allowedExtensions)}.",
                StatusCodes.Status400BadRequest));

        if (file.Length == 0)
            return Result.Failure<string>(new Error(
                "EmptyFile",
                "The uploaded file is empty.",
                StatusCodes.Status400BadRequest));

        if (file.Length > MaxFileSizeBytes)
            return Result.Failure<string>(new Error(
                "FileTooLarge",
                "Maximum allowed file size is 5 MB.",
                StatusCodes.Status400BadRequest));

        var folder = Path.Combine(env.WebRootPath, "uploads", iqamaNo.ToString());
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid()}{ext}";
        var fullPath = Path.Combine(folder, fileName);

        await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
        await file.CopyToAsync(stream, ct);

        return Result.Success($"/uploads/{iqamaNo}/{fileName}");
    }

    /// <summary>
    /// Returns the EmployeeDocuments row, auto-creating it when missing.
    /// Returns null when the employee does not exist.
    /// </summary>
    private async Task<EmployeeDocuments?> GetOrCreateDocumentRowAsync(
        long iqamaNo,
        CancellationToken ct)
    {
        var exists = await db.Employees
            .AnyAsync(e => e.IqamaNo == iqamaNo && !e.IsDeleted, ct);

        if (!exists) return null;

        var doc = await db.EmployeeDocuments
            .FirstOrDefaultAsync(d => d.EmployeeIqamaNo == iqamaNo, ct);

        if (doc is null)
        {
            doc = new EmployeeDocuments { EmployeeIqamaNo = iqamaNo };
            db.EmployeeDocuments.Add(doc);
        }

        return doc;
    }

    private static EmployeeDocumentsResponse ToFullResponse(EmployeeDocuments d) =>
        new(d.EmployeeIqamaNo,
            d.ProfileImagePath,
            d.PassportImagePath,
            d.IqamaImagePath,
            d.LicenseImagePath,
            d.WorkPermitImagePath,
            d.AdditionImage,
            d.AdditionImage1,
            d.AdditionImage2,
            d.AdditionImage3);

    /// Writes a URL (or null) into the correct column.
    private static void SetImagePath(
        EmployeeDocuments doc, EmployeeImageType type, string? url)
    {
        _ = type switch
        {
            EmployeeImageType.ProfileImage => doc.ProfileImagePath = url,
            EmployeeImageType.PassportImage => doc.PassportImagePath = url,
            EmployeeImageType.IqamaImage => doc.IqamaImagePath = url,
            EmployeeImageType.LicenseImage => doc.LicenseImagePath = url,
            EmployeeImageType.WorkPermitImage => doc.WorkPermitImagePath = url,
            EmployeeImageType.AdditionalImage1 => doc.AdditionImage = url,
            EmployeeImageType.AdditionalImage2 => doc.AdditionImage1 = url,
            EmployeeImageType.AdditionalImage3 => doc.AdditionImage2 = url,
            EmployeeImageType.AdditionalImage4 => doc.AdditionImage3 = url,
            _ => null
        };
    }

    /// Reads the current URL from the correct column.
    private static string? GetImagePath(
        EmployeeDocuments doc, EmployeeImageType type) =>
        type switch
        {
            EmployeeImageType.ProfileImage => doc.ProfileImagePath,
            EmployeeImageType.PassportImage => doc.PassportImagePath,
            EmployeeImageType.IqamaImage => doc.IqamaImagePath,
            EmployeeImageType.LicenseImage => doc.LicenseImagePath,
            EmployeeImageType.WorkPermitImage => doc.WorkPermitImagePath,
            EmployeeImageType.AdditionalImage1 => doc.AdditionImage,
            EmployeeImageType.AdditionalImage2 => doc.AdditionImage1,
            EmployeeImageType.AdditionalImage3 => doc.AdditionImage2,
            EmployeeImageType.AdditionalImage4 => doc.AdditionImage3,
            _ => null
        };

    // ═════════════════════════════════════════════════════════════════════
    //  UPLOAD
    // ═════════════════════════════════════════════════════════════════════

    public async Task<Result<ImageOperationResponse>> UploadImageAsync(
        long iqamaNo,
        EmployeeImageType imageType,
        IFormFile file,
        CancellationToken ct = default)
    {
        var doc = await GetOrCreateDocumentRowAsync(iqamaNo, ct);
        if (doc is null)
            return Result.Failure<ImageOperationResponse>(EmployeeDocumentErrors.EmployeeNotFound);

        var saveResult = await SaveFileAsync(iqamaNo, file, ct);
        if (!saveResult.IsSuccess)
            return Result.Failure<ImageOperationResponse>(saveResult.Error);

        SetImagePath(doc, imageType, saveResult.Value);
        await db.SaveChangesAsync(ct);

        return Result.Success(new ImageOperationResponse(
            iqamaNo, imageType, saveResult.Value, "Image uploaded successfully."));
    }

    public async Task<Result<EmployeeDocumentsResponse>> UploadAllImagesAsync(
        UploadAllImagesRequest request,
        CancellationToken ct = default)
    {
        var doc = await GetOrCreateDocumentRowAsync(request.IqamaNo, ct);
        if (doc is null)
            return Result.Failure<EmployeeDocumentsResponse>(EmployeeDocumentErrors.EmployeeNotFound);

        (EmployeeImageType Type, IFormFile? File)[] slots =
        [
            (EmployeeImageType.ProfileImage,     request.ProfileImage),
            (EmployeeImageType.PassportImage,    request.PassportImage),
            (EmployeeImageType.IqamaImage,       request.IqamaImage),
            (EmployeeImageType.LicenseImage,     request.LicenseImage),
            (EmployeeImageType.WorkPermitImage,  request.WorkPermitImage),
            (EmployeeImageType.AdditionalImage1, request.AdditionalImage1),
            (EmployeeImageType.AdditionalImage2, request.AdditionalImage2),
            (EmployeeImageType.AdditionalImage3, request.AdditionalImage3),
            (EmployeeImageType.AdditionalImage4, request.AdditionalImage4),
        ];

        foreach (var (type, file) in slots.Where(s => s.File is not null))
        {
            var saveResult = await SaveFileAsync(request.IqamaNo, file!, ct);
            if (!saveResult.IsSuccess)
                return Result.Failure<EmployeeDocumentsResponse>(saveResult.Error);

            SetImagePath(doc, type, saveResult.Value);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(ToFullResponse(doc));
    }

    // ═════════════════════════════════════════════════════════════════════
    //  READ
    // ═════════════════════════════════════════════════════════════════════

    public async Task<Result<EmployeeDocumentsResponse>> GetEmployeeImagesAsync(
        long iqamaNo,
        CancellationToken ct = default)
    {
        var doc = await db.EmployeeDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.EmployeeIqamaNo == iqamaNo, ct);

        if (doc is null)
            return Result.Failure<EmployeeDocumentsResponse>(EmployeeDocumentErrors.DocumentsNotFound);

        return Result.Success(ToFullResponse(doc));
    }

    public async Task<Result<IEnumerable<EmployeeMainImageResponse>>> GetAllEmployeesMainImagesAsync(
        CancellationToken ct = default)
    {
        // LEFT JOIN — employees with no document row still appear (null profile URL)
        var data = await (
            from e in db.Employees.AsNoTracking()
            where !e.IsDeleted
            join d in db.EmployeeDocuments.AsNoTracking()
                on e.IqamaNo equals d.EmployeeIqamaNo into docs
            from d in docs.DefaultIfEmpty()
            orderby e.NameEN
            select new EmployeeMainImageResponse(
                e.IqamaNo,
                e.NameAR,
                e.NameEN,
                d != null ? d.ProfileImagePath : null)
        ).ToListAsync(ct);

        return Result.Success<IEnumerable<EmployeeMainImageResponse>>(data);
    }

    public async Task<Result<EmployeeMainImageResponse>> GetEmployeeMainImageAsync(
        long iqamaNo,
        CancellationToken ct = default)
    {
        var row = await (
            from e in db.Employees.AsNoTracking()
            where e.IqamaNo == iqamaNo && !e.IsDeleted
            join d in db.EmployeeDocuments.AsNoTracking()
                on e.IqamaNo equals d.EmployeeIqamaNo into docs
            from d in docs.DefaultIfEmpty()
            select new EmployeeMainImageResponse(
                e.IqamaNo,
                e.NameAR,
                e.NameEN,
                d != null ? d.ProfileImagePath : null)
        ).FirstOrDefaultAsync(ct);

        if (row is null)
            return Result.Failure<EmployeeMainImageResponse>(EmployeeDocumentErrors.EmployeeNotFound);

        return Result.Success(row);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  DELETE  (soft — DB column nulled, file kept on disk)
    // ═════════════════════════════════════════════════════════════════════

    public async Task<Result<ImageOperationResponse>> DeleteImageAsync(
        long iqamaNo,
        EmployeeImageType imageType,
        CancellationToken ct = default)
    {
        var doc = await db.EmployeeDocuments
            .FirstOrDefaultAsync(d => d.EmployeeIqamaNo == iqamaNo, ct);

        if (doc is null)
            return Result.Failure<ImageOperationResponse>(EmployeeDocumentErrors.DocumentsNotFound);

        var previousUrl = GetImagePath(doc, imageType);
        if (previousUrl is null)
            return Result.Failure<ImageOperationResponse>(new Error(
                "ImageNotFound",
                $"No {imageType} image is currently set for this employee.",
                StatusCodes.Status404NotFound));

        // ⚠ Only the DB column is nulled.
        // File at wwwroot{previousUrl} is intentionally kept on disk.
        SetImagePath(doc, imageType, null);
        await db.SaveChangesAsync(ct);

        return Result.Success(new ImageOperationResponse(
            iqamaNo,
            imageType,
            null,
            $"{imageType} removed from record. Physical file still stored at: {previousUrl}"));
    }

    public async Task<Result> DeleteAllImagesAsync(
        long iqamaNo,
        CancellationToken ct = default)
    {
        var doc = await db.EmployeeDocuments
            .FirstOrDefaultAsync(d => d.EmployeeIqamaNo == iqamaNo, ct);

        if (doc is null)
            return Result.Failure(EmployeeDocumentErrors.DocumentsNotFound);

        // Null every column — physical files are intentionally kept on disk.
        doc.ProfileImagePath = null;
        doc.PassportImagePath = null;
        doc.IqamaImagePath = null;
        doc.LicenseImagePath = null;
        doc.WorkPermitImagePath = null;
        doc.AdditionImage = null;
        doc.AdditionImage1 = null;
        doc.AdditionImage2 = null;
        doc.AdditionImage3 = null;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}