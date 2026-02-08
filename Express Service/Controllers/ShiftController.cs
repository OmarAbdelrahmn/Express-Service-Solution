using Application.Service.Riders;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
//[Authorize(Roles = "Master,Admin")]
public class ShiftController(IRiderShiftService service) : ControllerBase
{
    private readonly IRiderShiftService service = service;

    [HttpPost("")]
    public async Task<IActionResult> CreateShiftAsync([FromBody] CreateRiderShiftRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateShiftAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
    [HttpPost("update-stacked")]
    public async Task<IActionResult> UpdateStackedDeliveriesFromExcelAsync(
    IFormFile excelFile,
    CancellationToken cancellationToken)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }

        using var stream = excelFile.OpenReadStream();
        var result = await service.UpdateStackedDeliveriesFromExcelAsync(stream, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("{WorkingId}")]
 
    public async Task<IActionResult> GetShiftAsync(string WorkingId,[FromQuery] DateOnly shiftDate, CancellationToken cancellationToken)
    {
        var result = await service.GetShiftAsync(WorkingId , shiftDate , cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("rider/{WorkingId}")]
 
    public async Task<IActionResult> GetShiftsByRiderAsync(string WorkingId, CancellationToken cancellationToken)
    {
        var result = await service.GetShiftsByRiderAsync(WorkingId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("date")]
    public async Task<IActionResult> GetShiftsByDateAsync([FromQuery]DateOnly shiftDate, CancellationToken cancellationToken)
    {
        var result = await service.GetShiftsByDateAsync(shiftDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPut("")]
    public async Task<IActionResult> UpdateShiftAsync([FromBody] UpdateRiderShiftRequest request, CancellationToken cancellationToken)
    {
        var result = await service.UpdateShiftAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpDelete("{WorkingId}")]
    public async Task<IActionResult> DeleteShiftAsync(string WorkingId,[FromQuery] DateOnly shiftDate, CancellationToken cancellationToken)
    {
        var result = await service.DeleteShiftAsync(WorkingId, shiftDate, cancellationToken);
        return result.IsSuccess ? Ok() : result.ToProblem();
    }

    [HttpGet("range")]
 
    public async Task<IActionResult> GetShiftsByDateRangeAsync([FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate, CancellationToken cancellationToken)
    {
        var result = await service.GetShiftsByDateRangeAsync(startDate, endDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
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
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }


    [HttpPost("update")]
    public async Task<IActionResult> updateShiftsFromExcelAsync(IFormFile excelFile)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }
        using var stream = excelFile.OpenReadStream();
        var result = await service.UpdateShiftsFromExcelAsync(stream);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpDelete("date")]
    public async Task<IActionResult> DeleteShiftsByDateAsync(
        [FromQuery] DateOnly shiftDate,
        [FromQuery] int? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await service.DeleteShiftsByDateAsync(shiftDate, companyId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }


    [HttpDelete("range")]
 
    public async Task<IActionResult> DeleteShiftsByDateRangeAsync([FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate, CancellationToken cancellationToken)
    {
        var result = await service.DeleteShiftsByDateRangeAsync(startDate, endDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // Add these endpoints to ShiftController class

    [HttpGet("accepted/date")]
 
    public async Task<IActionResult> GetAcceptedOrdersByDateAsync(
        [FromQuery] DateOnly shiftDate,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAcceptedOrdersByDateAsync(shiftDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("accepted/previous-day")]
 
    public async Task<IActionResult> GetPreviousDayAcceptedOrdersAsync(
        CancellationToken cancellationToken)
    {
        var result = await service.GetPreviousDayAcceptedOrdersAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("accepted/rider/{workingId}")]
    public async Task<IActionResult> GetAcceptedOrdersByRiderAndDateAsync(
        string workingId,
        [FromQuery] DateOnly shiftDate,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAcceptedOrdersByRiderAndDateAsync(
            workingId, shiftDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("accepted/rider/{workingId}/previous-day")]
    public async Task<IActionResult> GetPreviousDayAcceptedOrdersByRiderAsync(
        string workingId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetPreviousDayAcceptedOrdersByRiderAsync(
            workingId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpDelete("rider/{WorkingId}/range")]
    public async Task<IActionResult> DeleteShiftsByRiderAndDateRangeAsync(string WorkingId, [FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate, CancellationToken cancellationToken)
    {
        var result = await service.DeleteShiftsByRiderAndDateRangeAsync(WorkingId, startDate, endDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("comparisons")]
 
    public async Task<IActionResult> GetPendingComparisonsAsync([FromQuery] DateOnly shiftDate, CancellationToken cancellationToken)
    {
        var result = await service.GetPendingComparisonsAsync(shiftDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("comparisons/resolve")]
    public async Task<IActionResult> ResolveShiftComparisonsAsync([FromBody] ResolveComparisonsRequest request, CancellationToken cancellationToken)
    {
        var result = await service.ResolveShiftComparisonsAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
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
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }




}
