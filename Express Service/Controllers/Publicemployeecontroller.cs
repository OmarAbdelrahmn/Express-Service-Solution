using Application.Service.EmployeesFiles;
using Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Express_Service.Controllers;

/// <summary>
/// Public endpoints — no authentication required.
/// Only safe, non-sensitive data is exposed.
/// </summary>
[Route("api/public/employees")]
[ApiController]
public class PublicEmployeeController(
    ApplicationDbcontext db,
    IEmployeeDocumentsService docsService) : ControllerBase
{
    // ── GET /api/public/employees/{iqamaNo} ───────────────────────────────
    // Check if an employee exists and return their basic public info.
    // Returns 404 if not found or soft-deleted.
    [HttpGet("{iqamaNo:long}")]
    public async Task<IActionResult> GetEmployeeAsync(
        long iqamaNo, CancellationToken ct)
    {
        var employee = await db.Employees
            .AsNoTracking()
            .Include(e => e.RiderDetails)
                .ThenInclude(r => r.Company)
            .FirstOrDefaultAsync(e => e.IqamaNo == iqamaNo && !e.IsDeleted, ct);

        if (employee is null)
            return NotFound(new
            {
                title = "Employee.NotFound",
                status = 404,
                detail = "No active employee was found with the given Iqama number."
            });

        var response = new PublicEmployeeResponse(
            IqamaNo: employee.IqamaNo,
            NameAR: employee.NameAR,
            NameEN: employee.NameEN,
            JobTitle: employee.JobTitle,
            Company: employee.RiderDetails?.Company?.Name ?? string.Empty,
            Status: employee.Status
        );

        return Ok(response);
    }

    // ── POST /api/public/employees/{iqamaNo}/documents ────────────────────
    // Upload the 4 main document images for an employee.
    // Calls the same EmployeeDocumentsService used by the admin endpoints.
    // Returns 404 if the employee does not exist.
    [HttpPost("{iqamaNo:long}/documents")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadDocumentsAsync(
        long iqamaNo,
        [FromForm] PublicUploadDocumentsRequest request,
        CancellationToken ct)
    {
        // Route iqamaNo is authoritative
        var bulkRequest = new UploadAllImagesRequest(
            IqamaNo: iqamaNo,
            ProfileImage: request.ProfileImage,
            PassportImage: request.PassportImage,
            IqamaImage: request.IqamaImage,
            LicenseImage: request.LicenseImage,
            WorkPermitImage: null,
            AdditionalImage1: null,
            AdditionalImage2: null,
            AdditionalImage3: null,
            AdditionalImage4: null
        );

        var result = await docsService.UploadAllImagesAsync(bulkRequest, ct);

        if (!result.IsSuccess)
            return result.ToProblem();

        // Return only the 4 public slots — don't expose the other 5
        return Ok(new
        {
            iqamaNo = result.Value.IqamaNo,
            profileImageUrl = result.Value.ProfileImageUrl,
            passportImageUrl = result.Value.PassportImageUrl,
            iqamaImageUrl = result.Value.IqamaImageUrl,
            licenseImageUrl = result.Value.LicenseImageUrl,
        });
    }
}



/// <summary>
/// Public employee info — returned to unauthenticated users.
/// Only safe, non-sensitive fields are exposed.
/// </summary>
public record PublicEmployeeResponse(
    long IqamaNo,
    string NameAR,
    string NameEN,
    string JobTitle,
    string Company,
    string Status
);

/// <summary>
/// The 4 main document images submitted by a public user.
/// All 4 files are required.
/// </summary>
public record PublicUploadDocumentsRequest(
    long IqamaNo,
    IFormFile ProfileImage,
    IFormFile PassportImage,
    IFormFile IqamaImage,
    IFormFile LicenseImage
);