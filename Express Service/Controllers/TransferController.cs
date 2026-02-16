using Application.Contracts.SupplierCon;
using Application.Extensions;
using Application.Service.Transfer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Master,Admin")]
public class TransferController(ITransferService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> TransferToHousing([FromBody] TransferRequest request)
    {
        var transferredBy = User.GetUserId();

        var response = await service.TransferToHousingAsync(request, transferredBy!);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetAll()
    {
        var response = await service.GetAllTransfersAsync();
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetById(int id)
    {
        var response = await service.GetTransferByIdAsync(id);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetdById(int id)
    {
        var response = await service.DeleteTransferAsync(id);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("housing/{housingId}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetByHousing(int housingId)
    {
        var response = await service.GetTransfersByHousingAsync(housingId);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }
}