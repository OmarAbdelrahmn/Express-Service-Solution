using Application.Extensions;
using Application.Service.Empolyee;
using Application.Service.Member;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
public class MemberController(IMemberService housingService) : ControllerBase
{
    private readonly IMemberService housingService = housingService;

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingDashboard(iqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("details")]
    public async Task<IActionResult> GetHousingDetails()
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingDetails(iqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // Employees
    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployees()
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingEmployees(iqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("employees/{employeeIqamaNo}")]
    public async Task<IActionResult> GetEmployeeDetails(long employeeIqamaNo)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetEmployeeDetails(iqamaNo, employeeIqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // Riders
    [HttpGet("riders")]
    public async Task<IActionResult> GetRiders()
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingRiders(iqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("riders/{riderId}/performance")]
    public async Task<IActionResult> GetRiderPerformance(
        int riderId,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetRiderPerformance(iqamaNo, riderId, startDate, endDate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // Shifts
    [HttpGet("shifts")]
    public async Task<IActionResult> GetShifts(
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetRiderShifts(iqamaNo, startDate, endDate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("shifts/summary")]
    public async Task<IActionResult> GetShiftSummary([FromQuery] DateOnly date)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingShiftSummary(iqamaNo, date);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // Vehicles
    [HttpGet("vehicles")]
    public async Task<IActionResult> GetVehicles()
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingVehicles(iqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("vehicles/{vehicleNumber}/history")]
    public async Task<IActionResult> GetVehicleHistory(string vehicleNumber)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetVehicleStatusHistory(iqamaNo, vehicleNumber);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("vehicles/operations/pending")]
    public async Task<IActionResult> GetPendingVehicleOperations()
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetPendingVehicleOperations(iqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // Disabilities & Substitutions
    [HttpGet("disabilities")]
    public async Task<IActionResult> GetDisabilities(
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingDisabilities(iqamaNo, startDate, endDate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("substitutions/active")]
    public async Task<IActionResult> GetActiveSubstitutions()
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetActiveSubstitutions(iqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // Pending Requests
    [HttpGet("requests/employee-updates")]
    public async Task<IActionResult> GetPendingEmployeeUpdates()
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetPendingEmployeeUpdates(iqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("requests/status-changes")]
    public async Task<IActionResult> GetPendingStatusChanges()
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetPendingStatusChanges(iqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    // Reports
    [HttpGet("reports/monthly")]
    public async Task<IActionResult> GetMonthlyReport(
        [FromQuery] int year,
        [FromQuery] int month)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetMonthlyReport(iqamaNo, year, month);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("reports/export")]
    public async Task<IActionResult> ExportReport(
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.ExportHousingReport(iqamaNo, startDate, endDate);

        if (result.IsFailure)
            return result.ToProblem();

        return File(result.Value, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Housing_Report_{startDate}_{endDate}.xlsx");
    }
    [HttpPost("member/login")]
    public async Task<IActionResult> MemberLogin([FromBody] MemberAuthRequest request)
    {
        var response = await housingService.MemberSignInAsync(request);

        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }
}
