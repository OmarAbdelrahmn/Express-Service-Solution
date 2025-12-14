using Application.Contracts.Employees;
using Application.Service.Empolyee;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/vehicles")]
[ApiController]
public class VehicleController : ControllerBase
{
    private readonly IVehicleService _service;

    public VehicleController(IVehicleService service)
    {
        _service = service;
    }

    [HttpPost]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> Create([FromBody] VehicleRequest request)
    {
        var result = await _service.CreateAsync(request);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPut("{plateNumber}")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> Update([FromRoute] string plateNumber, [FromBody] UVehicleRequest request)
    {
        var result = await _service.UpdateAsync(plateNumber, request);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpDelete("{vehicleNumber}")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> Delete([FromRoute] string vehicleNumber, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteAsync(vehicleNumber, cancellationToken);
        return result.IsSuccess ? Ok(new ApiMessage("Deleted successfully")) : result.ToProblem();
    }

    [HttpGet]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetAll()
    {
        var result = await _service.GetAllEmployee();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("chase/{vehicleNumber}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetByNumber([FromRoute] string vehicleNumber)
    {
        var result = await _service.Get(vehicleNumber);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("serial/{serial:int}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetBySerial([FromRoute] int serial)
    {
        var result = await _service.GetSerial(serial);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("plate/{plate}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetByPlate([FromRoute] string plate)
    {
        var result = await _service.Getplate(plate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("available")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetAvailableVehicles()
    {
        var result = await _service.GetAvailableVehiclesAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
    [HttpGet("stolen")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetAvaieVehicles()
    {
        var result = await _service.GetStolenVehiclesAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
    [HttpGet("problem")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetAVehicles()
    {
        var result = await _service.GetProblemVehiclesAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
    [HttpGet("breakup")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetAvableVehicles()
    {
        var result = await _service.GetBreackupVehiclesAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("taken")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetTakenVehicles()
    {
        var result = await _service.GetUnavailableVehiclesAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("vehicle-history/{plate}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetVehicleHistory([FromRoute] string plate)
    {
        var result = await _service.GetVehicleHistoryAsync1(plate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("is-available/{plate}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> IsVehicleAvailable([FromRoute] string plate)
    {
        var result = await _service.IsVehicleAvailableAsync(plate);
        return result.IsSuccess ? Ok(new ApiMessage("This vehicle is available")) : result.ToProblem();
    }

    [HttpPost("take")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> TakeVehicle(
        [FromQuery] long IqamaNo,
        [FromQuery] string vehicleNumber,
        [FromQuery] string reason)
    {
        var result = await _service.TakeVehicleAsync(IqamaNo, vehicleNumber, reason);
        return result.IsSuccess ? Ok(new ApiMessage("Vehicle taken successfully")) : result.ToProblem();
    }

    [HttpPost("return")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> ReturnVehicle(
        [FromQuery] long IqamaNo,
        [FromQuery] string vehicleNumber,
        [FromQuery] string reason)
    {
        var result = await _service.ReturnVehicleAsync(IqamaNo, vehicleNumber, reason);
        return result.IsSuccess ? Ok(new ApiMessage("Vehicle returned successfully")) : result.ToProblem();
    }

    [HttpPut("change-location/{plate}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> ChangeVehicleLocation(
        [FromRoute] string plate,
        [FromQuery] string newLocation)
    {
        var result = await _service.ChangeLocation(plate, newLocation);
        return result.IsSuccess ? Ok(new ApiMessage("Vehicle location updated successfully")) : result.ToProblem();
    }

    [HttpPost("report-problem")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> ReportProblem(
        [FromQuery] long riderIqamaNo,
        [FromQuery] string plate,
        [FromQuery] string reason)
    {
        var result = await _service.ReportProblemAsync(riderIqamaNo, plate, reason);
        return result.IsSuccess ? Ok(new ApiMessage("Problem reported successfully")) : result.ToProblem();
    }

    [HttpPost("fix-problem")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> FixProblem(
        [FromQuery] string plate,
        [FromQuery] string reason)
    {
        var result = await _service.FixVehicleProblemAsync(plate, reason);
        return result.IsSuccess ? Ok(new ApiMessage("Problem fixed successfully")) : result.ToProblem();
    }

    [HttpPost("stolen")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> ReportStolen(
        [FromQuery] string plate,
        [FromQuery] int? reportedByIqamaNo,
        [FromQuery] string? reason)
    {
        var result = await _service.ReportVehicleStolenAsync(plate, reportedByIqamaNo, reason);
        return result.IsSuccess ? Ok(new ApiMessage("Report done successfully")) : result.ToProblem();
    }

    [HttpPost("break-up")]
    [Authorize(Roles = "Master,Admin,Member")]

    public async Task<IActionResult> MarkBroken(
        [FromQuery] string plate,
        [FromQuery] string reason)
    {
        var result = await _service.MarkVehicleAsBreakUpAsync(plate, reason);
        return result.IsSuccess ? Ok(new ApiMessage("Done successfully")) : result.ToProblem();
    }

    [HttpPut("recover-stolen")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> RecoverStolen(
        [FromQuery] string plate,
        [FromQuery] string reason)
    {
        var result = await _service.RecoverStolenVehicleAsync(plate, reason);
        return result.IsSuccess ? Ok(new ApiMessage("Done successfully")) : result.ToProblem();
    }

    [HttpGet("with-riders")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetAllVehiclesWithRiders()
    {
        var result = await _service.GetAllVehiclesWithRidersAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("with-rider/{plate}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetVehicleWithRider([FromRoute] string plate)
    {
        var result = await _service.GetVehicleWithRiderByVehicleNumberAsync(plate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("group-by-status")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetVehiclesGroupedByStatus()
    {
        var result = await _service.GetVehiclesGroupedByStatusAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}

public record ApiMessage(string Message);
