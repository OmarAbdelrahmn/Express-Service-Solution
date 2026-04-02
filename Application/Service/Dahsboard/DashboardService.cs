using Application.Abstraction;
using Application.Service.Dahsboard;
using Application.Service.Empolyee;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.Dashboard;

public class DashboardService(ApplicationDbcontext dbcontext) : IDashboardService
{
    private readonly ApplicationDbcontext dbcontext = dbcontext;

    public async Task<Result<DailyCompanyReport>> GetDailyReportAsync(DailyCompanyReportRequest request)
    {
        try
        {
            if (request.StartDate > request.EndDate)
                return Result.Failure<DailyCompanyReport>(
                    new Error("InvalidRange", "Start date must be before or equal to end date.", 400));

            int spanDays = request.EndDate.DayNumber - request.StartDate.DayNumber + 1;
            if (spanDays > 800)
                return Result.Failure<DailyCompanyReport>(
                    new Error("RangeTooLarge", "Date range cannot exceed 366 days.", 400));

            // ── Pull raw shifts in range ──────────────────────────────────────
            var query = dbcontext.RiderShifts
                .Where(s => s.ShiftDate >= request.StartDate && s.ShiftDate <= request.EndDate);

            if (request.CompanyId.HasValue)
                query = query.Where(s => s.CompanyId == request.CompanyId.Value);

            var rawShifts = await query
                .Select(s => new
                {
                    s.ShiftDate,
                    s.CompanyId,
                    CompanyName = s.Company.Name,
                    s.RiderId,
                    s.AcceptedDailyOrders,
                    s.RejectedDailyOrders,
                    s.WorkingHours
                })
                .ToListAsync();

            // ── Build the full date spine (every day in range) ────────────────
            var allDates = Enumerable.Range(0, spanDays)
                .Select(i => request.StartDate.AddDays(i))
                .ToList();

            // ── Group by company ──────────────────────────────────────────────
            var byCompany = rawShifts
                .GroupBy(s => new { s.CompanyId, s.CompanyName })
                .OrderBy(g => g.Key.CompanyName)
                .ToList();

            var companyReports = byCompany.Select(company =>
            {
                // Build one DayEntry per calendar day — even days with zero activity
                var days = allDates.Select(date =>
                {
                    var dayShifts = company.Where(s => s.ShiftDate == date).ToList();

                    int accepted = dayShifts.Sum(s => s.AcceptedDailyOrders);
                    int rejected = dayShifts.Sum(s => s.RejectedDailyOrders);
                    int riders = dayShifts.Select(s => s.RiderId).Distinct().Count();
                    int shifts = dayShifts.Count;
                    double hours = Math.Round(dayShifts.Sum(s => (double)s.WorkingHours), 1);
                    double avgPerR = riders > 0 ? Math.Round((double)accepted / riders, 1) : 0;

                    return new DayEntry(
                        Date: date,
                        DateLabel: date.ToString("ddd dd MMM"),
                        DayOfWeek: date.DayOfWeek.ToString(),
                        AcceptedOrders: accepted,
                        RejectedOrders: rejected,
                        UniqueRiders: riders,
                        TotalShifts: shifts,
                        AvgOrdersPerRider: avgPerR,
                        TotalWorkingHours: hours
                    );
                }).ToList();

                int totalOrders = days.Sum(d => d.AcceptedOrders);
                int totalShifts = days.Sum(d => d.TotalShifts);
                int totalRiders = company.Select(s => s.RiderId).Distinct().Count();
                int activeDays = days.Count(d => d.AcceptedOrders > 0);

                return new CompanyDailyReport(
                    CompanyId: company.Key.CompanyId,
                    CompanyName: company.Key.CompanyName,
                    TotalOrders: totalOrders,
                    TotalShifts: totalShifts,
                    TotalUniqueRiders: totalRiders,
                    AvgOrdersPerDay: activeDays > 0 ? Math.Round((double)totalOrders / activeDays, 1) : 0,
                    AvgRidersPerDay: activeDays > 0 ? Math.Round((double)totalRiders / activeDays, 1) : 0,
                    Days: days
                );
            }).ToList();

            return Result.Success(new DailyCompanyReport(
                StartDate: request.StartDate,
                EndDate: request.EndDate,
                TotalDays: spanDays,
                TotalCompanies: companyReports.Count,
                GrandTotalOrders: companyReports.Sum(c => (long)c.TotalOrders),
                GrandTotalShifts: companyReports.Sum(c => (int)c.TotalShifts),
                Companies: companyReports
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<DailyCompanyReport>(
                new Error("ReportError", $"Failed to generate report: {ex.Message}", 500));
        }
    }

    // ── Overview ──────────────────────────────────────────────────────────────

    public async Task<Result<DashboardOverview>> GetOverviewAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3).AddDays(-1));

