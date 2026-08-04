using Application.Contracts.KeetaBreaks;
using Application.Extensions;
using Application.Service.KeetaBreaks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/keeta-breaks")]
[ApiController]
[Authorize(Roles = "Master,Admin")]
public class KeetaBreaksController(IKeetaBreakService service) : ControllerBase
{
    [HttpGet("configurations")]
    public async Task<IActionResult> GetConfigurations(CancellationToken cancellationToken) => await ToActionAsync(service.GetConfigurationsAsync(cancellationToken));

    [HttpPost("configurations")]
    public async Task<IActionResult> CreateConfiguration([FromBody] CreateKeetaBreakConfigurationRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId(); if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        return await ToActionAsync(service.CreateConfigurationAsync(request, actor, cancellationToken));
    }

    [HttpPost("capacity-plans")]
    public async Task<IActionResult> CreateCapacityPlan([FromBody] CreateKeetaBreakCapacityPlanRequest request, CancellationToken cancellationToken) =>
        await ToActionAsync(service.CreateCapacityPlanAsync(request, cancellationToken));

    private static async Task<IActionResult> ToActionAsync<T>(Task<Application.Abstraction.Result<T>> task)
    {
        var result = await task;
        return result.IsSuccess ? new OkObjectResult(result.Value) : result.ToProblem();
    }
}
