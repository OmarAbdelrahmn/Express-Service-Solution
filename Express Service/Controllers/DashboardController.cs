using Application.Service.Dahsboard;
using Application.Service.Dashboard;
using Express_Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    private readonly IDashboardService _dashboardService = dashboardService;


    [HttpGet("companies")]
    public async Task<IActionResult> Get(
    [FromQuery] DateOnly startDate,
    [FromQuery] DateOnly endDate,
    [FromQuery] int? companyId = null)
    {
        var request = new DailyCompanyReportRequest(startDate, endDate, companyId);
        var result = await _dashboardService.GetDailyReportAsync(request);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }


    /// <summary>
    /// Overall system metrics — KPI cards for the top of the dashboard
    /// </summary>
    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview()
    {
        var result = await _dashboardService.GetOverviewAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Total accepted orders per company for a given month
    /// </summary>
    [HttpGet("orders/by-company")]
    public async Task<IActionResult> GetOrdersByCompany([FromQuery] int year, [FromQuery] int month)
    {
        if (year == 0) year = DateTime.UtcNow.Year;
        if (month == 0) month = DateTime.UtcNow.Month;

        var result = await _dashboardService.GetOrdersByCompanyAsync(year, month);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Monthly order totals for the last N months — trend line chart
    /// </summary>
    [HttpGet("orders/trend")]
    public async Task<IActionResult> GetOrderTrend([FromQuery] int months = 6)
    {
        var result = await _dashboardService.GetOrderTrendAsync(Math.Clamp(months, 1, 24));
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Daily order totals for the last N days
    /// </summary>
    [HttpGet("orders/daily")]
    public async Task<IActionResult> GetDailyOrders([FromQuery] int days = 30, [FromQuery] int? companyId = null)
    {
        var result = await _dashboardService.GetDailyOrdersTrendAsync(Math.Clamp(days, 7, 90), companyId);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Top performing riders by accepted order count for a given month
    /// </summary>
    [HttpGet("riders/top")]
    public async Task<IActionResult> GetTopRiders(
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] int? companyId = null,
        [FromQuery] int top = 10)
    {
        if (year == 0) year = DateTime.UtcNow.Year;
        if (month == 0) month = DateTime.UtcNow.Month;

        var result = await _dashboardService.GetTopRidersAsync(year, month, companyId, Math.Clamp(top, 5, 50));
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Number of riders currently assigned to each company
    /// </summary>
    [HttpGet("riders/by-company")]
    public async Task<IActionResult> GetRiderCountByCompany()
    {
        var result = await _dashboardService.GetRiderCountByCompanyAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Vehicle availability breakdown (available / taken / problem / stolen / breakup)
    /// </summary>
    [HttpGet("vehicles/stats")]
    public async Task<IActionResult> GetVehicleStats()
    {
        var result = await _dashboardService.GetVehicleStatsAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Housing occupancy rates for all housings
    /// </summary>
    [HttpGet("housing/stats")]
    public async Task<IActionResult> GetHousingStats()
    {
        var result = await _dashboardService.GetHousingStatsAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Iqama expiry urgency distribution (expired / critical / warning / upcoming / safe)
    /// </summary>
    [HttpGet("iqama/expiry")]
    public async Task<IActionResult> GetIqamaExpiryStats()
    {
        var result = await _dashboardService.GetIqamaExpiryStatsAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Employee & rider distribution by status
    /// </summary>
    [HttpGet("employees/status")]
    public async Task<IActionResult> GetEmployeeStatusStats()
    {
        var result = await _dashboardService.GetEmployeeStatusStatsAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Monthly validity results (valid / invalid / freelancer) per company
    /// </summary>
    [HttpGet("validity")]
    public async Task<IActionResult> GetMonthlyValidity([FromQuery] int year, [FromQuery] int month)
    {
        if (year == 0) year = DateTime.UtcNow.Year;
        if (month == 0) month = DateTime.UtcNow.Month;

        var result = await _dashboardService.GetMonthlyValidityStatsAsync(year, month);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Orders per company for every month of the year — matrix for stacked/grouped bar chart
    /// </summary>
    [HttpGet("orders/matrix")]
    public async Task<IActionResult> GetOrdersMatrix([FromQuery] int year = 0)
    {
        if (year == 0) year = DateTime.UtcNow.Year;
        var result = await _dashboardService.GetRiderOrdersMatrixAsync(year);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Employee and rider headcount by nationality
    /// </summary>
    [HttpGet("employees/countries")]
    public async Task<IActionResult> GetCountryDistribution()
    {
        var result = await _dashboardService.GetCountryDistributionAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Headcount grouped by sponsor / kafeel
    /// </summary>
    [HttpGet("employees/sponsors")]
    public async Task<IActionResult> GetSponsorStats()
    {
        var result = await _dashboardService.GetSponsorStatsAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
}