            // Employees
            var employees = await dbcontext.Employees
                .Where(e => e.IsEmployee && !e.IsDeleted)
                .GroupBy(e => e.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            int totalEmployees = employees.Sum(e => e.Count);
            int activeEmployees = employees.FirstOrDefault(e => e.Status.ToLower() == "enable")?.Count ?? 0;

            // Riders
            var riders = await dbcontext.Employees
                .Where(e => !e.IsEmployee && !e.IsDeleted)
                .GroupBy(e => e.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            int totalRiders = riders.Sum(r => r.Count);
            int activeRiders = riders.FirstOrDefault(r => r.Status.ToLower() == "enable")?.Count ?? 0;

            // Vehicles
            int totalVehicles = await dbcontext.Vehicles.CountAsync();
            var activeVehicleStatuses = await dbcontext.RiderVehicleStatus
                .Where(s => s.IsActive)
                .GroupBy(s => s.StatusType)
                .Select(g => new { Type = g.Key, Count = g.Select(s => s.VehicleNumber).Distinct().Count() })
                .ToListAsync();

            int takenVehicles = activeVehicleStatuses.FirstOrDefault(v => v.Type == VehicleStatusType.Taken)?.Count ?? 0;
            int problemVehicles = activeVehicleStatuses.FirstOrDefault(v => v.Type == VehicleStatusType.Problem)?.Count ?? 0;
            int stolenVehicles = activeVehicleStatuses.FirstOrDefault(v => v.Type == VehicleStatusType.Stolen)?.Count ?? 0;
            int breakUpVehicles = activeVehicleStatuses.FirstOrDefault(v => v.Type == VehicleStatusType.BreakUp)?.Count ?? 0;
            int unavailableVehicles = takenVehicles + problemVehicles + stolenVehicles + breakUpVehicles;
            int availableVehicles = totalVehicles - unavailableVehicles;

            // Housing
            int totalHousings = await dbcontext.Housings.CountAsync();

            // Companies
            int totalCompanies = await dbcontext.Companies.CountAsync();

            // Iqama expiry
            var allActiveEmployees = await dbcontext.Employees
                .Where(e => !e.IsDeleted && e.Status.ToLower() != "fleeing")
                .Select(e => e.IqamaEndM)
                .ToListAsync();

            int expiredIqamas = allActiveEmployees.Count(d => d.DayNumber - today.DayNumber <= 0);
            int criticalIqamas = allActiveEmployees.Count(d => d.DayNumber - today.DayNumber is > 0 and <= 30);

            // Today's shifts
            int todayShifts = await dbcontext.RiderShifts
                .Where(s => s.ShiftDate == today)
                .CountAsync();

            int todayOrders = await dbcontext.RiderShifts
                .Where(s => s.ShiftDate == today)
                .SumAsync(s => (int?)s.AcceptedDailyOrders) ?? 0;

            return Result.Success(new DashboardOverview(
                TotalEmployees: totalEmployees,
                TotalRiders: totalRiders,
                TotalVehicles: totalVehicles,
                TotalHousings: totalHousings,
                ActiveEmployees: activeEmployees,
                ActiveRiders: activeRiders,
                AvailableVehicles: availableVehicles,
                TakenVehicles: takenVehicles,
                ProblemVehicles: problemVehicles,
                StolenVehicles: stolenVehicles,
                BreakUpVehicles: breakUpVehicles,
                ExpiredIqamas: expiredIqamas,
                CriticalIqamas: criticalIqamas,
                TotalCompanies: totalCompanies,
                TodayShifts: todayShifts,
                TodayOrders: todayOrders
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<DashboardOverview>(
                new Error("DashboardError", $"Failed to load overview: {ex.Message}", 500));
        }
    }

    // ── Company Orders ────────────────────────────────────────────────────────

    public async Task<Result<IEnumerable<CompanyOrderStats>>> GetOrdersByCompanyAsync(int year, int month)
    {
        try
        {
            var from = new DateOnly(year, month, 1);
            var to = from.AddMonths(1).AddDays(-1);

            var stats = await dbcontext.RiderShifts
                .Where(s => s.ShiftDate >= from && s.ShiftDate <= to)
                .GroupBy(s => new { s.CompanyId, s.Company.Name })
                .Select(g => new
                {
                    g.Key.CompanyId,
                    g.Key.Name,
                    TotalOrders = g.Sum(s => s.AcceptedDailyOrders),
                    TotalShifts = g.Count(),
                    TotalRiders = g.Select(s => s.RiderId).Distinct().Count(),
                })
                .OrderByDescending(g => g.TotalOrders)
                .ToListAsync();

            var result = stats.Select(s => new CompanyOrderStats(
                CompanyId: s.CompanyId,
                CompanyName: s.Name,
                TotalOrders: s.TotalOrders,
                TotalShifts: s.TotalShifts,
                TotalRiders: s.TotalRiders,
                AvgOrdersPerRider: s.TotalRiders > 0 ? Math.Round((double)s.TotalOrders / s.TotalRiders, 1) : 0,
                AvgOrdersPerShift: s.TotalShifts > 0 ? Math.Round((double)s.TotalOrders / s.TotalShifts, 1) : 0,
                Month: month,
                Year: year
            ));

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<CompanyOrderStats>>(
                new Error("DashboardError", $"Failed to load company orders: {ex.Message}", 500));
        }
    }

    // ── Monthly Trend ─────────────────────────────────────────────────────────

    public async Task<Result<IEnumerable<MonthlyOrderTrend>>> GetOrderTrendAsync(int months = 6)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
            var from = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));

