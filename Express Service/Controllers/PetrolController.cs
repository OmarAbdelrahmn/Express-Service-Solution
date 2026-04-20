using Application.Extensions;
using Application.Service.Petrol;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PetrolController(IPetrolService service) : ControllerBase
{
    private readonly IPetrolService _service = service;



    /// <summary>
    /// Full petrol picture for a given date:
    /// every vehicle cost with all its rider attributions (including unattributed rows).
    /// </summary>
    [HttpGet("daily")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetDailyReport(
        [FromQuery] DateOnly date,
        CancellationToken ct)
    {
        var response = await _service.GetDailyReportAsync(date, ct);

        return response.IsSuccess
            ? Ok(response.Value)
            : response.ToProblem();
    }


    [HttpDelete("date/{date}")]
    //[Authorize(Roles = "Master")]         // guard this — it's destructive
    public async Task<IActionResult> DeleteByDate(
    DateOnly date,
    CancellationToken ct)
    {
        var result = await _service.DeleteByDateAsync(date, ct);

        return result.IsSuccess
            ? Ok(result.Value)
            :result.ToProblem();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // UPLOAD & ATTRIBUTION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Upload an Excel file (PlateNumberE + Cost columns) for a given report date.
    /// Automatically resolves vehicles and attributes costs to riders.
    /// </summary>
    [HttpPost("upload")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromQuery] DateOnly reportDate,
        CancellationToken ct)
    {
        var uploadedBy = User.GetUserId();
        var response = await _service.ProcessUploadAsync(file, reportDate, uploadedBy!, ct);

        return response.IsSuccess
            ? Ok(response.Value)
            : response.ToProblem();
    }

    /// <summary>
    /// Re-run attribution for all pending VehiclePetrolCost rows.
    /// Useful after fixing unresolved vehicle plates.
    /// </summary>
    [HttpPost("attribute-pending")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> AttributePending(CancellationToken ct)
    {
        var response = await _service.AttributePendingAsync(ct);

        return response.IsSuccess
            ? Ok(new Re("Attribution completed successfully"))
            : response.ToProblem();
    }

    /// <summary>
    /// Re-run attribution for a single VehiclePetrolCost record by Id.
    /// </summary>
    [HttpPost("attribute/{id:int}")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> AttributeSingle(int id, CancellationToken ct)
    {
        var response = await _service.AttributeSingleByIdAsync(id, ct);

        return response.IsSuccess
            ? Ok(new Re("Record attributed successfully"))
            : response.ToProblem();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RIDER REPORTS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Full monthly petrol breakdown for a single rider.
    /// </summary>
    [HttpGet("rider/{iqamaNo:long}/monthly")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetRiderMonthly(
        long iqamaNo,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct)
    {
        if (year < 2000 || year > DateTime.UtcNow.Year + 1)
            return BadRequest(new { error = "Invalid year." });

        if (month < 1 || month > 12)
            return BadRequest(new { error = "Month must be between 1 and 12." });

        var response = await _service.GetRiderMonthlyReportAsync(iqamaNo, year, month, ct);

        return response.IsSuccess
            ? Ok(response.Value)
            : response.ToProblem();
    }

    /// <summary>
    /// Summary of all riders with petrol costs in a given month, ordered by total cost.
    /// </summary>
    [HttpGet("riders/summary")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetAllRidersSummary(
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct)
    {
        if (month < 1 || month > 12)
            return BadRequest(new { error = "Month must be between 1 and 12." });

        var response = await _service.GetAllRidersSummaryAsync(year, month, ct);

        return response.IsSuccess
            ? Ok(response.Value)
            : response.ToProblem();
    }

    /// <summary>
    /// All petrol cost rows for a specific rider on a specific date.
    /// </summary>
    [HttpGet("rider/{iqamaNo:long}/date")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetRiderCostsOnDate(
        long iqamaNo,
        [FromQuery] DateOnly date,
        CancellationToken ct)
    {
        var response = await _service.GetRiderCostsOnDateAsync(iqamaNo, date, ct);

        return response.IsSuccess
            ? Ok(response.Value)
            : response.ToProblem();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // VEHICLE REPORTS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Full monthly petrol breakdown for a single vehicle.
    /// </summary>
    [HttpGet("vehicle/{vehicleNumber}/monthly")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetVehicleMonthly(
        string vehicleNumber,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct)
    {
        if (year < 2000 || year > DateTime.UtcNow.Year + 1)
            return BadRequest(new { error = "Invalid year." });

        if (month < 1 || month > 12)
            return BadRequest(new { error = "Month must be between 1 and 12." });

        var response = await _service.GetVehicleMonthlyReportAsync(vehicleNumber, year, month, ct);

        return response.IsSuccess
            ? Ok(response.Value)
            : response.ToProblem();
    }

    /// <summary>
    /// Summary of all vehicles with petrol costs in a given month, ordered by total cost.
    /// </summary>
    [HttpGet("vehicles/summary")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetAllVehiclesSummary(
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct)
    {
        if (month < 1 || month > 12)
            return BadRequest(new { error = "Month must be between 1 and 12." });

        var response = await _service.GetAllVehiclesSummaryAsync(year, month, ct);

        return response.IsSuccess
            ? Ok(response.Value)
            : response.ToProblem();
    }

    /// <summary>
    /// All petrol cost rows for a specific vehicle on a specific date.
    /// </summary>
    [HttpGet("vehicle/{vehicleNumber}/date")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetVehicleCostsOnDate(
        string vehicleNumber,
        [FromQuery] DateOnly date,
        CancellationToken ct)
    {
        var response = await _service.GetVehicleCostsOnDateAsync(vehicleNumber, date, ct);

        return response.IsSuccess
            ? Ok(response.Value)
            : response.ToProblem();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // UNATTRIBUTED
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// All costs where no rider could be resolved. Used for manual review.
    /// </summary>
    [HttpGet("unattributed")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetUnattributed(
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct)
    {
        if (month < 1 || month > 12)
            return BadRequest(new { error = "Month must be between 1 and 12." });

        var response = await _service.GetUnattributedCostsAsync(year, month, ct);

        return response.IsSuccess
            ? Ok(response.Value)
            : response.ToProblem();
    }

    [HttpPut("{vehicleNumber}/note")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> UpdateVehicleNote(
        string vehicleNumber,
        [FromQuery] DateOnly date,
        [FromQuery] string note,
        CancellationToken ct)
    {
        var response = await _service.AddVehicleNoteAsync(vehicleNumber, note, date, ct);

        return response.IsSuccess
            ? Ok()
            : response.ToProblem();
    }
}