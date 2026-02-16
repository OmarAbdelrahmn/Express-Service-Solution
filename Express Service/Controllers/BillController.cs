using Application.Contracts.SupplierCon;
using Application.Extensions;
using Application.Service.SupplierSer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Master,Admin")]
public class BillController(IBillService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> ReceiveBill([FromBody] ReceiveBillRequest request)
    {
        var processedBy = User.GetUserId();

        var response = await service.ReceiveBillAsync(request, processedBy!);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetAll()
    {
        var response = await service.GetAllBillsAsync();
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetById(int id)
    {
        var response = await service.GetBillByIdAsync(id);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("date-range")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetByDateRange(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate)
    {
        if (startDate > endDate)
            return BadRequest("Start date must be before or equal to end date");

        var response = await service.GetBillsByDateRangeAsync(startDate, endDate);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("supplier/{supplierId}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetBySupplier(int supplierId)
    {
        var response = await service.GetBillsBySupplierAsync(supplierId);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var response = await service.DeleteBillAsync(id);
        return response.IsSuccess ?
            Ok(new { message = "Bill deleted and quantities reversed" }) :
            response.ToProblem();
    }
}