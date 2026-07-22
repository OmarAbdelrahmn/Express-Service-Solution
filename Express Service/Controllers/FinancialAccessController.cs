using Application.Contracts.FinancialAccess;
using Application.Extensions;
using Application.Service.FinancialAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/financial-access")]
[ApiController]
[Authorize(Roles = "Master")]
public class FinancialAccessController(IFinancialAccessService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Grant([FromBody] GrantFinancialUserAccessRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor))
            return Unauthorized();

        var result = await service.GrantAsync(request, actor, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("legal-entities/{legalEntityId:int}")]
    public async Task<IActionResult> GetForLegalEntity(int legalEntityId, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor))
            return Unauthorized();

        var result = await service.GetForLegalEntityAsync(legalEntityId, actor, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpDelete("legal-entities/{legalEntityId:int}/users/{userId}")]
    public async Task<IActionResult> Revoke(string userId, int legalEntityId, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor))
            return Unauthorized();

        var result = await service.RevokeAsync(userId, legalEntityId, actor, cancellationToken);
        return result.IsSuccess ? NoContent() : result.ToProblem();
    }
}
