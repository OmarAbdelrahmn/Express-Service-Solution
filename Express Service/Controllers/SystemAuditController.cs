using Application.Contracts.SystemAudit;
using Application.Service.SystemAudit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/system-audit")]
[ApiController]
[Authorize(Roles = "Master")]
public class SystemAuditController(ISystemAuditService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] SystemAuditQuery query, CancellationToken cancellationToken)
    {
        var result = await service.GetAllAsync(query, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
