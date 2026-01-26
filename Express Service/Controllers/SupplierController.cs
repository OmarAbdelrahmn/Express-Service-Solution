using Application.Contracts.SupplierCon;
using Application.Service.SupplierSer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Master,Admin,Member")]
public class SupplierController(ISupplierService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var response = await service.GetAllAsync();
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var response = await service.GetActiveAsync();
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
    public async Task<IActionResult> Create([FromBody] SupplierRequest request)
    {
        var response = await service.CreateAsync(request);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] SupplierRequest request)
    {
        var response = await service.UpdateAsync(id, request);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpPatch("{id}/toggle-active")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var response = await service.ToggleActiveAsync(id);
        return response.IsSuccess ?
            Ok(new { message = "Supplier status toggled successfully" }) :
            response.ToProblem();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var response = await service.DeleteAsync(id);
        return response.IsSuccess ?
            Ok(new { message = "Supplier deleted successfully" }) :
            response.ToProblem();
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Search query cannot be empty");

        var response = await service.SearchAsync(q);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }
}