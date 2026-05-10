using Application.Contracts.TransporterShifts;
using Application.Service.TransporterShifts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

/// <summary>
/// Manages the Transporter (Company 3) weekly / monthly shift schedule that is
/// imported from Excel and can be manually corrected via the API.
///
/// Base route: /api/transporter-shifts
/// </summary>
[Route("api/transporter-shifts")]
[ApiController]
[Authorize(Roles = "Master,Admin")]
public class TransporterShiftController(ITransporterShiftService service) : ControllerBase
{
    private string Actor => User.Identity?.Name ?? "Unknown";

    // ═══════════════════════════════════════════════════════════════════════
    // Import
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Import the full transporter schedule extracted from Excel.
    ///
    /// The request body is a list of cells. Each cell carries:
    ///   - TransporterId  (Column B in the sheet, maps to RiderDetails.WorkingId)
    ///   - AssociateName  (Column A – used for warnings only)
    ///   - ColumnHeader   (e.g. "Sun, 03/May")
    ///   - CellContent    (e.g. "Driver • 6 PM • 5h\nDriver • 12 PM • 5h")
    ///
    /// The server parses dates from the column headers and resolves the year
    /// automatically (current Saudi-time year). Pass OverrideYear to force a
    /// specific year when importing a schedule that crosses a year boundary.
    ///
    /// Existing shifts for the same (riderId, date) are replaced atomically.
    /// </summary>
    [HttpPost("import")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> Import([FromBody] ImportTransporterScheduleRequest request)
    {
        if (request.Cells is null || request.Cells.Count == 0)
            return BadRequest(new { error = "No cells provided." });

        var result = await service.ImportScheduleAsync(request, Actor);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Schedule Queries
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Full day schedule for ALL Company-3 riders.
    /// Returns riders with shifts, riders on break, and riders with no data.
    /// Format: yyyy-MM-dd
    /// </summary>
    [HttpGet("day")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetDaySchedule([FromQuery] DateOnly date)
    {
        var result = await service.GetDayScheduleAsync(date);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Today's full schedule (shortcut – no query param needed).
    /// </summary>
    [HttpGet("day/today")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetTodaySchedule()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var result = await service.GetDayScheduleAsync(today);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Snapshot of which riders are ACTIVE (shift window covers the requested time)
    /// versus INACTIVE on a given date + time.
    ///
    /// Useful for order dispatching: query with the current time to see who is
    /// on shift right now.
    ///
    /// Query params:
    ///   date  – yyyy-MM-dd   (defaults to today when omitted)
    ///   time  – HH:mm        (defaults to current Saudi time when omitted)
    /// </summary>
    [HttpGet("active")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetActiveAtTime(
        [FromQuery] DateOnly? date,
        [FromQuery] TimeOnly? time)
    {
        var now = DateTime.UtcNow.AddHours(3);
        var d = date ?? DateOnly.FromDateTime(now);
        var t = time ?? TimeOnly.FromDateTime(now);

        var result = await service.GetActiveAtTimeAsync(d, t);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Monthly breakdown for a single rider.
    /// Path: /api/transporter-shifts/riders/{riderId}/monthly?year=2025&amp;month=5
    /// </summary>
    [HttpGet("riders/{riderId:int}/monthly")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetRiderMonthly(
        int riderId,
        [FromQuery] int? year,
        [FromQuery] int? month)
    {
        var now = DateTime.UtcNow.AddHours(3);
        var y = year ?? now.Year;
        var m = month ?? now.Month;

        if (m < 1 || m > 12)
            return BadRequest(new { error = "Month must be between 1 and 12." });

        var result = await service.GetRiderMonthlyScheduleAsync(riderId, y, m);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Monthly breakdown for ALL Company-3 riders.
    /// Path: /api/transporter-shifts/monthly?year=2025&amp;month=5
    /// </summary>
    [HttpGet("monthly")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetAllMonthly(
        [FromQuery] int? year,
        [FromQuery] int? month)
    {
        var now = DateTime.UtcNow.AddHours(3);
        var y = year ?? now.Year;
        var m = month ?? now.Month;

        if (m < 1 || m > 12)
            return BadRequest(new { error = "Month must be between 1 and 12." });

        var result = await service.GetAllRidersMonthlyScheduleAsync(y, m);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Manual Edits
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create OR fully replace a shift block for a rider on a given date.
    ///
    /// ShiftIndex:
    ///   1 = first shift block of the day
    ///   2 = second shift block of the day
    ///
    /// Set IsBreakDay = true to override whatever was imported and mark the day
    /// as a rest day (StartTime / DurationHours are ignored).
    ///
    /// The record is flagged IsManuallyEdited = true automatically.
    /// </summary>
    [HttpPut("shifts")]
    public async Task<IActionResult> UpsertShift([FromBody] UpsertShiftRequest request)
    {
        if (request.ShiftIndex is < 1 or > 2)
            return BadRequest(new { error = "ShiftIndex must be 1 or 2." });

        if (!request.IsBreakDay && request.DurationHours <= 0)
            return BadRequest(new { error = "DurationHours must be > 0 for a working shift." });

        var result = await service.UpsertShiftAsync(request, Actor);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Patch only the timing fields of an existing shift block.
    /// Use this when the Excel had a wrong time and you want to correct it
    /// without touching other fields.
    ///
    /// Only fields that are non-null in the request body are updated.
    /// </summary>
    [HttpPatch("shifts/{shiftId:int}/times")]
    public async Task<IActionResult> PatchShiftTimes(
        int shiftId,
        [FromBody] PatchShiftTimesRequest request)
    {
        if (shiftId != request.ShiftId)
            return BadRequest(new { error = "Route shiftId and body ShiftId must match." });

        var result = await service.PatchShiftTimesAsync(request with { UpdatedBy = Actor });
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Delete a single shift block by its database id.
    /// To remove both blocks of a two-shift day, call this endpoint twice.
    /// </summary>
    [HttpDelete("shifts/{shiftId:int}")]
    public async Task<IActionResult> DeleteShift(int shiftId)
    {
        var result = await service.DeleteShiftAsync(shiftId, Actor);
        return result.IsSuccess
            ? Ok(new { message = "Shift deleted successfully." })
            : result.ToProblem();
    }

    /// <summary>
    /// Mark an entire day as a break day for a rider.
    /// Any existing shift blocks for that day are removed and replaced with a
    /// single break-day sentinel record.
    ///
    /// POST /api/transporter-shifts/riders/{riderId}/break?date=2025-05-09
    /// </summary>
    [HttpPost("riders/{riderId:int}/break")]
    public async Task<IActionResult> MarkBreakDay(
        int riderId,
        [FromQuery] DateOnly date)
    {
        var result = await service.MarkBreakDayAsync(riderId, date, Actor);
        return result.IsSuccess
            ? Ok(new { message = $"Rider {riderId} marked as break on {date}." })
            : result.ToProblem();
    }
}