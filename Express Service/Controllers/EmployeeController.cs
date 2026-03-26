using Application.Contracts.Employees;
using Application.Service.Empolyee;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class EmployeeController(IEmployeeService service) : ControllerBase
{
    private readonly IEmployeeService service = service;

    [HttpGet("iqama-end-report")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetIqamaEndReport(
    [FromQuery] IqamaExpiryUrgency? urgency = null,
    [FromQuery] string? housingName = null,
    [FromQuery] string? sponsor = null)
    {
        var response = await service.GetIqamaEndReportAsync(urgency, housingName, sponsor);

        return response.IsSuccess
            ? Ok(response.Value)
            : response.ToProblem();
    }

    [HttpPost("change-employee-rider")]
    public async Task<IActionResult> ld(long iqama)
    {
        var response = await service.Togle(iqama);
        return response ?
            Ok(new Re("Done Successfully")) :
            BadRequest(new Re("Failed to toggle employee status."));

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

    [HttpGet("{IqamaNo:long}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> Get(long IqamaNo)
    {
        var response = await service.Get(IqamaNo);

        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("one/{IqamaNo:long}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> Get1(long IqamaNo)
    {
        var response = await service.Get1(IqamaNo);

        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("history/{iqamaNo}")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetEmployeeHistory(long iqamaNo)
    {
        var response = await service.GetEmployeeStatusHistoryAsync(iqamaNo);

        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("date-range")]
    [Authorize(Roles = "Master,Admin")]

    public async Task<IActionResult> GetStatusChangesByDateRange(
       [FromQuery] DateTime startDate,
       [FromQuery] DateTime endDate)
    {
        if (startDate > endDate)
            return BadRequest(new { error = "Start date must be before or equal to end date." });

        if ((endDate - startDate).TotalDays > 365)
            return BadRequest(new { error = "Date range cannot exceed 365 days." });

        var result = await service.GetStatusChangesByDateRangeAsync(startDate, endDate);

        if (result.IsFailure)
            return StatusCode(404, new { result.Error.Code });

        return Ok(new
        {
            startDate = startDate.ToString("yyyy-MM-dd"),
            endDate = endDate.ToString("yyyy-MM-dd"),
            totalRecords = result.Value?.Count() ?? 0,
            data = result.Value
        });
    }
    [HttpGet("statistics")]
    [Authorize(Roles = "Master,Admin")]

    public async Task<IActionResult> GetStatistics()
    {
        var result = await service.GetStatusChangeStatisticsAsync();

        if (result.IsFailure)
            return StatusCode(404, new { result.Error.Code });

        return Ok(result.Value);
    }

    [HttpPost("")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> Create([FromBody] EmpolyeeRequest Request)
    {
        var response = await service.CreateAsync(Request);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpPut("{IqamaNo:long}")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> Update(long IqamaNo, [FromBody] UEmpolyeeRequest Request)
    {
        var response = await service.UpdateAsync(IqamaNo, Request);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }


    [HttpDelete("{IqamaNo:long}")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> Delete(long IqamaNo)
    {
        var response = await service.DeleteAsync(IqamaNo);
        return response.IsSuccess ?
            Ok(new Re("Done Successfully")) :
            response.ToProblem();
    }

    [HttpGet("search")]
    [Authorize(Roles = "Master,Admin,Member")]

    public async Task<IActionResult> Search([FromQuery] EmployeeFilter Request)
    {
        var response = await service.Filter(Request);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("multi-search")]
    [Authorize(Roles = "Master,Admin,Member")]

    public async Task<IActionResult> Filter([FromQuery] EmployeeFilter2 filter)
    {
        var response = await service.Filter2(filter);
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("smart-search")]
    [Authorize(Roles = "Master,Admin,Member")]

    public async Task<IActionResult> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Query cannot be empty.");

        var result = await service.SmartSearch(q);

        return Ok(result);
    }


    [HttpGet("deleted")]
    [Authorize(Roles = "Master,Admin,Member")]

    public async Task<IActionResult> GetDeletedEmployees()
    {
        var response = await service.GetAlldeletedEmployee();
        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }
}

public record Re(string massege);
