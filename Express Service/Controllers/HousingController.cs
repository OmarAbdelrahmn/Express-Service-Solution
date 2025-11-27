using Application.Contracts.Employees;
using Application.Service.Empolyee;
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
    public async Task<IActionResult> add(int IqamaNo, string HousingName)
    {
        var response = await service1.AddEmployeeToHousing(IqamaNo, HousingName);
        return response.IsSuccess ?
            Ok(new Re("done")) :
            response.ToProblem();
    }

    [HttpPut("{IqamaNo}/change/{oldHousingName}/{NewHousingName}")]
    public async Task<IActionResult> Update(int IqamaNo, string oldHousingName, string NewHousingName)
    {
        var response = await service1.ChangeEmployeeToHousing(IqamaNo, oldHousingName, NewHousingName);
        return response.IsSuccess ?
            Ok(new Re("done")) :
            response.ToProblem();
    }

    [HttpGet("")]
    public async Task<IActionResult> GetAll()
    {
        var response = await service.GetAllEmployee();
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("{Name}")]
    public async Task<IActionResult> Get(string Name)
    {
        var response = await service.Get(Name);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("manager/{ManagerIqamaNo}")]
    public async Task<IActionResult> GetWithManagerIqama(int ManagerIqamaNo)
    {
        var response = await service.GetWithManagerIqama(ManagerIqamaNo);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpPost("")]
    public async Task<IActionResult> Create([FromBody]HousingRequest request)
    {
        var response = await service.CreateAsync(request);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpDelete("{Name}")]
    public async Task<IActionResult> Delete(string Name)
    {
        var response = await service.DeleteAsync(Name);
        return response.IsSuccess ?
            Ok(new Re("done")) :
            response.ToProblem();
    }

    [HttpPut("")]
    public async Task<IActionResult> Update([FromBody] HousingRequest request)
    {
        var response = await service.UpdateAsync(request);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    
}
