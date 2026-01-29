using Application.Extensions;
using Application.Service.SparePart;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Master,Admin,Member")]
public class CostTrackingController(ICostTrackingService service) : ControllerBase
{
    [HttpGet("vehicle/{vehicleNumber}")]
    public async Task<IActionResult> GetVehicleCost(string vehicleNumber)
    {
        var response = await service.GetVehicleCostAsync(vehicleNumber);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("vehicle/{vehicleNumber}/date-range")]
    public async Task<IActionResult> GetVehicleCostByDateRange(
        string vehicleNumber,
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate)
    {
        if (fromDate > toDate)
            return BadRequest("From date must be before or equal to to date");

        var response = await service.GetVehicleCostByDateRangeAsync(vehicleNumber, fromDate, toDate);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("rider/{riderId:int}")]
    public async Task<IActionResult> GetRiderCost(int riderId)
    {
        var response = await service.GetRiderCostAsync(riderId);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("rider/{riderId:int}/date-range")]
    public async Task<IActionResult> GetRiderCostByDateRange(
        int riderId,
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate)
    {
        if (fromDate > toDate)
            return BadRequest("From date must be before or equal to to date");

        var response = await service.GetRiderCostByDateRangeAsync(riderId, fromDate, toDate);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetCostSummary(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate)
    {
        if (fromDate > toDate)
            return BadRequest("From date must be before or equal to to date");

        var response = await service.GetCostSummaryAsync(fromDate, toDate);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }
}