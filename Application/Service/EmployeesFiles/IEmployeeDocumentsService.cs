using Application.Abstraction;
using Microsoft.AspNetCore.Http;

namespace Application.Service.EmployeesFiles;

public interface IEmployeeDocumentsService
{
    // ── Write ────────────────────────────────────────────────────────────

    /// <summary>Upload or replace one image slot.</summary>
    Task<Result<ImageOperationResponse>> UploadImageAsync(
        long iqamaNo,
        EmployeeImageType imageType,
        IFormFile file,
        CancellationToken ct = default);

    /// <summary>Upload multiple slots at once; null slots are skipped.</summary>
    Task<Result<EmployeeDocumentsResponse>> UploadAllImagesAsync(
        UploadAllImagesRequest request,
        CancellationToken ct = default);

    // ── Read ─────────────────────────────────────────────────────────────

    /// <summary>All 9 image URLs for one employee.</summary>
    Task<Result<EmployeeDocumentsResponse>> GetEmployeeImagesAsync(
        long iqamaNo,
        CancellationToken ct = default);

    /// <summary>Profile image URL for every active employee.</summary>
    Task<Result<IEnumerable<EmployeeMainImageResponse>>> GetAllEmployeesMainImagesAsync(
        CancellationToken ct = default);

    /// <summary>Profile image URL for a single employee.</summary>
    Task<Result<EmployeeMainImageResponse>> GetEmployeeMainImageAsync(
        long iqamaNo,
        CancellationToken ct = default);

    // ── Delete ───────────────────────────────────────────────────────────

    /// <summary>
    /// Soft-delete ONE slot: nulls the DB column.
    /// Physical file stays in wwwroot.
    /// </summary>
    Task<Result<ImageOperationResponse>> DeleteImageAsync(
        long iqamaNo,
        EmployeeImageType imageType,
        CancellationToken ct = default);

    /// <summary>
    /// Nulls ALL 9 image columns in the DB.
    /// Physical files are NOT deleted from wwwroot.
    /// </summary>
    Task<Result> DeleteAllImagesAsync(
        long iqamaNo,
        CancellationToken ct = default);
}