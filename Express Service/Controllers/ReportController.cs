using Application.Service.Reports;
using Domain.Migrations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("[controller]")]
[ApiController]
public class ReportController(IReportService service) : ControllerBase
{
    private readonly IReportService service = service;


    [HttpGet("")]
    public async Task<IActionResult> getall(DateOnly? startDate = null,
      DateOnly? endDate = null)
    {
        var result = await service.GetComprehensiveDashboardAsync(startDate, endDate);

        return result.IsSuccess ?
            Ok(result.Value) :
            result.ToProblem();
    }




    [HttpGet("monthly/{workingId:int}")]
    public async Task<IActionResult> GetMonthlyReportByWorkingIdAsync(
        [FromRoute] int workingId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetMonthlyReportByWorkingIdAsync(
            workingId,
            year,
            month,
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("monthly/all")]
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

    [HttpGet("yearly/{workingId:int}")]
    public async Task<IActionResult> GetYearlyReportByWorkingIdAsync(
        [FromRoute] int workingId,
        [FromQuery] int year,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetYearlyReportByWorkingIdAsync(
            workingId,
            year,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("yearly/all")]
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

    [HttpGet("{workingId:int}/{startDate}-{endDate}")]
    public async Task<IActionResult> GetCustomDateRangeReportByWorkingIdAsync(
        [FromRoute] int workingId,
        [FromRoute] DateOnly startDate,
        [FromRoute] DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetCustomDateRangeReportByWorkingIdAsync(
            workingId,
            startDate,
            endDate,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("all/{startDate}-{endDate}")]
    public async Task<IActionResult> GetAllRidersCustomDateRangeReportAsync(
        [FromRoute] DateOnly startDate,
        [FromRoute] DateOnly endDate,
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

    [HttpGet("riders")]
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

    [HttpGet("compare-rider-periods/{workingId:int}")]
    public async Task<IActionResult> CompareRiderPeriodsAsync(
        [FromRoute] int workingId,
        [FromQuery] DateOnly period1Start,
        [FromQuery] DateOnly period1End,
        [FromQuery] DateOnly period2Start,
        [FromQuery] DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        var result = await service.CompareRiderPeriodsAsync(
            workingId,
            period1Start,
            period1End,
            period2Start,
            period2End,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("compare-rider-monthly/{workingId}/{year1:int}/{month1:int}/{Year2}/{month2}")]
    public async Task<IActionResult> CompareRidersMonthlyAsync(
        [FromRoute] int workingId,
        [FromRoute] int year1,
        [FromRoute] int month1,
        [FromRoute] int Year2,
        [FromRoute] int month2,
        CancellationToken cancellationToken = default)
    {
        var result = await service.CompareRiderMonthsAsync(
            workingId,
            year1,
            month1,
            Year2,
            month2,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }
    
    [HttpGet("compare-rider-yearly/{workingId}/{year1:int}/{Year2}")]
    public async Task<IActionResult> CompareRiderYearlyAsync(
        [FromRoute] int workingId,
        [FromRoute] int year1,
        [FromRoute] int Year2,
        CancellationToken cancellationToken = default)
    {
        var result = await service.CompareRiderYearsAsync(
            workingId,
            year1,
            Year2,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("compare-housing")]
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

    [HttpGet("housing")]
    public  async Task<IActionResult> GetRidersForHousingAsync(string housingName,
        DateOnly startDate,
        DateOnly endDate)
    {
        var result = await service.GetRidersForHousingAsync(housingName, startDate, endDate);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("housing-compare")]
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

    [HttpGet("top-riders-yearly")]
    public async Task<IActionResult> GetTopRidersForYearAsync(int year,
        int topCount = 10)
    {
        var result = await service.GetTopRidersForYearAsync(year, topCount);

        return result.IsSuccess ?
            Ok(result.Value) :
            result.ToProblem();
    }
    
    
    [HttpGet("top-riders-monthly")]
    public async Task<IActionResult> GetTopRidersFormonthAsync(int year, int month,

        int topCount = 10)
    {
        var result = await service.GetTopRidersForMonthAsync(year,month, topCount);

        return result.IsSuccess ?
            Ok(result.Value) :
            result.ToProblem();
    }

    [HttpGet("top-riders-company")]
    public async Task<IActionResult> GetTopRidersPerCompanyAsync(DateOnly Start , DateOnly End)
    {
        var result = await service.GetTopRidersPerCompanyAsync(Start , End);

        return result.IsSuccess ?
            Ok(result.Value) :
            result.ToProblem();
    }

   
}
