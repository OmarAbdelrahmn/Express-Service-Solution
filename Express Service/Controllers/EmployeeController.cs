using Application.Contracts.Employees;
using Application.Service.Empolyee;
using Azure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class EmployeeController(IEmployeeService service) : ControllerBase
{
    private readonly IEmployeeService service = service;

    [HttpGet("")]
    public async Task<IActionResult> GetAllEmployee()
    {
        var response = await service.GetAllEmployee();

        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }
    
    [HttpGet("{IqamaNo:int}")]
    public async Task<IActionResult> Get(int IqamaNo)
    {
        var response = await service.Get(IqamaNo);

        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpPost("")]
    public async Task<IActionResult> Create([FromBody] EmpolyeeRequest Request)
    {
        var response = await service.CreateAsync(Request);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpPut("{IqamaNo:int}")]
    public async Task<IActionResult> Update(int IqamaNo, [FromBody] UEmpolyeeRequest Request)
    {
        var response = await service.UpdateAsync(IqamaNo, Request);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }
    

    [HttpDelete("{IqamaNo:int}")]
    public async Task<IActionResult> Delete(int IqamaNo)
    {
        var response = await service.DeleteAsync(IqamaNo);
        return response.IsSuccess ?
            Ok(new Re("Done Successfully")) :
            response.ToProblem();
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] EmployeeFilter Request)
    {
        var response = await service.Filter(Request);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("multi-search")]
    public async Task<IActionResult> Filter([FromQuery] EmployeeFilter2 filter)
    {
        var response = await service.Filter2(filter);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("smart-search")]
    public async Task<IActionResult> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Query cannot be empty.");

        var result = await service.SmartSearch(q);

        return Ok(result);
    }

}

public record Re(string massege);
