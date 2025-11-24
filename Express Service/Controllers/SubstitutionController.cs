using Application.Service.Riders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class SubstitutionController(IRiderSub service) : ControllerBase
{
    private readonly IRiderSub service = service;

    [HttpGet("")]
    public async Task<IActionResult> GetAll()
    {
        var result = await service.GetAllSubstitutions();
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var result = await service.GetActiveSubstitutions();
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }
    [HttpGet("inactive")]
    public async Task<IActionResult> GetInactive()
    {
        var result = await service.GetInactiveSubstitutions();
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }
    [HttpGet("history/{RiderWorkingId:int}")]
    public async Task<IActionResult> GetHistory(int RiderWorkingId)
    {
        var result = await service.GetSubstitutionHistory(RiderWorkingId);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }
    [HttpPost("")]
    public async Task<IActionResult> StartSubstitution(StartSubstitutionRequest request)
    {
        var result = await service.StartSubstitution(request);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpPut("{workingId}/stop")]
    public async Task<IActionResult> StopSubstitution(int workingId)
    {
        var result = await service.StopSubstitutionByWorkingId(workingId);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

}
