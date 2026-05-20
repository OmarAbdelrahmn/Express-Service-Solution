// Application/Service/AI/GeminiService.cs
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace Application.Service.AI;

public class GeminiService(
    IAiToolDispatcher dispatcher,
    IConfiguration configuration) : IGeminiService
{
    private readonly string _apiKey = configuration["Gemini:ApiKey"]!;
    private const string GeminiUrl =
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";

    public async Task<AiChatResponse> ChatAsync(AiChatRequest request, string callerUserId)
    {
        try
        {
            if (request.ConfirmationToken is not null)
                return await dispatcher.ExecuteConfirmedAsync(request.ConfirmationToken);

            var geminiRequest = new
            {
                contents = BuildContents(request),
                tools = new[] { new { functionDeclarations = GetToolDeclarations() } },
                systemInstruction = new { parts = new[] { new { text = GetSystemPrompt() } } }
            };

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(60);

            var json = JsonSerializer.Serialize(geminiRequest);
            var httpResponse = await http.PostAsync(
                $"{GeminiUrl}?key={_apiKey}",
                new StringContent(json, Encoding.UTF8, "application/json"));

            var responseText = await httpResponse.Content.ReadAsStringAsync();

            if (!httpResponse.IsSuccessStatusCode)
                return new AiChatResponse($"Gemini API error ({(int)httpResponse.StatusCode}): {responseText}");

            var geminiResponse = JsonSerializer.Deserialize<JsonElement>(responseText);

            if (!geminiResponse.TryGetProperty("candidates", out var candidates)
                || candidates.GetArrayLength() == 0)
                return new AiChatResponse($"Gemini returned no candidates.");

            var firstCandidate = candidates[0];

            if (firstCandidate.TryGetProperty("finishReason", out var finishReason)
                && finishReason.GetString() == "MALFORMED_FUNCTION_CALL")
                return new AiChatResponse(
                    "I understood what you need but couldn't form the request correctly. " +
                    "Please be more specific.");

            if (!firstCandidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.GetArrayLength() == 0)
                return new AiChatResponse("Gemini returned an unreadable response.");

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("functionCall", out var fc))
                {
                    var funcName = fc.GetProperty("name").GetString()!;
                    var funcArgs = fc.GetProperty("args");
                    return await dispatcher.DispatchAsync(funcName, funcArgs, callerUserId);
                }
            }

            if (parts[0].TryGetProperty("text", out var textProp))
                return new AiChatResponse(textProp.GetString()!);

            return new AiChatResponse("Gemini returned a response I could not read.");
        }
        catch (TaskCanceledException) { return new AiChatResponse("Request to Gemini timed out. Please try again."); }
        catch (HttpRequestException ex) { return new AiChatResponse($"Network error reaching Gemini: {ex.Message}"); }
        catch (JsonException ex) { return new AiChatResponse($"Failed to parse Gemini response: {ex.Message}"); }
        catch (Exception ex) { return new AiChatResponse($"Unexpected error: {ex.Message}"); }
    }

    // ── Tool declarations — Gemini learns what it can call ──────────────────
    private static object[] GetToolDeclarations() =>
    [
        // Users
        Tool("get_all_users", "Get all registered system users"),
        Tool("get_user_by_name", "Get a user by username", P("userName","string","Username")),
        Tool("toggle_user_status", "Enable/disable a user account (requires confirmation)", P("userName","string","Username")),
        Tool("delete_user", "Permanently delete a user (requires confirmation)", P("userName","string","Username")),
        Tool("reset_password", "Reset user password to default (requires confirmation)", P("userName","string","Username")),

        // Employees
        Tool("get_all_employees", "Get all employees in the system"),
        Tool("get_employee_by_iqama", "Get employee by Iqama number", P("iqamaNo","string","Iqama number")),
        Tool("get_employees_by_status", "Filter employees by status (enable/disable/fleeing/vacation)", P("status","string","Status")),
        Tool("get_employees_by_housing", "Get employees in a housing unit", P("housingName","string","Housing name")),
        Tool("get_employees_expiring_iqama", "Get employees with Iqama expiring within N days", P("days","integer","Days window (default 30)")),
        Tool("get_employees_not_in_ksa", "Get employees currently not in Saudi Arabia"),
        Tool("get_escaped_employees", "Get all escaped employees currently being tracked"),
        Tool("get_iqama_expiry_report", "Get Iqama expiry report with urgency levels", P("urgency","string","Filter: Expired/Critical/Warning/Upcoming/Safe"), P("housingName","string","Filter by housing (optional)"), P("sponsor","string","Filter by sponsor (optional)")),
        Tool("get_employee_status_history", "Get full status change history for an employee", P("iqamaNo","string","Iqama number")),
        Tool("get_status_change_statistics", "Get system-wide employee status change statistics"),

        // Riders
        Tool("get_all_riders", "Get all riders in the system"),
        Tool("get_rider_by_iqama", "Get rider by Iqama number", P("iqamaNo","string","Iqama number")),
        Tool("get_rider_by_working_id", "Get rider by working ID", P("workingId","string","Working ID")),
        Tool("get_riders_by_company", "Get all riders at a company", P("companyName","string","Company name")),
        Tool("get_riders_by_housing", "Get all riders in a housing unit", P("housingName","string","Housing name")),
        Tool("get_rider_vehicle", "Get the vehicle assigned to a rider", P("iqamaNo","string","Rider Iqama number")),
        Tool("smart_search_riders", "Smart search across names, iqama, working ID, company etc.", P("keyword","string","Search keyword")),
        Tool("get_employee_statistics", "Get total counts of employees vs riders"),
        Tool("get_rider_status_logs", "Get status change logs for a rider", P("iqamaNo","string","Iqama number")),
        Tool("get_working_id_history", "Get history of who has held a working ID", P("workingId","string","Working ID")),

        // Substitutions
        Tool("get_active_substitutions", "Get all currently active rider substitutions"),
        Tool("get_all_substitutions", "Get all substitutions (active and historical)"),
        Tool("start_substitution", "Start a rider substitution (requires confirmation)", P("actualRiderWorkingId","string","Working ID being substituted"), P("substituteWorkingId","string","Substitute rider working ID"), P("reason","string","Reason for substitution")),
        Tool("stop_substitution", "Stop an active substitution (requires confirmation)", P("workingId","string","Working ID whose substitution to stop")),

        // Shift reports
        Tool("get_top_riders_by_orders", "Top riders by orders for a date range", P("startDate","string","Start date YYYY-MM-DD"), P("endDate","string","End date (optional)"), P("companyName","string","Filter by company (optional)"), P("topN","integer","How many (optional)")),
        Tool("get_top_riders_by_orders_month", "Top riders by orders for a specific month. Use for 'this month', 'last month', best rider questions.", P("month","string","Month YYYY-MM"), P("companyName","string","Filter by company (optional)"), P("topN","integer","How many (optional)")),
        Tool("get_rider_shift_history", "Get all shifts for a rider", P("workingId","string","Working ID")),
        Tool("get_daily_shift_report", "Get all shifts on a specific date", P("date","string","Date YYYY-MM-DD"), P("companyName","string","Filter by company (optional)")),
        Tool("get_company_performance_summary", "Performance summary grouped by company for a date range", P("startDate","string","Start date"), P("endDate","string","End date (optional)")),
        Tool("get_riders_high_rejection", "Riders with highest rejection counts", P("startDate","string","Start date"), P("endDate","string","End date (optional)"), P("companyName","string","Filter (optional)"), P("topN","integer","How many (optional)")),
        Tool("get_riders_by_working_hours", "Riders ranked by total working hours", P("startDate","string","Start date"), P("endDate","string","End date (optional)"), P("companyName","string","Filter (optional)"), P("topN","integer","How many (optional)")),
        Tool("get_riders_zero_orders", "Riders who worked but had zero orders", P("date","string","Date YYYY-MM-DD"), P("companyName","string","Filter (optional)")),
        Tool("get_shift_summary_range", "Aggregated summary for one rider over a date range", P("workingId","string","Working ID"), P("startDate","string","Start date"), P("endDate","string","End date (optional)")),

        // Hunger report
        Tool("get_hunger_monthly_validation", "Get Hunger company monthly rider validation report with validity status", P("month","string","Month YYYY-MM")),

        // Monthly validity
        Tool("get_monthly_validity_report", "Get monthly validity report (valid/invalid/freelancer)", P("month","string","Month YYYY-MM"), P("companyName","string","Filter (optional)")),
        Tool("get_invalid_riders_month", "Get invalid riders for a month", P("month","string","Month YYYY-MM")),
        Tool("get_freelancers_month", "Get freelancer riders for a month", P("month","string","Month YYYY-MM")),

        // Housing
        Tool("get_all_housing", "Get all housing units with capacity and occupancy"),
        Tool("get_housing_by_name", "Get housing details by name", P("housingName","string","Housing name")),
        Tool("get_housing_occupancy", "Get detailed occupancy with resident list", P("housingName","string","Housing name")),

        // Vehicles
        Tool("get_all_vehicles", "Get all vehicles with current assignment status"),
        Tool("get_vehicle_by_plate", "Get vehicle details by plate number", P("plateNumberA","string","Arabic plate number")),
        Tool("get_vehicles_expiring_license", "Get vehicles with license expiring soon", P("days","integer","Days window (default 30)")),
        Tool("get_unassigned_vehicles", "Get all available/unassigned vehicles"),
        Tool("get_available_vehicles", "Get vehicles currently available to be taken"),
        Tool("get_unavailable_vehicles", "Get unavailable vehicles filtered by status", P("statusFilter","string","all/unavailable/problem/stolen/breakup")),
        Tool("get_vehicle_history", "Get full status history for a vehicle", P("plateNumberA","string","Arabic plate number")),
        Tool("get_vehicles_grouped_by_status", "Get vehicles grouped by their current status"),
        Tool("get_rider_vehicle_history", "Get vehicle history for a specific rider", P("iqamaNo","string","Rider Iqama number")),
        Tool("take_vehicle", "Assign a vehicle to a rider (requires confirmation)", P("iqamaNo","string","Rider Iqama"), P("plateNumberA","string","Vehicle plate"), P("reason","string","Reason"), P("permission","string","Permission number")),
        Tool("return_vehicle", "Return a vehicle from a rider (requires confirmation)", P("iqamaNo","string","Rider Iqama"), P("plateNumberA","string","Vehicle plate"), P("reason","string","Reason")),
        Tool("report_vehicle_problem", "Report a problem with a vehicle (requires confirmation)", P("plateNumberA","string","Vehicle plate"), P("reason","string","Problem description"), P("iqamaNo","string","Rider Iqama (optional)")),
        Tool("mark_vehicle_stolen", "Mark a vehicle as stolen (requires confirmation)", P("plateNumberA","string","Vehicle plate"), P("reason","string","Details"), P("iqamaNo","string","Reported by Iqama (optional)")),

        // Wallet
        Tool("get_all_wallet_records", "Get all wallet payment records"),
        Tool("get_wallet_by_rider", "Get wallet records for a specific rider", P("workingId","string","Working ID")),
        Tool("get_wallet_summary_range", "Total earnings per rider for a date range", P("startDate","string","Start date"), P("endDate","string","End date (optional)")),
        Tool("get_top_earners", "Top earners for a month", P("month","string","Month YYYY-MM"), P("topN","integer","How many (optional)")),

        // Petrol
        Tool("get_petrol_daily_report", "Get petrol costs for a specific date", P("date","string","Date YYYY-MM-DD")),
        Tool("get_rider_petrol_monthly", "Get petrol costs for a rider in a month", P("iqamaNo","string","Rider Iqama"), P("month","string","Month YYYY-MM")),
        Tool("get_all_riders_petrol_summary", "Petrol cost summary for all riders in a month", P("month","string","Month YYYY-MM")),
        Tool("get_vehicle_petrol_monthly", "Petrol costs for a vehicle in a month", P("vehicleNumber","string","Vehicle number"), P("month","string","Month YYYY-MM")),
        Tool("get_all_vehicles_petrol_summary", "Petrol cost summary for all vehicles in a month", P("month","string","Month YYYY-MM")),
        Tool("get_unattributed_petrol", "Get petrol costs that couldn't be attributed to a rider", P("month","string","Month YYYY-MM")),

        // Spare parts & accessories
        Tool("get_all_spare_parts", "Get all spare parts with quantities and prices"),
        Tool("get_spare_parts_usage_history", "Get usage history for a spare part", P("sparePartId","integer","Spare part ID")),
        Tool("get_vehicle_spare_parts", "Get spare parts used on a vehicle", P("vehicleNumber","string","Vehicle number")),
        Tool("get_all_accessories", "Get all rider accessories"),
        Tool("get_rider_accessories", "Get accessories issued to a rider", P("riderId","integer","Rider ID")),
        Tool("get_housing_cost_report", "Get detailed cost report for a housing unit", P("housingName","string","Housing name"), P("fromDate","string","From date YYYY-MM-DD"), P("toDate","string","To date YYYY-MM-DD")),
        Tool("get_all_housings_cost_summary", "Cost summary across all housings", P("fromDate","string","From date YYYY-MM-DD"), P("toDate","string","To date YYYY-MM-DD")),

        // Suppliers & Bills
        Tool("get_all_suppliers", "Get all suppliers"),
        Tool("get_all_bills", "Get all bills/invoices"),
        Tool("get_bills_by_supplier", "Get bills for a specific supplier", P("supplierId","integer","Supplier ID")),
        Tool("get_bills_by_date_range", "Get bills within a date range", P("fromDate","string","From date"), P("toDate","string","To date")),

        // Transfers
        Tool("get_all_transfers", "Get all inventory transfers between locations"),
        Tool("get_transfers_by_housing", "Get transfers to a specific housing", P("housingId","integer","Housing ID")),

        // Companies
        Tool("get_all_companies", "Get all delivery companies in the system"),

        // Multi-service aggregated
        Tool("get_rider_full_profile", "Get complete rider profile: personal info + vehicle + recent shifts + wallet + accessories in one call", P("iqamaNo","string","Rider Iqama number")),
        Tool("get_company_full_dashboard", "Get company dashboard: rider counts, monthly performance, top/bottom performers, validity", P("companyName","string","Company name"), P("month","string","Month YYYY-MM")),
        Tool("get_housing_full_dashboard", "Get housing dashboard: residents, vehicles, iqama alerts, yesterday's performance", P("housingName","string","Housing name")),
        Tool("get_operational_overview", "Get system-wide operational snapshot: all domain totals at a glance"),
    ];

    private static object Tool(string name, string description, params (string n, string t, string d)[] parameters)
    {
        if (!parameters.Any()) return new { name, description };
        return new
        {
            name,
            description,
            parameters = new
            {
                type = "object",
                properties = parameters.ToDictionary(p => p.n, p => (object)new { type = p.t, description = p.d }),
                required = Array.Empty<string>()
            }
        };
    }

    private static (string n, string t, string d) P(string name, string type, string desc) => (name, type, desc);

    private static string GetSystemPrompt()
    {
        var today = DateTime.UtcNow.AddHours(3);
        return $"""
        You are an intelligent AI assistant for a rider and employee management platform.
        You have access to the ENTIRE system — employees, riders, vehicles, housing, shifts,
        wallet, petrol, spare parts, accessories, suppliers, bills, transfers, and more.
        
        TODAY: {today:yyyy-MM-dd} | CURRENT MONTH: {today:yyyy-MM}

        Core rules:
        - NEVER guess data — always use the appropriate function call.
        - NEVER write code or compute values yourself.
        - For "this month" use {today:yyyy-MM}. For "last month" use {today.AddMonths(-1):yyyy-MM}.
        - For comprehensive questions, prefer the aggregated tools (get_rider_full_profile,
          get_company_full_dashboard, get_housing_full_dashboard, get_operational_overview).
        - Write operations (toggle/delete/reset/assign/return/start-sub/stop-sub) always require
          confirmation — call the function so the system shows a confirmation dialog.
        - Respond in the same language the user uses (Arabic or English).
        - Be concise — the UI renders data in tables; just summarize the key insight.
        - For "top rider", "best performer", "most orders" → use get_top_riders_by_orders_month.
        - For broad system questions ("what's happening today?") → use get_operational_overview.
        """;
    }

    private static List<object> BuildContents(AiChatRequest request)
    {
        var contents = new List<object>();
        foreach (var h in request.History ?? [])
            contents.Add(new { role = h.Role, parts = new[] { new { text = h.Content } } });
        contents.Add(new { role = "user", parts = new[] { new { text = request.Message } } });
        return contents;
    }
}

public interface IGeminiService
{
    Task<AiChatResponse> ChatAsync(AiChatRequest request, string callerUserId);
}