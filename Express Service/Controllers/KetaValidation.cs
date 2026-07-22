using Application.Service.KetaValidation;
using k8s.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize(Roles = "Master,Admin")]
public class KetaValidation(IMonthlyValidityService service) : ControllerBase
{
    private readonly IMonthlyValidityService service = service;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var result = await service.GetAllRidersValidityAsync();

        if (result.IsFailure)
        {
            return result.ToProblem();
        }
        return Ok(result.Value);
    }

    [HttpGet("{iqamano}")]
    public async Task<IActionResult> GetByIqama(long iqamano)
    {
        var result = await service.GetRiderValidityByIqamaAsync(iqamano);
        if (result.IsFailure)
        {
            return result.ToProblem();
        }
        return Ok(result.Value);
    }


    /// <summary>
    /// POST /api/KetaValidation/shifts/import
    /// Imports a Keeta platform daily driver-report Excel file.
    /// Body: multipart/form-data  →  file (IFormFile)
    /// Header or query: uploadedBy (string)
    /// </summary>
    [HttpPost("shifts/import")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB
    public async Task<IActionResult> ImportShifts(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        var uploadedBy = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(uploadedBy))
            return Unauthorized();

        var result = await service.ImportKeetaDriverShiftsAsync(
            file,
            uploadedBy,
            progressCallback: null);

        return result.IsFailure ? result.ToProblem() : Ok(result.Value);
    }

    // ── Keeta driver shift queries ────────────────────────────────────────

    /// <summary>
    /// GET /api/KetaValidation/shifts
    /// Returns all Keeta driver shifts day-by-day, grouped by rider.
    ///
    /// Optional filters:
    ///   ?from=2025-05-01          (DateOnly, inclusive)
    ///   ?to=2025-05-31            (DateOnly, inclusive)
    ///   ?driverId=DRV-0042        (PlatformDriverId or WorkingId)
    /// </summary>
    [HttpGet("shifts")]
    public async Task<IActionResult> GetAllShifts(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? driverId = null)
    {
        var result = await service.GetAllKeetaDriverShiftsAsync(from, to, driverId);
        return result.IsFailure ? result.ToProblem() : Ok(result.Value);
    }

    //
    [HttpPost("keeta-attendance")]
    public async Task<IActionResult> ImportKeetaAttendance(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded or file is empty" });

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return BadRequest(new { error = "File must be Excel format" });

        var uploadedBy = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(uploadedBy))
            return Unauthorized();
        var result = await service.ImportAttendanceAsync(file, uploadedBy);

        if (result.IsSuccess)
        {
            return Ok(new
            {
                success = true,
                data = result.Value,
                summary = new
                {
                    totalRows = result.Value.TotalRows,
                    uniqueDrivers = result.Value.UniqueDrivers,
                    uniqueDays = result.Value.UniqueDays,
                    workDays = result.Value.WorkDays,
                    nonWorkDays = result.Value.NonWorkDays,
                    unmatchedRows = result.Value.UnmatchedRows,
                }
            });
        }

        return result.ToProblem();
    }

    // ════════════════════════════════════════════════════════════════════════════
}
