using Application.Contracts.DailyWork;
using Application.Service.DailyWork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[ApiController]
[Route("api/daily-work")]
[Authorize(Roles = "Master,Admin")]
public class DailyWorkController(IDailyWorkService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly date, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(date, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPut]
    public async Task<IActionResult> Edit([FromBody] EditDailyWorkRequest request, CancellationToken cancellationToken)
    {
        var result = await service.EditAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
