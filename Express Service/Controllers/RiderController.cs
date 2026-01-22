using Application.Contracts.rider;
using Application.Service.Empolyee;
using Application.Service.Riders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]

public class RiderController(IRiderService service) : ControllerBase
{
    private readonly IRiderService service = service;
    
    [HttpGet("")]
    [Authorize(Roles = "Master,Admin,Member")]
 
    public async Task<IActionResult> GetAllRiders()
    {
        var result = await service.GetAllEmployee();
        
        return result.IsSuccess
            ? Ok(result.Value) : result.ToProblem();
    }
    [HttpGet("2")]
    [Authorize(Roles = "Master,Admin,Member")]
 
    public async Task<IActionResult> GetAllRiders2()
    {
        var result = await service.GetAllEmployee2();
        
        return result.IsSuccess
            ? Ok(result.Value) : result.ToProblem();
    }
    [HttpGet("statistics")]
 
    public async Task<IActionResult> GetEmployeeStatistics()
    {
        var result = await service.GetEmployeeStatistics();

        return result.IsSuccess
           ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("inactive")]
    [Authorize(Roles = "Master,Admin,Member")]
 
    public async Task<IActionResult> GetAlRiders()
    {
        var result = await service.GetAllEmployeeNO();
        
        return result.IsSuccess
            ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("id/{id:int}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetRiderById(int id)
    {
        var result = await service.Getbyid(id);
        
        return result.IsSuccess
            ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("iqama/{IqamaNo:long}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetRiderByIqama(long IqamaNo)
    {
        var result = await service.Get(IqamaNo);
        
        return result.IsSuccess
            ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> CreateRider([FromBody] RiderRequest request)
    {
        var result = await service.CreateAsync(request);
        
        return result.IsSuccess
            ? Ok(new Re("Rider Add Successfully")) : result.ToProblem();
    }

    [HttpPut("{IqamaNo:long}")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> UpdateRider(long IqamaNo, [FromBody] URiderRequest request)
    {
        var result = await service.UpdateAsync(IqamaNo, request);
        
        return result.IsSuccess
            ? Ok(result.Value) : result.ToProblem();
    }

    [HttpDelete("{IqamaNo:long}")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> DeleteRider(long IqamaNo , string Reason)
    {
        var result = await service.DeleteAsync(IqamaNo , Reason);
        
        return result.IsSuccess
            ? Ok(new Re("Rider Deleted Successfully")) : result.ToProblem();
    }

    [HttpPost("change-working-id")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> ChangeWorkingId([FromQuery] string oldWorkingId, [FromQuery] string newWorkingId)
    {
        var result = await service.ChangeWorkinId(oldWorkingId, newWorkingId);
        
        return result.IsSuccess
            ? Ok(new Re("Working ID Changed Successfully")) : result.ToProblem();
    }

    [HttpPost("{IqamaNo:long}/add-employee")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> AddETOR(long IqamaNo, [FromBody] EMTOR request)
    {
        var result = await service.AddETOR(IqamaNo, request);
        
        return result.IsSuccess
            ? Ok(new Re("Added Successfully")) : result.ToProblem();
    }

    [HttpGet("smart-search")]
    [Authorize(Roles = "Master,Admin,Member")]
 

    public async Task<IActionResult> SmartSearch([FromQuery] string keyword)
    {
        var result = await service.SmartSearch(keyword);
        
        return Ok(result);
    }


    [HttpGet("search")]
    [Authorize(Roles = "Master,Admin,Member")]
 
    public async Task<IActionResult> Search([FromQuery] EmployeeFilterr Request)
    {
        var response = await service.Filter(Request);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }


}