            var shifts = await dbcontext.RiderShifts
                .Where(s => s.ShiftDate >= from)
                .Select(s => new
                {
                    s.ShiftDate,
                    s.AcceptedDailyOrders,
                    s.RejectedDailyOrders,
                    s.RiderId
                })
                .ToListAsync();

            var grouped = shifts
                .GroupBy(s => new { s.ShiftDate.Year, s.ShiftDate.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g =>
                {
                    var totalOrders = g.Sum(s => s.AcceptedDailyOrders);
                    var totalRiders = g.Select(s => s.RiderId).Distinct().Count();
                    return new MonthlyOrderTrend(
                        Year: g.Key.Year,
                        Month: g.Key.Month,
                        MonthLabel: $"{g.Key.Year}/{g.Key.Month:D2}",
                        TotalOrders: totalOrders,
                        TotalShifts: g.Count(),
                        TotalRiders: totalRiders,
                        TotalRejected: g.Sum(s => s.RejectedDailyOrders),
                        AvgOrdersPerRider: totalRiders > 0 ? Math.Round((double)totalOrders / totalRiders, 1) : 0
                    );
                });

            return Result.Success(grouped.AsEnumerable());
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<MonthlyOrderTrend>>(
                new Error("DashboardError", $"Failed to load order trend: {ex.Message}", 500));
        }
    }

    // ── Daily Orders Trend ────────────────────────────────────────────────────

    public async Task<Result<IEnumerable<DailyOrderStats>>> GetDailyOrdersTrendAsync(int days = 30, int? companyId = null)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3).AddDays(-1));
            var from = today.AddDays(-days);

            var query = dbcontext.RiderShifts
                .Where(s => s.ShiftDate >= from && s.ShiftDate <= today);

            if (companyId.HasValue)
                query = query.Where(s => s.CompanyId == companyId.Value);

            var shifts = await query
                .Select(s => new { s.ShiftDate, s.AcceptedDailyOrders, s.RejectedDailyOrders, s.RiderId })
                .ToListAsync();

            // Fill all days even if no data
            var result = Enumerable.Range(0, days)
                .Select(i => from.AddDays(i))
                .Select(date =>
                {
                    var dayShifts = shifts.Where(s => s.ShiftDate == date).ToList();
                    int totalOrders = dayShifts.Sum(s => s.AcceptedDailyOrders);
                    int totalRiders = dayShifts.Select(s => s.RiderId).Distinct().Count();

                    return new DailyOrderStats(
                        Date: date,
                        DateLabel: date.ToString("MM/dd"),
                        TotalOrders: totalOrders,
                        TotalShifts: dayShifts.Count,
                        TotalRiders: totalRiders,
                        TotalRejected: dayShifts.Sum(s => s.RejectedDailyOrders),
                        AvgOrdersPerRider: totalRiders > 0 ? Math.Round((double)totalOrders / totalRiders, 1) : 0
                    );
                });

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<DailyOrderStats>>(
                new Error("DashboardError", $"Failed to load daily orders: {ex.Message}", 500));
        }
    }

    // ── Top Riders ────────────────────────────────────────────────────────────

    public async Task<Result<IEnumerable<TopRiderStats>>> GetTopRidersAsync(int year, int month, int? companyId = null, int top = 10)
    {
        try
        {
            var from = new DateOnly(year, month, 1);
            var to = from.AddMonths(1).AddDays(-1);

            var query = dbcontext.RiderShifts
                .Where(s => s.ShiftDate >= from && s.ShiftDate <= to);

            if (companyId.HasValue)
                query = query.Where(s => s.CompanyId == companyId.Value);

            var riderStats = await query
                .GroupBy(s => new { s.RiderId, s.WorkingId, s.CompanyId, s.Company.Name })
                .Select(g => new
                {
                    g.Key.RiderId,
                    g.Key.WorkingId,
                    g.Key.CompanyId,
                    CompanyName = g.Key.Name,
                    TotalOrders = g.Sum(s => s.AcceptedDailyOrders),
                    TotalShifts = g.Count(),
                    TotalHours = g.Sum(s => (double)s.WorkingHours),
                    TotalRejected = g.Sum(s => s.RejectedDailyOrders),
                })
                .OrderByDescending(g => g.TotalOrders)
                .Take(top)
                .ToListAsync();

            // Load rider details
            var riderIds = riderStats.Select(r => r.RiderId).ToList();

            var riderDetails = await dbcontext.RiderDetails
                .Include(rd => rd.Employee)
                    .ThenInclude(e => e.Housing)
                .Where(rd => riderIds.Contains(rd.Id))
                .ToDictionaryAsync(rd => rd.Id, rd => rd);

            var result = riderStats
                .Select((r, idx) =>
                {
                    var detail = riderDetails.GetValueOrDefault(r.RiderId);
                    return new TopRiderStats(
                        Rank: idx + 1,
                        IqamaNo: detail?.EmployeeIqamaNo ?? 0,
                        WorkingId: r.WorkingId,
                        NameAR: detail?.Employee.NameAR ?? "N/A",
                        NameEN: detail?.Employee.NameEN ?? "N/A",
                        CompanyName: r.CompanyName,
                        HousingName: detail?.Employee.Housing?.Name,
                        TotalOrders: r.TotalOrders,
                        TotalShifts: r.TotalShifts,
                        AvgOrdersPerShift: r.TotalShifts > 0 ? Math.Round((double)r.TotalOrders / r.TotalShifts, 1) : 0,
                        TotalHours: Math.Round(r.TotalHours, 1),
                        TotalRejected: r.TotalRejected,
                        Month: month,
                        Year: year
                    );
                });

            return Result.Success(result.AsEnumerable());
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<TopRiderStats>>(
                new Error("DashboardError", $"Failed to load top riders: {ex.Message}", 500));
        }
    }

    // ── Vehicle Stats ─────────────────────────────────────────────────────────

    public async Task<Result<VehicleStatusStats>> GetVehicleStatsAsync()
    {
        try
        {
            int total = await dbcontext.Vehicles.CountAsync();

            var activeStatuses = await dbcontext.RiderVehicleStatus
                .Where(s => s.IsActive)
                .Select(s => new { s.VehicleNumber, s.StatusType })
                .ToListAsync();

            var vehicleNumbers = activeStatuses.GroupBy(s => s.VehicleNumber)
                .ToDictionary(g => g.Key, g => g.First().StatusType);

            int taken = vehicleNumbers.Count(v => v.Value == VehicleStatusType.Taken);
            int problem = vehicleNumbers.Count(v => v.Value == VehicleStatusType.Problem);
            int stolen = vehicleNumbers.Count(v => v.Value == VehicleStatusType.Stolen);
            int breakUp = vehicleNumbers.Count(v => v.Value == VehicleStatusType.BreakUp);
            int available = total - taken - problem - stolen - breakUp;

            // By vehicle type
            var byType = await dbcontext.Vehicles
                .GroupBy(v => v.VehicleType)
                .Select(g => new
                {
                    VehicleType = g.Key,
                    Count = g.Count(),
                    VehicleNumbers = g.Select(v => v.VehicleNumber).ToList()
                })
                .ToListAsync();

            var typeBreakdowns = byType.Select(t => new VehicleTypeBreakdown(
                VehicleType: t.VehicleType,
                Count: t.Count,
                Available: t.VehicleNumbers.Count(vn => !vehicleNumbers.ContainsKey(vn) ||
                    (vehicleNumbers.ContainsKey(vn) &&
                     vehicleNumbers[vn] != VehicleStatusType.Taken &&
                     vehicleNumbers[vn] != VehicleStatusType.Problem &&
                     vehicleNumbers[vn] != VehicleStatusType.Stolen &&
                     vehicleNumbers[vn] != VehicleStatusType.BreakUp)),
                Taken: t.VehicleNumbers.Count(vn => vehicleNumbers.ContainsKey(vn) && vehicleNumbers[vn] == VehicleStatusType.Taken)
            )).ToList();

            return Result.Success(new VehicleStatusStats(
                Total: total,
                Available: available,
                Taken: taken,
                Problem: problem,
                Stolen: stolen,
                BreakUp: breakUp,
                ByType: typeBreakdowns
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<VehicleStatusStats>(
                new Error("DashboardError", $"Failed to load vehicle stats: {ex.Message}", 500));
        }
    }

    // ── Housing ───────────────────────────────────────────────────────────────

    public async Task<Result<IEnumerable<HousingOccupancyStats>>> GetHousingStatsAsync()
    {
        try
        {
            var housings = await dbcontext.Housings
                .Include(h => h.Employees.Where(e => !e.IsDeleted))
                .AsNoTracking()
                .ToListAsync();

            var result = housings.Select(h => new HousingOccupancyStats(
                HousingId: h.Id,
                HousingName: h.Name,
                Address: h.Address,
                Capacity: h.Capacity,
                OccupiedCount: h.Employees.Count,
                OccupancyRate: h.Capacity > 0 ? Math.Round((double)h.Employees.Count / h.Capacity * 100, 1) : 0
            )).OrderByDescending(h => h.OccupancyRate);

            return Result.Success(result.AsEnumerable());
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<HousingOccupancyStats>>(
                new Error("DashboardError", $"Failed to load housing stats: {ex.Message}", 500));
        }
    }

    // ── Iqama Expiry ──────────────────────────────────────────────────────────

    public async Task<Result<IqamaExpiryStats>> GetIqamaExpiryStatsAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

            var expiries = await dbcontext.Employees
                .Where(e => !e.IsDeleted && e.Status.ToLower() != "fleeing")
                .Select(e => e.IqamaEndM)
                .ToListAsync();

            int expired = expiries.Count(d => d.DayNumber - today.DayNumber <= 0);
            int critical = expiries.Count(d => { int days = d.DayNumber - today.DayNumber; return days > 0 && days <= 30; });
            int warning = expiries.Count(d => { int days = d.DayNumber - today.DayNumber; return days > 30 && days <= 90; });
            int upcoming = expiries.Count(d => { int days = d.DayNumber - today.DayNumber; return days > 90 && days <= 180; });
            int safe = expiries.Count(d => d.DayNumber - today.DayNumber > 180);

            return Result.Success(new IqamaExpiryStats(expired, critical, warning, upcoming, safe));
        }
        catch (Exception ex)
        {
            return Result.Failure<IqamaExpiryStats>(
                new Error("DashboardError", $"Failed to load Iqama stats: {ex.Message}", 500));
        }
    }

    // ── Employee Status ───────────────────────────────────────────────────────

    public async Task<Result<EmployeeStatusStats>> GetEmployeeStatusStatsAsync()
    {
        try
        {
            var allPeople = await dbcontext.Employees
                .Where(e => !e.IsDeleted)
                .Select(e => new { e.Status, e.IsEmployee })
                .ToListAsync();

            int CountStatus(string status, bool isEmployee) =>
                allPeople.Count(e => e.Status.ToLower() == status && e.IsEmployee == isEmployee);

            return Result.Success(new EmployeeStatusStats(
                Enable: allPeople.Count(e => e.Status.ToLower() == "enable"),
                Disable: allPeople.Count(e => e.Status.ToLower() == "disable"),
                Fleeing: allPeople.Count(e => e.Status.ToLower() == "fleeing"),
                Vacation: allPeople.Count(e => e.Status.ToLower() == "vacation"),
                Accident: allPeople.Count(e => e.Status.ToLower() == "accident"),
                Sick: allPeople.Count(e => e.Status.ToLower() == "sick"),
                TotalEmployees: allPeople.Count(e => e.IsEmployee),
                TotalRiders: allPeople.Count(e => !e.IsEmployee)
            ));
        }
        catch (Exception ex)
        {
            return Result.Failure<EmployeeStatusStats>(
                new Error("DashboardError", $"Failed to load status stats: {ex.Message}", 500));
        }
    }

    // ── Monthly Validity ──────────────────────────────────────────────────────

    public async Task<Result<IEnumerable<MonthlyValidityStats>>> GetMonthlyValidityStatsAsync(int year, int month)
    {
        try
        {
            var data = await dbcontext.Set<RiderMonthlyValidity>()
                .Where(v => v.Year == year && v.Month == month)
                .Include(v => v.Employee)
                    .ThenInclude(e => e.RiderDetails)
                        .ThenInclude(rd => rd.Company)
                .ToListAsync();

            var grouped = data
                .Where(v => v.Employee?.RiderDetails != null)
                .GroupBy(v => new { v.Employee.RiderDetails.CompanyId, v.Employee.RiderDetails.Company.Name })
                .Select(g => new MonthlyValidityStats(
                    CompanyId: g.Key.CompanyId,
                    CompanyName: g.Key.Name,
                    Valid: g.Count(v => v.Status == ValidityStatus.Valid),
                    Invalid: g.Count(v => v.Status == ValidityStatus.Invalid),
                    Freelancer: g.Count(v => v.Status == ValidityStatus.Freelancer),
                    Total: g.Count(),
                    Year: year,
                    Month: month
                ));

            return Result.Success(grouped.AsEnumerable());
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<MonthlyValidityStats>>(
                new Error("DashboardError", $"Failed to load validity stats: {ex.Message}", 500));
        }
    }

    // ── Rider Orders Matrix (all months in a year per company) ────────────────

    public async Task<Result<IEnumerable<RiderOrdersByCompanyAndMonth>>> GetRiderOrdersMatrixAsync(int year)
    {
        try
        {
            var shifts = await dbcontext.RiderShifts
                .Where(s => s.ShiftDate.Year == year)
                .Select(s => new { s.ShiftDate.Month, s.CompanyId, s.Company.Name, s.AcceptedDailyOrders, s.RiderId })
                .ToListAsync();

            var companies = shifts.GroupBy(s => new { s.CompanyId, s.Name });

            var result = companies.Select(company =>
            {
                var monthlyData = Enumerable.Range(1, 12).Select(m =>
                {
                    var monthShifts = company.Where(s => s.Month == m).ToList();
                    return new MonthOrderPoint(
                        Month: m,
                        MonthLabel: new DateTime(year, m, 1).ToString("MMM"),
                        TotalOrders: monthShifts.Sum(s => s.AcceptedDailyOrders),
                        TotalRiders: monthShifts.Select(s => s.RiderId).Distinct().Count()
                    );
                }).ToList();

                return new RiderOrdersByCompanyAndMonth(
                    CompanyId: company.Key.CompanyId,
                    CompanyName: company.Key.Name,
                    MonthlyData: monthlyData
                );
            });

            return Result.Success(result.AsEnumerable());
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<RiderOrdersByCompanyAndMonth>>(
                new Error("DashboardError", $"Failed to load orders matrix: {ex.Message}", 500));
        }
    }

    // ── Country Distribution ──────────────────────────────────────────────────

    public async Task<Result<IEnumerable<CountryDistributionStats>>> GetCountryDistributionAsync()
    {
        try
        {
            var data = await dbcontext.Employees
                .Where(e => !e.IsDeleted)
                .GroupBy(e => new { e.Country, e.IsEmployee })
                .Select(g => new { g.Key.Country, g.Key.IsEmployee, Count = g.Count() })
                .ToListAsync();

            var result = data
                .GroupBy(d => d.Country)
                .Select(g => new CountryDistributionStats(
                    Country: g.Key,
                    EmployeeCount: g.FirstOrDefault(d => d.IsEmployee)?.Count ?? 0,
                    RiderCount: g.FirstOrDefault(d => !d.IsEmployee)?.Count ?? 0,
                    Total: g.Sum(d => d.Count)
                ))
                .OrderByDescending(d => d.Total);

            return Result.Success(result.AsEnumerable());
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<CountryDistributionStats>>(
                new Error("DashboardError", $"Failed to load country stats: {ex.Message}", 500));
        }
    }

    // ── Sponsor Stats ─────────────────────────────────────────────────────────

    public async Task<Result<IEnumerable<SponsorStats>>> GetSponsorStatsAsync()
    {
        try
        {
            var data = await dbcontext.Employees
                .Where(e => !e.IsDeleted)
                .GroupBy(e => new { e.Sponsor, e.sponsorNo, e.IsEmployee })
                .Select(g => new { g.Key.Sponsor, g.Key.sponsorNo, g.Key.IsEmployee, Count = g.Count() })
                .ToListAsync();

            var result = data
                .GroupBy(d => new { d.Sponsor, d.sponsorNo })
                .Select(g => new SponsorStats(
                    Sponsor: g.Key.Sponsor,
                    SponsorNo: g.Key.sponsorNo,
                    EmployeeCount: g.FirstOrDefault(d => d.IsEmployee)?.Count ?? 0,
                    RiderCount: g.FirstOrDefault(d => !d.IsEmployee)?.Count ?? 0,
                    Total: g.Sum(d => d.Count)
                ))
                .OrderByDescending(d => d.Total);

            return Result.Success(result.AsEnumerable());
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<SponsorStats>>(
                new Error("DashboardError", $"Failed to load sponsor stats: {ex.Message}", 500));
        }
    }

    // ── Rider Count by Company ────────────────────────────────────────────────

    public async Task<Result<IEnumerable<CompanyRiderCountStats>>> GetRiderCountByCompanyAsync()
    {
        try
        {
            var data = await dbcontext.RiderDetails
                .Include(rd => rd.Employee)
                .Include(rd => rd.Company)
                .Where(rd => !rd.Employee.IsDeleted)
                .GroupBy(rd => new { rd.CompanyId, rd.Company.Name })
                .Select(g => new
                {
                    g.Key.CompanyId,
                    CompanyName = g.Key.Name,
                    Total = g.Count(),
                    Active = g.Count(rd => rd.Employee.Status.ToLower() == "enable"),
                })
                .OrderByDescending(g => g.Total)
                .ToListAsync();

            var result = data.Select(d => new CompanyRiderCountStats(
                CompanyId: d.CompanyId,
                CompanyName: d.CompanyName,
                TotalRiders: d.Total,
                ActiveRiders: d.Active,
                InactiveRiders: d.Total - d.Active
            ));

            return Result.Success(result.AsEnumerable());
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<CompanyRiderCountStats>>(
                new Error("DashboardError", $"Failed to load company rider counts: {ex.Message}", 500));
        }
    }
}