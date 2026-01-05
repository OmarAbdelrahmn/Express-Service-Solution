using Application.Contracts.Employees;
using Application.Service.Empolyee;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class HousingController(IHousingService service , IEmployeeService service1) : ControllerBase
{
    private readonly IHousingService service = service;
    private readonly IEmployeeService service1 = service1;


    [HttpPut("{IqamaNo}/add/{HousingName}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> add(long IqamaNo, string HousingName)
    {
        var response = await service1.AddEmployeeToHousing(IqamaNo, HousingName);
        return response.IsSuccess ?
            Ok(new Re("done")) :
            response.ToProblem();
    }

    [HttpPut("{IqamaNo}/change/{oldHousingName}/{NewHousingName}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> Update(long IqamaNo, string oldHousingName, string NewHousingName)
    {
        var response = await service1.ChangeEmployeeToHousing(IqamaNo, oldHousingName, NewHousingName);
        return response.IsSuccess ?
            Ok(new Re("done")) :
            response.ToProblem();
    }

    [HttpGet("")]
    [Authorize(Roles = "Master,Admin,Member")]
 
    public async Task<IActionResult> GetAll()
    {
        var response = await service.GetAllEmployee();
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("{Name}")]
    [Authorize(Roles = "Master,Admin,Member")]
 
    public async Task<IActionResult> Get(string Name)
    {
        var response = await service.Get(Name);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("manager/{ManagerIqamaNo}")]
    [Authorize(Roles = "Master,Admin,Member")]
 
    public async Task<IActionResult> GetWithManagerIqama(int ManagerIqamaNo)
    {
        var response = await service.GetWithManagerIqama(ManagerIqamaNo);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpPost("")]
    [Authorize(Roles = "Master")]
    public async Task<IActionResult> Create([FromBody]HousingRequest request)
    {
        var response = await service.CreateAsync(request);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpDelete("{Name}")]
    [Authorize(Roles = "Master")]
    public async Task<IActionResult> Delete(string Name)
    {
        var response = await service.DeleteAsync(Name);
        return response.IsSuccess ?
            Ok(new Re("done")) :
            response.ToProblem();
    }

    [HttpPut("{editHousingName}")]
    [Authorize(Roles = "Master")]
    public async Task<IActionResult> Update(string editHousingName ,[FromBody] HousingRequest request)
    {
        var response = await service.UpdateAsync(editHousingName,request);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    
}
