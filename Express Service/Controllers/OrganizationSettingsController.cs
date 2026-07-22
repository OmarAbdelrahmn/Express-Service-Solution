using Application.Contracts.Organization;
using Application.Extensions;
using Application.Service.Organization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/organization-settings")]
[ApiController]
[Authorize]
public class OrganizationSettingsController(IOrganizationSettingsService service) : ControllerBase
{
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        if (string.IsNullOrWhiteSpace(actor)) return Unauthorized();
        var result = await service.GetCurrentAsync(actor, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("tenants/{tenantId:int}")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetTenant(int tenantId, CancellationToken cancellationToken)
    {
        var result = await service.GetTenantAsync(tenantId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("tenants")]
    [Authorize(Roles = "Master,Accountant")]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateTenantAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("legal-entities")]
    [Authorize(Roles = "Master,Accountant")]
    public async Task<IActionResult> CreateLegalEntity([FromBody] CreateLegalEntityRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateLegalEntityAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("branches")]
    [Authorize(Roles = "Master,Accountant")]
    public async Task<IActionResult> CreateBranch([FromBody] CreateBranchRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateBranchAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("platform-accounts")]
    [Authorize(Roles = "Master,Accountant")]
    public async Task<IActionResult> CreatePlatformAccount([FromBody] CreatePlatformAccountRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreatePlatformAccountAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("legacy-company-mappings")]
    [Authorize(Roles = "Master,Accountant")]
    public async Task<IActionResult> CreateLegacyCompanyMapping([FromBody] CreateLegacyCompanyPlatformMappingRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateLegacyCompanyPlatformMappingAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
