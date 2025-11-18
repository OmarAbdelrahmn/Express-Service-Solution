using Application.Contracts.Employees;
using Application.Service.Empolyee;
using Azure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("[controller]")]
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
    
    [HttpGet("{IqamaNo}")]
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

    [HttpPut("{IqamaNo}")]
    public async Task<IActionResult> Update(int IqamaNo, [FromBody] UEmpolyeeRequest Request)
    {
        var response = await service.UpdateAsync(IqamaNo, Request);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }
    
    [HttpPut("{IqamaNo}/housing/{HousingName}")]
    public async Task<IActionResult> Update(int IqamaNo, string HousingName)
    {
        var response = await service.AddEmployeeToHousing(IqamaNo, HousingName);
        return response.IsSuccess ?
            Ok(new Re("done")) :
            response.ToProblem();
    }
    
    [HttpPut("{IqamaNo}/housing/{oldHousingName}-{NewHousingName}")]
    public async Task<IActionResult> Update(int IqamaNo, string oldHousingName , string NewHousingName)
    {
        var response = await service.ChangeEmployeeToHousing(IqamaNo, oldHousingName, NewHousingName);
        return response.IsSuccess ?
            Ok(new Re("done")) :
            response.ToProblem();
    }

    [HttpDelete("{IqamaNo}")]
    public async Task<IActionResult> Delete(int IqamaNo)
    {
        var response = await service.DeleteAsync(IqamaNo);
        return response.IsSuccess ?
            Ok(new Re("Done Successfully")) :
            response.ToProblem();
    }

}

public record Re(string massege);
