using Application.Service.HungerReports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class HungerReportsController(IHungerReportService service) : ControllerBase
{
    private readonly IHungerReportService service = service;

    [HttpGet("monthly-validation")]
    //[Authorize]
    public async Task<IActionResult> GetMonthlyValidation(
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetHungerMonthlyRiderValidationAsync(year, month, cancellationToken);

        return result.IsSuccess ?
            Ok(result.Value) :
            result.ToProblem();
    }
}