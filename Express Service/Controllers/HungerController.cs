using Application.Service;
using Application.Service.Riders;
using Express_Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Master,Admin")]

public class HungerController(IHungerDisabilityService service) : ControllerBase
{
    private readonly IHungerDisabilityService service = service;

    [HttpPost("import")]
    public async Task<IActionResult> ImportFromExcel(
         IFormFile file,
         [FromQuery] DateOnly shiftDate,
         CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "File must be an Excel file (.xlsx)" });

        using var stream = file.OpenReadStream();
        var response = await service.ImportFromExcelAsync(stream, shiftDate, cancellationToken);

        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }


    [HttpGet("date-range")]
    public async Task<IActionResult> GetReportsByDateRange(
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var response = await service.GetReportsByDateRangeAsync(startDate, endDate, cancellationToken);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

    [HttpGet("month")]
    public async Task<IActionResult> GetReportsByMonth(
        [FromQuery]int year,
        [FromQuery]int month,
        CancellationToken cancellationToken = default)
    {
        if (month < 1 || month > 12)
            return BadRequest(new { error = "Month must be between 1 and 12" });

        var response = await service.GetReportsByMonthAsync(year, month, cancellationToken);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }


    [HttpGet("year")]
    public async Task<IActionResult> GetReportsByYear(
        [FromQuery]int year,
        CancellationToken cancellationToken = default)
    {
        if (year < 2000 || year > 2100)
            return BadRequest(new { error = "Invalid year" });

        var response = await service.GetReportsByYearAsync(year, cancellationToken);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }

 
    [HttpGet("rider/{actualWorkingId}")]
    public async Task<IActionResult> GetReportByRider(
        string actualWorkingId,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var response = await service.GetReportByRiderAndDateRangeAsync(
            actualWorkingId, startDate, endDate, cancellationToken);

        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }


    [HttpGet("summary")]
    public async Task<IActionResult> GetOverallSummary(
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var response = await service.GetOverallSummaryAsync(startDate, endDate, cancellationToken);
        return response.IsSuccess ? Ok(response.Value) : response.ToProblem();
    }


    //[HttpGet("above-target")]
    //public async Task<IActionResult> GetRidersAboveTarget(
    //    [FromQuery] DateOnly startDate,
    //    [FromQuery] DateOnly endDate,
    //    CancellationToken cancellationToken = default)
    //{
    //    var response = await service.GetReportsByDateRangeAsync(startDate, endDate, cancellationToken);

    //    if (!response.IsSuccess)
    //        return response.ToProblem();

    //    var aboveTarget = response.Value.Where(r => r.IsAboveTarget).ToList();

    //    return Ok(new
    //    {
    //        totalAboveTarget = aboveTarget.Count,
    //        reports = aboveTarget
    //    });
    //}


    //[HttpGet("below-target")]
    //public async Task<IActionResult> GetRidersBelowTarget(
    //    [FromQuery] DateOnly startDate,
    //    [FromQuery] DateOnly endDate,
    //    CancellationToken cancellationToken = default)
    //{
    //    var response = await service.GetReportsByDateRangeAsync(startDate, endDate, cancellationToken);

    //    if (!response.IsSuccess)
    //        return response.ToProblem();

    //    var belowTarget = response.Value.Where(r => !r.IsAboveTarget).ToList();

    //    return Ok(new
    //    {
    //        totalBelowTarget = belowTarget.Count,
    //        reports = belowTarget
    //    });
    //}


    //[HttpGet("with-substitutes")]
    //public async Task<IActionResult> GetRidersWithSubstitutes(
    //    [FromQuery] DateOnly startDate,
    //    [FromQuery] DateOnly endDate,
    //    CancellationToken cancellationToken = default)
    //{
    //    var response = await service.GetReportsByDateRangeAsync(startDate, endDate, cancellationToken);

    //    if (!response.IsSuccess)
    //        return response.ToProblem();

    //    var withSubstitutes = response.Value.Where(r => r.HasSubstitute).ToList();

    //    return Ok(new
    //    {
    //        totalWithSubstitutes = withSubstitutes.Count,
    //        reports = withSubstitutes
    //    });
    //}

    //[HttpGet("without-substitutes")]
    //public async Task<IActionResult> GetRidersWithoutSubstitutes(
    //    [FromQuery] DateOnly startDate,
    //    [FromQuery] DateOnly endDate,
    //    CancellationToken cancellationToken = default)
    //{
    //    var response = await service.GetReportsByDateRangeAsync(startDate, endDate, cancellationToken);

    //    if (!response.IsSuccess)
    //        return response.ToProblem();

    //    var withoutSubstitutes = response.Value.Where(r => !r.HasSubstitute).ToList();

    //    return Ok(new
    //    {
    //        totalWithoutSubstitutes = withoutSubstitutes.Count,
    //        warning = "⚠️ These disabled riders appear in shift data without active substitutions",
    //        reports = withoutSubstitutes
    //    });
    //}
}