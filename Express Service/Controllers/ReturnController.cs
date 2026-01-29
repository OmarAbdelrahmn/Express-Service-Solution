using Application.Contracts.SparePartCo;
using Application.Extensions;
using Application.Service.Return;
using Application.Service.SparePart;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Master,Admin")]
public class ReturnController(IReturnService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateReturn([FromBody] ReturnRequest request)
    {
        var response = await service.CreateReturnAsync(request);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetAll()
    {
        var response = await service.GetAllReturnsAsync();
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetById(int id)
    {
        var response = await service.GetReturnByIdAsync(id);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("supplier/{supplierId}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetBySupplier(int supplierId)
    {
        var response = await service.GetReturnsBySupplierAsync(supplierId);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("date-range")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetByDateRange(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate)
    {
        if (fromDate > toDate)
            return BadRequest("From date must be before or equal to to date");

        var response = await service.GetReturnsByDateRangeAsync(fromDate, toDate);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }
}