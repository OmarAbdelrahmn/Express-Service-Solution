
using Application.Service.EmployeesFiles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[ApiController]
[Route("api/Images")]
[Authorize]
public class ImagesController(IEmployeeDocumentsService service) : ControllerBase
{
    // GET api/employees/documents/images/all
    [HttpGet("all")]
    public async Task<IActionResult> GetAllEmployeesMainImages(CancellationToken ct)
    {
        var result = await service.GetAllEmployeesMainImagesAsync(ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();

    }
}