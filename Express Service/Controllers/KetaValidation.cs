using Application.Service.KetaValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class KetaValidation(IMonthlyValidityService service) : ControllerBase
{
    private readonly IMonthlyValidityService service = service;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var result = await service.GetAllRidersValidityAsync();

        if (result.IsFailure)
        {
            return result.ToProblem();
        }
        return Ok(result.Value);
    }

    [HttpGet("{iqamano}")]
    public async Task<IActionResult> GetByIqama(long iqamano)
    {
        var result = await service.GetRiderValidityByIqamaAsync(iqamano);
        if (result.IsFailure)
        {
            return result.ToProblem();
        }
        return Ok(result.Value);
    }
}
