using Application.Contracts.Vacation;
using Application.Extensions;
using Application.Service.Vacation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/vacation-access")]
[ApiController]
[Authorize(Roles = "Master")]
public class VacationAccessController(IVacationService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await service.GetRoleAssignmentsAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPut("users/{userId}")]
    public async Task<IActionResult> Set(string userId, [FromBody] SetVacationRolesRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.SetRolesAsync(actor, userId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
