using Application.Contracts.OutageShiftPerformances;
using Application.Service.OutageShiftPerformances;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/outage-shift-performances")]
[ApiController]
[Authorize(Roles = "Master,Admin")]
public class OutageShiftPerformanceController(IOutageShiftPerformanceService service) : ControllerBase
{
    private string Actor => User.Identity?.Name ?? "Unknown";

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromQuery] DateOnly shiftDate,
        [FromBody] CreateOutageShiftPerformanceRequest request,
        CancellationToken cancellationToken)
    {
        if (shiftDate == default)
            return BadRequest(new { error = "shiftDate query string is required." });

        var result = await service.CreateAsync(request, shiftDate, Actor, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : result.ToProblem();
    }

    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromQuery] DateOnly shiftDate,
        CancellationToken cancellationToken = default)
    {
        if (shiftDate == default)
            return BadRequest(new { error = "shiftDate query string is required." });

        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not ".xlsx" and not ".xls")
            return BadRequest(new { error = "Only .xlsx / .xls files are accepted." });

        List<string> parserWarnings;
        ImportOutageShiftPerformanceRequest importRequest;

        try
        {
            await using var stream = file.OpenReadStream();
            (importRequest, parserWarnings) = OutageShiftPerformanceExcelParser.Parse(stream, shiftDate);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Failed to read Excel file: {ex.Message}" });
        }

        if (importRequest.Rows.Count == 0)
            return BadRequest(new
            {
                error = "The file contained no valid rows to import.",
                parserWarnings
            });

        var result = await service.ImportAsync(importRequest, Actor, cancellationToken);
        if (!result.IsSuccess)
            return result.ToProblem();

        return Ok(new
        {
            result.Value.TotalRowsProcessed,
            result.Value.RecordsCreated,
            Warnings = result.Value.Warnings.Concat(parserWarnings).ToList(),
            ParserWarningsOnly = parserWarnings
        });
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
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(riderId, from, to, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromQuery] DateOnly shiftDate,
        [FromBody] UpdateOutageShiftPerformanceRequest request,
        CancellationToken cancellationToken)
    {
        if (shiftDate == default)
            return BadRequest(new { error = "shiftDate query string is required." });

        var result = await service.UpdateAsync(id, request, shiftDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(new { message = "Outage shift performance record deleted successfully." })
            : result.ToProblem();
    }
}
