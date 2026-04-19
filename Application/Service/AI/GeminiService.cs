using Application.Service.Admin;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace Application.Service.AI;

public class GeminiService(
    IAdminService adminService,
    ApplicationDbcontext db,
    IConfiguration configuration) : IGeminiService
{
    private readonly string _apiKey = configuration["Gemini:ApiKey"]!;
    private const string GeminiUrl =
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";

    // Token store for pending confirmations (swap for IMemoryCache in production)
    private static readonly Dictionary<string, PendingActionDetails> _pendingActions = new();

    // ═══════════════════════════════════════════════════════════════════════
    //  ENTRY POINT
    // ═══════════════════════════════════════════════════════════════════════
    public async Task<AiChatResponse> ChatAsync(AiChatRequest request, string callerUserId)
    {
        try
        {
            if (request.ConfirmationToken is not null)
                return await ExecuteConfirmedActionAsync(request.ConfirmationToken);

            var contents = BuildContents(request);

            var geminiRequest = new
            {
                contents,
                tools = new[] { new { functionDeclarations = GetToolDeclarations() } },
                systemInstruction = new
                {
                    parts = new[] { new { text = GetSystemPrompt() } }
                }
            };

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(30);

            var json = JsonSerializer.Serialize(geminiRequest);
            var httpResponse = await http.PostAsync(
                $"{GeminiUrl}?key={_apiKey}",
                new StringContent(json, Encoding.UTF8, "application/json"));

            var responseText = await httpResponse.Content.ReadAsStringAsync();

            // ── Check for non-success HTTP status from Gemini ────────────────
            if (!httpResponse.IsSuccessStatusCode)
                return new AiChatResponse(
                    $"Gemini API error ({(int)httpResponse.StatusCode}): {responseText}");

            var geminiResponse = JsonSerializer.Deserialize<JsonElement>(responseText);

            // ── Check Gemini returned candidates ─────────────────────────────
            if (!geminiResponse.TryGetProperty("candidates", out var candidates)
                || candidates.GetArrayLength() == 0)
            {
                // Gemini sometimes returns "promptFeedback" with a block reason instead
                if (geminiResponse.TryGetProperty("promptFeedback", out var feedback))
                    return new AiChatResponse(
                        $"Request was blocked by Gemini: {feedback}");

                return new AiChatResponse(
                    $"Gemini returned no candidates. Raw response: {responseText}");
            }

            var firstCandidate = candidates[0];

            if (firstCandidate.TryGetProperty("finishReason", out var finishReason)
                && finishReason.GetString() == "MALFORMED_FUNCTION_CALL")
            {
                return new AiChatResponse(
                    "I understood what you need but could not form the request correctly. " +
                    "Please be more specific — for example: " +
                    "'top rider at company Hunger for month 2025-04'");
            }

            if (!firstCandidate.TryGetProperty("content", out var content))
                return new AiChatResponse($"Gemini candidate has no content. Raw: {responseText}");

            if (!content.TryGetProperty("parts", out var parts)
                || parts.GetArrayLength() == 0)
                return new AiChatResponse(
                    $"Gemini content has no parts. Raw: {responseText}");

            // ── Check for function call ───────────────────────────────────────
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("functionCall", out var fc))
                {
                    var funcName = fc.GetProperty("name").GetString()!;
                    var args = fc.GetProperty("args");
                    return await DispatchFunctionAsync(funcName, args);
                }
            }

            // ── Plain text reply ──────────────────────────────────────────────
            if (parts[0].TryGetProperty("text", out var textProp))
                return new AiChatResponse(textProp.GetString()!);

            return new AiChatResponse("Gemini returned a response I could not read.");
        }
        catch (TaskCanceledException)
        {
            return new AiChatResponse("The request to Gemini timed out. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            return new AiChatResponse($"Network error reaching Gemini: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return new AiChatResponse($"Failed to parse Gemini response: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new AiChatResponse($"Unexpected error: {ex.Message}");
        }
    }
    // ═══════════════════════════════════════════════════════════════════════
    //  DISPATCHER
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> DispatchFunctionAsync(string funcName, JsonElement args)
    {
        return funcName switch
        {
            // ── Users ────────────────────────────────────────────────────
            "get_all_users" => await HandleGetAllUsers(),
            "get_user_by_name" => await HandleGetUserByName(args),
            "get_user_by_id" => await HandleGetUserById(args),

            // ── Employees ────────────────────────────────────────────────
            "get_all_employees" => await HandleGetAllEmployees(),
            "get_employee_by_iqama" => await HandleGetEmployeeByIqama(args),
            "get_employees_by_status" => await HandleGetEmployeesByStatus(args),
            "get_employees_by_housing" => await HandleGetEmployeesByHousing(args),
            "get_employees_expiring_iqama" => await HandleGetEmployeesExpiringIqama(args),
            "get_employees_not_in_ksa" => await HandleGetEmployeesNotInKsa(),
            "get_escaped_employees" => await HandleGetEscapedEmployees(),

            // ── Riders ───────────────────────────────────────────────────
            "get_all_riders" => await HandleGetAllRiders(),
            "get_rider_by_working_id" => await HandleGetRiderByWorkingId(args),
            "get_riders_by_company" => await HandleGetRidersByCompany(args),
            "get_riders_by_housing" => await HandleGetRidersByHousing(args),
            "get_active_substitutions" => await HandleGetActiveSubstitutions(),

            // ── Shift Reports ────────────────────────────────────────────
            "get_top_riders_by_orders" => await HandleGetTopRidersByOrders(args),
            "get_top_riders_by_orders_month" => await HandleGetTopRidersByOrdersMonth(args),
            "get_rider_shift_history" => await HandleGetRiderShiftHistory(args),
            "get_daily_shift_report" => await HandleGetDailyShiftReport(args),
            "get_company_performance_summary" => await HandleGetCompanyPerformanceSummary(args),
            "get_riders_high_rejection" => await HandleGetRidersHighRejection(args),
            "get_riders_by_working_hours" => await HandleGetRidersByWorkingHours(args),
            "get_riders_zero_orders" => await HandleGetRidersZeroOrders(args),
            "get_shift_summary_range" => await HandleGetShiftSummaryRange(args),

            // ── Monthly Validity ─────────────────────────────────────────
            "get_monthly_validity_report" => await HandleGetMonthlyValidityReport(args),
            "get_invalid_riders_month" => await HandleGetInvalidRidersMonth(args),
            "get_freelancers_month" => await HandleGetFreelancersMonth(args),

            // ── Housing ──────────────────────────────────────────────────
            "get_all_housing" => await HandleGetAllHousing(),
            "get_housing_occupancy" => await HandleGetHousingOccupancy(args),

            // ── Vehicles ─────────────────────────────────────────────────
            "get_all_vehicles" => await HandleGetAllVehicles(),
            "get_vehicle_by_number" => await HandleGetVehicleByNumber(args),
            "get_vehicles_expiring_license" => await HandleGetVehiclesExpiringLicense(args),
            "get_unassigned_vehicles" => await HandleGetUnassignedVehicles(),

            // ── Wallet ───────────────────────────────────────────────────
            "get_wallet_by_rider" => await HandleGetWalletByRider(args),
            "get_wallet_summary_date_range" => await HandleGetWalletSummaryDateRange(args),
            "get_top_earners" => await HandleGetTopEarners(args),

            // ── Hunger Disability ────────────────────────────────────────
            //"get_hunger_disability_records" => await HandleGetHungerDisabilityRecords(args),

            // ── Companies ────────────────────────────────────────────────
            "get_all_companies" => await HandleGetAllCompanies(),

            // ── Write (require confirmation) ──────────────────────────────
            "toggle_user_status" => BuildConfirmation("ToggleUserStatus",
                $"Toggle (enable/disable) status for user '{GetStr(args, "userName")}'", args),
            "delete_user" => BuildConfirmation("DeleteUser",
                $"Permanently delete user '{GetStr(args, "userName")}'", args),
            "reset_password" => BuildConfirmation("ResetPassword",
                $"Reset password for user '{GetStr(args, "userName")}' to the system default", args),

            _ => new AiChatResponse("I don't have a handler for that operation yet.")
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
        var userName = GetStr(args, "userName");
        var result = await adminService.GetUser2Async(userName);
        return result.IsSuccess
            ? new AiChatResponse($"Here is the profile for '{userName}'.", Data: result.Value)
            : new AiChatResponse($"User '{userName}' was not found.");
    }

    private async Task<AiChatResponse> HandleGetUserById(JsonElement args)
    {
        var id = GetStr(args, "userId");
        var result = await adminService.GetUserAsync(id);
        return result.IsSuccess
            ? new AiChatResponse($"User found.", Data: result.Value)
            : new AiChatResponse($"User with ID '{id}' was not found.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  EMPLOYEE HANDLERS
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetAllEmployees()
    {
        var employees = await db.Employees
            .Where(e => !e.IsDeleted)
            .Include(e => e.Housing)
            .Select(e => new
            {
                e.IqamaNo,
                e.NameEN,
                e.NameAR,
                e.JobTitle,
                e.Status,
                e.Country,
                e.Phone,
                e.INKSA,
                HousingName = e.Housing != null ? e.Housing.Name : null,
                e.IqamaEndM,
                e.CreatedAt
            })
            .OrderBy(e => e.NameEN)
            .ToListAsync();

        return new AiChatResponse(
            $"Found {employees.Count} employees in the system.",
            Data: employees);
    }

    private async Task<AiChatResponse> HandleGetEmployeeByIqama(JsonElement args)
    {
        if (!long.TryParse(GetStr(args, "iqamaNo"), out var iqama))
            return new AiChatResponse("Invalid Iqama number format.");

        var employee = await db.Employees
            .Include(e => e.Housing)
            .Include(e => e.RiderDetails)
                .ThenInclude(r => r!.Company)
            .Where(e => e.IqamaNo == iqama && !e.IsDeleted)
            .Select(e => new
            {
                e.IqamaNo,
                e.NameEN,
                e.NameAR,
                e.JobTitle,
                e.Status,
                e.Country,
                e.Phone,
                e.IqamaEndM,
                e.IqamaEndH,
                e.PassportNo,
                e.PassportEnd,
                e.Sponsor,
                e.IBAN,
                e.INKSA,
                e.DateOfBirth,
                e.CreatedAt,
                HousingName = e.Housing != null ? e.Housing.Name : null,
                CompanyName = e.RiderDetails != null ? e.RiderDetails.Company.Name : null,
                WorkingId = e.RiderDetails != null ? e.RiderDetails.WorkingId : null
            })
            .FirstOrDefaultAsync();

        return employee is null
            ? new AiChatResponse($"No employee found with Iqama No: {iqama}")
            : new AiChatResponse($"Employee details for Iqama No: {iqama}", Data: employee);
    }

    private async Task<AiChatResponse> HandleGetEmployeesByStatus(JsonElement args)
    {
        var status = GetStr(args, "status"); // "enable" | "disable" | "escaped"

        var employees = await db.Employees
            .Where(e => !e.IsDeleted && e.Status.ToLower() == status.ToLower())
            .Include(e => e.Housing)
            .Select(e => new
            {
                e.IqamaNo,
                e.NameEN,
                e.NameAR,
                e.JobTitle,
                e.Status,
                e.Country,
                e.Phone,
                e.INKSA,
                HousingName = e.Housing != null ? e.Housing.Name : null
            })
            .OrderBy(e => e.NameEN)
            .ToListAsync();

        return new AiChatResponse(
            $"Found {employees.Count} employees with status '{status}'.",
            Data: employees);
    }

    private async Task<AiChatResponse> HandleGetEmployeesByHousing(JsonElement args)
    {
        var housingName = GetStr(args, "housingName");

        var employees = await db.Employees
            .Where(e => !e.IsDeleted && e.Housing != null &&
                        e.Housing.Name.ToLower().Contains(housingName.ToLower()))
            .Include(e => e.Housing)
            .Select(e => new
            {
                e.IqamaNo,
                e.NameEN,
                e.NameAR,
                e.JobTitle,
                e.Status,
                e.Phone,
                HousingName = e.Housing!.Name
            })
            .OrderBy(e => e.NameEN)
            .ToListAsync();

        return new AiChatResponse(
            $"Found {employees.Count} employees in housing '{housingName}'.",
            Data: employees);
    }

    private async Task<AiChatResponse> HandleGetEmployeesExpiringIqama(JsonElement args)
    {
        int days = GetInt(args, "days", 30);
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3).AddDays(days));

        var employees = await db.Employees
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
            .OrderBy(e => e.IqamaEndM)
            .ToListAsync();

        return new AiChatResponse(
            $"Found {employees.Count} employees with Iqama expiring within {days} days.",
            Data: employees);
    }

    private async Task<AiChatResponse> HandleGetEmployeesNotInKsa()
    {
        var employees = await db.Employees
            .Where(e => !e.IsDeleted && !e.INKSA)
            .Select(e => new
            {
                e.IqamaNo,
                e.NameEN,
                e.NameAR,
                e.JobTitle,
                e.Status,
                e.Phone,
                e.Country
            })
            .OrderBy(e => e.NameEN)
            .ToListAsync();

        return new AiChatResponse(
            $"Found {employees.Count} employees currently NOT in Saudi Arabia.",
            Data: employees);
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
            .OrderBy(e => e.RemainingDays)
            .ToListAsync();

        return new AiChatResponse(
            $"Found {escaped.Count} escaped employees being tracked.",
            Data: escaped);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  RIDER HANDLERS
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetAllRiders()
    {
        var riders = await db.RiderDetails
            .Include(r => r.Employee)
            .Include(r => r.Company)
            .Include(r => r.Vehicle)
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
                EmployeeStatus = r.Employee.Status
            })
            .OrderBy(r => r.NameEN)
            .ToListAsync();

        return new AiChatResponse($"Found {riders.Count} riders in the system.", Data: riders);
    }

    private async Task<AiChatResponse> HandleGetRiderByWorkingId(JsonElement args)
    {
        var workingId = GetStr(args, "workingId");

        var rider = await db.RiderDetails
            .Include(r => r.Employee)
                .ThenInclude(e => e.Housing)
            .Include(r => r.Company)
            .Include(r => r.Vehicle)
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
            })
            .FirstOrDefaultAsync();

        return rider is null
            ? new AiChatResponse($"No rider found with Working ID '{workingId}'.")
            : new AiChatResponse($"Rider details for '{workingId}'.", Data: rider);
    }

    private async Task<AiChatResponse> HandleGetRidersByCompany(JsonElement args)
    {
        var companyName = GetStr(args, "companyName");

        var riders = await db.RiderDetails
            .Include(r => r.Employee)
            .Include(r => r.Company)
            .Where(r => r.Company.Name.ToLower().Contains(companyName.ToLower()))
            .Select(r => new
            {
                r.Id,
                r.WorkingId,
                NameEN = r.Employee.NameEN,
                NameAR = r.Employee.NameAR,
                IqamaNo = r.EmployeeIqamaNo,
                CompanyName = r.Company.Name,
                EmployeeStatus = r.Employee.Status
            })
            .OrderBy(r => r.NameEN)
            .ToListAsync();

        return new AiChatResponse(
            $"Found {riders.Count} riders at company '{companyName}'.",
            Data: riders);
    }

    private async Task<AiChatResponse> HandleGetRidersByHousing(JsonElement args)
    {
        var housingName = GetStr(args, "housingName");

        var riders = await db.RiderDetails
            .Include(r => r.Employee)
                .ThenInclude(e => e.Housing)
            .Include(r => r.Company)
            .Where(r => r.Employee.Housing != null &&
                        r.Employee.Housing.Name.ToLower().Contains(housingName.ToLower()))
            .Select(r => new
            {
                r.Id,
                r.WorkingId,
                NameEN = r.Employee.NameEN,
                NameAR = r.Employee.NameAR,
                CompanyName = r.Company.Name,
                HousingName = r.Employee.Housing!.Name
            })
            .OrderBy(r => r.NameEN)
            .ToListAsync();

        return new AiChatResponse(
            $"Found {riders.Count} riders living in housing '{housingName}'.",
            Data: riders);
    }

    private async Task<AiChatResponse> HandleGetActiveSubstitutions()
    {
        var subs = await db.RiderShiftSubstitutions
            .Where(s => s.IsActive)
            .Include(s => s.ActualRider)
                .ThenInclude(r => r!.Employee)
            .Include(s => s.SubstituteRider)
                .ThenInclude(r => r.Employee)
            .Select(s => new
            {
                s.Id,
                ActualWorkingId = s.ActualRiderWorkingId,
                ActualRiderName = s.ActualRider != null ? s.ActualRider.Employee.NameEN : "N/A",
                SubstituteWorkingId = s.SubstituteWorkingId,
                SubstituteRiderName = s.SubstituteRider.Employee.NameEN,
                s.StartDate,
                s.EndDate,
                s.Reason
            })
            .ToListAsync();

        return new AiChatResponse(
            $"Found {subs.Count} active rider substitutions.",
            Data: subs);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SHIFT REPORT HANDLERS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Core report: highest riders by accepted orders at a company for a date range.
    /// </summary>
    private async Task<AiChatResponse> HandleGetTopRidersByOrders(JsonElement args)
    {
        var companyName = GetStrOrNull(args, "companyName");
        var startDate = ParseDate(GetStrOrNull(args, "startDate"));
        var endDate = ParseDate(GetStrOrNull(args, "endDate")) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var topN = GetInt(args, "topN", 10);

        if (startDate is null)
            return new AiChatResponse("Please provide a start date (e.g. 'from 2024-05-01').");

        var query = db.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Include(s => s.Company)
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate);

        if (!string.IsNullOrWhiteSpace(companyName))
            query = query.Where(s => s.Company.Name.ToLower().Contains(companyName.ToLower()));

        var results = await query
            .GroupBy(s => new
            {
                s.RiderId,
                s.WorkingId,
                NameEN = s.Rider.Employee.NameEN,
                NameAR = s.Rider.Employee.NameAR,
                CompanyName = s.Company.Name
            })
            .Select(g => new
            {
                g.Key.RiderId,
                g.Key.WorkingId,
                g.Key.NameEN,
                g.Key.NameAR,
                g.Key.CompanyName,
                TotalAcceptedOrders = g.Sum(s => s.AcceptedDailyOrders),
                TotalRejectedOrders = g.Sum(s => s.RejectedDailyOrders),
                TotalWorkingHours = Math.Round((double)g.Sum(s => s.WorkingHours), 1),
                TotalShifts = g.Count(),
                AvgOrdersPerShift = Math.Round(g.Average(s => s.AcceptedDailyOrders), 1),
                AvgHoursPerShift = Math.Round((double)g.Average(s => s.WorkingHours), 1),
                BestDay = g.OrderByDescending(s => s.AcceptedDailyOrders)
                                        .Select(s => s.ShiftDate).FirstOrDefault(),
                BestDayOrders = g.Max(s => s.AcceptedDailyOrders)
            })
            .OrderByDescending(r => r.TotalAcceptedOrders)
            .Take(topN)
            .ToListAsync();

        var company = string.IsNullOrWhiteSpace(companyName) ? "all companies" : companyName;
        var summary = $"Top {results.Count} riders by accepted orders at {company} " +
                      $"from {startDate} to {endDate}.";

        return new AiChatResponse(summary, Data: results);
    }

    /// <summary>
    /// Top riders for a specific month (e.g. "2024-05").
    /// </summary>
    private async Task<AiChatResponse> HandleGetTopRidersByOrdersMonth(JsonElement args)
    {
        var monthStr = GetStr(args, "month");  // format: "YYYY-MM"
        var companyName = GetStrOrNull(args, "companyName");
        var topN = GetInt(args, "topN", 10);

        if (!TryParseMonth(monthStr, out var year, out var month))
            return new AiChatResponse("Please provide month in YYYY-MM format (e.g. '2024-05').");

        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var query = db.RiderShifts
            .Include(s => s.Rider).ThenInclude(r => r.Employee)
            .Include(s => s.Company)
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate);

        if (!string.IsNullOrWhiteSpace(companyName))
            query = query.Where(s => s.Company.Name.ToLower().Contains(companyName.ToLower()));

        var results = await query
            .GroupBy(s => new
            {
                s.RiderId,
                s.WorkingId,
                NameEN = s.Rider.Employee.NameEN,
                NameAR = s.Rider.Employee.NameAR,
                CompanyName = s.Company.Name
            })
            .Select(g => new
            {
                Rank = 0, // filled below
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
            .OrderByDescending(r => r.TotalAcceptedOrders)
            .Take(topN)
            .ToListAsync();

        // Add rank numbers
        var ranked = results.Select((r, i) => new
        {
            Rank = i + 1,
            r.WorkingId,
            r.NameEN,
            r.NameAR,
            r.CompanyName,
            r.TotalAcceptedOrders,
            r.TotalRejectedOrders,
            r.TotalWorkingHours,
            r.WorkingDays,
            r.AvgOrdersPerDay,
            r.BestDayOrders
        }).ToList();

        var company = string.IsNullOrWhiteSpace(companyName) ? "all companies" : companyName;
        return new AiChatResponse(
            $"Top {ranked.Count} riders by accepted orders at {company} for {monthStr}.",
            Data: ranked);
    }

    private async Task<AiChatResponse> HandleGetRiderShiftHistory(JsonElement args)
    {
        var workingId = GetStr(args, "workingId");
        var startDate = ParseDate(GetStrOrNull(args, "startDate"));
        var endDate = ParseDate(GetStrOrNull(args, "endDate")) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        var query = db.RiderShifts
            .Include(s => s.Company)
            .Where(s => s.WorkingId == workingId);

        if (startDate.HasValue)
            query = query.Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate);

        var shifts = await query
            .OrderByDescending(s => s.ShiftDate)
            .Select(s => new
            {
                s.ShiftDate,
                s.AcceptedDailyOrders,
                s.RejectedDailyOrders,
                s.RealRejectedDailyOrders,
                s.StackedDeliveries,
                s.WorkingHours,
                s.ShiftStatus,
                CompanyName = s.Company.Name
            })
            .ToListAsync();

        if (!shifts.Any())
            return new AiChatResponse($"No shifts found for rider '{workingId}'.");

        var totalOrders = shifts.Sum(s => s.AcceptedDailyOrders);
        var totalHours = shifts.Sum(s => s.WorkingHours);

        return new AiChatResponse(
            $"Rider '{workingId}' has {shifts.Count} shifts with {totalOrders} total accepted orders " +
            $"and {Math.Round((double)totalHours, 1)} total working hours.",
            Data: new { Summary = new { TotalShifts = shifts.Count, totalOrders, totalHours }, Shifts = shifts });
    }

    private async Task<AiChatResponse> HandleGetDailyShiftReport(JsonElement args)
    {
        var dateStr = GetStr(args, "date");
        if (!DateOnly.TryParse(dateStr, out var date))
            return new AiChatResponse("Please provide a valid date (e.g. '2024-05-15').");

        var companyName = GetStrOrNull(args, "companyName");

        var query = db.RiderShifts
            .Include(s => s.Rider).ThenInclude(r => r.Employee)
            .Include(s => s.Company)
            .Where(s => s.ShiftDate == date);

        if (!string.IsNullOrWhiteSpace(companyName))
            query = query.Where(s => s.Company.Name.ToLower().Contains(companyName.ToLower()));

        var shifts = await query
            .Select(s => new
            {
                s.WorkingId,
                NameEN = s.Rider.Employee.NameEN,
                NameAR = s.Rider.Employee.NameAR,
                CompanyName = s.Company.Name,
                s.AcceptedDailyOrders,
                s.RejectedDailyOrders,
                s.WorkingHours,
                s.ShiftStatus
            })
            .OrderByDescending(s => s.AcceptedDailyOrders)
            .ToListAsync();

        var totalOrders = shifts.Sum(s => s.AcceptedDailyOrders);
        var company = string.IsNullOrWhiteSpace(companyName) ? "all companies" : companyName;

        return new AiChatResponse(
            $"Daily report for {date}: {shifts.Count} riders worked at {company}, " +
            $"total accepted orders: {totalOrders}.",
            Data: shifts);
    }

    private async Task<AiChatResponse> HandleGetCompanyPerformanceSummary(JsonElement args)
    {
        var startDate = ParseDate(GetStrOrNull(args, "startDate"));
        var endDate = ParseDate(GetStrOrNull(args, "endDate")) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        if (startDate is null)
            return new AiChatResponse("Please provide a start date.");

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
                AvgOrdersPerRider = Math.Round(g.Average(s => s.AcceptedDailyOrders), 1),
                BestDayOrders = g.Max(s => s.AcceptedDailyOrders)
            })
            .OrderByDescending(c => c.TotalAcceptedOrders)
            .ToListAsync();

        return new AiChatResponse(
            $"Company performance summary from {startDate} to {endDate}.",
            Data: summary);
    }

    private async Task<AiChatResponse> HandleGetRidersHighRejection(JsonElement args)
    {
        var startDate = ParseDate(GetStrOrNull(args, "startDate"));
        var endDate = ParseDate(GetStrOrNull(args, "endDate")) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var companyName = GetStrOrNull(args, "companyName");
        var topN = GetInt(args, "topN", 10);

        if (startDate is null)
            return new AiChatResponse("Please provide a start date.");

        var query = db.RiderShifts
            .Include(s => s.Rider).ThenInclude(r => r.Employee)
            .Include(s => s.Company)
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate);

        if (!string.IsNullOrWhiteSpace(companyName))
            query = query.Where(s => s.Company.Name.ToLower().Contains(companyName.ToLower()));

        var results = await query
            .GroupBy(s => new
            {
                s.WorkingId,
                NameEN = s.Rider.Employee.NameEN,
                CompanyName = s.Company.Name
            })
            .Select(g => new
            {
                g.Key.WorkingId,
                g.Key.NameEN,
                g.Key.CompanyName,
                TotalAcceptedOrders = g.Sum(s => s.AcceptedDailyOrders),
                TotalRejectedOrders = g.Sum(s => s.RejectedDailyOrders),
                TotalRealRejected = g.Sum(s => s.RealRejectedDailyOrders),
                RejectionRate = g.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders) == 0 ? 0 :
                                      Math.Round(
                                          (double)g.Sum(s => s.RejectedDailyOrders) /
                                          (double)g.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders) * 100, 1)
            })
            .OrderByDescending(r => r.TotalRejectedOrders)
            .Take(topN)
            .ToListAsync();

        return new AiChatResponse(
            $"Top {results.Count} riders with highest rejections.",
            Data: results);
    }

    private async Task<AiChatResponse> HandleGetRidersByWorkingHours(JsonElement args)
    {
        var startDate = ParseDate(GetStrOrNull(args, "startDate"));
        var endDate = ParseDate(GetStrOrNull(args, "endDate")) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var companyName = GetStrOrNull(args, "companyName");
        var topN = GetInt(args, "topN", 10);

        if (startDate is null)
            return new AiChatResponse("Please provide a start date.");

        var query = db.RiderShifts
            .Include(s => s.Rider).ThenInclude(r => r.Employee)
            .Include(s => s.Company)
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate);

        if (!string.IsNullOrWhiteSpace(companyName))
            query = query.Where(s => s.Company.Name.ToLower().Contains(companyName.ToLower()));

        var results = await query
            .GroupBy(s => new
            {
                s.WorkingId,
                NameEN = s.Rider.Employee.NameEN,
                CompanyName = s.Company.Name
            })
            .Select(g => new
            {
                g.Key.WorkingId,
                g.Key.NameEN,
                g.Key.CompanyName,
                TotalWorkingHours = Math.Round((double)g.Sum(s => s.WorkingHours), 1),
                TotalAcceptedOrders = g.Sum(s => s.AcceptedDailyOrders),
                TotalShifts = g.Count(),
                AvgHoursPerShift = Math.Round((double)g.Average(s => s.WorkingHours), 1)
            })
            .OrderByDescending(r => r.TotalWorkingHours)
            .Take(topN)
            .ToListAsync();

        return new AiChatResponse(
            $"Top {results.Count} riders by total working hours.",
            Data: results);
    }

    private async Task<AiChatResponse> HandleGetRidersZeroOrders(JsonElement args)
    {
        var dateStr = GetStr(args, "date");
        if (!DateOnly.TryParse(dateStr, out var date))
            return new AiChatResponse("Please provide a valid date.");

        var companyName = GetStrOrNull(args, "companyName");

        var query = db.RiderShifts
            .Include(s => s.Rider).ThenInclude(r => r.Employee)
            .Include(s => s.Company)
            .Where(s => s.ShiftDate == date && s.AcceptedDailyOrders == 0);

        if (!string.IsNullOrWhiteSpace(companyName))
            query = query.Where(s => s.Company.Name.ToLower().Contains(companyName.ToLower()));

        var riders = await query
            .Select(s => new
            {
                s.WorkingId,
                NameEN = s.Rider.Employee.NameEN,
                CompanyName = s.Company.Name,
                s.WorkingHours,
                s.ShiftStatus
            })
            .ToListAsync();

        return new AiChatResponse(
            $"Found {riders.Count} riders with zero accepted orders on {date}.",
            Data: riders);
    }

    private async Task<AiChatResponse> HandleGetShiftSummaryRange(JsonElement args)
    {
        var workingId = GetStr(args, "workingId");
        var startDate = ParseDate(GetStrOrNull(args, "startDate"));
        var endDate = ParseDate(GetStrOrNull(args, "endDate")) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        if (startDate is null)
            return new AiChatResponse("Please provide a start date.");

        var summary = await db.RiderShifts
            .Where(s => s.WorkingId == workingId &&
                        s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .GroupBy(s => s.WorkingId)
            .Select(g => new
            {
                WorkingId = g.Key,
                TotalAcceptedOrders = g.Sum(s => s.AcceptedDailyOrders),
                TotalRejectedOrders = g.Sum(s => s.RejectedDailyOrders),
                TotalWorkingHours = Math.Round((double)g.Sum(s => s.WorkingHours), 1),
                TotalShifts = g.Count(),
                BestDayOrders = g.Max(s => s.AcceptedDailyOrders),
                BestDay = g.OrderByDescending(s => s.AcceptedDailyOrders).Select(s => s.ShiftDate).FirstOrDefault(),
                AvgOrdersPerDay = Math.Round(g.Average(s => s.AcceptedDailyOrders), 1),
                AvgHoursPerDay = Math.Round((double)g.Average(s => s.WorkingHours), 1)
            })
            .FirstOrDefaultAsync();

        return summary is null
            ? new AiChatResponse($"No shifts found for rider '{workingId}' in the given range.")
            : new AiChatResponse($"Performance summary for rider '{workingId}'.", Data: summary);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MONTHLY VALIDITY HANDLERS
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetMonthlyValidityReport(JsonElement args)
    {
        var monthStr = GetStr(args, "month");
        if (!TryParseMonth(monthStr, out var year, out var month))
            return new AiChatResponse("Please provide month in YYYY-MM format.");

        var companyName = GetStrOrNull(args, "companyName");

        var query = db.RiderMonthlyValidities
            .Include(v => v.Employee)
                .ThenInclude(e => e.RiderDetails)
                    .ThenInclude(r => r!.Company)
            .Where(v => v.Year == year && v.Month == month);

        if (!string.IsNullOrWhiteSpace(companyName))
            query = query.Where(v => v.Employee.RiderDetails != null &&
                                     v.Employee.RiderDetails.Company.Name
                                      .ToLower().Contains(companyName.ToLower()));

        var records = await query
            .Select(v => new
            {
                IqamaNo = v.EmployeeIqamaNo,
                NameEN = v.Employee.NameEN,
                NameAR = v.Employee.NameAR,
                CompanyName = v.Employee.RiderDetails != null ? v.Employee.RiderDetails.Company.Name : null,
                Status = v.Status.ToString(),
                v.TotalOrders,
                v.CreatedAt
            })
            .OrderBy(v => v.Status)
            .ThenByDescending(v => v.TotalOrders)
            .ToListAsync();

        var valid = records.Count(r => r.Status == "Valid");
        var invalid = records.Count(r => r.Status == "Invalid");
        var freelancer = records.Count(r => r.Status == "Freelancer");

        return new AiChatResponse(
            $"Monthly validity for {monthStr}: {valid} valid, {invalid} invalid, {freelancer} freelancers.",
            Data: new { Summary = new { Valid = valid, Invalid = invalid, Freelancer = freelancer }, Records = records });
    }

    private async Task<AiChatResponse> HandleGetInvalidRidersMonth(JsonElement args)
    {
        var monthStr = GetStr(args, "month");
        if (!TryParseMonth(monthStr, out var year, out var month))
            return new AiChatResponse("Please provide month in YYYY-MM format.");

        var records = await db.RiderMonthlyValidities
            .Include(v => v.Employee)
                .ThenInclude(e => e.RiderDetails)
                    .ThenInclude(r => r!.Company)
            .Where(v => v.Year == year && v.Month == month && v.Status == ValidityStatus.Invalid)
            .Select(v => new
            {
                IqamaNo = v.EmployeeIqamaNo,
                NameEN = v.Employee.NameEN,
                NameAR = v.Employee.NameAR,
                CompanyName = v.Employee.RiderDetails != null ? v.Employee.RiderDetails.Company.Name : null,
                v.TotalOrders
            })
            .OrderBy(v => v.TotalOrders)
            .ToListAsync();

        return new AiChatResponse(
            $"Found {records.Count} invalid riders for {monthStr}.",
            Data: records);
    }

    private async Task<AiChatResponse> HandleGetFreelancersMonth(JsonElement args)
    {
        var monthStr = GetStr(args, "month");

        var freelancers = await db.KetaFreeLancers
            .Include(f => f.Rider)
                .ThenInclude(r => r.Employee)
            .Where(f => f.Month == monthStr)
            .Select(f => new
            {
                f.WorkingId,
                NameEN = f.Rider.Employee.NameEN,
                f.TotalOrders,
                f.CreatedAt
            })
            .OrderByDescending(f => f.TotalOrders)
            .ToListAsync();

        return new AiChatResponse(
            $"Found {freelancers.Count} freelancers for month '{monthStr}'.",
            Data: freelancers);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HOUSING HANDLERS
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetAllHousing()
    {
        var housing = await db.Housings
            .Select(h => new
            {
                h.Id,
                h.Name,
                h.Address,
                h.Capacity,
                CurrentOccupancy = h.Employees.Count(e => !e.IsDeleted),
                AvailableSlots = h.Capacity - h.Employees.Count(e => !e.IsDeleted),
                ManagerIqamaNo = h.ManagerIqamaNo
            })
            .OrderBy(h => h.Name)
            .ToListAsync();

        return new AiChatResponse($"Found {housing.Count} housing units.", Data: housing);
    }

    private async Task<AiChatResponse> HandleGetHousingOccupancy(JsonElement args)
    {
        var housingName = GetStr(args, "housingName");

        var housing = await db.Housings
            .Include(h => h.Employees)
            .Where(h => h.Name.ToLower().Contains(housingName.ToLower()))
            .Select(h => new
            {
                h.Id,
                h.Name,
                h.Address,
                h.Capacity,
                Employees = h.Employees
                    .Where(e => !e.IsDeleted)
                    .Select(e => new { e.IqamaNo, e.NameEN, e.NameAR, e.JobTitle, e.Status })
                    .ToList(),
                CurrentOccupancy = h.Employees.Count(e => !e.IsDeleted),
                AvailableSlots = h.Capacity - h.Employees.Count(e => !e.IsDeleted)
            })
            .FirstOrDefaultAsync();

        return housing is null
            ? new AiChatResponse($"Housing '{housingName}' not found.")
            : new AiChatResponse(
                $"Housing '{housing.Name}': {housing.CurrentOccupancy}/{housing.Capacity} occupied.",
                Data: housing);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  VEHICLE HANDLERS
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetAllVehicles()
    {
        var vehicles = await db.Vehicles
            .Include(v => v.RiderDetails)
                .ThenInclude(r => r!.Employee)
            .Select(v => new
            {
                v.VehicleNumber,
                v.VehicleType,
                v.PlateNumberA,
                v.PlateNumberE,
                v.Manufacturer,
                v.ManufactureYear,
                v.LicenseExpiryDate,
                v.Location,
                AssignedTo = v.RiderDetails != null ? v.RiderDetails.Employee.NameEN : "Unassigned",
                AssignedWorkingId = v.RiderDetails != null ? v.RiderDetails.WorkingId : null,
                LicenseDaysLeft = (v.LicenseExpiryDate.ToDateTime(TimeOnly.MinValue) -
                                   DateTime.UtcNow.AddHours(3).Date).Days
            })
            .OrderBy(v => v.VehicleNumber)
            .ToListAsync();

        return new AiChatResponse($"Found {vehicles.Count} vehicles.", Data: vehicles);
    }

    private async Task<AiChatResponse> HandleGetVehicleByNumber(JsonElement args)
    {
        var vehicleNumber = GetStr(args, "vehicleNumber");

        var vehicle = await db.Vehicles
            .Include(v => v.RiderDetails)
                .ThenInclude(r => r!.Employee)
            .Include(v => v.RiderVehicleStatuses)
            .Where(v => v.VehicleNumber == vehicleNumber)
            .Select(v => new
            {
                v.VehicleNumber,
                v.VehicleType,
                v.PlateNumberA,
                v.PlateNumberE,
                v.Manufacturer,
                v.ManufactureYear,
                v.LicenseExpiryDate,
                v.Location,
                v.OwnerId,
                v.OwnerName,
                AssignedTo = v.RiderDetails != null ? v.RiderDetails.Employee.NameEN : "Unassigned",
                RecentStatuses = v.RiderVehicleStatuses
                    .OrderByDescending(s => s.Timestamp).Take(5)
                    .Select(s => new { s.StatusType, s.Timestamp, s.Reason, s.IsActive })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        return vehicle is null
            ? new AiChatResponse($"Vehicle '{vehicleNumber}' not found.")
            : new AiChatResponse($"Details for vehicle '{vehicleNumber}'.", Data: vehicle);
    }

    private async Task<AiChatResponse> HandleGetVehiclesExpiringLicense(JsonElement args)
    {
        int days = GetInt(args, "days", 30);
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3).AddDays(days));

        var vehicles = await db.Vehicles
            .Include(v => v.RiderDetails)
                .ThenInclude(r => r!.Employee)
            .Where(v => v.LicenseExpiryDate <= cutoff)
            .Select(v => new
            {
                v.VehicleNumber,
                v.VehicleType,
                v.PlateNumberA,
                v.LicenseExpiryDate,
                DaysLeft = (v.LicenseExpiryDate.ToDateTime(TimeOnly.MinValue) -
                            DateTime.UtcNow.AddHours(3).Date).Days,
                AssignedTo = v.RiderDetails != null ? v.RiderDetails.Employee.NameEN : "Unassigned"
            })
            .OrderBy(v => v.LicenseExpiryDate)
            .ToListAsync();

        return new AiChatResponse(
            $"Found {vehicles.Count} vehicles with license expiring within {days} days.",
            Data: vehicles);
    }

    private async Task<AiChatResponse> HandleGetUnassignedVehicles()
    {
        var vehicles = await db.Vehicles
            .Where(v => v.RiderDetails == null)
            .Select(v => new
            {
                v.VehicleNumber,
                v.VehicleType,
                v.PlateNumberA,
                v.Manufacturer,
                v.Location,
                v.LicenseExpiryDate
            })
            .OrderBy(v => v.VehicleNumber)
            .ToListAsync();

        return new AiChatResponse(
            $"Found {vehicles.Count} unassigned vehicles.",
            Data: vehicles);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  WALLET HANDLERS
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetWalletByRider(JsonElement args)
    {
        var workingId = GetStr(args, "workingId");

        var records = await db.Wallets
            .Include(w => w.WorkedRider)
                .ThenInclude(r => r.Employee)
            .Where(w => w.WorkedRider.WorkingId == workingId)
            .OrderByDescending(w => w.Date)
            .Select(w => new
            {
                w.Date,
                w.Amount,
                WorkedRider = w.WorkedRider.Employee.NameEN,
                HasSubstitution = w.MainRiderId.HasValue
            })
            .ToListAsync();

        var totalAmount = records.Sum(r => r.Amount);
        return new AiChatResponse(
            $"Found {records.Count} wallet entries for rider '{workingId}'. Total: {totalAmount:F2} SAR.",
            Data: new { TotalAmount = totalAmount, Records = records });
    }

    private async Task<AiChatResponse> HandleGetWalletSummaryDateRange(JsonElement args)
    {
        var startDate = ParseDate(GetStrOrNull(args, "startDate"));
        var endDate = ParseDate(GetStrOrNull(args, "endDate")) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        if (startDate is null)
            return new AiChatResponse("Please provide a start date.");

        var summary = await db.Wallets
            .Include(w => w.WorkedRider)
                .ThenInclude(r => r.Employee)
            .Where(w => w.Date >= startDate && w.Date <= endDate)
            .GroupBy(w => new
            {
                w.WorkedRiderId,
                WorkingId = w.WorkedRider.WorkingId,
                NameEN = w.WorkedRider.Employee.NameEN
            })
            .Select(g => new
            {
                g.Key.WorkingId,
                g.Key.NameEN,
                TotalAmount = g.Sum(w => w.Amount),
                PaymentDays = g.Count()
            })
            .OrderByDescending(r => r.TotalAmount)
            .ToListAsync();

        return new AiChatResponse(
            $"Wallet summary from {startDate} to {endDate}: {summary.Count} riders, " +
            $"total payout: {summary.Sum(s => s.TotalAmount):F2} SAR.",
            Data: summary);
    }

    private async Task<AiChatResponse> HandleGetTopEarners(JsonElement args)
    {
        var monthStr = GetStr(args, "month");
        if (!TryParseMonth(monthStr, out var year, out var month))
            return new AiChatResponse("Please provide month in YYYY-MM format.");

        var topN = GetInt(args, "topN", 10);
        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var earners = await db.Wallets
            .Include(w => w.WorkedRider)
                .ThenInclude(r => r.Employee)
            .Where(w => w.Date >= startDate && w.Date <= endDate)
            .GroupBy(w => new
            {
                WorkingId = w.WorkedRider.WorkingId,
                NameEN = w.WorkedRider.Employee.NameEN,
                NameAR = w.WorkedRider.Employee.NameAR
            })
            .Select(g => new
            {
                g.Key.WorkingId,
                g.Key.NameEN,
                g.Key.NameAR,
                TotalEarnings = g.Sum(w => w.Amount),
                PaymentDays = g.Count()
            })
            .OrderByDescending(r => r.TotalEarnings)
            .Take(topN)
            .ToListAsync();

        return new AiChatResponse(
            $"Top {earners.Count} earners for {monthStr}.",
            Data: earners);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HUNGER DISABILITY HANDLERS
    // ═══════════════════════════════════════════════════════════════════════
    //private async Task<AiChatResponse> HandleGetHungerDisabilityRecords(JsonElement args)
    //{
    //    var companyName = GetStrOrNull(args, "companyName");
    //    var startDate = ParseDate(GetStrOrNull(args, "startDate"));
    //    var endDate = ParseDate(GetStrOrNull(args, "endDate")) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    //    var query = db.h
    //        .Include(h => h.Rider)
    //            .ThenInclude(r => r.Employee)
    //        .Include(h => h.Company)
    //        .AsQueryable();

    //    if (!string.IsNullOrWhiteSpace(companyName))
    //        query = query.Where(h => h.Company.Name.ToLower().Contains(companyName.ToLower()));
    //    if (startDate.HasValue)
    //        query = query.Where(h => h.ShiftDate >= startDate && h.ShiftDate <= endDate);

    //    var records = await query
    //        .Select(h => new
    //        {
    //            h.ShiftDate,
    //            ActualWorkingId = h.ActualWorkingId,
    //            ActualRiderName = h.Rider.Employee.NameEN,
    //            h.SubstituteWorkingId,
    //            h.Days,
    //            h.AcceptedDailyOrders,
    //            CompanyName = h.Company.Name
    //        })
    //        .OrderByDescending(h => h.ShiftDate)
    //        .ToListAsync();

    //    return new AiChatResponse(
    //        $"Found {records.Count} hunger disability records.",
    //        Data: records);
    //}

    // ═══════════════════════════════════════════════════════════════════════
    //  COMPANY HANDLERS
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<AiChatResponse> HandleGetAllCompanies()
    {
        var companies = await db.Companies
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Address,
                c.Phone,
                c.Email,
                c.From,
                c.To,
                c.Details,
                RiderCount = db.RiderDetails.Count(r => r.CompanyId == c.Id)
            })
            .OrderBy(c => c.Name)
            .ToListAsync();

        return new AiChatResponse($"Found {companies.Count} companies.", Data: companies);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  WRITE OPERATIONS (CONFIRMATION FLOW)
    // ═══════════════════════════════════════════════════════════════════════
    private AiChatResponse BuildConfirmation(string actionType, string description, JsonElement args)
    {
        var token = Guid.NewGuid().ToString();
        _pendingActions[token] = new PendingActionDetails(actionType, args.ToString());
        return new AiChatResponse(
            $"⚠️ I'm about to: **{description}**. Do you want to proceed?",
            NeedsConfirmation: true,
            PendingAction: new AiPendingAction(token, actionType, description));
    }

    private async Task<AiChatResponse> ExecuteConfirmedActionAsync(string token)
    {
        if (!_pendingActions.TryGetValue(token, out var pending))
            return new AiChatResponse("Confirmation expired or not found. Please try the action again.");

        _pendingActions.Remove(token);
        var args = JsonSerializer.Deserialize<JsonElement>(pending.ArgsJson);

        return pending.ActionType switch
        {
            "ToggleUserStatus" => await ExecuteToggle(args),
            "DeleteUser" => await ExecuteDelete(args),
            "ResetPassword" => await ExecuteReset(args),
            _ => new AiChatResponse("Unknown confirmed action.")
        };
    }

    private async Task<AiChatResponse> ExecuteToggle(JsonElement args)
    {
        var userName = GetStr(args, "userName");
        var result = await adminService.ToggleStatusAsync(userName);
        return result.IsSuccess
            ? new AiChatResponse($"✅ Status for '{userName}' has been toggled successfully.")
            : new AiChatResponse($"❌ Failed: {result.Error.Description}");
    }

    private async Task<AiChatResponse> ExecuteDelete(JsonElement args)
    {
        var userName = GetStr(args, "userName");
        var result = await adminService.DeletaUserAsync(userName);
        return result.IsSuccess
            ? new AiChatResponse($"✅ User '{userName}' has been permanently deleted.")
            : new AiChatResponse($"❌ Failed: {result.Error.Description}");
    }

    private async Task<AiChatResponse> ExecuteReset(JsonElement args)
    {
        var userName = GetStr(args, "userName");
        var result = await adminService.ResetPasswordAsync(userName);
        return result.IsSuccess
            ? new AiChatResponse($"✅ Password for '{userName}' has been reset to the default.")
            : new AiChatResponse($"❌ Failed: {result.Error.Description}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TOOL DECLARATIONS  (sent to Gemini so it knows what to call)
    // ═══════════════════════════════════════════════════════════════════════
    private static object[] GetToolDeclarations() => new object[]
    {
        // ── Users ──────────────────────────────────────────────────────────
        Tool("get_all_users",
             "Get a list of all registered system users with their roles and account status"),

        Tool("get_user_by_name",
             "Get the full profile of a specific user by username",
             Param("userName", "string", "The username to look up")),

        Tool("get_user_by_id",
             "Get the full profile of a specific user by their ID",
             Param("userId", "string", "The user ID to look up")),

        // ── Employees ──────────────────────────────────────────────────────
        Tool("get_all_employees",
             "Get all employees in the system (excludes deleted)"),

        Tool("get_employee_by_iqama",
             "Get details of a specific employee by their Iqama number",
             Param("iqamaNo", "string", "The employee Iqama number")),

        Tool("get_employees_by_status",
             "Filter employees by their status (enable, disable, escaped)",
             Param("status", "string", "Status to filter by: 'enable', 'disable', or 'escaped'")),

        Tool("get_employees_by_housing",
             "Get all employees living in a specific housing unit",
             Param("housingName", "string", "Name or partial name of the housing unit")),

        Tool("get_employees_expiring_iqama",
             "Get employees whose Iqama will expire within a given number of days",
             Param("days", "integer", "Number of days window (default 30)")),

        Tool("get_employees_not_in_ksa",
             "Get all employees who are currently not in Saudi Arabia"),

        Tool("get_escaped_employees",
             "Get all currently tracked escaped employees with their removal deadlines"),

        // ── Riders ─────────────────────────────────────────────────────────
        Tool("get_all_riders",
             "Get all riders in the system with their details"),

        Tool("get_rider_by_working_id",
             "Get a specific rider by their working ID",
             Param("workingId", "string", "The rider working ID")),

        Tool("get_riders_by_company",
             "Get all riders belonging to a specific company",
             Param("companyName", "string", "Company name or partial name")),

        Tool("get_riders_by_housing",
             "Get all riders living in a specific housing unit",
             Param("housingName", "string", "Housing name or partial name")),

        Tool("get_active_substitutions",
             "Get all currently active rider shift substitutions"),

        // ── Shift Reports ──────────────────────────────────────────────────
        Tool("get_top_riders_by_orders",
             "Get the top riders ranked by total accepted orders for a date range. " +
             "Use this for 'best rider', 'highest orders', 'top performer' questions.",
             Param("startDate", "string", "Start date YYYY-MM-DD"),
             Param("endDate",   "string", "End date YYYY-MM-DD (optional, defaults to today)"),
             Param("companyName","string","Filter by company name (optional)"),
             Param("topN",      "integer","How many riders to return (optional, default 10)")),

        Tool("get_top_riders_by_orders_month",
             "Get the top riders ranked by accepted orders for a specific month. " +
             "Use this when the user says 'this month', 'last month', or provides a month name.",
             Param("month",      "string",  "Month in YYYY-MM format e.g. '2024-05'"),
             Param("companyName","string",  "Filter by company name (optional)"),
             Param("topN",       "integer", "How many riders to return (optional, default 10)")),

        Tool("get_rider_shift_history",
             "Get the complete shift history for a specific rider",
             Param("workingId",  "string", "The rider working ID"),
             Param("startDate",  "string", "Start date YYYY-MM-DD (optional)"),
             Param("endDate",    "string", "End date YYYY-MM-DD (optional)")),

        Tool("get_daily_shift_report",
             "Get all rider shifts for a specific date",
             Param("date",        "string", "Date in YYYY-MM-DD format"),
             Param("companyName", "string", "Filter by company name (optional)")),

        Tool("get_company_performance_summary",
             "Get a performance summary grouped by company for a date range",
             Param("startDate", "string", "Start date YYYY-MM-DD"),
             Param("endDate",   "string", "End date YYYY-MM-DD (optional)")),

        Tool("get_riders_high_rejection",
             "Get riders with the most rejected orders (worst rejection rate)",
             Param("startDate",   "string",  "Start date YYYY-MM-DD"),
             Param("endDate",     "string",  "End date YYYY-MM-DD (optional)"),
             Param("companyName", "string",  "Filter by company (optional)"),
             Param("topN",        "integer", "How many to return (optional, default 10)")),

        Tool("get_riders_by_working_hours",
             "Get riders ranked by total working hours",
             Param("startDate",   "string",  "Start date YYYY-MM-DD"),
             Param("endDate",     "string",  "End date YYYY-MM-DD (optional)"),
             Param("companyName", "string",  "Filter by company (optional)"),
             Param("topN",        "integer", "How many to return (optional, default 10)")),

        Tool("get_riders_zero_orders",
             "Get riders who worked but had zero accepted orders on a specific date",
             Param("date",        "string", "Date in YYYY-MM-DD format"),
             Param("companyName", "string", "Filter by company (optional)")),

        Tool("get_shift_summary_range",
             "Get an aggregated performance summary for a specific rider over a date range",
             Param("workingId",  "string", "The rider working ID"),
             Param("startDate",  "string", "Start date YYYY-MM-DD"),
             Param("endDate",    "string", "End date YYYY-MM-DD (optional)")),

        // ── Monthly Validity ───────────────────────────────────────────────
        Tool("get_monthly_validity_report",
             "Get the monthly validity report (valid/invalid/freelancer) for all riders",
             Param("month",       "string", "Month in YYYY-MM format"),
             Param("companyName", "string", "Filter by company (optional)")),

        Tool("get_invalid_riders_month",
             "Get only invalid riders for a specific month",
             Param("month", "string", "Month in YYYY-MM format")),

        Tool("get_freelancers_month",
             "Get freelancer riders for a specific month",
             Param("month", "string", "Month in YYYY-MM format")),

        // ── Housing ────────────────────────────────────────────────────────
        Tool("get_all_housing",
             "Get all housing units with their capacity and current occupancy"),

        Tool("get_housing_occupancy",
             "Get details and resident list of a specific housing unit",
             Param("housingName", "string", "Housing name or partial name")),

        // ── Vehicles ───────────────────────────────────────────────────────
        Tool("get_all_vehicles",
             "Get all vehicles in the system with assignment and license info"),

        Tool("get_vehicle_by_number",
             "Get details and recent status history for a specific vehicle",
             Param("vehicleNumber", "string", "The vehicle number")),

        Tool("get_vehicles_expiring_license",
             "Get vehicles whose license will expire within a number of days",
             Param("days", "integer", "Days window (default 30)")),

        Tool("get_unassigned_vehicles",
             "Get all vehicles that are not currently assigned to any rider"),

        // ── Wallet ─────────────────────────────────────────────────────────
        Tool("get_wallet_by_rider",
             "Get wallet payment records for a specific rider",
             Param("workingId", "string", "The rider working ID")),

        Tool("get_wallet_summary_date_range",
             "Get total earnings per rider for a date range",
             Param("startDate", "string", "Start date YYYY-MM-DD"),
             Param("endDate",   "string", "End date YYYY-MM-DD (optional)")),

        Tool("get_top_earners",
             "Get the riders with the highest total earnings for a given month",
             Param("month", "string",  "Month in YYYY-MM format"),
             Param("topN",  "integer", "How many to return (optional, default 10)")),

        // ── Hunger Disability ──────────────────────────────────────────────
        Tool("get_hunger_disability_records",
             "Get hunger disability records, optionally filtered by company and date range",
             Param("companyName", "string", "Filter by company (optional)"),
             Param("startDate",   "string", "Start date YYYY-MM-DD (optional)"),
             Param("endDate",     "string", "End date YYYY-MM-DD (optional)")),

        // ── Companies ──────────────────────────────────────────────────────
        Tool("get_all_companies",
             "Get all companies in the system with their rider counts"),

        // ── Write operations ───────────────────────────────────────────────
        Tool("toggle_user_status",
             "Enable or disable a user account (requires confirmation)",
             Param("userName", "string", "Username of the user")),

        Tool("delete_user",
             "Permanently delete a user account (requires confirmation)",
             Param("userName", "string", "Username of the user to delete")),

        Tool("reset_password",
             "Reset a user's password to the system default (requires confirmation)",
             Param("userName", "string", "Username to reset")),
    };

    // ═══════════════════════════════════════════════════════════════════════
    //  TOOL / PARAM BUILDER HELPERS
    // ═══════════════════════════════════════════════════════════════════════
    private static object Tool(string name, string description, params (string name, string type, string desc)[] parameters)
    {
        if (!parameters.Any())
            return new { name, description };

        return new
        {
            name,
            description,
            parameters = new
            {
                type = "object",
                properties = parameters.ToDictionary(
                    p => p.name,
                    p => (object)new { type = p.type, description = p.desc }),
                required = Array.Empty<string>()  // All params optional; Gemini infers from context
            }
        };
    }

    private static (string name, string type, string desc) Param(string name, string type, string desc)
        => (name, type, desc);

    // ═══════════════════════════════════════════════════════════════════════
    //  SYSTEM PROMPT
    // ═══════════════════════════════════════════════════════════════════════
    private static string GetSystemPrompt()
    {
        var today = DateTime.UtcNow.AddHours(3); // Arabia Standard Time
        return $"""
        You are an intelligent assistant for a rider and employee management platform.
        You have access to the full system data via function calls — NEVER guess or invent data.
        NEVER write code to compute values. NEVER use Python or any programming language.

        TODAY'S DATE: {today:yyyy-MM-dd}
        CURRENT MONTH: {today:yyyy-MM}

        Guidelines:
        - Always use the appropriate function to answer any data question.
        - For "this month" use: {today:yyyy-MM}. For "last month" use: {today.AddMonths(-1):yyyy-MM}.
        - Always pass dates as strings in YYYY-MM-DD or YYYY-MM format directly — do not compute them.
        - For top/best/highest rider questions, use get_top_riders_by_orders_month when a month is implied.
        - For write operations (toggle, delete, reset), always call the function so the system
          can show the user a confirmation dialog before executing.
        - Respond in the same language the user uses (Arabic or English).
        - Be concise — the UI will display the data; just summarize the key insight.
        """;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONVERSATION BUILDER
    // ═══════════════════════════════════════════════════════════════════════
    private static List<object> BuildContents(AiChatRequest request)
    {
        var contents = new List<object>();
        foreach (var h in request.History ?? [])
            contents.Add(new { role = h.Role, parts = new[] { new { text = h.Content } } });
        contents.Add(new { role = "user", parts = new[] { new { text = request.Message } } });
        return contents;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  UTILITY
    // ═══════════════════════════════════════════════════════════════════════
    private static string GetStr(JsonElement args, string key)
    {
        if (args.TryGetProperty(key, out var v)) return v.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static string? GetStrOrNull(JsonElement args, string key)
    {
        if (args.TryGetProperty(key, out var v)) return v.GetString();
        return null;
    }

    private static int GetInt(JsonElement args, string key, int defaultValue)
    {
        if (args.TryGetProperty(key, out var v) && v.TryGetInt32(out var i)) return i;
        return defaultValue;
    }

    private static DateOnly? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateOnly.TryParse(raw, out var d) ? d : null;
    }

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

internal record PendingActionDetails(string ActionType, string ArgsJson);

// ── Application/Contracts/AI/AiChatRequest.cs ────────────────────────────────

public record AiChatRequest(
    string Message,
    List<AiChatMessage>? History = null,
    string? ConfirmationToken = null   // sent when user clicks "Yes" on confirmation
);

public record AiChatMessage(
    string Role,    // "user" or "model"
    string Content
);


// ── Application/Contracts/AI/AiChatResponse.cs ───────────────────────────────

public record AiChatResponse(
    string Message,
    object? Data = null,
    bool NeedsConfirmation = false,
    AiPendingAction? PendingAction = null
);

public record AiPendingAction(
    string Token,          // GUID — frontend sends this back to confirm
    string ActionType,     // e.g. "ToggleUserStatus"
    string Description,    // human-readable: "Toggle status for 'john_doe'?"
    object? Preview = null // optional: show what will change
);



public interface IGeminiService
{
    Task<AiChatResponse> ChatAsync(AiChatRequest request, string callerUserId);
}