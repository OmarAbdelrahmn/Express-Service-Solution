using Application.Service.export;
using Application.Service.Reports;
using Application.Service.Riders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ReportController(IReportService service) : ControllerBase
{
    private readonly IReportService service = service;


    //[HttpGet("export/dashboard/excel")]
    //public async Task<IActionResult> ExportDashboardToExcelAsync(
    //    DateOnly? startDate = null,
    //    DateOnly? endDate = null,
    //    CancellationToken cancellationToken = default)
    //{
    //    var result = await service.GetComprehensiveDashboardAsync(
    //        startDate, endDate, cancellationToken);

    //    if (!result.IsSuccess)
    //        return result.ToProblem();

    //    var exportService = new ReportExportService();
    //    var excelBytes = await exportService.ExportToExcelAsync(
    //        result.Value, "ComprehensiveDashboard");

    //    var fileName = $"Dashboard_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
    //    return File(excelBytes,
    //        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    //        fileName);
    //}

    //[HttpGet("export/dashboard/pdf")]
    //public async Task<IActionResult> ExportDashboardToPdfAsync(
    //    DateOnly? startDate = null,
    //    DateOnly? endDate = null,
    //    CancellationToken cancellationToken = default)
    //{
    //    var result = await service.GetComprehensiveDashboardAsync(
    //        startDate, endDate, cancellationToken);

    //    if (!result.IsSuccess)
    //        return result.ToProblem();

    //    var exportService = new ReportExportService();
    //    var pdfBytes = await exportService.ExportToPdfAsync(
    //        result.Value, "ComprehensiveDashboard");

    //    var fileName = $"Dashboard_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
    //    return File(pdfBytes, "application/pdf", fileName);
    //}

    //[HttpGet("export/monthly/{WorkingId}/excel")]
    //public async Task<IActionResult> ExportMonthlyToExcelAsync(
    //    [FromRoute] string WorkingId,
    //    [FromQuery] int year,
    //    [FromQuery] int month,
    //    CancellationToken cancellationToken = default)
    //{
    //    var result = await service.GetMonthlyReportByWorkingIdAsync(
    //        WorkingId, year, month, cancellationToken);

    //    if (!result.IsSuccess)
    //        return result.ToProblem();

    //    var exportService = new ReportExportService();
    //    var excelBytes = await exportService.ExportToExcelAsync(
    //        result.Value, "MonthlyRiderReport");

    //    var fileName = $"Monthly_{WorkingId}_{year}{month:D2}.xlsx";
    //    return File(excelBytes,
    //        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    //        fileName);
    //}

    //[HttpGet("export/top-riders/pdf")]
    //public async Task<IActionResult> ExportTopRidersToPdfAsync(
    //    [FromQuery] DateOnly startDate,
    //    [FromQuery] DateOnly endDate,
    //    [FromQuery] int topCount = 100,
    //    CancellationToken cancellationToken = default)
    //{
    //    var request = new TopRidersRequest(
    //        StartDate: startDate,
    //        EndDate: endDate,
    //        TopCount: topCount
    //    );

    //    var result = await service.GetTopRidersInPeriodAsync(request, cancellationToken);

    //    if (!result.IsSuccess)
    //        return result.ToProblem();

    //    var exportService = new ReportExportService();
    //    var pdfBytes = await exportService.ExportToPdfAsync(
    //        result.Value, "TopRidersReport");

    //    var fileName = $"TopRiders_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.pdf";
    //    return File(pdfBytes, "application/pdf", fileName);
    //}

    //[HttpGet("export/generic/excel")]
    //public async Task<IActionResult> ExportGenericToExcelAsync(
    //    [FromQuery] string reportType,
    //    [FromQuery] string reportDataJson)
    //{
    //    // This allows frontend to pass any report data as JSON
    //    try
    //    {
    //        var exportService = new ReportExportService();

    //        // Deserialize based on report type
    //        object reportData = reportType switch
    //        {
    //            "ComprehensiveDashboard" =>
    //                JsonSerializer.Deserialize<ComprehensiveDashboard>(reportDataJson),
    //            "MonthlyRiderReport" =>
    //                JsonSerializer.Deserialize<MonthlyRiderReport>(reportDataJson),
    //            "TopRidersReport" =>
    //                JsonSerializer.Deserialize<TopRidersReport>(reportDataJson),
    //            _ => throw new NotSupportedException($"Report type {reportType} not supported")
    //        };

    //        var method = typeof(ReportExportService)
    //            .GetMethod(nameof(ReportExportService.ExportToExcelAsync))
    //            .MakeGenericMethod(reportData.GetType());

    //        var task = (Task<byte[]>)method.Invoke(exportService,
    //            new[] { reportData, reportType });

    //        var excelBytes = await task;

    //        var fileName = $"{reportType}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
    //        return File(excelBytes,
    //            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    //            fileName);
    //    }
    //    catch (Exception ex)
    //    {
    //        return BadRequest(new { error = ex.Message });
    //    }
    //}

    [HttpGet("")]
    public async Task<IActionResult> GetDashboard(DateOnly? startDate = null,
      DateOnly? endDate = null)
    {
        var result = await service.GetComprehensiveDashboardAsync(startDate, endDate);

        return result.IsSuccess ?
            Ok(result.Value) :
            result.ToProblem();
    }



    [HttpGet("monthly/{WorkingId}")]
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

    [HttpGet("riders/compare-periods")]
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
    public  async Task<IActionResult> GetRidersForHousingAsync(string housingName,
        DateOnly startDate,
        DateOnly endDate)
    {
        var result = await service.GetRidersForHousingAsync(housingName, startDate, endDate);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToProblem();
    }

    [HttpGet("housing/riders-compare")]
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
    public async Task<IActionResult> GetTopRidersForYearAsync(int year,
        int topCount = 10)
    {
        var result = await service.GetTopRidersForYearAsync(year, topCount);

        return result.IsSuccess ?
            Ok(result.Value) :
            result.ToProblem();
    }
    
    
    [HttpGet("top-riders/monthly")]
    public async Task<IActionResult> GetTopRidersFormonthAsync(int year, int month,

        int topCount = 10)
    {
        var result = await service.GetTopRidersForMonthAsync(year,month, topCount);

        return result.IsSuccess ?
            Ok(result.Value) :
            result.ToProblem();
    }

    [HttpGet("top-riders/company")]
    public async Task<IActionResult> GetTopRidersPerCompanyAsync(DateOnly Start , DateOnly End)
    {
        var result = await service.GetTopRidersPerCompanyAsync(Start , End);

        return result.IsSuccess ?
            Ok(result.Value) :
            result.ToProblem();
    }

    [HttpGet("stacked/{WorkingId}")]
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
