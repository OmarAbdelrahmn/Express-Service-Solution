using Application.Extensions;
using Application.Service.Reminder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static Application.Service.Reminder.IReminderService;

namespace Express_Service.Controllers;

/// <summary>
/// Admin-only controller for managing maintenance interval rules
/// and the global reminder dashboard.
/// </summary>
[Route("api/maintenance")]
[ApiController]
[Authorize(Roles = "Master,Admin")]
public class MaintenanceReminderController(IReminderService reminderService) : ControllerBase
{
    private readonly IReminderService _reminder = reminderService;

    // ══════════════════════════════════════════════════════════════
    //  INTERVAL MANAGEMENT
    // ══════════════════════════════════════════════════════════════

    /// <summary>List all maintenance interval rules (active + inactive).</summary>
    [HttpGet("intervals")]
    public async Task<IActionResult> GetIntervals()
    {
        var result = await _reminder.GetAllIntervalsAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>Get a single interval by ID.</summary>
    [HttpGet("intervals/{id:int}")]
    public async Task<IActionResult> GetInterval(int id)
    {
        var result = await _reminder.GetIntervalByIdAsync(id);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Create a new maintenance interval rule.
    /// Example: Oil Filter (SparePartId=5) every 5 days, alert 1 day early.
    /// </summary>
    [HttpPost("intervals")]
    public async Task<IActionResult> CreateInterval([FromBody] CreateIntervalRequest request)
    {
        var createdBy = User.Identity?.Name ?? "admin";
        var result = await _reminder.CreateIntervalAsync(request, createdBy);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetInterval), new { id = result.Value.Id }, result.Value)
            : result.ToProblem();
    }

    /// <summary>Update interval timing, alert window, scope or notes.</summary>
    [HttpPut("intervals/{id:int}")]
    public async Task<IActionResult> UpdateInterval(
        int id,
        [FromBody] UpdateIntervalRequest request)
    {
        var updatedBy = User.Identity?.Name ?? "admin";
        var result = await _reminder.UpdateIntervalAsync(id, request, updatedBy);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>Flip an interval between active and inactive without deleting it.</summary>
    [HttpPatch("intervals/{id:int}/toggle")]
    public async Task<IActionResult> ToggleInterval(int id)
    {
        var updatedBy = User.Identity?.Name ?? "admin";
        var result = await _reminder.ToggleIntervalActiveAsync(id, updatedBy);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>Permanently delete an interval.</summary>
    [HttpDelete("intervals/{id:int}")]
    public async Task<IActionResult> DeleteInterval(int id)
    {
        var result = await _reminder.DeleteIntervalAsync(id);
        return result.IsSuccess
            ? Ok(new { message = "Interval deleted successfully." })
            : result.ToProblem();
    }

    // ══════════════════════════════════════════════════════════════
    //  GLOBAL REMINDER DASHBOARD
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Get every vehicle and rider across ALL housings whose maintenance
    /// is due/overdue/upcoming on the given date.
    /// Omit checkDate to use today (KSA time).
    /// </summary>
    [HttpGet("reminders")]
    public async Task<IActionResult> GetAllDueMaintenance(
        [FromQuery] DateOnly? checkDate = null)
    {
        var result = await _reminder.GetAllDueMaintenanceAsync(checkDate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}