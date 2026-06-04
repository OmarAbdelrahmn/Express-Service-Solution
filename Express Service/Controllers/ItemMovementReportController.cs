using Application.Service.SparePart;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ItemMovementReportController(IItemMovementReportService service) : ControllerBase
{
    [HttpGet("full")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetFullReport(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] string? itemName = null,
        [FromQuery] string? location = null)
    {
        if (fromDate > toDate)
            return BadRequest("fromDate must be before or equal to toDate.");

        var result = await service.GetFullReportAsync(fromDate, toDate, itemName, location);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("spare-parts")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetSparePartReport(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] string? itemName = null,
        [FromQuery] string? location = null)
    {
        if (fromDate > toDate)
            return BadRequest("fromDate must be before or equal to toDate.");

        var result = await service.GetSparePartReportAsync(fromDate, toDate, itemName, location);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("accessories")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetAccessoryReport(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] string? itemName = null,
        [FromQuery] string? location = null)
    {
        if (fromDate > toDate)
            return BadRequest("fromDate must be before or equal to toDate.");

        var result = await service.GetAccessoryReportAsync(fromDate, toDate, itemName, location);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}