using Application.Contracts.RiderScorePerformance;
using Application.Service.RiderScorePerformance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Master,Admin")]
public class RiderScorePerformanceController(IRiderScorePerformanceService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateRiderScorePerformanceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] RiderScorePerformanceFilter filter,
        CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(filter, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateRiderScorePerformanceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : result.ToProblem();
    }

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Import(
        IFormFile excelFile,
        [FromQuery] DateOnly performanceDate,
        CancellationToken cancellationToken)
    {
        if (excelFile is null || excelFile.Length == 0)
            return BadRequest("No file uploaded.");

        var extension = Path.GetExtension(excelFile.FileName);
        if (!extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only .xlsx and .xlsm Excel files are supported.");

        using var stream = excelFile.OpenReadStream();
        var result = await service.ImportAsync(stream, performanceDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
