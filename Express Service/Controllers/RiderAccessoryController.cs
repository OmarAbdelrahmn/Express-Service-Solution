
using Application.Contracts.RiderAccessoryCon;
using Application.Contracts.SparePartCo;
using Application.Service.RiderAccessory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Master,Admin,Member")]
public class RiderAccessoryController(IRiderAccessoryService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var response = await service.GetAllAsync();
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }
    [HttpGet("2")]
    public async Task<IActionResult> GetAll2()
    {
        var response = await service.GetAllAsync2();
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var response = await service.GetByIdAsync(id);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpPost]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> Create([FromBody] RiderAccessoryRequest request)
    {
        var response = await service.CreateAsync(request);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] RiderAccessoryRequest request)
    {
        var response = await service.UpdateAsync(id, request);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var response = await service.DeleteAsync(id);
        return response.IsSuccess ? Ok(new { message = "Deleted successfully" }) : response.ToProblem();
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Search query cannot be empty");

        var response = await service.SearchAsync(q);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpPost("{id}/issue")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> IssueToRider(int id, [FromBody] IssueAccessoryRequest request)
    {
        var response = await service.IssueToRiderAsync(id, request);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("rider/{riderId}")]
    public async Task<IActionResult> GetRiderAccessories(int riderId)
    {
        var response = await service.GetRiderAccessoriesAsync(riderId);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("{id}/history")]
    public async Task<IActionResult> GetAccessoryHistory(int id)
    {
        var response = await service.GetAccessoryHistoryAsync(id);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpPost("accessories")]
    public async Task<IActionResult> RecordBatchAccessoryUsage([FromQuery] DateTime Date, [FromBody] BatchRiderAccessoryUsageRequest request)
    {
        if (request.Usages == null || !request.Usages.Any())
            return BadRequest("At least one usage record is required");

        var response = await service.RecordBatchRiderAccessoryUsageAsync(Date, request);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }
}