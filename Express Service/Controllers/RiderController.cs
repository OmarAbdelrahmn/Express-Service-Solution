using Application.Contracts.rider;
using Application.Service.Riders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class RiderController(IRiderService service) : ControllerBase
{
    private readonly IRiderService service = service;
    
    [HttpGet("")]
    public async Task<IActionResult> GetAllRiders()
    {
        var result = await service.GetAllEmployee();
        
        return result.IsSuccess
            ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("id/{id:int}")]
    public async Task<IActionResult> GetRiderById(int id)
    {
        var result = await service.Getbyid(id);
        
        return result.IsSuccess
            ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("iqama/{iqamaNo:int}")]
    public async Task<IActionResult> GetRiderByIqama(int iqamaNo)
    {
        var result = await service.Get(iqamaNo);
        
        return result.IsSuccess
            ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("")]
    public async Task<IActionResult> CreateRider([FromBody] RiderRequest request)
    {
        var result = await service.CreateAsync(request);
        
        return result.IsSuccess
            ? Ok(new Re("Rider Add Successfully")) : result.ToProblem();
    }

    [HttpPut("{iqamaNo:int}")]
    public async Task<IActionResult> UpdateRider(int iqamaNo, [FromBody] URiderRequest request)
    {
        var result = await service.UpdateAsync(iqamaNo, request);
        
        return result.IsSuccess
            ? Ok(result.Value) : result.ToProblem();
    }

    [HttpDelete("{iqamaNo:int}")]
    public async Task<IActionResult> DeleteRider(int iqamaNo)
    {
        var result = await service.DeleteAsync(iqamaNo);
        
        return result.IsSuccess
            ? Ok(new Re("Rider Deleted Successfully")) : result.ToProblem();
    }

    [HttpPost("change-working-id")]
    public async Task<IActionResult> ChangeWorkingId([FromQuery] int oldWorkingId, [FromQuery] int newWorkingId)
    {
        var result = await service.ChangeWorkinId(oldWorkingId, newWorkingId);
        
        return result.IsSuccess
            ? Ok(new Re("Working ID Changed Successfully")) : result.ToProblem();
    }

    [HttpPost("{iqamaNo:int}/add-employee")]
    public async Task<IActionResult> AddETOR(int iqamaNo, [FromBody] EMTOR request)
    {
        var result = await service.AddETOR(iqamaNo, request);
        
        return result.IsSuccess
            ? Ok(new Re("Added Successfully")) : result.ToProblem();
    }

    [HttpGet("smart-search")]
    public async Task<IActionResult> SmartSearch([FromQuery] string keyword)
    {
        var result = await service.SmartSearch(keyword);
        
        return Ok(result);
    }


}
