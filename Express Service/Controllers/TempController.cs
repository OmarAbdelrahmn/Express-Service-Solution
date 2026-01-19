using Application.Extensions;
using Application.Service.Empolyee;
using Application.Service.temp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TempController(ITemp service, IEmployeeService service1, IVehicleService service2) : ControllerBase
{
    private readonly ITemp service = service;
    private readonly IEmployeeService service1 = service1;
    private readonly IVehicleService service2 = service2;

    [HttpGet("employees")]
    [Authorize(Roles = "Master,Admin")]
 
    public async Task<IActionResult> GetTempData()
    {
        var result = await service.GetPendingUpdatesAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPut("employees")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> ResolveTempData([FromBody] BulkResolutionRequest request)
    {
        var result = await service.ResolveUpdatesAsync(request);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("import-employees")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> CreateTempData(IFormFile excelFile)
    {
        var UserId = User.GetUserId();
        if (excelFile == null || excelFile.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }
        using var stream = excelFile.OpenReadStream();
        var result = await service.UploadEmployeeExcelAsync(stream, UserId!);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("request-change")]
    [Authorize(Roles = "Member")]
    public async Task<IActionResult> RequestStatusChange([FromBody] StatusChangeRequest request)
    {
        var result = await service1.RequestStatusChangeAsync(
            request.IqamaNo,
            request.NewStatus,
            request.Reason,
            request.RequestedBy
        );

        return result.IsSuccess ? Ok() : result.ToProblem();
    }

    [HttpGet("employee-pending-status-changes")]
    [Authorize(Roles = "Master,Admin")]
 
    public async Task<IActionResult> GetPendingStatusChanges()
    {
        var response = await service1.GetPendingStatusChangesAsync();
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpPost("employee-resolve-status-changes")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> ResolveStatusChange([FromBody] ResolveStatusChangeRequest request)
    {
        var result = await service1.ResolveStatusChangeAsync(
            request.IqamaNo,
            request.Resolution,
            request.ResolvedBy,
            request.AdminNotes
        );

        return result.IsSuccess ? Ok() : result.ToProblem();
    }

    [HttpPost("vehicle-request-return")]
    [Authorize(Roles = "Member")]
    public async Task<IActionResult> Vehicleretrunrequest([FromBody] SVehicleResolutionRequest request, [FromQuery] string reason = "leave the work")
    {
        var userId = User.GetUserId();
        var response = await service2.RequestReturnVehicleAsync(request,userId!, reason);
        return response.IsSuccess ?
            Ok(new Re("done ....")) :
            response.ToProblem();
    }

    [HttpPost("vehicle-request-take")]
    [Authorize(Roles = "Member")]
    public async Task<IActionResult> VehicleTakerequest([FromBody] SVehicleResolutionRequest request, [FromQuery] string reason = "work")
    {
        var userId = User.GetUserId();
        var response = await service2.RequestTakeVehicleAsync(request,userId!, reason);
        return response.IsSuccess ?
            Ok(new Re("done ....")) :
            response.ToProblem();
    }

    [HttpPost("vehicle-request-problem")]
    [Authorize(Roles = "Member")]
    public async Task<IActionResult> Vehicleproblemrequest([FromBody] SVehicleResolutionRequest request, [FromQuery] string reason = "problem at vichle")
    {
        var userId = User.GetUserId();

        var response = await service2.RequestReportProblemAsync(request,userId!, reason);
        return response.IsSuccess ?
            Ok(new Re("done ....")) :
            response.ToProblem();
    }

    [HttpGet("vehicles")]
    [Authorize(Roles = "Master,Admin")]
 
    public async Task<IActionResult> getv()
    {
        var response = await service2.GetPendingOperationsAsync();
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpPut("vehicle-resolve")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> resolvev([FromBody] VehicleResolutionRequest request)
    {
        var response = await service2.ResolveOperationAsync(request);
        return response.IsSuccess ?
            Ok(new Re("done....")) :
            response.ToProblem();
    }
}

public record StatusChangeRequest(
    long IqamaNo,
    string NewStatus,
    string Reason,
    string RequestedBy
);

public record ResolveStatusChangeRequest(
    long IqamaNo,
    string Resolution,
    string ResolvedBy,
    string? AdminNotes = null
);

