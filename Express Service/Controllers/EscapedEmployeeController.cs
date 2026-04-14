using Application.Contracts.Employees;
using Application.Extensions;
using Application.Service.Escaped;
using Application.Service.EscapedEmployee;
using Domain.Entities;
using Express_Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[ApiController]
[Route("api/escaped")]
[Authorize]
public class EscapedEmployeeController(IEscapedEmployeeService service) : ControllerBase
{
    private readonly IEscapedEmployeeService _service = service;

    // GET api/escaped
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await _service.GetAllEscapedAsync(ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // GET api/escaped/stats
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var result = await _service.GetStatsAsync(ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // GET api/escaped/overdue
    [HttpGet("overdue")]
    public async Task<IActionResult> GetOverdue(CancellationToken ct)
    {
        var result = await _service.GetOverdueAsync(ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // GET api/escaped/by-path/{path}
    // path: 0 = None, 1 = Reported, 2 = Outage
    [HttpGet("by-path/{path:int}")]
    public async Task<IActionResult> GetByPath(int path, CancellationToken ct)
    {
        if (!Enum.IsDefined(typeof(EscapedPath), path))
            return BadRequest("Invalid path value. Use 0 (None), 1 (Reported), or 2 (Outage).");

        var result = await _service.GetByPathAsync((EscapedPath)path, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // PUT api/escaped/{iqamaNo}/reported
    [HttpPut("{iqamaNo:long}/reported")]
    public async Task<IActionResult> SetReported(
        long iqamaNo,
        [FromBody] SetReportedPathRequest request,
        CancellationToken ct)
    {
        var result = await _service.SetReportedPathAsync(iqamaNo, request, ct);
        return result.IsSuccess ? NoContent() : result.ToProblem();
    }

    // PUT api/escaped/{iqamaNo}/outage
    [HttpPut("{iqamaNo:long}/outage")]
    public async Task<IActionResult> SetOutage(
        long iqamaNo,
        [FromBody] SetOutagePathRequest request,
        CancellationToken ct)
    {
        var result = await _service.SetOutagePathAsync(iqamaNo, request, ct);
        return result.IsSuccess ? NoContent() : result.ToProblem();
    }

    // PUT api/escaped/{iqamaNo}/switch-path
    [HttpPut("{iqamaNo:long}/switch-path")]
    public async Task<IActionResult> SwitchPath(
        long iqamaNo,
        [FromBody] SwitchPathRequest request,
        CancellationToken ct)
    {
        var result = await _service.SwitchPathAsync(iqamaNo, request, ct);
        return result.IsSuccess ? NoContent() : result.ToProblem();
    }

    // PATCH api/escaped/{iqamaNo}/notes
    [HttpPatch("{iqamaNo:long}/notes")]
    public async Task<IActionResult> UpdateNotes(
        long iqamaNo,
        [FromBody] UpdateNotesRequest request,
        CancellationToken ct)
    {
        var result = await _service.UpdateNotesAsync(iqamaNo, request.Notes, ct);
        return result.IsSuccess ? NoContent() : result.ToProblem();
    }

    // DELETE api/escaped/{iqamaNo}
    [HttpDelete("{iqamaNo:long}")]
    public async Task<IActionResult> Remove(
        long iqamaNo,
        [FromQuery] string removedBy,
        CancellationToken ct)
    {
        var result = await _service.RemoveEscapedEmployeeAsync(iqamaNo, removedBy, ct);
        return result.IsSuccess ? NoContent() : result.ToProblem();
    }

    // POST api/escaped/backfill-fleeing
    [HttpPost("backfill-fleeing")]
    [AllowAnonymous]
    public async Task<IActionResult> BackfillFleeingEmployees(
        [FromQuery] string createdBy,
        CancellationToken ct)
    {
        var result = await _service.BackfillFleeingEmployeesAsync(createdBy, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // PATCH api/escaped/{iqamaNo}/deactivate
    [HttpPatch("{iqamaNo:long}/deactivate")]
    public async Task<IActionResult> Deactivate(
        long iqamaNo,
        CancellationToken ct)
    {
        var userid = User.GetUserId();
        var result = await _service.DeactivateEscapedEmployeeAsync(iqamaNo, userid!, ct);
        return result.IsSuccess ? NoContent() : result.ToProblem();
    }

    // DELETE api/escaped/{iqamaNo}/force
    [HttpDelete("{iqamaNo:long}/force")]
    [Authorize(Roles = "Admin")]          
    public async Task<IActionResult> ForceDelete(
        long iqamaNo,
        CancellationToken ct)
    {
        var result = await _service.ForceDeleteEscapedEmployeeAsync(
            iqamaNo, ct);
        return result.IsSuccess ? NoContent() : result.ToProblem();
    }
}

public record UpdateNotesRequest(string Notes);