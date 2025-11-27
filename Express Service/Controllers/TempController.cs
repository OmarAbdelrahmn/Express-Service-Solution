using Application.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TempController(ITemp service) : ControllerBase
{
    private readonly ITemp service = service;

    [HttpGet("employee")]
    public async Task<IActionResult> GetTempData()
    {
        var result = await service.GetPendingUpdatesAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPut("employee")]
    public async Task<IActionResult> ResolveTempData([FromBody] BulkResolutionRequest request)
    {
        var result = await service.ResolveUpdatesAsync(request);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("employee")]
    public async Task<IActionResult> CreateTempData(IFormFile excelFile)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }
        using var stream = excelFile.OpenReadStream();
        var result = await service.UploadEmployeeExcelAsync(stream,"omar");
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
