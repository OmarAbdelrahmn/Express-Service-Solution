using Application.Service.Riders;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ShiftController(IRiderShiftService service) : ControllerBase
{
    private readonly IRiderShiftService service = service;

    [HttpPost("")]
    public async Task<IActionResult> CreateShiftAsync([FromBody] CreateRiderShiftRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateShiftAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{workingId:int}")]
    public async Task<IActionResult> GetShiftAsync(int workingId,[FromQuery] DateOnly shiftDate, CancellationToken cancellationToken)
    {
        var result = await service.GetShiftAsync(workingId , shiftDate , cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("rider/{workingId:int}")]
    public async Task<IActionResult> GetShiftsByRiderAsync(int workingId, CancellationToken cancellationToken)
    {
        var result = await service.GetShiftsByRiderAsync(workingId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("date")]
    public async Task<IActionResult> GetShiftsByDateAsync([FromQuery]DateOnly shiftDate, CancellationToken cancellationToken)
    {
        var result = await service.GetShiftsByDateAsync(shiftDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("")]
    public async Task<IActionResult> UpdateShiftAsync([FromBody] UpdateRiderShiftRequest request, CancellationToken cancellationToken)
    {
        var result = await service.UpdateShiftAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpDelete("{workingId:int}")]
    public async Task<IActionResult> DeleteShiftAsync(int workingId,[FromQuery] DateOnly shiftDate, CancellationToken cancellationToken)
    {
        var result = await service.DeleteShiftAsync(workingId, shiftDate, cancellationToken);
        return result.IsSuccess ? Ok() : BadRequest(result.Error);
    }

    [HttpGet("range")]
    public async Task<IActionResult> GetShiftsByDateRangeAsync([FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate, CancellationToken cancellationToken)
    {
        var result = await service.GetShiftsByDateRangeAsync(startDate, endDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportShiftsFromExcelAsync(IFormFile excelFile ,[FromQuery] DateOnly ShiftDate)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }
        using var stream = excelFile.OpenReadStream();
        var result = await service.ImportShiftsFromExcelAsync(stream, ShiftDate);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpDelete("date")]
    public async Task<IActionResult> DeleteShiftsByDateAsync([FromQuery] DateOnly shiftDate, CancellationToken cancellationToken)
    {
        var result = await service.DeleteShiftsByDateAsync(shiftDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpDelete("range")]
    public async Task<IActionResult> DeleteShiftsByDateRangeAsync([FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate, CancellationToken cancellationToken)
    {
        var result = await service.DeleteShiftsByDateRangeAsync(startDate, endDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpDelete("rider/{workingId:int}/range")]
    public async Task<IActionResult> DeleteShiftsByRiderAndDateRangeAsync(int workingId, [FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate, CancellationToken cancellationToken)
    {
        var result = await service.DeleteShiftsByRiderAndDateRangeAsync(workingId, startDate, endDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("comparisons")]
    public async Task<IActionResult> GetPendingComparisonsAsync([FromQuery] DateOnly shiftDate, CancellationToken cancellationToken)
    {
        var result = await service.GetPendingComparisonsAsync(shiftDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("comparisons/resolve")]
    public async Task<IActionResult> ResolveShiftComparisonsAsync([FromBody] ResolveComparisonsRequest request, CancellationToken cancellationToken)
    {
        var result = await service.ResolveShiftComparisonsAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("comparisons/import")]
    public async Task<IActionResult> CreateShiftComparisonsAsync(IFormFile excelFile,[FromQuery] DateOnly shiftDate, [FromQuery] int rejectionThreshold = 2)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }
        using var stream = excelFile.OpenReadStream();
        var result = await service.CreateShiftComparisonsAsync(stream, shiftDate, rejectionThreshold);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }




}
