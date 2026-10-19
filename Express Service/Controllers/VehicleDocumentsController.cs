using Application.Contracts.Employees;
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
