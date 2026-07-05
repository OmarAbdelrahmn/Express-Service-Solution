using Application.Contracts.OutRiderInfos;
using Application.Service.OutRiderInfos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/out-rider-infos")]
[ApiController]
[Authorize(Roles = "Master,Admin")]
public class OutRiderInfoController(IOutRiderInfoService service) : ControllerBase
{
    private string Actor => User.Identity?.Name ?? "Unknown";

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateOutRiderInfoRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, Actor, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : result.ToProblem();
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? riderId,
        [FromQuery] string? phoneNumber,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(riderId, phoneNumber, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateOutRiderInfoRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(new { message = "Out rider info record deleted successfully." })
            : result.ToProblem();
    }
}
