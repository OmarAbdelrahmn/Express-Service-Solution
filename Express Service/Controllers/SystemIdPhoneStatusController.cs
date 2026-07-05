using Application.Contracts.SystemIdPhoneStatuses;
using Application.Service.SystemIdPhoneStatuses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/system-id-phone-statuses")]
[ApiController]
[Authorize(Roles = "Master,Admin")]
public class SystemIdPhoneStatusController(ISystemIdPhoneStatusService service) : ControllerBase
{
    private string Actor => User.Identity?.Name ?? "Unknown";

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromQuery] DateOnly statusDate,
        [FromBody] CreateSystemIdPhoneStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (statusDate == default)
            return BadRequest(new { error = "statusDate query string is required." });

        var result = await service.CreateAsync(request, statusDate, Actor, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : result.ToProblem();
    }

    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromQuery] DateOnly statusDate,
        CancellationToken cancellationToken = default)
    {
        if (statusDate == default)
            return BadRequest(new { error = "statusDate query string is required." });

        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not ".xlsx" and not ".xls")
            return BadRequest(new { error = "Only .xlsx / .xls files are accepted." });

        List<string> parserWarnings;
        Application.Contracts.SystemIdPhoneStatuses.ImportSystemIdPhoneStatusRequest importRequest;

        try
        {
            await using var stream = file.OpenReadStream();
            (importRequest, parserWarnings) = SystemIdPhoneStatusExcelParser.Parse(stream, statusDate);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Failed to read Excel file: {ex.Message}" });
        }

        if (importRequest.Cells.Count == 0)
            return BadRequest(new
            {
                error = "The file contained no data rows to import.",
                parserWarnings
            });

        var result = await service.ImportAsync(importRequest, Actor, cancellationToken);
        if (!result.IsSuccess)
            return result.ToProblem();

        return Ok(new
        {
            result.Value.TotalCellsProcessed,
            result.Value.RecordsCreated,
            result.Value.BlankCellsSkipped,
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
        [FromQuery] string? systemId,
        [FromQuery] string? phoneNumber,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(systemId, phoneNumber, from, to, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromQuery] DateOnly statusDate,
        [FromBody] UpdateSystemIdPhoneStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (statusDate == default)
            return BadRequest(new { error = "statusDate query string is required." });

        var result = await service.UpdateAsync(id, request, statusDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(new { message = "System ID phone status record deleted successfully." })
            : result.ToProblem();
    }
}
