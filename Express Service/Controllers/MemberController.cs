using Application.Extensions;
using Application.Service.Member;
using Application.Service.Reminder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using static Application.Service.Member.IMemberService;

namespace Express_Service.Controllers;

[Route("api/[controller]")]
[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize(Roles = "Member")]
//[ResponseCache(Duration = 300)]
public class MemberController(IMemberService housingService, IReminderService reminderService) : ControllerBase
{
    private readonly IMemberService housingService = housingService;
    private readonly IReminderService reminderService = reminderService;


    /// <summary>
    /// Get all spare-part and accessory usage history for this housing,
    /// optionally filtered by date range
    /// </summary>
    [HttpGet("usage-history")]
    public async Task<IActionResult> GetUsageHistory(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingUsageHistoryAsync(iqamaNo, fromDate, toDate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Get every manual spare-part / rider-accessory change made at this
    /// housing (quantity, price, location, etc.) — who did it, when, and the
    /// before/after values. Optionally filter by date range.
    /// </summary>
    [HttpGet("inventory/audit-log")]
    public async Task<IActionResult> GetInventoryAuditLog(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingInventoryAuditLogAsync(iqamaNo, fromDate, toDate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }



    #region Maintenance Reminders

    /// <summary>
    /// Get all vehicles and riders in this housing that need maintenance
    /// on the given date.  Omit checkDate (or pass today's date) for today's list.
    /// Pass tomorrow's date to preview what comes due tomorrow, etc.
    /// </summary>
    [HttpGet("maintenance/reminders")]
    public async Task<IActionResult> GetMaintenanceReminders(
        [FromQuery] DateOnly? checkDate = null)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await reminderService.GetHousingDueMaintenanceAsync(iqamaNo, checkDate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    #endregion

    /// <summary>
    /// Get detailed spending breakdown for spare parts (per vehicle) and
    /// accessories (per rider) over a date range
    /// </summary>
    [HttpGet("reports/spending")]
    public async Task<IActionResult> GetSpendingReport(
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingSpendingReportAsync(
            managerIqamaNo, startDate, endDate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("housing/detailed-daily-performance")]
    public async Task<IActionResult> GetDetailedDailyPerformance(
    [FromQuery] DateOnly startDate,
    [FromQuery] DateOnly endDate,
    CancellationToken cancellationToken = default)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingDetailedDailyPerformanceForManagerAsync(
            managerIqamaNo, startDate, endDate, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("transfers")]
    public async Task<IActionResult> TransferFromHousing([FromBody] MemberTransferRequest request)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.TransferFromHousingAsync(managerIqamaNo, request);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpPost("edit-rider-company")]
    public async Task<IActionResult> editridercompany([FromBody] MemberUpdateRiderCompanyRequest request)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.UpdateRiderCompanyAsync(managerIqamaNo, request);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Get all transfers made by this housing
    /// </summary>
    [HttpGet("transfers")]
    public async Task<IActionResult> GetHousingTransfers()
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingTransfersAsync(managerIqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    #region Spare Parts
    /// <summary>
    /// Get all spare parts in housing inventory
    /// </summary>
    [HttpGet("spare-parts")]
    public async Task<IActionResult> GetSpareParts()
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingSparePartsAsync(managerIqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Get spare part by ID
    /// </summary>
    [HttpGet("spare-parts/{id}")]
    public async Task<IActionResult> GetSparePartById(int id)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetSparePartByIdAsync(managerIqamaNo, id);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Search spare parts by keyword
    /// </summary>
    [HttpGet("spare-parts/search")]
    public async Task<IActionResult> SearchSpareParts([FromQuery] string keyword)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.SearchSparePartsAsync(managerIqamaNo, keyword);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Record batch spare part usage for housing vehicles
    /// </summary>
    [HttpPost("spare-parts/usage/batch")]
    public async Task<IActionResult> RecordBatchSparePartUsage(
        [FromQuery] DateTime Date,
        [FromBody] MemberBatchSparePartUsageRequest request)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.RecordBatchSparePartUsageAsync(Date,
            managerIqamaNo, request);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Get spare part usage history
    /// </summary>
    [HttpGet("spare-parts/{sparePartId}/usage-history")]
    public async Task<IActionResult> GetSparePartUsageHistory(int sparePartId)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetSparePartUsageHistoryAsync(
            managerIqamaNo, sparePartId);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Get spare part usage history for a vehicle
    /// </summary>
    [HttpGet("vehicles/{vehicleNumber}/spare-parts-history")]
    public async Task<IActionResult> GetVehicleSparePartHistory(string vehicleNumber)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetVehicleSparePartHistoryAsync(
            managerIqamaNo, vehicleNumber);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    #endregion

    #region Accessories

    /// <summary>
    /// Get all accessories in housing inventory
    /// </summary>
    [HttpGet("accessories")]
    public async Task<IActionResult> GetAccessories()
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingAccessoriesAsync(managerIqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Get accessory by ID
    /// </summary>
    [HttpGet("accessories/{id}")]
    public async Task<IActionResult> GetAccessoryById(int id)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetAccessoryByIdAsync(managerIqamaNo, id);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Search accessories by keyword
    /// </summary>
    [HttpGet("accessories/search")]
    public async Task<IActionResult> SearchAccessories([FromQuery] string keyword)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.SearchAccessoriesAsync(managerIqamaNo, keyword);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Issue accessories to riders in batch
    /// </summary>
    [HttpPost("accessories/usage/batch")]
    public async Task<IActionResult> RecordBatchAccessoryUsage(
        [FromQuery] DateTime Date,
        [FromBody] MemberBatchAccessoryUsageRequest request)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.RecordBatchAccessoryUsageAsync(Date,
            managerIqamaNo, request);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Get accessory usage history
    /// </summary>
    [HttpGet("accessories/{accessoryId}/usage-history")]
    public async Task<IActionResult> GetAccessoryUsageHistory(int accessoryId)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetAccessoryUsageHistoryAsync(
            managerIqamaNo, accessoryId);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Get accessories issued to a specific rider
    /// </summary>
    [HttpGet("riders/{riderId}/accessories-history")]
    public async Task<IActionResult> GetRiderAccessoryHistory(int riderId)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetRiderAccessoryHistoryAsync(
            managerIqamaNo, riderId);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    #endregion

    #region Cost Tracking

    /// <summary>
    /// Get total cost for a vehicle (spare parts)
    /// </summary>
    [HttpGet("vehicles/{vehicleNumber}/cost")]
    public async Task<IActionResult> GetVehicleCost(string vehicleNumber)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetVehicleCostAsync(
            managerIqamaNo, vehicleNumber);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Get vehicle cost for a date range
    /// </summary>
    [HttpGet("vehicles/{vehicleNumber}/cost/date-range")]
    public async Task<IActionResult> GetVehicleCostByDateRange(
        string vehicleNumber,
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetVehicleCostByDateRangeAsync(
            managerIqamaNo, vehicleNumber, fromDate, toDate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Get total cost for a rider (accessories)
    /// </summary>
    [HttpGet("riders/{riderId}/cost")]
    public async Task<IActionResult> GetRiderCost(int riderId)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetRiderCostAsync(
            managerIqamaNo, riderId);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Get rider cost for a date range
    /// </summary>
    [HttpGet("riders/{riderId}/cost/date-range")]
    public async Task<IActionResult> GetRiderCostByDateRange(
        int riderId,
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetRiderCostByDateRangeAsync(
            managerIqamaNo, riderId, fromDate, toDate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Get cost summary for entire housing
    /// </summary>
    [HttpGet("cost-summary")]
    public async Task<IActionResult> GetHousingCostSummary(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate)
    {
        var managerIqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingCostSummaryAsync(
            managerIqamaNo, fromDate, toDate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    #endregion


    [HttpGet("reports/rider-daily-detail")]
    public async Task<IActionResult> GetRiderDailyDetail(
    [FromQuery] string workingId,
    [FromQuery] DateOnly startDate,
    [FromQuery] DateOnly endDate)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetRiderDailyDetailReportAsync(
            iqamaNo, workingId, startDate, endDate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }


    [HttpDelete("requests/vehicle-operation/{requestId}")]
    public async Task<IActionResult> CancelVehicleOperation(int requestId)
    {
        var managerIqamaNo = long.Parse(User.Identity!.Name!);
        var result = await housingService.CancelVehicleOperationRequestAsync(
            managerIqamaNo,
            requestId
        );

        return result.IsSuccess
            ? Ok(new { message = "Request cancelled successfully" })
            : result.ToProblem();
    }

    [HttpDelete("requests/employee-status/{requestId}")]
    public async Task<IActionResult> CancelEmployeeStatusChange(int requestId)
    {
        var managerIqamaNo = long.Parse(User.Identity!.Name!);
        var result = await housingService.CancelEmployeeStatusChangeRequestAsync(
            managerIqamaNo,
            requestId
        );

        return result.IsSuccess
            ? Ok(new { message = "Request cancelled successfully" })
            : result.ToProblem();
    }

    //// Generic endpoint
    //[HttpDelete("requests/cancel")]
    //public async Task<IActionResult> CancelRequest(
    //    [FromQuery] RequestType requestType,
    //    [FromQuery] int requestId)
    //{
    //    var managerIqamaNo = long.Parse(User.Identity!.Name!);
    //    var result = await housingService.CancelRequestAsync(
    //        managerIqamaNo,
    //        requestType,
    //        requestId
    //    );

    //    return result.IsSuccess
    //        ? Ok(new { message = "Request cancelled successfully" })
    //        : result.ToProblem();
    //}

    /// <summary>
    /// Get summary report for all riders in housing showing hours and orders performance
    /// </summary>
    [HttpGet("reports/riders-summary")]
    public async Task<IActionResult> GetAllRidersSummary(
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetAllRidersSummaryReportAsync(
            iqamaNo, startDate, endDate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Get rejection report for all riders showing rejection rates
    /// </summary>
    [HttpGet("reports/rejection")]
    public async Task<IActionResult> GetRejectionReport(
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetRejectionReportAsync(
            iqamaNo, startDate, endDate);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    [HttpGet("reports/compare-periods")]
    public async Task<IActionResult> ComparePeriodOrders(
    [FromQuery] DateOnly period2Start,
    [FromQuery] DateOnly period2End)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.ComparePeriodOrdersAsync(
            iqamaNo, period2Start, period2End);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Get daily summary for housing on specific date
    /// </summary>
    [HttpGet("reports/daily-summary")]
    public async Task<IActionResult> GetDailySummary([FromQuery] DateOnly date)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingDailySummaryAsync(iqamaNo, date);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    /// <summary>
    /// Get detailed daily report with individual rider performances
    /// </summary>
    [HttpGet("reports/daily-detailed")]
    public async Task<IActionResult> GetDailyDetailed([FromQuery] DateOnly date)
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingDailyDetailedReportAsync(iqamaNo, date);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }

    //[HttpGet("housing/rider-daily-detail")]
    //public async Task<IActionResult> GetHousingRiderDailyDetail(
    //[FromQuery] long managerIqamaNo,
    //[FromQuery] string workingId,
    //[FromQuery] DateOnly startDate,
    //[FromQuery] DateOnly endDate)
    //{
    //    var result = await housingService.GetHousingRiderDailyDetailReportAsync(
    //        managerIqamaNo, workingId, startDate, endDate);
    //    return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    //}

    ///// <summary>
    ///// Get summary report for specific housing
    ///// </summary>
    //[HttpGet("housing/riders-summary")]
    //public async Task<IActionResult> GetHousingSummary(
    //    [FromQuery] long managerIqamaNo,
    //    [FromQuery] DateOnly startDate,
    //    [FromQuery] DateOnly endDate)
    //{
    //    var result = await housingService.GetHousingAllRidersSummaryReportAsync(
    //        managerIqamaNo, startDate, endDate);
    //    return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    //}

    ///// <summary>
    ///// Get rejection report for specific housing
    ///// </summary>
    //[HttpGet("housing/rejection")]
    //public async Task<IActionResult> GetHousingRejection(
    //    [FromQuery] long managerIqamaNo,
    //    [FromQuery] DateOnly startDate,
    //    [FromQuery] DateOnly endDate)
    //{
    //    var result = await housingService.GetHousingRejectionReportAsync(
    //        managerIqamaNo, startDate, endDate);
    //    return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    //}

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

    [HttpGet("vehicles/problems")]
    public async Task<IActionResult> GetProblemVehicles()
    {
        var iqamaNo = User.GetUserIqamaNo()!;
        var result = await housingService.GetHousingProblemVehicles(iqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }


    [HttpGet("rider/history")]
    public async Task<IActionResult> GetRiderHistory(
    [FromQuery] long riderIqamaNo)

    {
        var iqamaNo = User.GetUserIqamaNo();
        var result = await housingService.GetRiderMonthlyHistoryForHousingAsync(iqamaNo,
            riderIqamaNo);
        return result.IsSuccess ? Ok(result.Value) : result.ToProblem();
    }
    [HttpPost("vehicles/request-fix-problem")]
    public async Task<IActionResult> RequestFixProblem([FromBody] MemberFixVehicleRequest request)
    {
        var managerIqamaNo = User.GetUserIqamaNo();
        var result = await housingService.RequestFixVehicleProblemForHousingAsync(managerIqamaNo, request);
        return result.IsSuccess
            ? Ok(new { message = "Vehicle fix request submitted successfully" })
            : result.ToProblem();
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
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
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
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> MemberLogin([FromBody] MemberAuthRequest request)
    {
        var response = await housingService.MemberSignInAsync(request);

        return response.IsSuccess ?
            Ok(response.Value) :
            response.ToProblem();
    }

    [HttpPost("employees/request-status-change")]
    public async Task<IActionResult> RequestStatusChange([FromBody] MemberStatusChangeRequest request)
    {
        var managerIqamaNo = User.GetUserIqamaNo();
        var result = await housingService.RequestEmployeeStatusChangeForHousingAsync(managerIqamaNo, request);
        return result.IsSuccess
            ? Ok(new { message = "Status change request submitted successfully" })
            : result.ToProblem();
    }



    [HttpPost("vehicles/request-take")]
    public async Task<IActionResult> RequestTakeVehicle([FromBody] MemberVehicleOperationRequest request)
    {
        var managerIqamaNo = User.GetUserIqamaNo();
        var result = await housingService.RequestTakeVehicleForHousingAsync(managerIqamaNo, request);
        return result.IsSuccess
            ? Ok(new { message = "Vehicle take request submitted successfully" })
            : result.ToProblem();
    }

    [HttpPost("vehicles/request-return")]
    public async Task<IActionResult> RequestReturnVehicle([FromBody] MemberVehicleOperationRequest request)
    {
        var managerIqamaNo = User.GetUserIqamaNo();
        var result = await housingService.RequestReturnVehicleForHousingAsync(managerIqamaNo, request);
        return result.IsSuccess
            ? Ok(new { message = "Vehicle return request submitted successfully" })
            : result.ToProblem();
    }

    [HttpPost("vehicles/request-report-problem")]
    public async Task<IActionResult> RequestReportProblem([FromBody] MemberVehicleOperationRequest request)
    {
        var managerIqamaNo = User.GetUserIqamaNo();
        var result = await housingService.RequestReportProblemForHousingAsync(managerIqamaNo, request);
        return result.IsSuccess
            ? Ok(new { message = "Vehicle problem report submitted successfully" })
            : result.ToProblem();
    }


    [HttpPost("vehicles/request-switch-vehicel")]
    public async Task<IActionResult> Requestswitch([FromBody] MemberSwitchVehicleRequest request)
    {
        var managerIqamaNo = User.GetUserIqamaNo();
        var result = await housingService.RequestSwitchVehicleForHousingAsync(managerIqamaNo, request);
        return result.IsSuccess
            ? Ok(new { message = "Vehicle problem report submitted successfully" })
            : result.ToProblem();
    }
}
