using Application.Abstraction;
using Application.Contracts.Orders;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.Orders;

public class OrderService(ApplicationDbcontext dbcontext) : IOrderService
{
    private readonly ApplicationDbcontext dbcontext = dbcontext;

    // ══════════════════════════════════════════════════════════════════════════
    // Employee Queries
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<Result<IEnumerable<Company4EmployeeResponse>>> GetCompany4EmployeesAsync()
    {
        try
        {
            var now = DateTime.UtcNow.AddHours(3);
            var today = DateOnly.FromDateTime(now);

            var eligibleIqamas = await dbcontext.RiderDetails
                .Where(r => r.CompanyId == 3)
                .Select(r => new { r.EmployeeIqamaNo, r.WorkingId })
                .ToListAsync();

            if (eligibleIqamas.Count == 0)
                return Result.Failure<IEnumerable<Company4EmployeeResponse>>(
                    new Error("Company4.NoEmployees", "No employees found in Company 4.", 404));

            var iqamaSet = eligibleIqamas.Select(x => x.EmployeeIqamaNo).ToList();

            var employees = await dbcontext.Employees
                .Where(e => iqamaSet.Contains(e.IqamaNo) && e.Status.ToLower() == "enable")
                .Include(e => e.Housing)
                .Include(e => e.EmployeeDocuments)   // ← ADD THIS
                .AsNoTracking()
                .ToListAsync();

            if (employees.Count == 0)
                return Result.Failure<IEnumerable<Company4EmployeeResponse>>(
                    new Error("Company4.NoActiveEmployees",
                        "No active (enable + IsEmployee) employees found in Company 4.", 404));

            var todayOrders = await dbcontext.EmployeeOrders
                .Where(o => iqamaSet.Contains(o.EmployeeIqamaNo) && o.OrderDate == today)
                .ToListAsync();

            var workingIdMap = eligibleIqamas.ToDictionary(x => x.EmployeeIqamaNo, x => x.WorkingId);

            var result = employees.Select(emp =>
            {
                var empOrders = todayOrders
                    .Where(o => o.EmployeeIqamaNo == emp.IqamaNo)
                    .OrderByDescending(o => o.StartedAt)
                    .ToList();

                var openOrder = empOrders.FirstOrDefault(o => o.EndedAt == null);

                return new Company4EmployeeResponse(
                    IqamaNo: emp.IqamaNo,
                    NameAR: emp.NameAR,
                    NameEN: emp.NameEN,
                    JobTitle: emp.JobTitle,
                    Country: emp.Country,
                    Phone: emp.Phone,
                    Status: emp.Status,
                    IBAN: emp.IBAN,
                    INKSA: emp.INKSA,
                    IqamaEndM: emp.IqamaEndM,
                    IqamaEndH: emp.IqamaEndH,
                    HousingName: emp.Housing?.Name,
                    WorkingId: workingIdMap.GetValueOrDefault(emp.IqamaNo),
                    CreatedAt: emp.CreatedAt,
                    IsCurrentlyOnOrder: openOrder is not null,
                    TotalOrdersToday: empOrders.Count,
                    CurrentOrderStartedAt: openOrder?.StartedAt,
                    ProfileImagePath: emp.EmployeeDocuments?.ProfileImagePath  // ← ADD THIS
                );
            }).ToList();

            return Result.Success<IEnumerable<Company4EmployeeResponse>>(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<Company4EmployeeResponse>>(
                new Error("Company4.GetEmployeesFailed",
                    $"An unexpected error occurred while retrieving employees: {ex.Message}", 500));
        }
    }

    public async Task<Result<Company4EmployeeResponse>> GetCompany4EmployeeAsync(long iqamaNo)
    {
        try
        {
            var now = DateTime.UtcNow.AddHours(3);
            var today = DateOnly.FromDateTime(now);

            var riderDetail = await dbcontext.RiderDetails
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == iqamaNo && r.CompanyId == 3);

            if (riderDetail is null)
                return Result.Failure<Company4EmployeeResponse>(
                    new Error("Company4.NotFound", "Employee not found in Company 4.", 404));

            var employee = await dbcontext.Employees
                .Include(e => e.Housing)
                .Include(e => e.EmployeeDocuments)   // ← ADD THIS
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.IqamaNo == iqamaNo && e.Status.ToLower() == "enable");

            if (employee is null)
                return Result.Failure<Company4EmployeeResponse>(
                    new Error("Company4.NotEligible",
                        "Employee is not active or not IsEmployee.", 404));

            var todayOrders = await dbcontext.EmployeeOrders
                .Where(o => o.EmployeeIqamaNo == iqamaNo && o.OrderDate == today)
                .OrderByDescending(o => o.StartedAt)
                .ToListAsync();

            var openOrder = todayOrders.FirstOrDefault(o => o.EndedAt == null);

            return Result.Success(new Company4EmployeeResponse(
                IqamaNo: employee.IqamaNo,
                NameAR: employee.NameAR,
                NameEN: employee.NameEN,
                JobTitle: employee.JobTitle,
                Country: employee.Country,
                Phone: employee.Phone,
                Status: employee.Status,
                IBAN: employee.IBAN,
                INKSA: employee.INKSA,
                IqamaEndM: employee.IqamaEndM,
                IqamaEndH: employee.IqamaEndH,
                HousingName: employee.Housing?.Name,
                WorkingId: riderDetail.WorkingId,
                CreatedAt: employee.CreatedAt,
                IsCurrentlyOnOrder: openOrder is not null,
                TotalOrdersToday: todayOrders.Count,
                CurrentOrderStartedAt: openOrder?.StartedAt,
                ProfileImagePath: employee.EmployeeDocuments?.ProfileImagePath  // ← ADD THIS
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<Company4EmployeeResponse>(
                new Error("Company4.GetEmployeeFailed",
                    $"An unexpected error occurred while retrieving employee {iqamaNo}: {ex.Message}", 500));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Order CRUD
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<Result<OrderDetailResponse>> CreateOrderAsync(
        CreateOrderRequest request,
        string requestedBy)
    {
        try
        {
            var riderDetail = await dbcontext.RiderDetails
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == request.EmployeeIqamaNo && r.CompanyId == 3);

            if (riderDetail is null)
                return Result.Failure<OrderDetailResponse>(new Error(
                    "Order.NotInCompany4", "Employee is not in Company 4.", 400));

            var employee = await dbcontext.Employees
                .Include(e => e.Housing)
                .FirstOrDefaultAsync(e =>
                    e.IqamaNo == request.EmployeeIqamaNo &&
                    e.Status.ToLower() == "enable");

            if (employee is null)
                return Result.Failure<OrderDetailResponse>(new Error(
                    "Order.InvalidEmployee",
                    "Employee not found, not IsEmployee, or not enabled.", 404));

            var now = DateTime.UtcNow.AddHours(3);
            var today = DateOnly.FromDateTime(now);

            var openOrder = await dbcontext.EmployeeOrders
                .Where(o => o.EmployeeIqamaNo == request.EmployeeIqamaNo
                         && o.OrderDate == today
                         && o.EndedAt == null)
                .FirstOrDefaultAsync();

            if (openOrder is not null)
                openOrder.EndedAt = now;

            var newOrder = new EmployeeOrder
            {
                EmployeeIqamaNo = request.EmployeeIqamaNo,
                Order = request.Order,
                StartedAt = now,
                EndedAt = null,
                OrderDate = today,
                CompanyId = 3,
                RequestedBy = requestedBy,
                CreatedAt = now,
                Notes = request.Notes
            };

            await dbcontext.EmployeeOrders.AddAsync(newOrder);
            await dbcontext.SaveChangesAsync();

            return Result.Success(MapToDetail(newOrder, employee, riderDetail.WorkingId));
        }
        catch (DbUpdateException ex)
        {
            return Result.Failure<OrderDetailResponse>(new Error(
                "Order.DatabaseError",
                $"A database error occurred while creating the order: {ex.InnerException?.Message ?? ex.Message}", 500));
        }
        catch (Exception ex)
        {
            return Result.Failure<OrderDetailResponse>(new Error(
                "Order.CreateFailed",
                $"An unexpected error occurred while creating the order: {ex.Message}", 500));
        }
    }

    public async Task<Result> CloseEmployeeOrderAsync(long iqamaNo, string closedBy)
    {
        try
        {
            var now = DateTime.UtcNow.AddHours(3);
            var today = DateOnly.FromDateTime(now);

            var openOrder = await dbcontext.EmployeeOrders
                .FirstOrDefaultAsync(o =>
                    o.EmployeeIqamaNo == iqamaNo &&
                    o.OrderDate == today &&
                    o.EndedAt == null);

            if (openOrder is null)
                return Result.Failure(new Error(
                    "Order.NoneOpen", "No open order found for this employee today.", 404));

            openOrder.EndedAt = now;
            await dbcontext.SaveChangesAsync();
            return Result.Success();
        }
        catch (DbUpdateException ex)
        {
            return Result.Failure(new Error(
                "Order.DatabaseError",
                $"A database error occurred while closing the order: {ex.InnerException?.Message ?? ex.Message}", 500));
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error(
                "Order.CloseFailed",
                $"An unexpected error occurred while closing the order: {ex.Message}", 500));
        }
    }

    public async Task<Result> CloseAllOpenOrdersAsync(string closedBy)
    {
        try
        {
            var now = DateTime.UtcNow.AddHours(3);
            var today = DateOnly.FromDateTime(now);

            var openOrders = await dbcontext.EmployeeOrders
                .Where(o => o.OrderDate == today && o.EndedAt == null)
                .ToListAsync();

            if (openOrders.Count == 0)
                return Result.Failure(new Error(
                    "Order.NoneOpen", "No open orders found for today.", 404));

            foreach (var o in openOrders)
                o.EndedAt = now;

            await dbcontext.SaveChangesAsync();
            return Result.Success();
        }
        catch (DbUpdateException ex)
        {
            return Result.Failure(new Error(
                "Order.DatabaseError",
                $"A database error occurred while closing all orders: {ex.InnerException?.Message ?? ex.Message}", 500));
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error(
                "Order.CloseAllFailed",
                $"An unexpected error occurred while closing all orders: {ex.Message}", 500));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Order Queries
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<Result<EmployeeOrderHistoryResponse>> GetEmployeeOrderHistoryAsync(long iqamaNo)
    {
        try
        {
            var riderDetail = await dbcontext.RiderDetails
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == iqamaNo && r.CompanyId == 3);

            if (riderDetail is null)
                return Result.Failure<EmployeeOrderHistoryResponse>(new Error(
                    "Order.NotInCompany4", "Employee not found in Company 4.", 404));

            var employee = await dbcontext.Employees
                .Include(e => e.Housing)
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.IqamaNo == iqamaNo);

            if (employee is null)
                return Result.Failure<EmployeeOrderHistoryResponse>(new Error(
                    "Order.EmployeeNotFound", "Employee not found.", 404));

            var orders = await dbcontext.EmployeeOrders
                .Where(o => o.EmployeeIqamaNo == iqamaNo)
                .OrderByDescending(o => o.StartedAt)
                .ToListAsync();

            var closedOrders = orders.Where(o => o.EndedAt.HasValue).ToList();

            double totalMinutes = closedOrders
                .Sum(o => (o.EndedAt!.Value - o.StartedAt).TotalMinutes);

            var distinctDays = orders.Select(o => o.OrderDate).Distinct().Count();

            double avgOrdersPerDay = distinctDays > 0
                ? Math.Round((double)orders.Count / distinctDays, 2)
                : 0;

            double avgMinutesPerOrder = closedOrders.Count > 0
                ? Math.Round(totalMinutes / closedOrders.Count, 2)
                : 0;

            var details = orders
                .Select(o => MapToDetail(o, employee, riderDetail.WorkingId))
                .ToList();

            return Result.Success(new EmployeeOrderHistoryResponse(
                IqamaNo: employee.IqamaNo,
                NameAR: employee.NameAR,
                NameEN: employee.NameEN,
                JobTitle: employee.JobTitle,
                Country: employee.Country,
                HousingName: employee.Housing?.Name,
                WorkingId: riderDetail.WorkingId,
                CurrentStatus: employee.Status,
                TotalOrders: orders.Count,
                TotalDaysWithOrders: distinctDays,
                TotalMinutesOnOrder: Math.Round(totalMinutes, 2),
                AverageOrdersPerDay: avgOrdersPerDay,
                AverageMinutesPerOrder: avgMinutesPerOrder,
                FirstOrderEver: orders.Any() ? orders.Min(o => o.StartedAt) : null,
                LastOrderEver: orders.Any() ? orders.Max(o => o.StartedAt) : null,
                Orders: details
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<EmployeeOrderHistoryResponse>(new Error(
                "Order.HistoryFailed",
                $"An unexpected error occurred while retrieving order history: {ex.Message}", 500));
        }
    }

    public async Task<Result<ActiveOrderSnapshotResponse>> GetActiveOrdersSnapshotAsync()
    {
        try
        {
            var now = DateTime.UtcNow.AddHours(3);
            var today = DateOnly.FromDateTime(now);

            var eligibleIqamas = await dbcontext.RiderDetails
                .Where(r => r.CompanyId == 3)
                .Select(r => new { r.EmployeeIqamaNo, r.WorkingId })
                .ToListAsync();

            var iqamaSet = eligibleIqamas.Select(x => x.EmployeeIqamaNo).ToList();

            var employees = await dbcontext.Employees
                .Where(e => iqamaSet.Contains(e.IqamaNo) && e.Status.ToLower() == "enable")
                .Include(e => e.Housing)
                .AsNoTracking()
                .ToListAsync();

            var activeOrders = await dbcontext.EmployeeOrders
                .Where(o => o.OrderDate == today && o.EndedAt == null && iqamaSet.Contains(o.EmployeeIqamaNo))
                .ToListAsync();

            var activeIqamas = activeOrders.Select(o => o.EmployeeIqamaNo).ToHashSet();
            var workingIdMap = eligibleIqamas.ToDictionary(x => x.EmployeeIqamaNo, x => x.WorkingId);

            var activeItems = activeOrders.Select(o =>
            {
                var emp = employees.FirstOrDefault(e => e.IqamaNo == o.EmployeeIqamaNo);
                return new ActiveOrderItem(
                    OrderId: o.Id,
                    IqamaNo: o.EmployeeIqamaNo,
                    NameAR: emp?.NameAR ?? "N/A",
                    NameEN: emp?.NameEN ?? "N/A",
                    JobTitle: emp?.JobTitle ?? "N/A",
                    HousingName: emp?.Housing?.Name,
                    WorkingId: workingIdMap.GetValueOrDefault(o.EmployeeIqamaNo),
                    StartedAt: o.StartedAt,
                    MinutesElapsed: Math.Round((now - o.StartedAt).TotalMinutes, 2),
                    Notes: o.Notes
                );
            }).OrderBy(x => x.StartedAt).ToList();

            var todayOrders = await dbcontext.EmployeeOrders
                .Where(o => o.OrderDate == today && iqamaSet.Contains(o.EmployeeIqamaNo))
                .ToListAsync();

            var notOnOrder = employees
                .Where(e => !activeIqamas.Contains(e.IqamaNo))
                .Select(emp =>
                {
                    var empOrders = todayOrders
                        .Where(o => o.EmployeeIqamaNo == emp.IqamaNo)
                        .OrderByDescending(o => o.StartedAt)
                        .ToList();

                    return new Company4EmployeeResponse(
                        IqamaNo: emp.IqamaNo,
                        NameAR: emp.NameAR,
                        NameEN: emp.NameEN,
                        JobTitle: emp.JobTitle,
                        Country: emp.Country,
                        Phone: emp.Phone,
                        Status: emp.Status,
                        IBAN: emp.IBAN,
                        INKSA: emp.INKSA,
                        IqamaEndM: emp.IqamaEndM,
                        IqamaEndH: emp.IqamaEndH,
                        HousingName: emp.Housing?.Name,
                        WorkingId: workingIdMap.GetValueOrDefault(emp.IqamaNo),
                        CreatedAt: emp.CreatedAt,
                        IsCurrentlyOnOrder: false,
                        TotalOrdersToday: empOrders.Count,
                        CurrentOrderStartedAt: null,
                        null
                    );
                }).ToList();

            return Result.Success(new ActiveOrderSnapshotResponse(
                SnapshotAt: now,
                TotalActiveOrders: activeItems.Count,
                TotalEligibleEmployees: employees.Count,
                ActiveOrders: activeItems,
                EmployeesNotOnOrder: notOnOrder
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<ActiveOrderSnapshotResponse>(new Error(
                "Order.SnapshotFailed",
                $"An unexpected error occurred while retrieving the active orders snapshot: {ex.Message}", 500));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Reports
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<Result<DailyOrderReportResponse>> GetTodayReportAsync()
        => await GetDailyReportAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)));

    public async Task<Result<DailyOrderReportResponse>> GetDailyReportAsync(DateOnly date)
    {
        try
        {
            var eligibleIqamas = await dbcontext.RiderDetails
                .Where(r => r.CompanyId == 3)
                .Select(r => new { r.EmployeeIqamaNo, r.WorkingId })
                .ToListAsync();

            var iqamaSet = eligibleIqamas.Select(x => x.EmployeeIqamaNo).ToList();

            var employees = await dbcontext.Employees
                .Where(e => iqamaSet.Contains(e.IqamaNo) && e.Status.ToLower() == "enable")
                .Include(e => e.Housing)
                .AsNoTracking()
                .ToListAsync();

            if (employees.Count == 0)
                return Result.Failure<DailyOrderReportResponse>(new Error(
                    "Company4.NoEmployees", "No eligible employees found in Company 4.", 404));

            var orders = await dbcontext.EmployeeOrders
                .Where(o => o.OrderDate == date && iqamaSet.Contains(o.EmployeeIqamaNo))
                .OrderBy(o => o.EmployeeIqamaNo)
                .ThenBy(o => o.StartedAt)
                .ToListAsync();

            var workingIdMap = eligibleIqamas.ToDictionary(x => x.EmployeeIqamaNo, x => x.WorkingId);
            var now = DateTime.UtcNow.AddHours(3);

            var employeeSummaries = employees.Select(emp =>
            {
                var empOrders = orders
                    .Where(o => o.EmployeeIqamaNo == emp.IqamaNo)
                    .ToList();

                var closedOrders = empOrders.Where(o => o.EndedAt.HasValue).ToList();

                double totalMinutes = closedOrders
                    .Sum(o => (o.EndedAt!.Value - o.StartedAt).TotalMinutes);

                var openOrder = empOrders.FirstOrDefault(o => o.EndedAt == null);

                if (openOrder is not null)
                    totalMinutes += (now - openOrder.StartedAt).TotalMinutes;

                var details = empOrders
                    .Select(o => MapToDetail(o, emp, workingIdMap.GetValueOrDefault(emp.IqamaNo)))
                    .ToList();

                return new DailyEmployeeOrderSummary(
                    IqamaNo: emp.IqamaNo,
                    NameAR: emp.NameAR,
                    NameEN: emp.NameEN,
                    JobTitle: emp.JobTitle,
                    HousingName: emp.Housing?.Name,
                    WorkingId: workingIdMap.GetValueOrDefault(emp.IqamaNo),
                    HadOrderToday: empOrders.Count > 0,
                    IsCurrentlyOnOrder: openOrder is not null,
                    TotalOrders: empOrders.Count,
                    TotalMinutesOnOrder: Math.Round(totalMinutes, 2),
                    FirstOrderAt: empOrders.Any() ? empOrders.Min(o => o.StartedAt) : null,
                    LastOrderAt: empOrders.Any() ? empOrders.Max(o => o.StartedAt) : null,
                    Orders: details
                );
            }).ToList();

            double totalMinutesWorked = employeeSummaries.Sum(e => e.TotalMinutesOnOrder);
            int employeesWithOrders = employeeSummaries.Count(e => e.HadOrderToday);
            int activeNow = employeeSummaries.Count(e => e.IsCurrentlyOnOrder);

            return Result.Success(new DailyOrderReportResponse(
                Date: date,
                GeneratedAt: now,
                TotalEligibleEmployees: employees.Count,
                EmployeesWithOrders: employeesWithOrders,
                EmployeesWithoutOrders: employees.Count - employeesWithOrders,
                TotalOrdersCreated: orders.Count,
                CurrentlyActiveOrders: activeNow,
                TotalMinutesWorked: Math.Round(totalMinutesWorked, 2),
                Employees: employeeSummaries
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<DailyOrderReportResponse>(new Error(
                "Order.DailyReportFailed",
                $"An unexpected error occurred while generating the daily report: {ex.Message}", 500));
        }
    }

    public async Task<Result<DateRangeOrderReportResponse>> GetDateRangeReportAsync(
        DateTime start,
        DateTime end)
    {
        try
        {
            var eligibleIqamas = await dbcontext.RiderDetails
                .Where(r => r.CompanyId == 3)
                .Select(r => new { r.EmployeeIqamaNo, r.WorkingId })
                .ToListAsync();

            var iqamaSet = eligibleIqamas.Select(x => x.EmployeeIqamaNo).ToList();

            var orders = await dbcontext.EmployeeOrders
                .Where(o => o.StartedAt >= start
                         && o.StartedAt <= end
                         && iqamaSet.Contains(o.EmployeeIqamaNo))
                .Include(o => o.Employee)
                .OrderBy(o => o.StartedAt)
                .ToListAsync();

            var workingIdMap = eligibleIqamas.ToDictionary(x => x.EmployeeIqamaNo, x => x.WorkingId);

            var daySummaries = orders
                .GroupBy(o => o.OrderDate)
                .Select(g =>
                {
                    double mins = g
                        .Where(o => o.EndedAt.HasValue)
                        .Sum(o => (o.EndedAt!.Value - o.StartedAt).TotalMinutes);

                    return new DateRangeDaySummary(
                        Date: g.Key,
                        TotalOrders: g.Count(),
                        ActiveEmployees: g.Select(o => o.EmployeeIqamaNo).Distinct().Count(),
                        TotalMinutesWorked: Math.Round(mins, 2)
                    );
                })
                .OrderBy(d => d.Date)
                .ToList();

            var employeeSummaries = orders
                .GroupBy(o => o.EmployeeIqamaNo)
                .Select(g =>
                {
                    var emp = g.First().Employee;
                    double mins = g
                        .Where(o => o.EndedAt.HasValue)
                        .Sum(o => (o.EndedAt!.Value - o.StartedAt).TotalMinutes);

                    return new DateRangeEmployeeSummary(
                        IqamaNo: g.Key,
                        NameAR: emp?.NameAR ?? "N/A",
                        NameEN: emp?.NameEN ?? "N/A",
                        WorkingId: workingIdMap.GetValueOrDefault(g.Key),
                        TotalOrders: g.Count(),
                        DaysActive: g.Select(o => o.OrderDate).Distinct().Count(),
                        TotalMinutesOnOrder: Math.Round(mins, 2)
                    );
                })
                .OrderByDescending(e => e.TotalOrders)
                .ToList();

            double totalDays = (end - start).TotalDays + 1;

            return Result.Success(new DateRangeOrderReportResponse(
                StartDate: start,
                EndDate: end,
                GeneratedAt: DateTime.UtcNow.AddHours(3),
                TotalDays: (int)totalDays,
                TotalOrders: orders.Count,
                TotalEmployeesInvolved: employeeSummaries.Count,
                TotalMinutesWorked: daySummaries.Sum(d => d.TotalMinutesWorked),
                DaySummaries: daySummaries,
                EmployeeSummaries: employeeSummaries
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<DateRangeOrderReportResponse>(new Error(
                "Order.DateRangeReportFailed",
                $"An unexpected error occurred while generating the date range report: {ex.Message}", 500));
        }
    }

    public async Task<Result<OrderStatisticsResponse>> GetStatisticsAsync()
    {
        try
        {
            var now = DateTime.UtcNow.AddHours(3);
            var today = DateOnly.FromDateTime(now);

            var eligibleIqamas = await dbcontext.RiderDetails
                .Where(r => r.CompanyId == 3)
                .Select(r => r.EmployeeIqamaNo)
                .ToListAsync();

            var totalEligible = await dbcontext.Employees
                .CountAsync(e => eligibleIqamas.Contains(e.IqamaNo)
                              && e.Status.ToLower() == "enable");

            var allOrders = await dbcontext.EmployeeOrders
                .Where(o => eligibleIqamas.Contains(o.EmployeeIqamaNo))
                .Include(o => o.Employee)
                .ToListAsync();

            var todayOrders = allOrders.Where(o => o.OrderDate == today).ToList();
            var activeNow = todayOrders.Count(o => o.EndedAt == null);

            var closedOrders = allOrders.Where(o => o.EndedAt.HasValue).ToList();

            double totalMinutes = closedOrders
                .Sum(o => (o.EndedAt!.Value - o.StartedAt).TotalMinutes);

            var distinctDays = allOrders.Select(o => o.OrderDate).Distinct().Count();
            double avgOrdersPerDay = distinctDays > 0
                ? Math.Round((double)allOrders.Count / distinctDays, 2)
                : 0;

            double avgMinutesPerOrder = closedOrders.Count > 0
                ? Math.Round(totalMinutes / closedOrders.Count, 2)
                : 0;

            var ordersByMonth = allOrders
                .GroupBy(o => $"{o.OrderDate.Year}-{o.OrderDate.Month:D2}")
                .OrderByDescending(g => g.Key)
                .Take(12)
                .ToDictionary(g => g.Key, g => g.Count());

            var ordersByEmployee = allOrders
                .GroupBy(o => o.EmployeeIqamaNo)
                .ToDictionary(
                    g => g.First().Employee?.NameEN ?? g.Key.ToString(),
                    g => g.Count()
                );

            var minutesByEmployee = closedOrders
                .GroupBy(o => o.EmployeeIqamaNo)
                .ToDictionary(
                    g => g.First().Employee?.NameEN ?? g.Key.ToString(),
                    g => Math.Round(g.Sum(o => (o.EndedAt!.Value - o.StartedAt).TotalMinutes), 2)
                );

            return Result.Success(new OrderStatisticsResponse(
                GeneratedAt: now,
                TotalEligibleEmployees: totalEligible,
                TotalOrdersAllTime: allOrders.Count,
                TotalOrdersToday: todayOrders.Count,
                CurrentlyActiveOrders: activeNow,
                TotalMinutesAllTime: Math.Round(totalMinutes, 2),
                AverageOrdersPerDay: avgOrdersPerDay,
                AverageMinutesPerOrder: avgMinutesPerOrder,
                OrdersByMonth: ordersByMonth,
                OrdersByEmployee: ordersByEmployee,
                MinutesByEmployee: minutesByEmployee
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<OrderStatisticsResponse>(new Error(
                "Order.StatisticsFailed",
                $"An unexpected error occurred while retrieving statistics: {ex.Message}", 500));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Private Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private static OrderDetailResponse MapToDetail(
        EmployeeOrder order,
        Employees employee,
        string? workingId)
    {
        double? durationMinutes = order.EndedAt.HasValue
            ? Math.Round((order.EndedAt.Value - order.StartedAt).TotalMinutes, 2)
            : null;

        return new OrderDetailResponse(
            Id: order.Id,
            EmployeeIqamaNo: order.EmployeeIqamaNo,
            EmployeeNameAR: employee.NameAR,
            EmployeeNameEN: employee.NameEN,
            EmployeeStatus: employee.Status,
            HousingName: employee.Housing?.Name,
            WorkingId: workingId,
            Order: order.Order,
            StartedAt: order.StartedAt,
            EndedAt: order.EndedAt,
            DurationMinutes: durationMinutes,
            OrderDate: order.OrderDate,
            RequestedBy: order.RequestedBy,
            Notes: order.Notes
        );
    }
}