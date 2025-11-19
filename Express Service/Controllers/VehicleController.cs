using Application.Contracts.Employees;
using Application.Service.Empolyee;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("[controller]")]
[ApiController]
public class VehicleController(IVehicleService service) : ControllerBase
{
    private readonly IVehicleService service = service;

    [HttpPost("Create")]
    public async Task<IActionResult> Create(VehicleRequest request)
    {
        var result = await service.CreateAsync(request);
        
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpDelete("{vehicleNumber}")]
    public async Task<IActionResult> Delete(string vehicleNumber, CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(vehicleNumber, cancellationToken);
        
        return result.IsSuccess ? Ok(new Re("Deleted successfully")) : result.ToProblem();
    }
  
    [HttpGet("chase/{vehicleNumber}")]
    public async Task<IActionResult> Get(string vehicleNumber)
    {
        var result = await service.Get(vehicleNumber);
        
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
    
    [HttpGet("serial/{Serial}")]
    public async Task<IActionResult> Get1(int Serial)
    {
        var result = await service.GetSerial(Serial);
        
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
    
    [HttpGet("plate/{plate}")]
    public async Task<IActionResult> Get2(string plate)
    {
        var result = await service.Getplate(plate);
        
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("")]
    public async Task<IActionResult> GetAll()
    {
        var result = await service.GetAllEmployee();
        
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPut("{PlateNumberA}")]
    public async Task<IActionResult> Update(string PlateNumberA, UVehicleRequest request)
    {
        var result = await service.UpdateAsync(PlateNumberA, request);
        
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }


    [HttpPost("take-vehicle")]
    public async Task<IActionResult> TakeVehicle(int iqamaNo, string VehicleNumber, string reason)
    {
        var result = await service.TakeVehicleAsync(iqamaNo, VehicleNumber, reason);
        
        return result.IsSuccess ? Ok(new Re("Vehicle taken successfully")) : result.ToProblem();
    }

    [HttpPost("return-vehicle")]
    public async Task<IActionResult> ReturnVehicle(int iqamaNo, string VehicleNumber , string reason)
    {
        var result = await service.ReturnVehicleAsync(iqamaNo, VehicleNumber,reason);
        
        return result.IsSuccess ? Ok(new Re("Vehicle returned successfully")) : result.ToProblem();
    }

    [HttpGet("taken-vehicles")]
    public async Task<IActionResult> GetTakenVehicles()
    {
        var result = await service.GetUnavailableVehiclesAsync();
        
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("available-vehicles")]
    public async Task<IActionResult> GetAvailableVehicles()
    {
        var result = await service.GetAvailableVehiclesAsync();
        
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    //[HttpGet("vehicle-history/{PlateNumberA}")]
    //public async Task<IActionResult> GetVehicleHistory(string PlateNumberA)
    //{
    //    var result = await service.GetVehicleHistoryAsync(PlateNumberA);
        
    //    return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    //}
    
    [HttpGet("vehicle-history/{PlateNumberA}")]
    public async Task<IActionResult> GetVehicleHistory2(string PlateNumberA)
    {
        var result = await service.GetVehicleHistoryAsync1(PlateNumberA);
        
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("is-available/{PlateNumberA}")]
    public async Task<IActionResult> GetEmployeeVehicles(string PlateNumberA)
    {
        var result = await service.IsVehicleAvailableAsync(PlateNumberA);
        
        return result.IsSuccess ? Ok(new Re("this vehicle is available")) : result.ToProblem();
    }
    
    [HttpPut("change-location/{PlateNumberA}")]
    public async Task<IActionResult> ChangeVehicleLocation(string PlateNumberA, string NewLocation)
    {
        var result = await service.ChangeLocation(PlateNumberA, NewLocation);
        
        return result.IsSuccess ? Ok(new Re("Vehicle location updated successfully")) : result.ToProblem();
    }

    [HttpPost("report-problem")]
    public async Task<IActionResult> ReportVehicleProblem(int RideriqamaNo, string PlateNumberA, string Reason)
    {
        var result = await service.ReportProblemAsync(RideriqamaNo, PlateNumberA, Reason);
        
        return result.IsSuccess ? Ok(new Re("Problem reported successfully")) : result.ToProblem();
    }

    [HttpPost("fix-problem")]
    public async Task<IActionResult> FixVehicleProblem(string PlateNumberA, string Reason)
    {
        var result = await service.FixVehicleProblemAsync(PlateNumberA, Reason);
        
        return result.IsSuccess ? Ok(new Re("Problem fixed successfully")) : result.ToProblem();
    }

    [HttpGet("all-vehicles-with-riders")]
    public async Task<IActionResult> GetAllVehiclesWithRidersAsync()
    {
        var result = await service.GetAllVehiclesWithRidersAsync();
        
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("vehicle-with-rider/{PlateNumberA}")]
    public async Task<IActionResult> GetVehicleWithRider(string PlateNumberA)
    {
        var result = await service.GetVehicleWithRiderByVehicleNumberAsync(PlateNumberA);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("stole-report")]
    public async Task<IActionResult> reportstolenvehicle(string PlateNumberA, int? reportedByIqamaNo, string? reason)
    {
        var result = await service.ReportVehicleStolenAsync(PlateNumberA , reportedByIqamaNo , reason);

        return result.IsSuccess ? Ok(new Re("Report done successfully")) : result.ToProblem();
    }
    
    [HttpPost("break-up")]
    public async Task<IActionResult> Breakthevehicleup(string PlateNumberA, string reason)
    {
        var result = await service.MarkVehicleAsBreakUpAsync(PlateNumberA , reason);

        return result.IsSuccess ? Ok(new Re("done successfully")) : result.ToProblem();
    }

    [HttpPut("recover-stolen")]
    public async Task<IActionResult> recoveryStolen(string PlateNumberA, string reason)
    {
        var result = await service.RecoverStolenVehicleAsync(PlateNumberA, reason);

        return result.IsSuccess ? Ok(new Re("done successfully")) : result.ToProblem();
    }

    [HttpGet("group-by-status")]
    public async Task<IActionResult> GetVehiclesGroupedByStatusAsync()
    {
        var result = await service.GetVehiclesGroupedByStatusAsync();

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

}
