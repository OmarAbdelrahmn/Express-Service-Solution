using Application.Service.Empolyee;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]

public class CompanyController(ICompanyService service) : ControllerBase
{
    private readonly ICompanyService service = service;

    [HttpGet("{CompanyName}")]
    [Authorize(Roles = "Master,Admin")]

    public async Task<IActionResult> Get(string CompanyName)
    {
        var response = await service.Get(CompanyName);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpPost("")]
    [Authorize(Roles = "Master")]
    public async Task<IActionResult> Create([FromBody] CompanyRequest Request)
    {
        var response = await service.CreateAsync(Request);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpDelete("{CompanyName}")]
    [Authorize(Roles = "Master")]
    public async Task<IActionResult> Delete(string CompanyName)
    {
        var response = await service.DeleteAsync(CompanyName);
        return response.IsSuccess ?
            Ok(new Re("Company deleted successfully.")) :
            response.ToProblem();
    }

    [HttpGet("")]
    [Authorize(Roles = "Master,Admin,Member")]

    public async Task<IActionResult> GetAllEmployee()
    {
        var response = await service.GetAllEmployee();
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpPut("{CompanyName}")]
    [Authorize(Roles = "Master")]
    public async Task<IActionResult> Update(string CompanyName, [FromBody] CompanyRequest Request)
    {
        var response = await service.UpdateAsync(CompanyName, Request);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }


}
