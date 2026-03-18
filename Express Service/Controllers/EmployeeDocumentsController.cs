using Application.Service.EmployeesFiles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

// ─────────────────────────────────────────────────────────────────────────
//  Per-employee image endpoints
//  Base route: /api/employees/{iqamaNo}/images
// ─────────────────────────────────────────────────────────────────────────
[Route("api/employees/{iqamaNo:long}/images")]
[ApiController]
[Authorize]
public class EmployeeDocumentsController(
    IEmployeeDocumentsService service) : ControllerBase
{
    // GET /api/employees/{iqamaNo}/images
    // Returns all 9 image URLs for one employee
    [HttpGet]
    public async Task<IActionResult> GetAllImages(
        long iqamaNo, CancellationToken ct)
    {
        var result = await service.GetEmployeeImagesAsync(iqamaNo, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // GET /api/employees/{iqamaNo}/images/profile
    // Profile image only — lightweight for avatar use-cases
    [HttpGet("profile")]
    public async Task<IActionResult> GetMainImage(
        long iqamaNo, CancellationToken ct)
    {
        var result = await service.GetEmployeeMainImageAsync(iqamaNo, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // POST /api/employees/{iqamaNo}/images
    // Upload ONE image slot
    //
    // multipart/form-data fields:
    //   imageType  int   (EmployeeImageType enum 1-9)
    //              1=Profile 2=Passport 3=Iqama 4=License 5=WorkPermit
    //              6=Additional1 7=Additional2 8=Additional3 9=Additional4
    //   file       file  the image / PDF
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadSingleImage(
        long iqamaNo,
        [FromForm] EmployeeImageType imageType,
        IFormFile file,
        CancellationToken ct)
    {
        var result = await service.UploadImageAsync(iqamaNo, imageType, file, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // POST /api/employees/{iqamaNo}/images/bulk
    // Upload multiple slots — send only the fields you want to fill
    [HttpPost("bulk")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadAllImages(
        long iqamaNo,
        [FromForm] UploadAllImagesRequest request,
        CancellationToken ct)
    {
        // Route iqamaNo is authoritative
        var safeRequest = request with { IqamaNo = iqamaNo };
        var result = await service.UploadAllImagesAsync(safeRequest, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // DELETE /api/employees/{iqamaNo}/images/{imageType}
    // Soft-delete ONE slot — nulls DB column, file stays on disk
    [HttpDelete("{imageType}")]
    public async Task<IActionResult> DeleteSingleImage(
        long iqamaNo,
        EmployeeImageType imageType,
        CancellationToken ct)
    {
        var result = await service.DeleteImageAsync(iqamaNo, imageType, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // DELETE /api/employees/{iqamaNo}/images
    // Soft-delete ALL image slots — files stay on disk
    [HttpDelete]
    public async Task<IActionResult> DeleteAllImages(
        long iqamaNo, CancellationToken ct)
    {
        var result = await service.DeleteAllImagesAsync(iqamaNo, ct);
        return result.IsSuccess ? NoContent() : result.ToProblem();
    }
}

// ─────────────────────────────────────────────────────────────────────────
//  Global listing  (no {iqamaNo} in route)
//  Base route: /api/employees/images
// ─────────────────────────────────────────────────────────────────────────
[Route("api/employees/images")]
[ApiController]
[Authorize]
public class EmployeeImagesListController(
    IEmployeeDocumentsService service) : ControllerBase
{
    // GET /api/employees/images/profiles
    // Profile image URL for every active employee
    [HttpGet("profiles")]
    public async Task<IActionResult> GetAllMainImages(CancellationToken ct)
    {
        var result = await service.GetAllEmployeesMainImagesAsync(ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}