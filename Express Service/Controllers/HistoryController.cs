using Application.Service.Riders;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class HistoryController(IRiderWorkingIdHistoryService service) : ControllerBase
{
    private readonly IRiderWorkingIdHistoryService service = service;

    [HttpGet("who-has/{workingId}")]
    public async Task<IActionResult> WhoHasWorkingId(string workingId)
    {
        var result = await service.WhoHasWorkingId(workingId);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("suggest-working-id")]
    public async Task<IActionResult> SuggestWorkingId(
        [FromQuery] long riderIqamaNo,
        [FromQuery] int companyId)
    {
        var result = await service.SuggestWorkingIdForCompany(riderIqamaNo, companyId);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("report/{riderIqamaNo:long}")]
    public async Task<IActionResult> GetRiderHistoryReport(long riderIqamaNo)
    {
        var result = await service.GetRiderHistoryReport(riderIqamaNo);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }
}
