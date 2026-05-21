using Application.Contracts.SparePartCo;
using Application.Service.HousingInventory;
using Application.Service.Import;
using Application.Service.SparePart;
using k8s.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
//[Authorize(Roles = "Master,Admin,Member")]
public class SparePartController(ISparePartService service , IHousingInventorySyncService importService) : ControllerBase
{
    private readonly IHousingInventorySyncService importService = importService;

    [HttpGet("all-housings")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetAllHousingsCostSummary(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate)
    {
        if (fromDate > toDate)
            return BadRequest("From date must be before or equal to to date");

        var response = await service.GetAllHousingsCostSummaryAsync(fromDate, toDate);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }


    [HttpGet("all-housings/details")]
    public async Task<IActionResult> GetAllHousingsCostReport(
     [FromQuery] DateTime fromDate,
     [FromQuery] DateTime toDate)
    {
        var result = await service.GetAllHousingsCostReportAsync(fromDate, toDate);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }
    /// <summary>
    /// Get cost summary for company main stock "الشركة"
    /// </summary>
    [HttpGet("company-stock")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetCompanyStockCost(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate)
    {
        if (fromDate > toDate)
            return BadRequest("From date must be before or equal to to date");

        var response = await service.GetCompanyStockCostAsync(fromDate, toDate);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    /// <summary>
    /// Compare costs across all housings with rankings and insights
    /// </summary>
    [HttpGet("comparison")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> CompareHousingCosts(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate)
    {
        if (fromDate > toDate)
            return BadRequest("From date must be before or equal to to date");

        var response = await service.CompareHousingCostsAsync(fromDate, toDate);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }


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
    //[Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> Create([FromBody] SparePartRequest request)
    {
        var response = await service.CreateAsync(request);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] SparePartRequest request)
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

    [HttpPost("{id}/usage")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> RecordUsage(int id, [FromBody] SparePartUsageRequest request)
    {
        var response = await service.RecordUsageAsync(id, request);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("{id}/history")]
    public async Task<IActionResult> GetUsageHistory(int id)
    {
        var response = await service.GetUsageHistoryAsync(id);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("vehicle/{vehicleNumber}/history")]
    public async Task<IActionResult> GetVehicleHistory(string vehicleNumber)
    {
        var response = await service.GetVehicleUsageHistoryAsync(vehicleNumber);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpPost("spare-parts")]
    public async Task<IActionResult> RecordBatchSparePartUsage([FromBody] BatchSparePartUsageRequest request)
    {
        if (request.Usages == null || !request.Usages.Any())
            return BadRequest("At least one usage record is required");

        var response = await service.RecordBatchSparePartUsageAsync(request);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    // POST /api/housing-inventory/check
    //
    // READ-ONLY.  Uploads an Excel file and returns, for every row, whether
    // the item exists anywhere in the database (spare parts OR accessories),
    // the detected item type, and the current stock across all locations.
    // Nothing is written to the database.
    //
    // Excel format:
    //   A – Item name   (required)
    //   B – Quantity    (optional; used only for reference in the response)
    //   C – Type hint   (optional: "SparePart" / "Accessory" / Arabic equivalents)
    // ──────────────────────────────────────────────────────────────────────
    [HttpPost("check")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CheckInventory(
        IFormFile file,
        [FromQuery] string checkedBy = "system")
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded." });

        var result = await importService.CheckInventoryFromExcelAsync(file, checkedBy);

        if (result.IsFailure)
            return result.ToProblem();

        return Ok(result.Value);
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST /api/housing-inventory/sync/{housingId}
    //
    // WRITE.  Uploads an Excel file and synchronises the quantities of every
    // listed item at the specified housing location.
    //
    //   • Item found in DB, housing record already exists → quantity is SET
    //     to the Excel value (0 when the cell is blank or zero).
    //   • Item found in DB, NO housing record yet → a new record is created
    //     at the housing location with the Excel quantity (same pattern used
    //     by TransferService).
    //   • Item NOT found anywhere in the DB → skipped and reported.
    //
    // Excel format:  same as /check  (A=Name, B=Quantity, C=Type hint)
    // ──────────────────────────────────────────────────────────────────────
    [HttpPost("sync/{housingId:int}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SyncHousingInventory(
        int housingId,
        IFormFile file,
        [FromQuery] string syncedBy = "system")
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded." });

        var result = await importService.SyncHousingInventoryFromExcelAsync(file, housingId, syncedBy);

        if (result.IsFailure)
            return result.ToProblem();

        return Ok(result.Value);
    }
}
