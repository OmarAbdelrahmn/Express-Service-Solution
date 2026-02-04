using Application.Extensions;
using Application.Service.Freelancer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class KetaFreeLancer(IFreelancerService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string Month)
    {
        var result = await service.GetKetaFreelancersByMonthAsync(Month);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpPost]
    public async Task<IActionResult> Post(IFormFile file)
    {

        if (file == null || file.Length == 0)
            return BadRequest(new { Error = "File is required" });

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return BadRequest(new { Error = "File must be Excel format (.xlsx or .xls)" });

        var uploadedBy = User.GetUserId();

        var result = await service.ImportKetaFreelancersFromExcelAsync(
            file,
            uploadedBy!
            );


        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }
}
