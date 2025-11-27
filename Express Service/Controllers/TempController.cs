using Application.Service;
using Application.Service.Empolyee;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TempController(ITemp service , IEmployeeService service1 , IVehicleService service2) : ControllerBase
{
    private readonly ITemp service = service;
    private readonly IEmployeeService service1 = service1;
    private readonly IVehicleService service2 = service2;

    [HttpGet("employee")]
    public async Task<IActionResult> GetTempData()
    {
        var result = await service.GetPendingUpdatesAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPut("employee")]
    public async Task<IActionResult> ResolveTempData([FromBody] BulkResolutionRequest request)
    {
        var result = await service.ResolveUpdatesAsync(request);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("employee")]
    public async Task<IActionResult> CreateTempData(IFormFile excelFile)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }
        using var stream = excelFile.OpenReadStream();
        var result = await service.UploadEmployeeExcelAsync(stream,"omar");
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("employee-request-enable/{iqamaNo:int}")]
    public async Task<IActionResult> RequestEnableEmployee(int iqamaNo, string Reason, string RequestedBy)
    {
        var response = await service1.RequestEnableEmployeeAsync(iqamaNo, Reason, RequestedBy);
        return response.IsSuccess ?
            Ok(new Re("Enable request submitted successfully.")) :
            response.ToProblem();
    }

    [HttpDelete("employee-request-disable/{iqamaNo:int}")]
    public async Task<IActionResult> RequestDisableEmployee(int iqamaNo, string Reason, string RequestedBy)
    {
        var response = await service1.RequestDisableEmployeeAsync(iqamaNo, Reason, RequestedBy);
        return response.IsSuccess ?
            Ok(new Re("Disable request submitted successfully.")) :
            response.ToProblem();
    }

    [HttpGet("employee-pending-status-changes")]
    public async Task<IActionResult> GetPendingStatusChanges()
    {
        var response = await service1.GetPendingStatusChangesAsync();
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpPost("employee-resolve-status-changes")]
    public async Task<IActionResult> ResolveStatusChanges([FromBody] EBulkResolutionRequest request)
    {
        var response = await service1.ResolveStatusChangesAsync(request);
        return response.IsSuccess ?
            Ok(new Re("done ....")) :
            response.ToProblem();
    }

    [HttpPost("vehicle-request-return")]
    public async Task<IActionResult> Vehicleretrunrequest(VehicleResolutionRequest request, string reason = "leave the work")
    {
        var response = await service2.RequestReturnVehicleAsync(request,reason);
        return response.IsSuccess ?
            Ok(new Re("done ....")) :
            response.ToProblem();
    }
    
    
    [HttpPost("vehicle-request-take")]
    public async Task<IActionResult> VehicleTakerequest(VehicleResolutionRequest request, string reason = "work")
    {
        var response = await service2.RequestTakeVehicleAsync(request,reason);
        return response.IsSuccess ?
            Ok(new Re("done ....")) :
            response.ToProblem();
    }


    [HttpPost("vehicle-request-problem")]
    public async Task<IActionResult> Vehicleproblemrequest(VehicleResolutionRequest request, string reason = "problem at vichle")
    {
        var response = await service2.RequestReportProblemAsync(request,reason);
        return response.IsSuccess ?
            Ok(new Re("done ....")) :
            response.ToProblem();
    }

    [HttpGet("vehicles")]
    public async Task<IActionResult> getv()
    {
        var response = await service2.GetPendingOperationsAsync();
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }
    
    
    [HttpGet("vehicle-resolve")]
    public async Task<IActionResult> resolvev(VehicleResolutionRequest request, string? note)
    {
        var response = await service2.ResolveOperationAsync(request,note);
        return response.IsSuccess ?
            Ok(new Re("done....")) :
            response.ToProblem();
    }


}
