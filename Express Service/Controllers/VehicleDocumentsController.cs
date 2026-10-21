using Application.Contracts.Employees;
using Application.Abstraction;
using Application.Extensions;
using Application.Service.Empolyee;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[ApiController]
[Route("api/vehicles/documents")]
[Authorize(Roles = "Master,Admin")]
public sealed class VehicleDocumentsController(IVehicleDocumentService service) : ControllerBase
{
    /// <summary>Update registration/inspection expiry dates from .xlsx, matched by serial number. Hijri dates are converted using Umm al-Qura.</summary>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> Import(
        IFormFile file, [FromQuery] bool dryRun = false, [FromQuery] string? worksheetName = null, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0 || file.Length > 10 * 1024 * 1024 ||
            !string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return Result.Failure<VehicleDocumentSheetImportReport>(new Error(
                "VehicleDocuments.InvalidFile", "Upload a non-empty .xlsx file up to 10 MB using the file form field.", 400)).ToProblem();

        await using var stream = file.OpenReadStream();
        var result = await service.ImportExpiryDatesAsync(stream, dryRun, worksheetName, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>Documents expiring today or within the selected window (default 30 days).</summary>
    [HttpGet("expiring")]
    public async Task<IActionResult> GetExpiring([FromQuery] VehicleDocumentQuery query, CancellationToken ct)
    {
        var result = await service.GetExpiringAsync(query, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>Vehicles with no recorded expiry date for the selected document(s).</summary>
    [HttpGet("missing")]
    public async Task<IActionResult> GetMissing([FromQuery] VehicleDocumentQuery query, CancellationToken ct)
    {
        var result = await service.GetMissingAsync(query, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>Live reminder feed: missing, expired, due today and upcoming documents.</summary>
    [HttpGet("reminders")]
    public async Task<IActionResult> GetReminders([FromQuery] VehicleDocumentQuery query, CancellationToken ct)
    {
        var result = await service.GetRemindersAsync(query, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>Replace all three expiry dates; null marks a date as missing.</summary>
    [HttpPut("{vehicleNumber}")]
    public async Task<IActionResult> Update(string vehicleNumber, [FromBody] UpdateVehicleDocumentsRequest request, CancellationToken ct)
    {
        var result = await service.UpdateAsync(vehicleNumber, request, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
