using Application.Service.Riders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Master,Admin")]
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
    [HttpGet("history/{RiderworkingId}")]
    public async Task<IActionResult> GetHistory(string RiderworkingId)
    {
        var result = await service.GetSubstitutionHistory(RiderworkingId);
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

    [HttpPut("{WorkingId}/stop")]
    public async Task<IActionResult> StopSubstitution(string WorkingId)
    {
        var result = await service.StopSubstitutionByWorkingId(WorkingId);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

}
