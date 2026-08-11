using Application.Service.InventoryAudit;
using Application.Contracts.InventoryAudit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

/// <summary>
/// Main-service view of the inventory audit trail: every manual add/edit/delete
/// made to spare parts and rider accessories, across ALL housings and the
/// main company stock ("الشركة"). For the housing-scoped equivalent (a
/// housing manager viewing only their own housing's changes) see
/// MemberController's GET /api/member/inventory/audit-log.
/// </summary>
[Route("api/inventory-audit")]
[ApiController]
[Authorize(Roles = "Master,Admin")]
public class InventoryAuditController(IInventoryAuditService service) : ControllerBase
{
    /// <summary>
    /// Get every manual inventory change (spare parts + rider accessories),
    /// across every housing and the main company stock, with optional filters.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] InventoryItemType? itemType,
        [FromQuery] string? location,
        [FromQuery] string? performedBy,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
            return BadRequest("From date must be before or equal to to date");

        var result = await service.GetAllAsync(fromDate, toDate, itemType, location, performedBy, page, pageSize);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}
