using Application.Service;
using Application.Service.Riders;
using Express_Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace RiderManagement.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
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
        {
            return BadRequest(new { error = "No file uploaded" });
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "File must be an Excel file (.xlsx)" });
        }

        using var stream = file.OpenReadStream();
        var response = await service.ImportFromExcelAsync(stream, shiftDate, cancellationToken);

        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet]
    public async Task<IActionResult> GetAllReports(CancellationToken cancellationToken = default)
    {
        var response = await service.GetAllReportsAsync(cancellationToken);

        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("by-date/{shiftDate}")]
    public async Task<IActionResult> GetReportsByDate(
        DateOnly shiftDate,
        CancellationToken cancellationToken = default)
    {
        var response = await service.GetReportsByDateAsync(shiftDate, cancellationToken);

        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("by-date-range")]
    public async Task<IActionResult> GetReportsByDateRange(
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var response = await service.GetReportsByDateRangeAsync(startDate, endDate, cancellationToken);

        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("by-rider/{actualWorkingId}")]
    public async Task<IActionResult> GetReportsByRider(
        string actualWorkingId,
        CancellationToken cancellationToken = default)
    {
        var response = await service.GetReportsByRiderAsync(actualWorkingId, cancellationToken);

        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("summary/by-rider/{actualWorkingId}")]
    public async Task<IActionResult> GetSummaryByRider(
        string actualWorkingId,
        CancellationToken cancellationToken = default)
    {
        var response = await service.GetSummaryByRiderAsync(actualWorkingId, cancellationToken);

        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("summary/by-date-range")]
    public async Task<IActionResult> GetSummaryByDateRange(
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var response = await service.GetSummaryByDateRangeAsync(startDate, endDate, cancellationToken);

        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpGet("below-target")]
    public async Task<IActionResult> GetRidersBelowTarget(
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<HungerDisabilityReportResponse> reports;

        if (startDate.HasValue && endDate.HasValue)
        {
            var response = await service.GetReportsByDateRangeAsync(
                startDate.Value,
                endDate.Value,
                cancellationToken);

            if (!response.IsSuccess)
            {
                return response.ToProblem();
            }

            reports = response.Value;
        }
        else
        {
            var response = await service.GetAllReportsAsync(cancellationToken);

            if (!response.IsSuccess)
            {
                return response.ToProblem();
            }

            reports = response.Value;
        }

        var belowTarget = reports.Where(r => !r.TargetAchieved).ToList();

        return Ok(new
        {
            totalBelowTarget = belowTarget.Count,
            reports = belowTarget
        });
    }

    [HttpGet("met-target")]
    public async Task<IActionResult> GetRidersMetTarget(
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<HungerDisabilityReportResponse> reports;

        if (startDate.HasValue && endDate.HasValue)
        {
            var response = await service.GetReportsByDateRangeAsync(
                startDate.Value,
                endDate.Value,
                cancellationToken);

            if (!response.IsSuccess)
            {
                return response.ToProblem();
            }

            reports = response.Value;
        }
        else
        {
            var response = await service.GetAllReportsAsync(cancellationToken);

            if (!response.IsSuccess)
            {
                return response.ToProblem();
            }

            reports = response.Value;
        }

        var metTarget = reports.Where(r => r.TargetAchieved).ToList();

        return Ok(new
        {
            totalMetTarget = metTarget.Count,
            reports = metTarget
        });
    }

    [HttpGet("with-substitutes")]
    public async Task<IActionResult> GetRidersWithSubstitutes(
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<HungerDisabilityReportResponse> reports;

        if (startDate.HasValue && endDate.HasValue)
        {
            var response = await service.GetReportsByDateRangeAsync(
                startDate.Value,
                endDate.Value,
                cancellationToken);

            if (!response.IsSuccess)
            {
                return response.ToProblem();
            }

            reports = response.Value;
        }
        else
        {
            var response = await service.GetAllReportsAsync(cancellationToken);

            if (!response.IsSuccess)
            {
                return response.ToProblem();
            }

            reports = response.Value;
        }

        var withSubstitutes = reports.Where(r => r.HasSubstitute).ToList();

        return Ok(new
        {
            totalWithSubstitutes = withSubstitutes.Count,
            reports = withSubstitutes
        });
    }

    [HttpGet("without-substitutes")]
    public async Task<IActionResult> GetRidersWithoutSubstitutes(
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<HungerDisabilityReportResponse> reports;

        if (startDate.HasValue && endDate.HasValue)
        {
            var response = await service.GetReportsByDateRangeAsync(
                startDate.Value,
                endDate.Value,
                cancellationToken);

            if (!response.IsSuccess)
            {
                return response.ToProblem();
            }

            reports = response.Value;
        }
        else
        {
            var response = await service.GetAllReportsAsync(cancellationToken);

            if (!response.IsSuccess)
            {
                return response.ToProblem();
            }

            reports = response.Value;
        }

        var withoutSubstitutes = reports.Where(r => !r.HasSubstitute).ToList();

        return Ok(new
        {
            totalWithoutSubstitutes = withoutSubstitutes.Count,
            warning = "⚠️ These disabled riders appear in shift data without active substitutions",
            reports = withoutSubstitutes
        });
    }
}