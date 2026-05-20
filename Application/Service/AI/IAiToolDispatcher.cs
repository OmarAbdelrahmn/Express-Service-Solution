// Application/Service/AI/AiToolDispatcher.cs
using Application.Service.Admin;
using Application.Service.Empolyee;
using Application.Service.HungerReports;
using Application.Service.Riders;
using Application.Service.Wallet;
using Application.Service.Petrol;
using Application.Service.SparePart;
using Application.Service.RiderAccessory;
using Application.Service.SupplierSer;
using Application.Service.Transfer;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Application.Service.AI;

public interface IAiToolDispatcher
{
    Task<AiChatResponse> DispatchAsync(string funcName, JsonElement args, string callerUserId);
    Task<AiChatResponse> ExecuteConfirmedAsync(string token);
}

public class AiToolDispatcher(
    // ── Core DB ──────────────────────────────────────────────────────────
    ApplicationDbcontext db,

    // ── Admin / Users ─────────────────────────────────────────────────────
    IAdminService adminService,

    // ── Employees & Riders ────────────────────────────────────────────────
    IEmployeeService employeeService,
    IRiderService riderService,
    IRiderShiftService shiftService,
    IRiderSub riderSub,
    IRiderWorkingIdHistoryService workingIdHistory,

    // ── Vehicles ──────────────────────────────────────────────────────────
    IVehicleService vehicleService,

    // ── Housing & Companies ───────────────────────────────────────────────
    IHousingService housingService,
    ICompanyService companyService,

    // ── Reports ───────────────────────────────────────────────────────────
    IHungerReportService hungerReport,

    // ── Financial / Operational ───────────────────────────────────────────
    IWalletService walletService,
    IPetrolService petrolService,
    ISparePartService sparePartService,
    IRiderAccessoryService accessoryService,

    // ── Suppliers / Inventory ─────────────────────────────────────────────
    ISupplierService supplierService,
    IBillService billService,
    ITransferService transferService,

    // ── Confirmation ──────────────────────────────────────────────────────
    IAiConfirmationStore confirmStore

) : IAiToolDispatcher
{
    // ═══════════════════════════════════════════════════════════════════════
    //  DISPATCHER
    // ═══════════════════════════════════════════════════════════════════════
    public async Task<AiChatResponse> DispatchAsync(
        string funcName, JsonElement args, string callerUserId)
    {
        return funcName switch
        {
            // ── Users ────────────────────────────────────────────────────
            "get_all_users" => await HandleGetAllUsers(),
            "get_user_by_name" => await HandleGetUserByName(args),
            "toggle_user_status" => BuildConfirmation("ToggleUserStatus",
                                          $"Toggle status for user '{S(args, "userName")}'", args),
            "delete_user" => BuildConfirmation("DeleteUser",
                                          $"Permanently delete user '{S(args, "userName")}'", args),
            "reset_password" => BuildConfirmation("ResetPassword",
                                          $"Reset password for '{S(args, "userName")}'", args),

            // ── Employees ────────────────────────────────────────────────
            "get_all_employees" => await HandleGetAllEmployees(),
            "get_employee_by_iqama" => await HandleGetEmployeeByIqama(args),
            "get_employees_by_status" => await HandleGetEmployeesByStatus(args),
            "get_employees_by_housing" => await HandleGetEmployeesByHousing(args),
            "get_employees_expiring_iqama" => await HandleGetEmployeesExpiringIqama(args),
            "get_employees_not_in_ksa" => await HandleGetEmployeesNotInKsa(),
            "get_escaped_employees" => await HandleGetEscapedEmployees(),
            "get_iqama_expiry_report" => await HandleGetIqamaExpiryReport(args),
            "get_employee_status_history" => await HandleGetEmployeeStatusHistory(args),
            "get_status_change_statistics" => await HandleGetStatusChangeStatistics(),

            // ── Riders ───────────────────────────────────────────────────
            "get_all_riders" => await HandleGetAllRiders(),
            "get_rider_by_iqama" => await HandleGetRiderByIqama(args),
            "get_rider_by_working_id" => await HandleGetRiderByWorkingId(args),
            "get_riders_by_company" => await HandleGetRidersByCompany(args),
            "get_riders_by_housing" => await HandleGetRidersByHousing(args),
            "get_rider_vehicle" => await HandleGetRiderVehicle(args),
            "smart_search_riders" => await HandleSmartSearch(args),
            "get_employee_statistics" => await HandleGetEmployeeStatistics(),
            "get_rider_status_logs" => await HandleGetRiderStatusLogs(args),
            "get_working_id_history" => await HandleGetWorkingIdHistory(args),

            // ── Rider substitutions ───────────────────────────────────────
            "get_active_substitutions" => await HandleGetActiveSubstitutions(),
            "get_all_substitutions" => await HandleGetAllSubstitutions(),
            "start_substitution" => BuildConfirmation("StartSubstitution",
                                            $"Start substitution: '{S(args, "actualRiderWorkingId")}' → '{S(args, "substituteWorkingId")}'", args),
            "stop_substitution" => BuildConfirmation("StopSubstitution",
                                            $"Stop substitution for WorkingId '{S(args, "workingId")}'", args),

            // ── Shift reports ─────────────────────────────────────────────
            "get_top_riders_by_orders" => await HandleGetTopRidersByOrders(args),
            "get_top_riders_by_orders_month" => await HandleGetTopRidersByOrdersMonth(args),
            "get_rider_shift_history" => await HandleGetRiderShiftHistory(args),
            "get_daily_shift_report" => await HandleGetDailyShiftReport(args),
            "get_company_performance_summary" => await HandleGetCompanyPerformanceSummary(args),
            "get_riders_high_rejection" => await HandleGetRidersHighRejection(args),
            "get_riders_by_working_hours" => await HandleGetRidersByWorkingHours(args),
            "get_riders_zero_orders" => await HandleGetRidersZeroOrders(args),
            "get_shift_summary_range" => await HandleGetShiftSummaryRange(args),
            "get_shifts_by_date" => await HandleGetShiftsByDate(args),

            // ── Hunger report ─────────────────────────────────────────────
            "get_hunger_monthly_validation" => await HandleGetHungerMonthlyValidation(args),

            // ── Monthly validity ──────────────────────────────────────────
            "get_monthly_validity_report" => await HandleGetMonthlyValidityReport(args),
            "get_invalid_riders_month" => await HandleGetInvalidRidersMonth(args),
            "get_freelancers_month" => await HandleGetFreelancersMonth(args),

            // ── Housing ───────────────────────────────────────────────────
            "get_all_housing" => await HandleGetAllHousing(),
            "get_housing_by_name" => await HandleGetHousingByName(args),
            "get_housing_occupancy" => await HandleGetHousingOccupancy(args),

            // ── Vehicles ──────────────────────────────────────────────────
            "get_all_vehicles" => await HandleGetAllVehicles(),
            "get_vehicle_by_plate" => await HandleGetVehicleByPlate(args),
            "get_vehicles_expiring_license" => await HandleGetVehiclesExpiringLicense(args),
            "get_unassigned_vehicles" => await HandleGetUnassignedVehicles(),
            "get_available_vehicles" => await HandleGetAvailableVehicles(),
            "get_unavailable_vehicles" => await HandleGetUnavailableVehicles(args),
            "get_vehicle_history" => await HandleGetVehicleHistory(args),
            "get_vehicles_grouped_by_status" => await HandleGetVehiclesGroupedByStatus(),
            "get_rider_vehicle_history" => await HandleGetRiderVehicleHistory(args),
            "take_vehicle" => BuildConfirmation("TakeVehicle",
                                    $"Assign vehicle '{S(args, "plateNumberA")}' to rider Iqama '{S(args, "iqamaNo")}'", args),
            "return_vehicle" => BuildConfirmation("ReturnVehicle",
                                    $"Return vehicle '{S(args, "plateNumberA")}' from rider Iqama '{S(args, "iqamaNo")}'", args),
            "report_vehicle_problem" => BuildConfirmation("ReportVehicleProblem",
                                    $"Report problem on vehicle '{S(args, "plateNumberA")}'", args),
            "mark_vehicle_stolen" => BuildConfirmation("MarkVehicleStolen",
                                    $"Mark vehicle '{S(args, "plateNumberA")}' as stolen", args),

            // ── Wallet ────────────────────────────────────────────────────
            "get_all_wallet_records" => await HandleGetAllWalletRecords(),
            "get_wallet_by_rider" => await HandleGetWalletByRider(args),
            "get_wallet_summary_range" => await HandleGetWalletSummaryRange(args),
            "get_top_earners" => await HandleGetTopEarners(args),

            // ── Petrol ────────────────────────────────────────────────────
            "get_petrol_daily_report" => await HandleGetPetrolDailyReport(args),
            "get_rider_petrol_monthly" => await HandleGetRiderPetrolMonthly(args),
            "get_all_riders_petrol_summary" => await HandleGetAllRidersPetrolSummary(args),
            "get_vehicle_petrol_monthly" => await HandleGetVehiclePetrolMonthly(args),
            "get_all_vehicles_petrol_summary" => await HandleGetAllVehiclesPetrolSummary(args),
            "get_unattributed_petrol" => await HandleGetUnattributedPetrol(args),

            // ── Spare parts & accessories ─────────────────────────────────
            "get_all_spare_parts" => await HandleGetAllSpareParts(),
            "get_spare_parts_usage_history" => await HandleGetSparePartsUsageHistory(args),
            "get_vehicle_spare_parts" => await HandleGetVehicleSpareParts(args),
            "get_all_accessories" => await HandleGetAllAccessories(),
            "get_rider_accessories" => await HandleGetRiderAccessories(args),
            "get_housing_cost_report" => await HandleGetHousingCostReport(args),
            "get_all_housings_cost_summary" => await HandleGetAllHousingsCostSummary(args),

            // ── Suppliers & Bills ─────────────────────────────────────────
            "get_all_suppliers" => await HandleGetAllSuppliers(),
            "get_all_bills" => await HandleGetAllBills(),
            "get_bills_by_supplier" => await HandleGetBillsBySupplier(args),
            "get_bills_by_date_range" => await HandleGetBillsByDateRange(args),

            // ── Transfers ────────────────────────────────────────────────
            "get_all_transfers" => await HandleGetAllTransfers(),
            "get_transfers_by_housing" => await HandleGetTransfersByHousing(args),

            // ── Companies ────────────────────────────────────────────────
            "get_all_companies" => await HandleGetAllCompanies(),

            // ── Multi-service / aggregated ────────────────────────────────
            "get_rider_full_profile" => await HandleGetRiderFullProfile(args),
            "get_company_full_dashboard" => await HandleGetCompanyFullDashboard(args),
            "get_housing_full_dashboard" => await HandleGetHousingFullDashboard(args),
            "get_operational_overview" => await HandleGetOperationalOverview(),

            _ => new AiChatResponse("I don't have a handler for that operation yet.")
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONFIRMED WRITE EXECUTION
    // ═══════════════════════════════════════════════════════════════════════
    public async Task<AiChatResponse> ExecuteConfirmedAsync(string token)
    {
        var pending = confirmStore.Pop(token);
        if (pending is null)
            return new AiChatResponse(
                "Confirmation expired or not found. Please try the action again.");

        var args = JsonSerializer.Deserialize<JsonElement>(pending.ArgsJson);

        return pending.ActionType switch
        {
            "ToggleUserStatus" => await ExecToggle(args),
            "DeleteUser" => await ExecDeleteUser(args),
            "ResetPassword" => await ExecResetPassword(args),
            "StartSubstitution" => await ExecStartSubstitution(args),
            "StopSubstitution" => await ExecStopSubstitution(args),
            "TakeVehicle" => await ExecTakeVehicle(args),
            "ReturnVehicle" => await ExecReturnVehicle(args),
            "ReportVehicleProblem" => await ExecReportVehicleProblem(args),
            "MarkVehicleStolen" => await ExecMarkVehicleStolen(args),
            _ => new AiChatResponse("Unknown confirmed action.")
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  USER HANDLERS
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetAllUsers()
    {
        var users = await adminService.GetAllUsers();
        var list = users.ToList();
        return new AiChatResponse($"Found {list.Count} registered users.", Data: list);
    }

    private async Task<AiChatResponse> HandleGetUserByName(JsonElement args)
    {
        var userName = S(args, "userName");
        var result = await adminService.GetUser2Async(userName);
        return result.IsSuccess
            ? new AiChatResponse($"Profile for '{userName}'.", Data: result.Value)
            : new AiChatResponse($"User '{userName}' was not found.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  EMPLOYEE HANDLERS (delegate to IEmployeeService)
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetAllEmployees()
    {
        var result = await employeeService.GetAllEmployee();
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} employees.", Data: result.Value)
            : new AiChatResponse("No employees found.");
    }

    private async Task<AiChatResponse> HandleGetEmployeeByIqama(JsonElement args)
    {
        if (!long.TryParse(S(args, "iqamaNo"), out var iqama))
            return new AiChatResponse("Invalid Iqama number.");
        var result = await employeeService.Get1(iqama);
        return result.IsSuccess
            ? new AiChatResponse($"Employee {iqama}.", Data: result.Value)
            : new AiChatResponse($"No employee with Iqama {iqama}.");
    }

    private async Task<AiChatResponse> HandleGetEmployeesByStatus(JsonElement args)
    {
        var status = S(args, "status");
        var filter = new EmployeeFilter(Status: status);
        var result = await employeeService.Filter(filter);
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} employees with status '{status}'.", Data: result.Value)
            : new AiChatResponse("No results.");
    }

    private async Task<AiChatResponse> HandleGetEmployeesByHousing(JsonElement args)
    {
        var housing = S(args, "housingName");
        var filter = new EmployeeFilter(HousingName: housing);
        var result = await employeeService.Filter(filter);
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} employees in '{housing}'.", Data: result.Value)
            : new AiChatResponse("No results.");
    }

    private async Task<AiChatResponse> HandleGetEmployeesExpiringIqama(JsonElement args)
    {
        int days = I(args, "days", 30);
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3).AddDays(days));
        var emps = await db.Employees
            .Where(e => !e.IsDeleted && e.IqamaEndM <= cutoff)
            .Select(e => new
            {
                e.IqamaNo,
                e.NameEN,
                e.NameAR,
                e.JobTitle,
                e.IqamaEndM,
                e.Status,
                e.Phone,
                DaysLeft = (e.IqamaEndM.ToDateTime(TimeOnly.MinValue) -
                            DateTime.UtcNow.AddHours(3).Date).Days
            })
            .OrderBy(e => e.IqamaEndM).ToListAsync();
        return new AiChatResponse(
            $"Found {emps.Count} employees with Iqama expiring within {days} days.",
            Data: emps);
    }

    private async Task<AiChatResponse> HandleGetEmployeesNotInKsa()
    {
        var filter = new EmployeeFilter(INKSA: false);
        var result = await employeeService.Filter(filter);
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} employees not in KSA.", Data: result.Value)
            : new AiChatResponse("No results.");
    }

    private async Task<AiChatResponse> HandleGetEscapedEmployees()
    {
        var escaped = await db.Employees
            .Where(e => !e.IsDeleted && e.EscapedDetails != null && e.EscapedDetails.IsActive)
            .Include(e => e.EscapedDetails)
            .Select(e => new
            {
                e.IqamaNo,
                e.NameEN,
                e.NameAR,
                e.Phone,
                EscapedAt = e.EscapedDetails!.EscapedAt,
                ActivePath = e.EscapedDetails.ActivePath.ToString(),
                RemovalDeadline = e.EscapedDetails.RemovalDeadline,
                RemainingDays = e.EscapedDetails.RemainingDaysToRemoval
            })
            .OrderBy(e => e.RemainingDays).ToListAsync();
        return new AiChatResponse(
            $"Found {escaped.Count} escaped employees.", Data: escaped);
    }

    private async Task<AiChatResponse> HandleGetIqamaExpiryReport(JsonElement args)
    {
        var urgencyStr = S(args, "urgency");
        IqamaExpiryUrgency? urgency = urgencyStr switch
        {
            "Expired" => IqamaExpiryUrgency.Expired,
            "Critical" => IqamaExpiryUrgency.Critical,
            "Warning" => IqamaExpiryUrgency.Warning,
            "Upcoming" => IqamaExpiryUrgency.Upcoming,
            "Safe" => IqamaExpiryUrgency.Safe,
            _ => null
        };
        var result = await employeeService.GetIqamaEndReportAsync(
            urgency,
            Sn(args, "housingName"),
            Sn(args, "sponsor"));
        return result.IsSuccess
            ? new AiChatResponse(
                $"Iqama expiry report: {result.Value.TotalEmployees} total | " +
                $"{result.Value.ExpiredCount} expired | {result.Value.CriticalCount} critical.",
                Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetEmployeeStatusHistory(JsonElement args)
    {
        if (!long.TryParse(S(args, "iqamaNo"), out var iqama))
            return new AiChatResponse("Invalid Iqama number.");
        var result = await employeeService.GetEmployeeStatusHistoryAsync(iqama);
        return result.IsSuccess
            ? new AiChatResponse($"Status history for {iqama}.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetStatusChangeStatistics()
    {
        var result = await employeeService.GetStatusChangeStatisticsAsync();
        return result.IsSuccess
            ? new AiChatResponse("Status change statistics.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  RIDER HANDLERS (delegate to IRiderService)
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetAllRiders()
    {
        var result = await riderService.GetAllEmployee2();
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} riders.", Data: result.Value)
            : new AiChatResponse("No riders found.");
    }

    private async Task<AiChatResponse> HandleGetRiderByIqama(JsonElement args)
    {
        if (!long.TryParse(S(args, "iqamaNo"), out var iqama))
            return new AiChatResponse("Invalid Iqama number.");
        var result = await riderService.Getbyid(iqama);
        return result.IsSuccess
            ? new AiChatResponse($"Rider {iqama}.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetRiderByWorkingId(JsonElement args)
    {
        var workingId = S(args, "workingId");
        var rider = await db.RiderDetails
            .Include(r => r.Employee).ThenInclude(e => e.Housing)
            .Include(r => r.Company).Include(r => r.Vehicle)
            .Where(r => r.WorkingId == workingId)
            .Select(r => new
            {
                r.Id,
                r.WorkingId,
                NameEN = r.Employee.NameEN,
                NameAR = r.Employee.NameAR,
                IqamaNo = r.EmployeeIqamaNo,
                CompanyName = r.Company.Name,
                r.TshirtSize,
                r.LicenseNumber,
                VehicleNumber = r.Vehicle != null ? r.Vehicle.VehicleNumber : null,
                HousingName = r.Employee.Housing != null ? r.Employee.Housing.Name : null,
                EmployeeStatus = r.Employee.Status,
                Phone = r.Employee.Phone
            }).FirstOrDefaultAsync();
        return rider is null
            ? new AiChatResponse($"No rider with WorkingId '{workingId}'.")
            : new AiChatResponse($"Rider '{workingId}'.", Data: rider);
    }

    private async Task<AiChatResponse> HandleGetRidersByCompany(JsonElement args)
    {
        var company = S(args, "companyName");
        var filter = new EmployeeFilterr(CompanyName: company);
        var result = await riderService.Filter(filter);
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} riders at '{company}'.", Data: result.Value)
            : new AiChatResponse("No results.");
    }

    private async Task<AiChatResponse> HandleGetRidersByHousing(JsonElement args)
    {
        var housing = S(args, "housingName");
        var filter = new EmployeeFilterr(HousingName: housing);
        var result = await riderService.Filter(filter);
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} riders in '{housing}'.", Data: result.Value)
            : new AiChatResponse("No results.");
    }

    private async Task<AiChatResponse> HandleGetRiderVehicle(JsonElement args)
    {
        if (!long.TryParse(S(args, "iqamaNo"), out var iqama))
            return new AiChatResponse("Invalid Iqama number.");
        var result = await riderService.GetRiderVehicle(iqama);
        return result.IsSuccess
            ? new AiChatResponse($"Vehicle for rider {iqama}.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleSmartSearch(JsonElement args)
    {
        var keyword = S(args, "keyword");
        var results = await riderService.SmartSearch(keyword);
        return new AiChatResponse($"Found {results.Count} results for '{keyword}'.", Data: results);
    }

    private async Task<AiChatResponse> HandleGetEmployeeStatistics()
    {
        var result = await riderService.GetEmployeeStatistics();
        return result.IsSuccess
            ? new AiChatResponse("Employee statistics.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetRiderStatusLogs(JsonElement args)
    {
        if (!long.TryParse(S(args, "iqamaNo"), out var iqama))
            return new AiChatResponse("Invalid Iqama number.");
        var result = await riderService.GetStatusLogsAsync(iqama);
        return result.IsSuccess
            ? new AiChatResponse($"Status logs for {iqama}.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetWorkingIdHistory(JsonElement args)
    {
        var workingId = S(args, "workingId");
        var result = await workingIdHistory.WhoHasWorkingId(workingId, default);
        return result.IsSuccess
            ? new AiChatResponse($"History for WorkingId '{workingId}'.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SUBSTITUTION HANDLERS
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetActiveSubstitutions()
    {
        var result = await riderSub.GetActiveSubstitutions();
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} active substitutions.", Data: result.Value)
            : new AiChatResponse("No active substitutions.");
    }

    private async Task<AiChatResponse> HandleGetAllSubstitutions()
    {
        var result = await riderSub.GetAllSubstitutions();
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} total substitutions.", Data: result.Value)
            : new AiChatResponse("No substitutions found.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SHIFT REPORT HANDLERS (delegate to IRiderShiftService)
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetTopRidersByOrders(JsonElement args)
    {
        var companyName = Sn(args, "companyName");
        var startDate = ParseDate(Sn(args, "startDate"));
        var endDate = ParseDate(Sn(args, "endDate")) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var topN = I(args, "topN", 10);
        if (startDate is null) return new AiChatResponse("Please provide a start date.");

        var query = db.RiderShifts
            .Include(s => s.Rider).ThenInclude(r => r.Employee)
            .Include(s => s.Company)
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate);

        if (!string.IsNullOrWhiteSpace(companyName))
            query = query.Where(s => s.Company.Name.ToLower().Contains(companyName.ToLower()));

        var results = await query
            .GroupBy(s => new { s.RiderId, s.WorkingId, NameEN = s.Rider.Employee.NameEN, CompanyName = s.Company.Name })
            .Select(g => new
            {
                g.Key.WorkingId,
                g.Key.NameEN,
                g.Key.CompanyName,
                TotalAcceptedOrders = g.Sum(s => s.AcceptedDailyOrders),
                TotalWorkingHours = Math.Round((double)g.Sum(s => s.WorkingHours), 1),
                TotalShifts = g.Count(),
                BestDayOrders = g.Max(s => s.AcceptedDailyOrders)
            })
            .OrderByDescending(r => r.TotalAcceptedOrders).Take(topN).ToListAsync();

        return new AiChatResponse(
            $"Top {results.Count} riders from {startDate} to {endDate}.", Data: results);
    }

    private async Task<AiChatResponse> HandleGetTopRidersByOrdersMonth(JsonElement args)
    {
        var monthStr = S(args, "month");
        var companyName = Sn(args, "companyName");
        var topN = I(args, "topN", 10);
        if (!TryParseMonth(monthStr, out var year, out var month))
            return new AiChatResponse("Please provide month as YYYY-MM.");

        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        var query = db.RiderShifts
            .Include(s => s.Rider).ThenInclude(r => r.Employee)
            .Include(s => s.Company)
            .Where(s => s.ShiftDate >= start && s.ShiftDate <= end);

        if (!string.IsNullOrWhiteSpace(companyName))
            query = query.Where(s => s.Company.Name.ToLower().Contains(companyName.ToLower()));

        var results = await query
            .GroupBy(s => new { s.WorkingId, NameEN = s.Rider.Employee.NameEN, NameAR = s.Rider.Employee.NameAR, CompanyName = s.Company.Name })
            .Select(g => new
            {
                g.Key.WorkingId,
                g.Key.NameEN,
                g.Key.NameAR,
                g.Key.CompanyName,
                TotalAcceptedOrders = g.Sum(s => s.AcceptedDailyOrders),
                TotalRejectedOrders = g.Sum(s => s.RejectedDailyOrders),
                TotalWorkingHours = Math.Round((double)g.Sum(s => s.WorkingHours), 1),
                WorkingDays = g.Count(),
                AvgOrdersPerDay = Math.Round(g.Average(s => s.AcceptedDailyOrders), 1),
                BestDayOrders = g.Max(s => s.AcceptedDailyOrders)
            })
            .OrderByDescending(r => r.TotalAcceptedOrders).Take(topN).ToListAsync();

        var ranked = results.Select((r, i) => new { Rank = i + 1, r.WorkingId, r.NameEN, r.NameAR, r.CompanyName, r.TotalAcceptedOrders, r.TotalRejectedOrders, r.TotalWorkingHours, r.WorkingDays, r.AvgOrdersPerDay, r.BestDayOrders }).ToList();
        var co = string.IsNullOrWhiteSpace(companyName) ? "all companies" : companyName;
        return new AiChatResponse($"Top {ranked.Count} riders at {co} for {monthStr}.", Data: ranked);
    }

    private async Task<AiChatResponse> HandleGetRiderShiftHistory(JsonElement args)
    {
        var workingId = S(args, "workingId");
        var result = await shiftService.GetShiftsByRiderAsync(workingId);
        return result.IsSuccess
            ? new AiChatResponse($"Shift history for '{workingId}': {result.Value.Count()} shifts.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetDailyShiftReport(JsonElement args)
    {
        var dateStr = S(args, "date");
        if (!DateOnly.TryParse(dateStr, out var date))
            return new AiChatResponse("Invalid date format.");
        var result = await shiftService.GetShiftsByDateAsync(date);
        return result.IsSuccess
            ? new AiChatResponse($"Daily report for {date}: {result.Value.Count()} riders.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetShiftsByDate(JsonElement args)
        => await HandleGetDailyShiftReport(args);

    private async Task<AiChatResponse> HandleGetCompanyPerformanceSummary(JsonElement args)
    {
        var startDate = ParseDate(Sn(args, "startDate"));
        var endDate = ParseDate(Sn(args, "endDate")) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        if (startDate is null) return new AiChatResponse("Please provide a start date.");

        var summary = await db.RiderShifts
            .Include(s => s.Company)
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .GroupBy(s => s.Company.Name)
            .Select(g => new
            {
                CompanyName = g.Key,
                TotalAcceptedOrders = g.Sum(s => s.AcceptedDailyOrders),
                TotalRejectedOrders = g.Sum(s => s.RejectedDailyOrders),
                TotalWorkingHours = Math.Round((double)g.Sum(s => s.WorkingHours), 1),
                TotalShifts = g.Count(),
                UniqueRiders = g.Select(s => s.RiderId).Distinct().Count(),
                AvgOrdersPerRider = Math.Round(g.Average(s => s.AcceptedDailyOrders), 1)
            })
            .OrderByDescending(c => c.TotalAcceptedOrders).ToListAsync();

        return new AiChatResponse($"Company performance from {startDate} to {endDate}.", Data: summary);
    }

    private async Task<AiChatResponse> HandleGetRidersHighRejection(JsonElement args)
    {
        var startDate = ParseDate(Sn(args, "startDate"));
        var endDate = ParseDate(Sn(args, "endDate")) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var companyName = Sn(args, "companyName");
        var topN = I(args, "topN", 10);
        if (startDate is null) return new AiChatResponse("Please provide a start date.");

        var query = db.RiderShifts.Include(s => s.Rider).ThenInclude(r => r.Employee)
            .Include(s => s.Company).Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate);
        if (!string.IsNullOrWhiteSpace(companyName))
            query = query.Where(s => s.Company.Name.ToLower().Contains(companyName.ToLower()));

        var results = await query
            .GroupBy(s => new { s.WorkingId, NameEN = s.Rider.Employee.NameEN, CompanyName = s.Company.Name })
            .Select(g => new
            {
                g.Key.WorkingId,
                g.Key.NameEN,
                g.Key.CompanyName,
                TotalAccepted = g.Sum(s => s.AcceptedDailyOrders),
                TotalRejected = g.Sum(s => s.RejectedDailyOrders),
                RejectionRate = g.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders) == 0 ? 0 :
                    Math.Round((double)g.Sum(s => s.RejectedDailyOrders) /
                               (double)g.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders) * 100, 1)
            })
            .OrderByDescending(r => r.TotalRejected).Take(topN).ToListAsync();

        return new AiChatResponse($"Top {results.Count} riders with highest rejections.", Data: results);
    }

    private async Task<AiChatResponse> HandleGetRidersByWorkingHours(JsonElement args)
    {
        var startDate = ParseDate(Sn(args, "startDate"));
        var endDate = ParseDate(Sn(args, "endDate")) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var companyName = Sn(args, "companyName");
        var topN = I(args, "topN", 10);
        if (startDate is null) return new AiChatResponse("Please provide a start date.");

        var query = db.RiderShifts.Include(s => s.Rider).ThenInclude(r => r.Employee)
            .Include(s => s.Company).Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate);
        if (!string.IsNullOrWhiteSpace(companyName))
            query = query.Where(s => s.Company.Name.ToLower().Contains(companyName.ToLower()));

        var results = await query
            .GroupBy(s => new { s.WorkingId, NameEN = s.Rider.Employee.NameEN, CompanyName = s.Company.Name })
            .Select(g => new
            {
                g.Key.WorkingId,
                g.Key.NameEN,
                g.Key.CompanyName,
                TotalWorkingHours = Math.Round((double)g.Sum(s => s.WorkingHours), 1),
                TotalAcceptedOrders = g.Sum(s => s.AcceptedDailyOrders),
                TotalShifts = g.Count()
            })
            .OrderByDescending(r => r.TotalWorkingHours).Take(topN).ToListAsync();

        return new AiChatResponse($"Top {results.Count} riders by working hours.", Data: results);
    }

    private async Task<AiChatResponse> HandleGetRidersZeroOrders(JsonElement args)
    {
        var dateStr = S(args, "date");
        if (!DateOnly.TryParse(dateStr, out var date))
            return new AiChatResponse("Invalid date.");
        var companyName = Sn(args, "companyName");

        var query = db.RiderShifts.Include(s => s.Rider).ThenInclude(r => r.Employee)
            .Include(s => s.Company)
            .Where(s => s.ShiftDate == date && s.AcceptedDailyOrders == 0);
        if (!string.IsNullOrWhiteSpace(companyName))
            query = query.Where(s => s.Company.Name.ToLower().Contains(companyName.ToLower()));

        var riders = await query
            .Select(s => new { s.WorkingId, NameEN = s.Rider.Employee.NameEN, CompanyName = s.Company.Name, s.WorkingHours, s.ShiftStatus })
            .ToListAsync();

        return new AiChatResponse($"Found {riders.Count} riders with zero orders on {date}.", Data: riders);
    }

    private async Task<AiChatResponse> HandleGetShiftSummaryRange(JsonElement args)
    {
        var workingId = S(args, "workingId");
        var startDate = ParseDate(Sn(args, "startDate"));
        var endDate = ParseDate(Sn(args, "endDate")) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        if (startDate is null) return new AiChatResponse("Please provide a start date.");

        var summary = await db.RiderShifts
            .Where(s => s.WorkingId == workingId && s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .GroupBy(s => s.WorkingId)
            .Select(g => new
            {
                WorkingId = g.Key,
                TotalAcceptedOrders = g.Sum(s => s.AcceptedDailyOrders),
                TotalRejectedOrders = g.Sum(s => s.RejectedDailyOrders),
                TotalWorkingHours = Math.Round((double)g.Sum(s => s.WorkingHours), 1),
                TotalShifts = g.Count(),
                BestDayOrders = g.Max(s => s.AcceptedDailyOrders),
                AvgOrdersPerDay = Math.Round(g.Average(s => s.AcceptedDailyOrders), 1)
            }).FirstOrDefaultAsync();

        return summary is null
            ? new AiChatResponse($"No shifts found for '{workingId}' in range.")
            : new AiChatResponse($"Summary for '{workingId}'.", Data: summary);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HUNGER REPORT
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetHungerMonthlyValidation(JsonElement args)
    {
        var monthStr = S(args, "month");
        if (!TryParseMonth(monthStr, out var year, out var month))
            return new AiChatResponse("Please provide month as YYYY-MM.");

        var result = await hungerReport.GetHungerMonthlyRiderValidationAsync(year, month);
        return result.IsSuccess
            ? new AiChatResponse(
                $"Hunger validation for {monthStr}: {result.Value.ValidRiders} valid, {result.Value.InvalidRiders} invalid.",
                Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MONTHLY VALIDITY
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetMonthlyValidityReport(JsonElement args)
    {
        var monthStr = S(args, "month");
        if (!TryParseMonth(monthStr, out var year, out var month))
            return new AiChatResponse("Please provide month as YYYY-MM.");

        var companyName = Sn(args, "companyName");
        var query = db.RiderMonthlyValidities
            .Include(v => v.Employee).ThenInclude(e => e.RiderDetails).ThenInclude(r => r!.Company)
            .Where(v => v.Year == year && v.Month == month);

        if (!string.IsNullOrWhiteSpace(companyName))
            query = query.Where(v => v.Employee.RiderDetails != null &&
                                     v.Employee.RiderDetails.Company.Name.ToLower().Contains(companyName.ToLower()));

        var records = await query.Select(v => new
        {
            IqamaNo = v.EmployeeIqamaNo,
            NameEN = v.Employee.NameEN,
            NameAR = v.Employee.NameAR,
            CompanyName = v.Employee.RiderDetails != null ? v.Employee.RiderDetails.Company.Name : null,
            Status = v.Status.ToString(),
            v.TotalOrders,
            v.CreatedAt
        }).OrderBy(v => v.Status).ThenByDescending(v => v.TotalOrders).ToListAsync();

        var valid = records.Count(r => r.Status == "Valid");
        var invalid = records.Count(r => r.Status == "Invalid");
        var freelancer = records.Count(r => r.Status == "Freelancer");

        return new AiChatResponse(
            $"Validity for {monthStr}: {valid} valid, {invalid} invalid, {freelancer} freelancers.",
            Data: new { Summary = new { Valid = valid, Invalid = invalid, Freelancer = freelancer }, Records = records });
    }

    private async Task<AiChatResponse> HandleGetInvalidRidersMonth(JsonElement args)
    {
        var monthStr = S(args, "month");
        if (!TryParseMonth(monthStr, out var year, out var month))
            return new AiChatResponse("Please provide month as YYYY-MM.");

        var records = await db.RiderMonthlyValidities
            .Include(v => v.Employee).ThenInclude(e => e.RiderDetails).ThenInclude(r => r!.Company)
            .Where(v => v.Year == year && v.Month == month && v.Status == ValidityStatus.Invalid)
            .Select(v => new { IqamaNo = v.EmployeeIqamaNo, v.Employee.NameEN, v.Employee.NameAR, CompanyName = v.Employee.RiderDetails != null ? v.Employee.RiderDetails.Company.Name : null, v.TotalOrders })
            .OrderBy(v => v.TotalOrders).ToListAsync();

        return new AiChatResponse($"Found {records.Count} invalid riders for {monthStr}.", Data: records);
    }

    private async Task<AiChatResponse> HandleGetFreelancersMonth(JsonElement args)
    {
        var monthStr = S(args, "month");
        var freelancers = await db.KetaFreeLancers
            .Include(f => f.Rider).ThenInclude(r => r.Employee)
            .Where(f => f.Month == monthStr)
            .Select(f => new { f.WorkingId, NameEN = f.Rider.Employee.NameEN, f.TotalOrders, f.CreatedAt })
            .OrderByDescending(f => f.TotalOrders).ToListAsync();

        return new AiChatResponse($"Found {freelancers.Count} freelancers for '{monthStr}'.", Data: freelancers);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HOUSING HANDLERS
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetAllHousing()
    {
        var result = await housingService.GetAllEmployee();
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} housing units.", Data: result.Value)
            : new AiChatResponse("No housing found.");
    }

    private async Task<AiChatResponse> HandleGetHousingByName(JsonElement args)
    {
        var name = S(args, "housingName");
        var result = await housingService.Get(name);
        return result.IsSuccess
            ? new AiChatResponse($"Housing results for '{name}'.", Data: result.Value)
            : new AiChatResponse($"Housing '{name}' not found.");
    }

    private async Task<AiChatResponse> HandleGetHousingOccupancy(JsonElement args)
    {
        var housingName = S(args, "housingName");
        var housing = await db.Housings
            .Include(h => h.Employees)
            .Where(h => h.Name.ToLower().Contains(housingName.ToLower()))
            .Select(h => new
            {
                h.Id,
                h.Name,
                h.Address,
                h.Capacity,
                Employees = h.Employees.Where(e => !e.IsDeleted)
                    .Select(e => new { e.IqamaNo, e.NameEN, e.NameAR, e.JobTitle, e.Status }).ToList(),
                CurrentOccupancy = h.Employees.Count(e => !e.IsDeleted),
                AvailableSlots = h.Capacity - h.Employees.Count(e => !e.IsDeleted)
            }).FirstOrDefaultAsync();

        return housing is null
            ? new AiChatResponse($"Housing '{housingName}' not found.")
            : new AiChatResponse(
                $"Housing '{housing.Name}': {housing.CurrentOccupancy}/{housing.Capacity} occupied.",
                Data: housing);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  VEHICLE HANDLERS (delegate to IVehicleService)
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetAllVehicles()
    {
        var result = await vehicleService.GetAllVehiclesWithRidersAsync();
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} vehicles.", Data: result.Value)
            : new AiChatResponse("No vehicles found.");
    }

    private async Task<AiChatResponse> HandleGetVehicleByPlate(JsonElement args)
    {
        var plate = S(args, "plateNumberA");
        var result = await vehicleService.GetVehicleWithRiderByVehicleNumberAsync(plate);
        return result.IsSuccess
            ? new AiChatResponse($"Vehicle '{plate}'.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetVehiclesExpiringLicense(JsonElement args)
    {
        int days = I(args, "days", 30);
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3).AddDays(days));
        var vehicles = await db.Vehicles
            .Include(v => v.RiderDetails).ThenInclude(r => r!.Employee)
            .Where(v => v.LicenseExpiryDate <= cutoff)
            .Select(v => new
            {
                v.VehicleNumber,
                v.PlateNumberA,
                v.LicenseExpiryDate,
                DaysLeft = (v.LicenseExpiryDate.ToDateTime(TimeOnly.MinValue) - DateTime.UtcNow.AddHours(3).Date).Days,
                AssignedTo = v.RiderDetails != null ? v.RiderDetails.Employee.NameEN : "Unassigned"
            })
            .OrderBy(v => v.LicenseExpiryDate).ToListAsync();

        return new AiChatResponse($"Found {vehicles.Count} vehicles with license expiring within {days} days.", Data: vehicles);
    }

    private async Task<AiChatResponse> HandleGetUnassignedVehicles()
    {
        var result = await vehicleService.GetAvailableVehiclesAsync();
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} available vehicles.", Data: result.Value)
            : new AiChatResponse("No available vehicles.");
    }

    private async Task<AiChatResponse> HandleGetAvailableVehicles()
        => await HandleGetUnassignedVehicles();

    private async Task<AiChatResponse> HandleGetUnavailableVehicles(JsonElement args)
    {
        var filter = S(args, "statusFilter") is { Length: > 0 } f ? f : "all";
        var result = await vehicleService.GetUnavailableVehiclesAsync(filter);
        return result.IsSuccess
            ? new AiChatResponse($"Unavailable vehicles ({filter}): {result.Value.TotalCount}.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetVehicleHistory(JsonElement args)
    {
        var plate = S(args, "plateNumberA");
        var result = await vehicleService.GetVehicleHistoryAsync1(plate);
        return result.IsSuccess
            ? new AiChatResponse($"History for vehicle '{plate}'.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetVehiclesGroupedByStatus()
    {
        var result = await vehicleService.GetVehiclesGroupedByStatusAsync();
        return result.IsSuccess
            ? new AiChatResponse("Vehicles grouped by status.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetRiderVehicleHistory(JsonElement args)
    {
        if (!long.TryParse(S(args, "iqamaNo"), out var iqama))
            return new AiChatResponse("Invalid Iqama number.");
        var result = await vehicleService.GetVehicleHistoryByIqamaAsync(iqama);
        return result.IsSuccess
            ? new AiChatResponse($"Vehicle history for rider {iqama}.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  WALLET HANDLERS
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetAllWalletRecords()
    {
        var result = await walletService.GetAllAsync();
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} wallet records.", Data: result.Value)
            : new AiChatResponse("No wallet records.");
    }

    private async Task<AiChatResponse> HandleGetWalletByRider(JsonElement args)
    {
        var workingId = S(args, "workingId");
        var records = await db.Wallets
            .Include(w => w.WorkedRider).ThenInclude(r => r.Employee)
            .Where(w => w.WorkedRider.WorkingId == workingId)
            .OrderByDescending(w => w.Date)
            .Select(w => new { w.Date, w.Amount, WorkedRider = w.WorkedRider.Employee.NameEN, HasSubstitution = w.MainRiderId.HasValue })
            .ToListAsync();
        var total = records.Sum(r => r.Amount);
        return new AiChatResponse($"{records.Count} wallet entries for '{workingId}'. Total: {total:F2} SAR.", Data: new { TotalAmount = total, Records = records });
    }

    private async Task<AiChatResponse> HandleGetWalletSummaryRange(JsonElement args)
    {
        var startDate = ParseDate(Sn(args, "startDate"));
        var endDate = ParseDate(Sn(args, "endDate")) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        if (startDate is null) return new AiChatResponse("Please provide a start date.");

        var summary = await db.Wallets
            .Include(w => w.WorkedRider).ThenInclude(r => r.Employee)
            .Where(w => w.Date >= startDate && w.Date <= endDate)
            .GroupBy(w => new { w.WorkedRiderId, WorkingId = w.WorkedRider.WorkingId, NameEN = w.WorkedRider.Employee.NameEN })
            .Select(g => new { g.Key.WorkingId, g.Key.NameEN, TotalAmount = g.Sum(w => w.Amount), PaymentDays = g.Count() })
            .OrderByDescending(r => r.TotalAmount).ToListAsync();

        return new AiChatResponse(
            $"Wallet summary {startDate} to {endDate}: {summary.Count} riders, total {summary.Sum(s => s.TotalAmount):F2} SAR.",
            Data: summary);
    }

    private async Task<AiChatResponse> HandleGetTopEarners(JsonElement args)
    {
        var monthStr = S(args, "month");
        if (!TryParseMonth(monthStr, out var year, out var month))
            return new AiChatResponse("Please provide month as YYYY-MM.");
        var topN = I(args, "topN", 10);
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        var earners = await db.Wallets
            .Include(w => w.WorkedRider).ThenInclude(r => r.Employee)
            .Where(w => w.Date >= start && w.Date <= end)
            .GroupBy(w => new { WorkingId = w.WorkedRider.WorkingId, NameEN = w.WorkedRider.Employee.NameEN, NameAR = w.WorkedRider.Employee.NameAR })
            .Select(g => new { g.Key.WorkingId, g.Key.NameEN, g.Key.NameAR, TotalEarnings = g.Sum(w => w.Amount), PaymentDays = g.Count() })
            .OrderByDescending(r => r.TotalEarnings).Take(topN).ToListAsync();

        return new AiChatResponse($"Top {earners.Count} earners for {monthStr}.", Data: earners);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PETROL HANDLERS
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetPetrolDailyReport(JsonElement args)
    {
        var dateStr = S(args, "date");
        if (!DateOnly.TryParse(dateStr, out var date))
            return new AiChatResponse("Invalid date format.");
        var result = await petrolService.GetDailyReportAsync(date);
        return result.IsSuccess
            ? new AiChatResponse($"Petrol report for {date}: {result.Value.TotalVehicles} vehicles, {result.Value.TotalCost:F2} SAR.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetRiderPetrolMonthly(JsonElement args)
    {
        if (!long.TryParse(S(args, "iqamaNo"), out var iqama))
            return new AiChatResponse("Invalid Iqama number.");
        var monthStr = S(args, "month");
        if (!TryParseMonth(monthStr, out var year, out var month))
            return new AiChatResponse("Please provide month as YYYY-MM.");
        var result = await petrolService.GetRiderMonthlyReportAsync(iqama, year, month);
        return result.IsSuccess
            ? new AiChatResponse($"Petrol report for rider {iqama} in {monthStr}: {result.Value.TotalCost:F2} SAR.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetAllRidersPetrolSummary(JsonElement args)
    {
        var monthStr = S(args, "month");
        if (!TryParseMonth(monthStr, out var year, out var month))
            return new AiChatResponse("Please provide month as YYYY-MM.");
        var result = await petrolService.GetAllRidersSummaryAsync(year, month);
        return result.IsSuccess
            ? new AiChatResponse($"Petrol summary for {monthStr}: {result.Value.Count} riders.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetVehiclePetrolMonthly(JsonElement args)
    {
        var vehicleNumber = S(args, "vehicleNumber");
        var monthStr = S(args, "month");
        if (!TryParseMonth(monthStr, out var year, out var month))
            return new AiChatResponse("Please provide month as YYYY-MM.");
        var result = await petrolService.GetVehicleMonthlyReportAsync(vehicleNumber, year, month);
        return result.IsSuccess
            ? new AiChatResponse($"Petrol report for vehicle '{vehicleNumber}' in {monthStr}.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetAllVehiclesPetrolSummary(JsonElement args)
    {
        var monthStr = S(args, "month");
        if (!TryParseMonth(monthStr, out var year, out var month))
            return new AiChatResponse("Please provide month as YYYY-MM.");
        var result = await petrolService.GetAllVehiclesSummaryAsync(year, month);
        return result.IsSuccess
            ? new AiChatResponse($"Vehicle petrol summary for {monthStr}.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetUnattributedPetrol(JsonElement args)
    {
        var monthStr = S(args, "month");
        if (!TryParseMonth(monthStr, out var year, out var month))
            return new AiChatResponse("Please provide month as YYYY-MM.");
        var result = await petrolService.GetUnattributedCostsAsync(year, month);
        return result.IsSuccess
            ? new AiChatResponse($"Unattributed petrol costs for {monthStr}: {result.Value.Count} records.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SPARE PARTS & ACCESSORIES
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetAllSpareParts()
    {
        var result = await sparePartService.GetAllAsync();
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} spare parts.", Data: result.Value)
            : new AiChatResponse("No spare parts found.");
    }

    private async Task<AiChatResponse> HandleGetSparePartsUsageHistory(JsonElement args)
    {
        var sparePartId = I(args, "sparePartId", 0);
        if (sparePartId == 0) return new AiChatResponse("Please provide sparePartId.");
        var result = await sparePartService.GetUsageHistoryAsync(sparePartId);
        return result.IsSuccess
            ? new AiChatResponse($"Usage history for spare part {sparePartId}.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetVehicleSpareParts(JsonElement args)
    {
        var vehicleNumber = S(args, "vehicleNumber");
        var result = await sparePartService.GetVehicleUsageHistoryAsync(vehicleNumber);
        return result.IsSuccess
            ? new AiChatResponse($"Spare parts usage for vehicle '{vehicleNumber}'.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetAllAccessories()
    {
        var result = await accessoryService.GetAllAsync();
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} accessories.", Data: result.Value)
            : new AiChatResponse("No accessories found.");
    }

    private async Task<AiChatResponse> HandleGetRiderAccessories(JsonElement args)
    {
        var riderId = I(args, "riderId", 0);
        if (riderId == 0) return new AiChatResponse("Please provide riderId.");
        var result = await accessoryService.GetRiderAccessoriesAsync(riderId);
        return result.IsSuccess
            ? new AiChatResponse($"Accessories for rider {riderId}.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetHousingCostReport(JsonElement args)
    {
        var housingName = S(args, "housingName");
        var from = DateTime.Parse(S(args, "fromDate"));
        var to = DateTime.Parse(S(args, "toDate"));
        var result = await sparePartService.GetHousingDetailedCostAsync(housingName, from, to);
        return result.IsSuccess
            ? new AiChatResponse($"Cost report for '{housingName}': {result.Value.GrandTotal:F2} SAR total.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetAllHousingsCostSummary(JsonElement args)
    {
        var from = DateTime.Parse(S(args, "fromDate"));
        var to = DateTime.Parse(S(args, "toDate"));
        var result = await sparePartService.GetAllHousingsCostSummaryAsync(from, to);
        return result.IsSuccess
            ? new AiChatResponse($"Housing cost summary: total {result.Value.GrandTotalCost:F2} SAR.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SUPPLIERS & BILLS
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetAllSuppliers()
    {
        var result = await supplierService.GetAllAsync();
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} suppliers.", Data: result.Value)
            : new AiChatResponse("No suppliers found.");
    }

    private async Task<AiChatResponse> HandleGetAllBills()
    {
        var result = await billService.GetAllBillsAsync();
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} bills.", Data: result.Value)
            : new AiChatResponse("No bills found.");
    }

    private async Task<AiChatResponse> HandleGetBillsBySupplier(JsonElement args)
    {
        var supplierId = I(args, "supplierId", 0);
        if (supplierId == 0) return new AiChatResponse("Please provide supplierId.");
        var result = await billService.GetBillsBySupplierAsync(supplierId);
        return result.IsSuccess
            ? new AiChatResponse($"Bills for supplier {supplierId}.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    private async Task<AiChatResponse> HandleGetBillsByDateRange(JsonElement args)
    {
        var from = DateTime.Parse(S(args, "fromDate"));
        var to = DateTime.Parse(S(args, "toDate"));
        var result = await billService.GetBillsByDateRangeAsync(from, to);
        return result.IsSuccess
            ? new AiChatResponse($"Bills from {from:d} to {to:d}.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TRANSFERS
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetAllTransfers()
    {
        var result = await transferService.GetAllTransfersAsync();
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} transfers.", Data: result.Value)
            : new AiChatResponse("No transfers found.");
    }

    private async Task<AiChatResponse> HandleGetTransfersByHousing(JsonElement args)
    {
        var housingId = I(args, "housingId", 0);
        if (housingId == 0) return new AiChatResponse("Please provide housingId.");
        var result = await transferService.GetTransfersByHousingAsync(housingId);
        return result.IsSuccess
            ? new AiChatResponse($"Transfers for housing {housingId}.", Data: result.Value)
            : new AiChatResponse(result.Error.Description);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  COMPANIES
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetAllCompanies()
    {
        var result = await companyService.GetAllEmployee();
        return result.IsSuccess
            ? new AiChatResponse($"Found {result.Value.Count()} companies.", Data: result.Value)
            : new AiChatResponse("No companies found.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MULTI-SERVICE AGGREGATED QUERIES
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Full rider profile: personal info + current vehicle + recent shifts + wallet + accessories.
    /// This single call aggregates what previously required 4-5 separate queries.
    /// </summary>
    private async Task<AiChatResponse> HandleGetRiderFullProfile(JsonElement args)
    {
        if (!long.TryParse(S(args, "iqamaNo"), out var iqama))
            return new AiChatResponse("Invalid Iqama number.");

        var riderResult = await riderService.Getbyid(iqama);
        if (!riderResult.IsSuccess)
            return new AiChatResponse($"Rider {iqama} not found.");

        var rider = riderResult.Value;
        var riderId = await db.RiderDetails
            .Where(r => r.EmployeeIqamaNo == iqama)
            .Select(r => r.Id)
            .FirstOrDefaultAsync();

        // Last 30 days shifts
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var thirtyDaysAgo = today.AddDays(-30);
        var recentShifts = await db.RiderShifts
            .Where(s => s.WorkingId == rider.WorkingId && s.ShiftDate >= thirtyDaysAgo)
            .OrderByDescending(s => s.ShiftDate)
            .Select(s => new { s.ShiftDate, s.AcceptedDailyOrders, s.WorkingHours, s.ShiftStatus })
            .ToListAsync();

        // Current wallet for this month
        var walletThisMonth = await db.Wallets
            .Where(w => w.WorkedRider.WorkingId == rider.WorkingId &&
                        w.Date.Year == today.Year && w.Date.Month == today.Month)
            .SumAsync(w => (decimal?)w.Amount) ?? 0;

        // Accessories
        var accessories = riderId > 0
            ? (await accessoryService.GetRiderAccessoriesAsync(riderId)).Value?.ToList()
            : null;

        // Active vehicle status
        var vehicleResult = await riderService.GetRiderVehicle(iqama);

        var profile = new
        {
            PersonalInfo = rider,
            Vehicle = vehicleResult.IsSuccess ? (object)vehicleResult.Value : "No vehicle assigned",
            RecentShifts = new
            {
                Period = $"{thirtyDaysAgo} to {today}",
                Count = recentShifts.Count,
                TotalOrders = recentShifts.Sum(s => s.AcceptedDailyOrders),
                TotalHours = Math.Round(recentShifts.Sum(s => s.WorkingHours), 1),
                Shifts = recentShifts
            },
            WalletThisMonth = walletThisMonth,
            Accessories = accessories
        };

        return new AiChatResponse($"Full profile for rider {iqama}.", Data: profile);
    }

    /// <summary>
    /// Company dashboard: riders count, current month performance, top/bottom performers, validity status.
    /// </summary>
    private async Task<AiChatResponse> HandleGetCompanyFullDashboard(JsonElement args)
    {
        var companyName = S(args, "companyName");
        var monthStr = S(args, "month");
        if (!TryParseMonth(monthStr, out var year, out var month))
            return new AiChatResponse("Please provide month as YYYY-MM.");

        var company = await db.Companies
            .FirstOrDefaultAsync(c => c.Name.ToLower().Contains(companyName.ToLower()));
        if (company is null) return new AiChatResponse($"Company '{companyName}' not found.");

        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        var riderCount = await db.RiderDetails.CountAsync(r => r.CompanyId == company.Id);

        var monthlyShifts = await db.RiderShifts
            .Include(s => s.Rider).ThenInclude(r => r.Employee)
            .Where(s => s.CompanyId == company.Id && s.ShiftDate >= start && s.ShiftDate <= end)
            .GroupBy(s => new { s.WorkingId, NameEN = s.Rider.Employee.NameEN })
            .Select(g => new
            {
                g.Key.WorkingId,
                g.Key.NameEN,
                TotalOrders = g.Sum(s => s.AcceptedDailyOrders),
                TotalHours = Math.Round((double)g.Sum(s => s.WorkingHours), 1),
                WorkingDays = g.Count()
            })
            .OrderByDescending(r => r.TotalOrders).ToListAsync();

        var validity = await db.RiderMonthlyValidities
            .Include(v => v.Employee)
            .Where(v => v.Employee.RiderDetails != null &&
                        v.Employee.RiderDetails.CompanyId == company.Id &&
                        v.Year == year && v.Month == month)
            .GroupBy(v => v.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        var dashboard = new
        {
            Company = company.Name,
            Month = monthStr,
            TotalRiders = riderCount,
            MonthlyStats = new
            {
                TotalShifts = monthlyShifts.Sum(r => r.WorkingDays),
                TotalOrders = monthlyShifts.Sum(r => r.TotalOrders),
                TotalHours = monthlyShifts.Sum(r => r.TotalHours),
                ActiveRiders = monthlyShifts.Count
            },
            Top5 = monthlyShifts.Take(5),
            Bottom5 = monthlyShifts.TakeLast(5).Reverse(),
            ValidityBreakdown = validity
        };

        return new AiChatResponse($"Dashboard for '{company.Name}' — {monthStr}.", Data: dashboard);
    }

    /// <summary>
    /// Housing dashboard: residents, vehicles, recent shifts, costs, iqama alerts.
    /// </summary>
    private async Task<AiChatResponse> HandleGetHousingFullDashboard(JsonElement args)
    {
        var housingName = S(args, "housingName");
        var housing = await db.Housings
            .Include(h => h.Employees)
            .FirstOrDefaultAsync(h => h.Name.ToLower().Contains(housingName.ToLower()));
        if (housing is null) return new AiChatResponse($"Housing '{housingName}' not found.");

        var iqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();
        var riderIds = await db.RiderDetails.Where(r => iqamas.Contains(r.EmployeeIqamaNo)).Select(r => r.Id).ToListAsync();
        var workingIds = await db.RiderDetails.Where(r => iqamas.Contains(r.EmployeeIqamaNo)).Select(r => r.WorkingId).ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        // Expiring iqamas within 60 days
        var expiringIqama = housing.Employees
            .Where(e => !e.IsDeleted && (e.IqamaEndM.DayNumber - today.DayNumber) <= 60)
            .Select(e => new { e.IqamaNo, e.NameEN, e.IqamaEndM, DaysLeft = e.IqamaEndM.DayNumber - today.DayNumber })
            .OrderBy(e => e.DaysLeft).ToList();

        // Vehicles assigned to housing residents
        var vehicles = await db.Vehicles
            .Where(v => v.Location == housing.Name)
            .Select(v => new { v.VehicleNumber, v.PlateNumberA, v.VehicleType, v.LicenseExpiryDate })
            .ToListAsync();

        // Yesterday's orders (if any)
        var yesterday = today.AddDays(-1);
        var yesterdayOrders = await db.RiderShifts
            .Where(s => workingIds.Contains(s.WorkingId) && s.ShiftDate == yesterday)
            .Select(s => new { s.WorkingId, s.AcceptedDailyOrders, s.WorkingHours })
            .ToListAsync();

        var dashboard = new
        {
            Housing = new { housing.Name, housing.Address, housing.Capacity, CurrentOccupancy = iqamas.Count, AvailableSlots = housing.Capacity - iqamas.Count },
            ExpiringIqamas = expiringIqama,
            Vehicles = vehicles,
            YesterdayPerformance = new
            {
                Date = yesterday,
                ActiveRiders = yesterdayOrders.Count,
                TotalOrders = yesterdayOrders.Sum(s => s.AcceptedDailyOrders),
                TotalHours = Math.Round(yesterdayOrders.Sum(s => s.WorkingHours), 1),
            }
        };

        return new AiChatResponse($"Dashboard for housing '{housing.Name}'.", Data: dashboard);
    }

    /// <summary>
    /// System-wide operational snapshot: totals across all domains.
    /// </summary>
    private async Task<AiChatResponse> HandleGetOperationalOverview()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var thisMonth = new DateOnly(today.Year, today.Month, 1);

        var overview = new
        {
            GeneratedAt = DateTime.UtcNow.AddHours(3),
            Employees = new
            {
                Total = await db.Employees.CountAsync(e => !e.IsDeleted),
                Riders = await db.Employees.CountAsync(e => !e.IsDeleted && !e.IsEmployee),
                Employees = await db.Employees.CountAsync(e => !e.IsDeleted && e.IsEmployee),
                Enabled = await db.Employees.CountAsync(e => !e.IsDeleted && e.Status == "enable"),
                Disabled = await db.Employees.CountAsync(e => !e.IsDeleted && e.Status == "disable"),
                Escaped = await db.Employees.CountAsync(e => !e.IsDeleted && e.EscapedDetails != null && e.EscapedDetails.IsActive),
                NotInKSA = await db.Employees.CountAsync(e => !e.IsDeleted && !e.INKSA)
            },
            Housing = new
            {
                Total = await db.Housings.CountAsync(),
                TotalCapacity = await db.Housings.SumAsync(h => h.Capacity),
                CurrentOccupancy = await db.Employees.CountAsync(e => !e.IsDeleted && e.HousingId != null)
            },
            Vehicles = new
            {
                Total = await db.Vehicles.CountAsync(),
                Assigned = await db.RiderDetails.CountAsync(r => r.VehicleNumber != null),
                Available = await db.Vehicles.CountAsync(v => !db.RiderVehicleStatus.Any(s => s.VehicleNumber == v.VehicleNumber && s.IsActive && s.StatusType == VehicleStatusType.Taken))
            },
            TodayShifts = new
            {
                Count = await db.RiderShifts.CountAsync(s => s.ShiftDate == today),
                TotalOrders = await db.RiderShifts.Where(s => s.ShiftDate == today).SumAsync(s => (int?)s.AcceptedDailyOrders) ?? 0
            },
            ThisMonthShifts = new
            {
                Count = await db.RiderShifts.CountAsync(s => s.ShiftDate >= thisMonth),
                TotalOrders = await db.RiderShifts.Where(s => s.ShiftDate >= thisMonth).SumAsync(s => (int?)s.AcceptedDailyOrders) ?? 0
            },
            ActiveSubstitutions = await db.RiderShiftSubstitutions.CountAsync(s => s.IsActive),
            Companies = await db.Companies.CountAsync()
        };

        return new AiChatResponse("System operational overview.", Data: overview);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  WRITE OPERATION EXECUTORS
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> ExecToggle(JsonElement args)
    {
        var r = await adminService.ToggleStatusAsync(S(args, "userName"));
        return r.IsSuccess
            ? new AiChatResponse($"✅ Status toggled for '{S(args, "userName")}'.")
            : new AiChatResponse($"❌ {r.Error.Description}");
    }

    private async Task<AiChatResponse> ExecDeleteUser(JsonElement args)
    {
        var r = await adminService.DeletaUserAsync(S(args, "userName"));
        return r.IsSuccess
            ? new AiChatResponse($"✅ User '{S(args, "userName")}' deleted.")
            : new AiChatResponse($"❌ {r.Error.Description}");
    }

    private async Task<AiChatResponse> ExecResetPassword(JsonElement args)
    {
        var r = await adminService.ResetPasswordAsync(S(args, "userName"));
        return r.IsSuccess
            ? new AiChatResponse($"✅ Password reset for '{S(args, "userName")}'.")
            : new AiChatResponse($"❌ {r.Error.Description}");
    }

    private async Task<AiChatResponse> ExecStartSubstitution(JsonElement args)
    {
        var request = new StartSubstitutionRequest(
            S(args, "actualRiderWorkingId"),
            S(args, "substituteWorkingId"),
            S(args, "reason"),
            Sn(args, "createdBy"));
        var r = await riderSub.StartSubstitution(request);
        return r.IsSuccess
            ? new AiChatResponse($"✅ Substitution started.", Data: r.Value)
            : new AiChatResponse($"❌ {r.Error.Description}");
    }

    private async Task<AiChatResponse> ExecStopSubstitution(JsonElement args)
    {
        var r = await riderSub.StopSubstitutionByWorkingId(S(args, "workingId"));
        return r.IsSuccess
            ? new AiChatResponse($"✅ Substitution stopped.", Data: r.Value)
            : new AiChatResponse($"❌ {r.Error.Description}");
    }

    private async Task<AiChatResponse> ExecTakeVehicle(JsonElement args)
    {
        if (!long.TryParse(S(args, "iqamaNo"), out var iqama))
            return new AiChatResponse("Invalid Iqama number.");
        var r = await vehicleService.TakeVehicleAsync(iqama, S(args, "plateNumberA"), S(args, "reason"), S(args, "permission"));
        return r.IsSuccess
            ? new AiChatResponse("✅ Vehicle assigned successfully.")
            : new AiChatResponse($"❌ {r.Error.Description}");
    }

    private async Task<AiChatResponse> ExecReturnVehicle(JsonElement args)
    {
        if (!long.TryParse(S(args, "iqamaNo"), out var iqama))
            return new AiChatResponse("Invalid Iqama number.");
        var r = await vehicleService.ReturnVehicleAsync(iqama, S(args, "plateNumberA"), S(args, "reason"));
        return r.IsSuccess
            ? new AiChatResponse("✅ Vehicle returned successfully.")
            : new AiChatResponse($"❌ {r.Error.Description}");
    }

    private async Task<AiChatResponse> ExecReportVehicleProblem(JsonElement args)
    {
        long? iqama = long.TryParse(Sn(args, "iqamaNo"), out var i) ? i : null;
        var r = await vehicleService.ReportProblemAsync(iqama, S(args, "plateNumberA"), S(args, "reason"));
        return r.IsSuccess
            ? new AiChatResponse("✅ Problem reported successfully.")
            : new AiChatResponse($"❌ {r.Error.Description}");
    }

    private async Task<AiChatResponse> ExecMarkVehicleStolen(JsonElement args)
    {
        long? iqama = long.TryParse(Sn(args, "iqamaNo"), out var i) ? i : null;
        var r = await vehicleService.ReportVehicleStolenAsync(S(args, "plateNumberA"), iqama, Sn(args, "reason"));
        return r.IsSuccess
            ? new AiChatResponse("✅ Vehicle marked as stolen.")
            : new AiChatResponse($"❌ {r.Error.Description}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONFIRMATION BUILDER
    // ═══════════════════════════════════════════════════════════════════════
    private AiChatResponse BuildConfirmation(string actionType, string description, JsonElement args)
    {
        var token = confirmStore.Store(actionType, args.ToString());
        return new AiChatResponse(
            $"⚠️ I'm about to: **{description}**. Do you want to proceed?",
            NeedsConfirmation: true,
            PendingAction: new AiPendingAction(token, actionType, description));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════════
    private static string S(JsonElement args, string key) =>
        args.TryGetProperty(key, out var v) ? v.GetString() ?? string.Empty : string.Empty;

    private static string? Sn(JsonElement args, string key) =>
        args.TryGetProperty(key, out var v) ? v.GetString() : null;

    private static int I(JsonElement args, string key, int def) =>
        args.TryGetProperty(key, out var v) && v.TryGetInt32(out var i) ? i : def;

    private static DateOnly? ParseDate(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null :
        DateOnly.TryParse(raw, out var d) ? d : null;

    private static bool TryParseMonth(string raw, out int year, out int month)
    {
        year = month = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var parts = raw.Split('-');
        return parts.Length == 2
            && int.TryParse(parts[0], out year)
            && int.TryParse(parts[1], out month)
            && month is >= 1 and <= 12;
    }
}