using Application.Service.export;
using Application.Service.Reports;
using Application.Service.Riders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ReportController(IReportService service) : ControllerBase
{
    private readonly IReportService service = service;



    [HttpGet("summary")]
    //[Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetPreviousDayCompanySummaryAsync(
    CancellationToken cancellationToken = default)
    {
        var result = await service.GetPreviousDayCompanySummaryAsync(cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }


    [HttpGet("special3")]
    public async Task<IActionResult> ComparePeriodOrdersAsync(
        [FromQuery] DateOnly period2Start,
        [FromQuery] DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        var result = await service.ComparePeriodOrdersAsync(
            period2Start,
            period2End,
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("special4")]
    public async Task<IActionResult> GetHousingDailySummaryAsync(
        [FromQuery] DateOnly reportDate,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetHousingDailySummaryAsync(
            reportDate,
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }


    [HttpGet("special5")]
    public async Task<IActionResult> GetHousingDailyDetailedReportAsync(
        [FromQuery] DateOnly reportDate,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetHousingDailyDetailedReportAsync(
            reportDate,
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }


    [HttpGet("special")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> CompareMonthOverMonthAsync(
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var yesterday = today.AddDays(-1);

        var period2Start = new DateOnly(yesterday.Year, yesterday.Month, 1);
        var period2End = yesterday;

        var result = await service.ComparePeriodOrdersAsync(
            period2Start,
            period2End,
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("special1")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetHousingYesterdaySummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3).AddDays(-1));

        var result = await service.GetHousingDailySummaryAsync(
            yesterday,
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("special2")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetHousingYesterdayDetailedAsync(
        CancellationToken cancellationToken = default)
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3).AddDays(-1));

        var result = await service.GetHousingDailyDetailedReportAsync(
            yesterday,
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetDashboard(DateOnly? startDate = null,
      DateOnly? endDate = null)
    {
        var result = await service.GetComprehensiveDashboardAsync(startDate, endDate);

        return result.IsSuccess ?
            Ok(result.Value) :
            result.ToProblem();
    }



    [HttpGet("monthly/{WorkingId}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetMonthlyReportByWorkingIdAsync(
        [FromRoute] string WorkingId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetMonthlyReportByWorkingIdAsync(
            WorkingId,
            year,
            month,
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("monthly/all")]
    [Authorize(Roles = "Master,Admin")]

    public async Task<IActionResult> GetAllRidersMonthlyReportAsync(
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetAllRidersMonthlyReportAsync(
            year,
            month,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("yearly/{WorkingId}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetYearlyReportByWorkingIdAsync(
        [FromRoute] string WorkingId,
        [FromQuery] int year,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetYearlyReportByWorkingIdAsync(
            WorkingId,
            year,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("yearly/all")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetAllRidersYearlyReportAsync(
        [FromQuery] int year,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetAllRidersYearlyReportAsync(
            year,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("riders/{WorkingId}/renge")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetCustomDateRangeReportByWorkingIdAsync(
        [FromRoute] string WorkingId,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetCustomDateRangeReportByWorkingIdAsync(
            WorkingId,
            startDate,
            endDate,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("all/range")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetAllRidersCustomDateRangeReportAsync(
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetAllRidersCustomDateRangeReportAsync(
            startDate,
            endDate,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("company-performance")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetCompanyPerformanceReportAsync(
        [FromQuery] string companyName,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetCompanyPerformanceReportAsync(
            companyName,
            startDate,
            endDate,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("compare-company-periods")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> CompareCompanyPeriodsAsync(
        [FromQuery] string companyName,
        [FromQuery] DateOnly period1Start,
        [FromQuery] DateOnly period1End,
        [FromQuery] DateOnly period2Start,
        [FromQuery] DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        var result = await service.CompareCompanyPeriodsAsync(
            companyName,
            period1Start,
            period1End,
            period2Start,
            period2End,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("problem")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> ProblemAsync(
        [FromQuery] DateOnly StartDate,
        [FromQuery] DateOnly EndDate,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetProblematicShiftsAsync(
            StartDate, EndDate,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("riders/compare-periods")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> CompareAllRidersPeriodsAsync(
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        var result = await service.CompareAllRidersPeriodsAsync(
            period1Start,
            period1End,
            period2Start,
            period2End,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("riders/compare/{WorkingId}")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> CompareRiderPeriodsAsync(
        [FromRoute] string WorkingId,
        [FromQuery] DateOnly period1Start,
        [FromQuery] DateOnly period1End,
        [FromQuery] DateOnly period2Start,
        [FromQuery] DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        var result = await service.CompareRiderPeriodsAsync(
            WorkingId,
            period1Start,
            period1End,
            period2Start,
            period2End,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("riders/compare-monthly/{WorkingId}")]
    [Authorize(Roles = "Master,Admin,Member")]

    public async Task<IActionResult> CompareRidersMonthlyAsync(
        [FromRoute] string WorkingId,
        [FromQuery] int year1,
        [FromQuery] int month1,
        [FromQuery] int Year2,
        [FromQuery] int month2,
        CancellationToken cancellationToken = default)
    {
        var result = await service.CompareRiderMonthsAsync(
            WorkingId,
            year1,
            month1,
            Year2,
            month2,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }
    
    [HttpGet("riders/compare-yearly/{WorkingId}")]
    [Authorize(Roles = "Master,Admin,Member")]

    public async Task<IActionResult> CompareRiderYearlyAsync(
        [FromRoute] string WorkingId,
        [FromQuery] int year1,
        [FromQuery] int Year2,
        CancellationToken cancellationToken = default)
    {
        var result = await service.CompareRiderYearsAsync(
            WorkingId,
            year1,
            Year2,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("housing/compare")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> CompareHousingCompaniesAsync(
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        [FromQuery] DateOnly startDate1,
        [FromQuery] DateOnly endDate1,
        CancellationToken cancellationToken = default)
    {
        var result = await service.CompareHousingPeriodsAsync(
            startDate,
            endDate,
            startDate1,
            endDate1,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("housing/riders")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> GetRidersForHousingAsync(string housingName,
        DateOnly startDate,
        DateOnly endDate)
    {
        var result = await service.GetRidersForHousingAsync(housingName, startDate, endDate);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("housing/riders-compare")]
    [Authorize(Roles = "Master,Admin,Member")]
    public async Task<IActionResult> CompareHousingRidersAsync(
        [FromQuery] string housingName,
        [FromQuery] DateOnly period1Start,
        [FromQuery] DateOnly period1End,
        [FromQuery] DateOnly period2Start,
        [FromQuery] DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        var result = await service.CompareSpecificHousingAsync(
            housingName,
            period1Start,
            period1End,
            period2Start,
            period2End,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("top-riders/yearly")]
    [Authorize(Roles = "Master,Admin")]

    public async Task<IActionResult> GetTopRidersForYearAsync(int year,
        int topCount = 10)
    {
        var result = await service.GetTopRidersForYearAsync(year, topCount);

        return result.IsSuccess ?
            Ok(result.Value) :
            result.ToProblem();
    }
    
    
    [HttpGet("top-riders/monthly")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetTopRidersFormonthAsync(int year, int month,

        int topCount = 10)
    {
        var result = await service.GetTopRidersForMonthAsync(year,month, topCount);

        return result.IsSuccess ?
            Ok(result.Value) :
            result.ToProblem();
    }

    [HttpGet("top-riders/company")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetTopRidersPerCompanyAsync(DateOnly Start , DateOnly End)
    {
        var result = await service.GetTopRidersPerCompanyAsync(Start , End);

        return result.IsSuccess ?
            Ok(result.Value) :
            result.ToProblem();
    }

    [HttpGet("stacked/{WorkingId}")]
    [Authorize(Roles = "Master,Admin,Member")]

    public async Task<IActionResult> GetMonthlyStackedDeliveriesByWorkingIdAsync(string WorkingId,
        [FromQuery]   int year,
        [FromQuery]  int month,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetMonthlyStackedDeliveriesByWorkingIdAsync(WorkingId,year,month,cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }
    
    
    [HttpGet("stacked")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetStackedDeliveriesByWorkingIdAsync(
        [FromQuery] DateOnly startDate,
        [FromQuery]DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetAllRidersStackedDeliveriesAsync(startDate,endDate,cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }
    

    [HttpGet("housing")]
    [Authorize(Roles = "Master,Admin")]
    public async Task<IActionResult> GetHousingReportAsync(
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetHousingAnalysisForPeriodAsync(
            startDate,
            endDate,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }


}
