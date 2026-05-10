using Application.Contracts.Orders;
using Application.Service.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Master,Admin")]
public class OrderController(IOrderService service) : ControllerBase
{
    private readonly IOrderService service = service;

    // ── Employees ─────────────────────────────────────────────────────────────

    /// <summary>List all Company 3 employees with today's order snapshot.</summary>
    [HttpGet("employees")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetEmployees()
    {
        var result = await service.GetCompany4EmployeesAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>Single Company 3 employee detail with today's snapshot.</summary>
    [HttpGet("employees/{iqamaNo:long}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetEmployee(long iqamaNo)
    {
        var result = await service.GetCompany4EmployeeAsync(iqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // ── Order CRUD ────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a new order for an employee.
    /// Automatically closes any currently open order for that employee today.
    /// </summary>
    [HttpPost("")]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
    {
        var requestedBy = User.Identity?.Name ?? "Unknown";
        var result = await service.CreateOrderAsync(request, requestedBy);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>Manually close the open order for a specific employee today.</summary>
    [HttpPatch("close/{iqamaNo:long}")]
    public async Task<IActionResult> CloseEmployee(long iqamaNo)
    {
        var by = User.Identity?.Name ?? "Unknown";
        var result = await service.CloseEmployeeOrderAsync(iqamaNo, by);
        return result.IsSuccess
            ? Ok(new { message = "Order closed successfully." })
            : result.ToProblem();
    }

    /// <summary>Close ALL open orders for today (end-of-day). Master only.</summary>
    [HttpPatch("close-all")]
    [Authorize(Roles = "Master")]
    public async Task<IActionResult> CloseAll()
    {
        var by = User.Identity?.Name ?? "Unknown";
        var result = await service.CloseAllOpenOrdersAsync(by);
        return result.IsSuccess
            ? Ok(new { message = "All open orders closed." })
            : result.ToProblem();
    }

    // ── Live Queries ──────────────────────────────────────────────────────────

    /// <summary>Live snapshot: who is currently on an order, who is not.</summary>
    [HttpGet("active")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetActiveSnapshot()
    {
        var result = await service.GetActiveOrdersSnapshotAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>Full order history for one employee.</summary>
    [HttpGet("employees/{iqamaNo:long}/history")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetEmployeeHistory(long iqamaNo)
    {
        var result = await service.GetEmployeeOrderHistoryAsync(iqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // ── Reports ───────────────────────────────────────────────────────────────

    /// <summary>Full report for today.</summary>
    [HttpGet("report/today")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetTodayReport()
    {
        var result = await service.GetTodayReportAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>Full report for any date. Format: yyyy-MM-dd</summary>
    [HttpGet("report/daily")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetDailyReport([FromQuery] DateOnly date)
    {
        var result = await service.GetDailyReportAsync(date);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>Report for a date range (max 365 days).</summary>
    [HttpGet("report/range")]
    public async Task<IActionResult> GetRangeReport(
        [FromQuery] DateTime start,
        [FromQuery] DateTime end)
    {
        if (start > end)
            return BadRequest(new { error = "Start must be before end." });

        if ((end - start).TotalDays > 365)
            return BadRequest(new { error = "Range cannot exceed 365 days." });

        var result = await service.GetDateRangeReportAsync(start, end);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>All-time statistics for Company 3 orders.</summary>
    [HttpGet("report/statistics")]
    public async Task<IActionResult> GetStatistics()
    {
        var result = await service.GetStatisticsAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // ── Dispatch (Shift-Aware) ────────────────────────────────────────────────

    /// <summary>
    /// Riders active RIGHT NOW based on their shift schedule,
    /// each enriched with their live order status.
    /// Also returns riders currently off-shift or on break.
    /// </summary>
    [HttpGet("dispatch/now")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetDispatchNow()
    {
        var result = await service.GetDispatchNowAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Riders active at any given date + time, enriched with order status.
    ///
    /// Query params (both optional – default to current Saudi time):
    ///   date  yyyy-MM-dd
    ///   time  HH:mm
    /// </summary>
    [HttpGet("dispatch")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetDispatch(
        [FromQuery] DateOnly? date,
        [FromQuery] TimeOnly? time)
    {
        var now = DateTime.UtcNow.AddHours(3);
        var d = date ?? DateOnly.FromDateTime(now);
        var t = time ?? TimeOnly.FromDateTime(now);

        var result = await service.GetDispatchAtAsync(d, t);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Full day planner: every Company-3 rider with their complete shift schedule
    /// and order summary for the given date. No time filter.
    ///
    /// Query param:
    ///   date  yyyy-MM-dd  (defaults to today)
    /// </summary>
    [HttpGet("dispatch/all")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetDispatchAll([FromQuery] DateOnly? date)
    {
        var d = date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        var result = await service.GetDispatchAllAsync(d);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}