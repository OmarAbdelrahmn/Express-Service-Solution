using Application.Abstraction;
using Application.Service.Riders;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Application.Service.Reports;

public class ReportService(ApplicationDbcontext dbcontext) : IReportService
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;


    public async Task<Result<ComprehensiveDashboard>> GetComprehensiveDashboardAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Set default date range if not provided (last 30 days)
            var effectiveEndDate = endDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var effectiveStartDate = startDate ?? effectiveEndDate.AddDays(-30);

            if (effectiveEndDate < effectiveStartDate)
                return Result.Failure<ComprehensiveDashboard>(
                    new Error("End date must be after start date", "invalid_input", 400));

            // Fetch all data in parallel
            var companiesTask = GetCompaniesStatisticsAsync(effectiveStartDate, effectiveEndDate, cancellationToken);
            var ridersTask = GetRidersStatisticsAsync(effectiveStartDate, effectiveEndDate, cancellationToken);
            var shiftsTask = GetShiftsStatisticsAsync(effectiveStartDate, effectiveEndDate, cancellationToken);
            var ordersTask = GetOrdersStatisticsAsync(effectiveStartDate, effectiveEndDate, cancellationToken);
            var performanceTask = GetPerformanceMetricsAsync(effectiveStartDate, effectiveEndDate, cancellationToken);
            var housingTask = GetHousingStatisticsAsync(effectiveStartDate, effectiveEndDate, cancellationToken);
            var trendsTask = GetTrendsAnalysisAsync(effectiveStartDate, effectiveEndDate, cancellationToken);

            await Task.WhenAll(companiesTask, ridersTask, shiftsTask, ordersTask,
                             performanceTask, housingTask, trendsTask);

            var dashboard = new ComprehensiveDashboard(
                GeneratedAt: DateTime.UtcNow,
                PeriodStart: effectiveStartDate,
                PeriodEnd: effectiveEndDate,
                Companies: companiesTask.Result,
                Riders: ridersTask.Result,
                Shifts: shiftsTask.Result,
                Orders: ordersTask.Result,
                Performance: performanceTask.Result,
                Housing: housingTask.Result,
                Trends: trendsTask.Result
            );

            return Result.Success(dashboard);
        }
        catch (Exception ex)
        {
            return Result.Failure<ComprehensiveDashboard>(
                new Error($"Error generating dashboard: {ex.Message}", "server_error", 500));
        }
    }

    private async Task<CompaniesStatistics> GetCompaniesStatisticsAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var allCompanies = await _dbcontext.Companies
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Company)
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var companyDetails = allCompanies.Select(company =>
        {
            var companyShifts = shifts.Where(s => s.Company.Id == company.Id).ToList();
            var dailyTarget = CompanyShiftConfiguration.GetDailyOrderTarget(company.Name);
            var totalShifts = companyShifts.Count;
            var expectedOrders = totalShifts * dailyTarget;
            var acceptedOrders = companyShifts.Sum(s => s.AcceptedDailyOrders);

            var performanceScore = expectedOrders > 0
                ? (decimal)acceptedOrders / expectedOrders * 100
                : 0;

            return new CompanyDetail(
                CompanyId: company.Id,
                CompanyName: company.Name,
                DailyOrderTarget: dailyTarget,
                TotalShifts: totalShifts,
                ActiveRiders: companyShifts.Select(s => s.RiderId).Distinct().Count(),
                TotalAcceptedOrders: acceptedOrders,
                TotalRejectedOrders: companyShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: companyShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: companyShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: companyShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString()),
                PerformanceScore: performanceScore,
                TotalWorkingHours: companyShifts.Sum(s => s.WorkingHours)
            );
        }).OrderByDescending(c => c.PerformanceScore).ToList();

        var topPerformer = companyDetails.FirstOrDefault();
        var lowestPerformer = companyDetails.LastOrDefault();

        return new CompaniesStatistics(
            TotalCompanies: allCompanies.Count,
            ActiveCompanies: companyDetails.Count(c => c.TotalShifts > 0),
            CompanyDetails: companyDetails,
            TopPerformingCompany: topPerformer != null ? topPerformer.CompanyName : null,
            LowestPerformingCompany: lowestPerformer != null ? lowestPerformer.CompanyName : null,
            AverageCompanyPerformance: companyDetails.Any() ? companyDetails.Average(c => c.PerformanceScore) : 0
        );
    }

    private async Task<RidersStatistics> GetRidersStatisticsAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var allRiders = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var shifts = await _dbcontext.RiderShifts
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var activeRiderIds = shifts.Select(s => s.RiderId).Distinct().ToList();
        var activeRiders = allRiders.Where(r => activeRiderIds.Contains(r.Id)).ToList();

        var substitutions = await _dbcontext.Set<RiderShiftSubstitution>()
            .Where(s => s.IsActive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new RidersStatistics(
            TotalRiders: allRiders.Count,
            ActiveRiders: activeRiders.Count,
            InactiveRiders: allRiders.Count - activeRiders.Count,
            RidersWithWorkingId: allRiders.Count(r => r.WorkingId.HasValue && r.WorkingId > 0),
            RidersWithSubstitution: substitutions.Count,
            AverageShiftsPerRider: activeRiders.Any() ? (decimal)shifts.Count / activeRiders.Count : 0,
            TotalWorkingHours: shifts.Sum(s => s.WorkingHours)
        );
    }

    private async Task<ShiftsStatistics> GetShiftsStatisticsAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var shifts = await _dbcontext.RiderShifts
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var totalShifts = shifts.Count;
        var completedShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString());
        var incompleteShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString());
        var failedShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString());

        var dailyBreakdown = shifts
            .GroupBy(s => s.ShiftDate)
            .Select(g => new DailyShiftBreakdown(
                Date: g.Key,
                TotalShifts: g.Count(),
                CompletedShifts: g.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                TotalOrders: g.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                AcceptedOrders: g.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: g.Sum(s => s.RejectedDailyOrders)
            ))
            .OrderBy(d => d.Date)
            .ToList();

        return new ShiftsStatistics(
            TotalShifts: totalShifts,
            CompletedShifts: completedShifts,
            IncompleteShifts: incompleteShifts,
            FailedShifts: failedShifts,
            CompletionRate: totalShifts > 0 ? (decimal)completedShifts / totalShifts * 100 : 0,
            AverageWorkingHoursPerShift: totalShifts > 0 ? shifts.Sum(s => s.WorkingHours) / totalShifts : 0,
            TotalWorkingHours: shifts.Sum(s => s.WorkingHours),
            DailyBreakdown: dailyBreakdown
        );
    }

    private async Task<OrdersStatistics> GetOrdersStatisticsAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var shifts = await _dbcontext.RiderShifts
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var totalAccepted = shifts.Sum(s => s.AcceptedDailyOrders);
        var totalRejected = shifts.Sum(s => s.RejectedDailyOrders);
        var totalRealRejected = shifts.Sum(s => s.RealRejectedDailyOrders);
        var totalOrders = totalAccepted + totalRejected;

        var acceptanceRate = totalOrders > 0 ? (decimal)totalAccepted / totalOrders * 100 : 0;
        var rejectionRate = totalOrders > 0 ? (decimal)totalRejected / totalOrders * 100 : 0;

        var avgOrdersPerShift = shifts.Count > 0 ? (decimal)totalAccepted / shifts.Count : 0;

        var problematicShifts = shifts.Count(s =>
            s.RealRejectedDailyOrders > CompanyShiftConfiguration.RejectionThreshold);

        return new OrdersStatistics(
            TotalOrders: totalOrders,
            TotalAcceptedOrders: totalAccepted,
            TotalRejectedOrders: totalRejected,
            TotalRealRejectedOrders: totalRealRejected,
            AcceptanceRate: acceptanceRate,
            RejectionRate: rejectionRate,
            AverageOrdersPerShift: avgOrdersPerShift,
            ProblematicShiftsCount: problematicShifts,
            TotalPenaltyAmount: shifts.Sum(s => CalculatePenalty(s))
        );
    }

    private async Task<PerformanceMetrics> GetPerformanceMetricsAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Company)
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Calculate overall performance score
        var companyGroups = shifts.GroupBy(s => s.Company.Name);
        var companyScores = new List<decimal>();

        foreach (var group in companyGroups)
        {
            var companyShifts = group.ToList();
            var target = CompanyShiftConfiguration.GetDailyOrderTarget(group.Key);
            var expected = companyShifts.Count * target;
            var actual = companyShifts.Sum(s => s.AcceptedDailyOrders);

            if (expected > 0)
            {
                companyScores.Add((decimal)actual / expected * 100);
            }
        }

        var overallScore = companyScores.Any() ? companyScores.Average() : 0;

        // Top performers
        var riderPerformances = shifts
            .GroupBy(s => s.RiderId)
            .Select(g =>
            {
                var riderShifts = g.ToList();
                var rider = riderShifts.First().Rider;
                var companyName = riderShifts.First().Company?.Name ?? "Unknown";
                var target = CompanyShiftConfiguration.GetDailyOrderTarget(companyName);
                var expected = riderShifts.Count * target;
                var actual = riderShifts.Sum(s => s.AcceptedDailyOrders);

                return new TopPerformer(
                    RiderId: g.Key,
                    RiderName: rider?.Employee.NameAR ?? "Unknown",
                    WorkingId: riderShifts.First().WorkingId,
                    TotalOrders: actual,
                    PerformanceScore: expected > 0 ? (decimal)actual / expected * 100 : 0,
                    CompletionRate: CalculateCompletionRate(riderShifts)
                );
            })
            .OrderByDescending(p => p.PerformanceScore)
            .Take(10)
            .ToList();

        return new PerformanceMetrics(
            OverallPerformanceScore: overallScore,
            TopPerformers: riderPerformances,
            AverageCompletionRate: shifts.Any()
                ? (decimal)shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()) / shifts.Count * 100
                : 0,
            AverageOrdersPerDay: CalculateAverageOrdersPerDay(shifts, startDate, endDate)
        );
    }

    private async Task<HousingStatistics> GetHousingStatisticsAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var allHousings = await _dbcontext.Housings
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
                .ThenInclude(e => e.Housing)
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var validShifts = shifts
            .Where(s => s.Rider?.Employee?.Housing != null)
            .ToList();

        var housingGroups = validShifts.GroupBy(s => s.Rider.Employee.HousingId);

        var housingDetails = housingGroups.Select(g =>
        {
            var housing = g.First().Rider.Employee.Housing;
            var housingShifts = g.ToList();
            var totalOrders = housingShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders);
            var accepted = housingShifts.Sum(s => s.AcceptedDailyOrders);

            return new HousingDetail(
                HousingId: housing.Id,
                HousingName: housing.Name,
                TotalRiders: housingShifts.Select(s => s.RiderId).Distinct().Count(),
                TotalShifts: housingShifts.Count,
                TotalOrders: totalOrders,
                AcceptedOrders: accepted,
                CompletionRate: totalOrders > 0 ? (decimal)accepted / totalOrders * 100 : 0
            );
        }).OrderByDescending(h => h.CompletionRate).ToList();

        return new HousingStatistics(
            TotalHousings: allHousings.Count,
            ActiveHousings: housingDetails.Count,
            HousingDetails: housingDetails,
            TopPerformingHousing: housingDetails.FirstOrDefault()?.HousingName,
            AverageRidersPerHousing: housingDetails.Any() ? housingDetails.Average(h => h.TotalRiders) : 0
        );
    }

    private async Task<TrendsAnalysis> GetTrendsAnalysisAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var shifts = await _dbcontext.RiderShifts
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Weekly trends
        var weeklyData = shifts
            .GroupBy(s => GetWeekNumber(s.ShiftDate))
            .Select(g => new WeeklyTrend(
                WeekNumber: g.Key,
                TotalShifts: g.Count(),
                TotalOrders: g.Sum(s => s.AcceptedDailyOrders),
                AveragePerformance: CalculateWeeklyPerformance(g.ToList())
            ))
            .OrderBy(w => w.WeekNumber)
            .ToList();

        // Growth metrics
        var firstWeek = weeklyData.FirstOrDefault();
        var lastWeek = weeklyData.LastOrDefault();

        var ordersGrowth = firstWeek != null && lastWeek != null && firstWeek.TotalOrders > 0
            ? ((decimal)(lastWeek.TotalOrders - firstWeek.TotalOrders) / firstWeek.TotalOrders) * 100
            : 0;

        var shiftsGrowth = firstWeek != null && lastWeek != null && firstWeek.TotalShifts > 0
            ? ((decimal)(lastWeek.TotalShifts - firstWeek.TotalShifts) / firstWeek.TotalShifts) * 100
            : 0;

        return new TrendsAnalysis(
            WeeklyTrends: weeklyData,
            OrdersGrowthRate: ordersGrowth,
            ShiftsGrowthRate: shiftsGrowth,
            PerformanceTrend: CalculatePerformanceTrend(weeklyData)
        );
    }

    private decimal CalculateCompletionRate(List<RiderShift> shifts)
    {
        var total = shifts.Count;
        var completed = shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString());
        return total > 0 ? (decimal)completed / total * 100 : 0;
    }

    private decimal CalculateAverageOrdersPerDay(List<RiderShift> shifts, DateOnly start, DateOnly end)
    {
        var totalDays = (end.ToDateTime(TimeOnly.MinValue) - start.ToDateTime(TimeOnly.MinValue)).Days + 1;
        var totalOrders = shifts.Sum(s => s.AcceptedDailyOrders);
        return totalDays > 0 ? (decimal)totalOrders / totalDays : 0;
    }

    private int GetWeekNumber(DateOnly date)
    {
        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        var day = (int)dateTime.DayOfWeek;
        return (dateTime.DayOfYear - day + 10) / 7;
    }

    private decimal CalculateWeeklyPerformance(List<RiderShift> shifts)
    {
        if (!shifts.Any()) return 0;

        var companyGroups = shifts.GroupBy(s => s.Company?.Name ?? "Unknown");
        var scores = new List<decimal>();

        foreach (var group in companyGroups)
        {
            var target = CompanyShiftConfiguration.GetDailyOrderTarget(group.Key);
            var expected = group.Count() * target;
            var actual = group.Sum(s => s.AcceptedDailyOrders);

            if (expected > 0)
            {
                scores.Add((decimal)actual / expected * 100);
            }
        }

        return scores.Any() ? scores.Average() : 0;
    }

    private string CalculatePerformanceTrend(List<WeeklyTrend> weeklyData)
    {
        if (weeklyData.Count < 2) return "Stable";

        var firstHalf = weeklyData.Take(weeklyData.Count / 2).Average(w => w.AveragePerformance);
        var secondHalf = weeklyData.Skip(weeklyData.Count / 2).Average(w => w.AveragePerformance);

        var difference = secondHalf - firstHalf;

        if (difference > 5) return "Improving";
        if (difference < -5) return "Declining";
        return "Stable";
    }




    public async Task<Result<MonthlyRiderReport>> GetMonthlyReportByWorkingIdAsync(
        int workingId,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (workingId <= 0)
            return Result.Failure<MonthlyRiderReport>(
                new Error("Invalid working ID", "invalid_input", 400));

        if (month < 1 || month > 12)
            return Result.Failure<MonthlyRiderReport>(
                new Error("Month must be between 1 and 12", "invalid_input", 400));

        var rider = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.WorkingId == workingId, cancellationToken);

        if (rider == null)
            return Result.Failure<MonthlyRiderReport>(
                new Error($"Rider with WorkingId {workingId} not found", "not_found", 404));

        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Company)
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => s.RiderId == rider.Id &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .OrderBy(s => s.ShiftDate)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
        {
            return Result.Success(CreateEmptyMonthlyReport(
                rider.Id, rider.Employee.NameAR, workingId, year, month));
        }

        var workingIdHistory = DetectWorkingIdChanges(shifts);
        var totalWorkingDays = shifts.Count;
        var completedShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString());
        var incompleteShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString());
        var failedShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString());
        var totalAccepted = shifts.Sum(s => s.AcceptedDailyOrders);
        var totalRejected = shifts.Sum(s => s.RejectedDailyOrders);
        var totalRealRejected = shifts.Sum(s => s.RealRejectedDailyOrders);
        var totalHours = shifts.Sum(s => s.WorkingHours);
        var totalPenalty = shifts.Sum(s => CalculatePenalty(s));

        var companyBreakdowns = CalculateCompanyBreakdowns(shifts);

        var overallPerformanceScore = companyBreakdowns.Any()
            ? companyBreakdowns.Sum(cb => cb.PerformanceScore * cb.WorkingDays) / totalWorkingDays
            : 0;

        var problematicShifts = shifts
            .Where(s => HasRejectionProblem(s) || s.ShiftStatus != ShiftStatus.Completed.ToString())
            .Select(CreateProblemShiftDetail)
            .ToList();

        var report = new MonthlyRiderReport(
            RiderId: rider.Id,
            RiderName: rider.Employee.NameAR,
            WorkingId: workingId,
            Year: year,
            Month: month,
            TotalWorkingDays: totalWorkingDays,
            CompletedShifts: completedShifts,
            IncompleteShifts: incompleteShifts,
            FailedShifts: failedShifts,
            TotalAcceptedOrders: totalAccepted,
            TotalRejectedOrders: totalRejected,
            TotalRealRejectedOrders: totalRealRejected,
            TotalWorkingHours: totalHours,
            ProblematicShiftsCount: problematicShifts.Count,
            TotalPenaltyAmount: totalPenalty,
            OverallPerformanceScore: overallPerformanceScore,
            CompanyBreakdowns: companyBreakdowns,
            ProblematicShifts: problematicShifts,
            WorkingIdHistory: workingIdHistory
        );

        return Result.Success(report);
    }

    public async Task<Result<IEnumerable<MonthlyRiderReport>>> GetAllRidersMonthlyReportAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (month < 1 || month > 12)
            return Result.Failure<IEnumerable<MonthlyRiderReport>>(
                new Error("Month must be between 1 and 12", "invalid_input", 400));

        var allRiders = await _dbcontext.RiderDetails
            .Where(r => r.WorkingId.HasValue && r.WorkingId > 0)
            .ToListAsync(cancellationToken);

        var reports = new List<MonthlyRiderReport>();

        foreach (var rider in allRiders)
        {
            var result = await GetMonthlyReportByWorkingIdAsync(
                rider.WorkingId!.Value, year, month, cancellationToken);

            if (result.IsSuccess)
            {
                reports.Add(result.Value);
            }
        }

        return Result.Success<IEnumerable<MonthlyRiderReport>>(reports);
    }

    public async Task<Result<YearlyRiderReport>> GetYearlyReportByWorkingIdAsync(
        int workingId,
        int year,
        CancellationToken cancellationToken = default)
    {
        if (workingId <= 0)
            return Result.Failure<YearlyRiderReport>(
                new Error("Invalid working ID", "invalid_input", 400));

        var rider = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.WorkingId == workingId, cancellationToken);

        if (rider == null)
            return Result.Failure<YearlyRiderReport>(
                new Error($"Rider with WorkingId {workingId} not found", "not_found", 404));

        var startDate = new DateOnly(year, 1, 1);
        var endDate = new DateOnly(year, 12, 31);

        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Company)
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => s.RiderId == rider.Id &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .OrderBy(s => s.ShiftDate)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
        {
            return Result.Success(CreateEmptyYearlyReport(
                rider.Id, rider.Employee.NameAR, workingId, year));
        }

        var workingIdHistory = DetectWorkingIdChanges(shifts);
        var totalWorkingDays = shifts.Count;
        var completedShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString());
        var incompleteShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString());
        var failedShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString());
        var totalPenalty = shifts.Sum(s => CalculatePenalty(s));
        var problematicCount = shifts.Count(s => HasRejectionProblem(s));

        var yearlyCompanyBreakdowns = CalculateYearlyCompanyBreakdowns(shifts);
        var monthlyBreakdowns = CalculateMonthlyBreakdowns(shifts);

        var avgPerformanceScore = monthlyBreakdowns.Any()
            ? monthlyBreakdowns.Average(mb => mb.PerformanceScore)
            : 0;

        var report = new YearlyRiderReport(
            RiderId: rider.Id,
            RiderName: rider.Employee.NameAR,
            WorkingId: workingId,
            Year: year,
            TotalWorkingDays: totalWorkingDays,
            CompletedShifts: completedShifts,
            IncompleteShifts: incompleteShifts,
            FailedShifts: failedShifts,
            TotalAcceptedOrders: shifts.Sum(s => s.AcceptedDailyOrders),
            TotalRejectedOrders: shifts.Sum(s => s.RejectedDailyOrders),
            TotalRealRejectedOrders: shifts.Sum(s => s.RealRejectedDailyOrders),
            TotalWorkingHours: shifts.Sum(s => s.WorkingHours),
            ProblematicShiftsCount: problematicCount,
            TotalPenaltyAmount: totalPenalty,
            AveragePerformanceScore: avgPerformanceScore,
            YearlyCompanyBreakdowns: yearlyCompanyBreakdowns,
            MonthlyBreakdowns: monthlyBreakdowns,
            WorkingIdHistory: workingIdHistory
        );

        return Result.Success(report);
    }

    public async Task<Result<IEnumerable<YearlyRiderReport>>> GetAllRidersYearlyReportAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        var allRiders = await _dbcontext.RiderDetails
            .Where(r => r.WorkingId.HasValue && r.WorkingId > 0)
            .ToListAsync(cancellationToken);

        var reports = new List<YearlyRiderReport>();

        foreach (var rider in allRiders)
        {
            var result = await GetYearlyReportByWorkingIdAsync(
                rider.WorkingId!.Value, year, cancellationToken);

            if (result.IsSuccess)
            {
                reports.Add(result.Value);
            }
        }

        return Result.Success<IEnumerable<YearlyRiderReport>>(reports);
    }


    public async Task<Result<DateRangeReport>> GetCustomDateRangeReportByWorkingIdAsync(
        int workingId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (workingId <= 0)
            return Result.Failure<DateRangeReport>(
                new Error("Invalid working ID", "invalid_input", 400));

        if (endDate < startDate)
            return Result.Failure<DateRangeReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        var rider = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.WorkingId == workingId, cancellationToken);

        if (rider == null)
            return Result.Failure<DateRangeReport>(
                new Error($"Rider with WorkingId {workingId} not found", "not_found", 404));

        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Company)
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => s.RiderId == rider.Id &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .OrderBy(s => s.ShiftDate)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
        {
            return Result.Success(CreateEmptyDateRangeReport(
                rider.Id, rider.Employee.NameAR, workingId, startDate, endDate));
        }

        var workingIdHistory = DetectWorkingIdChanges(shifts);
        var totalWorkingDays = shifts.Count;
        var totalPenalty = shifts.Sum(s => CalculatePenalty(s));

        var companyBreakdowns = CalculateCompanyBreakdowns(shifts);
        var overallPerformanceScore = companyBreakdowns.Any()
            ? companyBreakdowns.Sum(cb => cb.PerformanceScore * cb.WorkingDays) / totalWorkingDays
            : 0;

        var problematicShifts = shifts
            .Where(s => HasRejectionProblem(s) || s.ShiftStatus != ShiftStatus.Completed.ToString())
            .Select(CreateProblemShiftDetail)
            .ToList();

        var report = new DateRangeReport(
            RiderId: rider.Id,
            RiderName: rider.Employee.NameAR,
            WorkingId: workingId,
            StartDate: startDate,
            EndDate: endDate,
            TotalWorkingDays: totalWorkingDays,
            CompletedShifts: shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
            IncompleteShifts: shifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
            FailedShifts: shifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString()),
            TotalAcceptedOrders: shifts.Sum(s => s.AcceptedDailyOrders),
            TotalRejectedOrders: shifts.Sum(s => s.RejectedDailyOrders),
            TotalRealRejectedOrders: shifts.Sum(s => s.RealRejectedDailyOrders),
            TotalWorkingHours: shifts.Sum(s => s.WorkingHours),
            ProblematicShiftsCount: problematicShifts.Count,
            TotalPenaltyAmount: totalPenalty,
            OverallPerformanceScore: overallPerformanceScore,
            CompanyBreakdowns: companyBreakdowns,
            ProblematicShifts: problematicShifts,
            WorkingIdHistory: workingIdHistory
        );

        return Result.Success(report);
    }

    public async Task<Result<IEnumerable<DateRangeReport>>> GetAllRidersCustomDateRangeReportAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<IEnumerable<DateRangeReport>>(
                new Error("End date must be after start date", "invalid_input", 400));

        var allRiders = await _dbcontext.RiderDetails
            .Where(r => r.WorkingId.HasValue && r.WorkingId > 0)
            .ToListAsync(cancellationToken);

        var reports = new List<DateRangeReport>();

        foreach (var rider in allRiders)
        {
            var result = await GetCustomDateRangeReportByWorkingIdAsync(
                rider.WorkingId!.Value, startDate, endDate, cancellationToken);

            if (result.IsSuccess)
            {
                reports.Add(result.Value);
            }
        }

        return Result.Success<IEnumerable<DateRangeReport>>(reports);
    }


    public async Task<Result<CompanyPerformanceReport>> GetCompanyPerformanceReportAsync(
        string companyName,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyName))
            return Result.Failure<CompanyPerformanceReport>(
                new Error("Company name is required", "invalid_input", 400));

        if (endDate < startDate)
            return Result.Failure<CompanyPerformanceReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        var company = await _dbcontext.Companies
            .FirstOrDefaultAsync(c => c.Name == companyName, cancellationToken);

        if (company == null)
            return Result.Failure<CompanyPerformanceReport>(
                new Error($"Company '{companyName}' not found", "not_found", 404));

        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => s.Company.Name == companyName &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
            return Result.Failure<CompanyPerformanceReport>(
                new Error($"No shifts found for company '{companyName}' in the specified period", "no_data", 404));

        var dailyTarget = CompanyShiftConfiguration.GetDailyOrderTarget(companyName);
        var totalWorkingDays = shifts.Count;
        var expectedOrders = totalWorkingDays * dailyTarget;
        var totalAccepted = shifts.Sum(s => s.AcceptedDailyOrders);
        var performanceScore = expectedOrders > 0
            ? (decimal)totalAccepted / expectedOrders * 100
            : 0;

        var riderPerformances = shifts
            .GroupBy(s => s.RiderId)
            .Select(g => new RiderCompanyPerformance(
                RiderId: g.Key,
                RiderName: g.First().Rider?.Employee.NameAR ?? "Unknown",
                WorkingId: g.First().WorkingId,
                TotalShifts: g.Count(),
                CompletedShifts: g.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                TotalAcceptedOrders: g.Sum(s => s.AcceptedDailyOrders),
                TotalRejectedOrders: g.Sum(s => s.RejectedDailyOrders),
                PerformanceScore: CalculateRiderPerformanceScore(g.ToList(), dailyTarget)
            ))
            .OrderByDescending(r => r.PerformanceScore)
            .ToList();

        var report = new CompanyPerformanceReport(
            CompanyName: companyName,
            StartDate: startDate,
            EndDate: endDate,
            DailyOrderTarget: dailyTarget,
            TotalWorkingDays: totalWorkingDays,
            ExpectedOrders: expectedOrders,
            TotalAcceptedOrders: totalAccepted,
            TotalRejectedOrders: shifts.Sum(s => s.RejectedDailyOrders),
            CompletedShifts: shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
            IncompleteShifts: shifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
            FailedShifts: shifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString()),
            OverallPerformanceScore: performanceScore,
            TotalPenaltyAmount: shifts.Sum(s => CalculatePenalty(s)),
            RiderPerformances: riderPerformances
        );

        return Result.Success(report);
    }

    public async Task<Result<IEnumerable<ProblemShiftDetail>>> GetProblematicShiftsAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<IEnumerable<ProblemShiftDetail>>(
                new Error("End date must be after start date", "invalid_input", 400));

        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Include(s => s.Company)
            .Where(s => s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate &&
                       (s.ShiftStatus != ShiftStatus.Completed.ToString() ||
                        s.RealRejectedDailyOrders > CompanyShiftConfiguration.RejectionThreshold))
            .OrderByDescending(s => s.RealRejectedDailyOrders)
            .ThenBy(s => s.ShiftDate)
            .ToListAsync(cancellationToken);

        var problematicShifts = shifts
            .Select(CreateProblemShiftDetail)
            .ToList();

        return Result.Success<IEnumerable<ProblemShiftDetail>>(problematicShifts);
    }


    public Task<Result<RiderPeriodComparison>> CompareRiderPeriodsAsync(
        int workingId,
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Period comparison feature is not yet implemented");
    }

    public Task<Result<IEnumerable<RiderPeriodComparison>>> CompareAllRidersPeriodsAsync(
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Period comparison feature is not yet implemented");
    }

    public Task<Result<CompanyPeriodComparison>> CompareCompanyPeriodsAsync(
        string companyName,
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Period comparison feature is not yet implemented");
    }

    public Task<Result<RiderPeriodComparison>> CompareRiderMonthsAsync(
        int workingId,
        int year1,
        int month1,
        int year2,
        int month2,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Period comparison feature is not yet implemented");
    }

    public Task<Result<RiderPeriodComparison>> CompareRiderYearsAsync(
        int workingId,
        int year1,
        int year2,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Period comparison feature is not yet implemented");
    }


    public async Task<Result<List<HousingPeriodComparison>>> CompareHousingPeriodsAsync(
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        // Validate dates
        if (period1End < period1Start)
            return Result.Failure<List<HousingPeriodComparison>>(
                new Error("Period 1: End date must be after start date", "invalid_input", 400));

        if (period2End < period2Start)
            return Result.Failure<List<HousingPeriodComparison>>(
                new Error("Period 2: End date must be after start date", "invalid_input", 400));

        // Get analysis for both periods
        var period1Result = await GetHousingAnalysisForPeriodAsync(
            period1Start, period1End, cancellationToken);

        if (!period1Result.IsSuccess)
            return Result.Failure<List<HousingPeriodComparison>>(period1Result.Error);

        var period2Result = await GetHousingAnalysisForPeriodAsync(
            period2Start, period2End, cancellationToken);

        if (!period2Result.IsSuccess)
            return Result.Failure<List<HousingPeriodComparison>>(period2Result.Error);

        var period1Analysis = period1Result.Value;
        var period2Analysis = period2Result.Value;

        // Get all housing IDs from both periods
        var allHousingIds = period1Analysis.HousingBreakdowns
            .Select(h => h.HousingId)
            .Union(period2Analysis.HousingBreakdowns.Select(h => h.HousingId))
            .Distinct()
            .ToList();

        var comparisons = new List<HousingPeriodComparison>();

        foreach (var housingId in allHousingIds)
        {
            var p1Housing = period1Analysis.HousingBreakdowns
                .FirstOrDefault(h => h.HousingId == housingId);

            var p2Housing = period2Analysis.HousingBreakdowns
                .FirstOrDefault(h => h.HousingId == housingId);

            // Only compare if housing exists in both periods
            if (p1Housing != null && p2Housing != null)
            {
                var metrics = CalculateHousingComparisonMetrics(p1Housing, p2Housing);
                var insights = GenerateHousingInsights(p1Housing, p2Housing, metrics);

                comparisons.Add(new HousingPeriodComparison(
                    HousingName: p2Housing.HousingName,
                    Period1Breakdown: p1Housing,
                    Period2Breakdown: p2Housing,
                    Comparison: metrics,
                    Insights: insights
                ));
            }
        }

        return Result.Success(comparisons.OrderByDescending(c => c.Period2Breakdown.CompletionRate).ToList());
    }

    public async Task<Result<PeriodHousingAnalysis>> GetHousingAnalysisForPeriodAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<PeriodHousingAnalysis>(
                new Error("End date must be after start date", "invalid_input", 400));

        // Get all shifts in the period with necessary includes
        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
                .ThenInclude(e => e.Housing)
            .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
            return Result.Failure<PeriodHousingAnalysis>(
                new Error("No shifts found in the specified period", "no_data", 404));

        // Filter out shifts without housing information
        var validShifts = shifts
            .Where(s => s.Rider?.Employee?.Housing != null)
            .ToList();

        if (!validShifts.Any())
            return Result.Failure<PeriodHousingAnalysis>(
                new Error("No shifts with housing information found", "no_data", 404));

        // Group by housing
        var housingGroups = validShifts.GroupBy(s => s.Rider.Employee.HousingId);
        var housingBreakdowns = new List<HousingPeriodBreakdown>();
        var totalOrders = 0;
        var allRiderIds = new HashSet<int>();

        foreach (var group in housingGroups)
        {
            var housing = group.First().Rider.Employee.Housing;
            if (housing == null) continue;

            var housingShifts = group.ToList();
            var totalDailyOrders = housingShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders);
            var completedOrders = housingShifts.Sum(s => s.AcceptedDailyOrders);
            var rejectedOrders = housingShifts.Sum(s => s.RejectedDailyOrders);

            var completionRate = totalDailyOrders > 0
                ? (decimal)completedOrders / totalDailyOrders * 100
                : 0;

            var riderIds = housingShifts.Select(s => s.RiderId).Distinct().ToList();
            allRiderIds.UnionWith(riderIds);

            var riderAssignments = GetRiderAssignmentsForHousingFromShifts(
                riderIds, housingShifts);

            var problematicOrders = housingShifts
                .Count(s => s.RealRejectedDailyOrders > CompanyShiftConfiguration.RejectionThreshold);

            var avgOrdersPerRider = riderIds.Count > 0
                ? (decimal)completedOrders / riderIds.Count
                : 0;

            totalOrders += totalDailyOrders;

            housingBreakdowns.Add(new HousingPeriodBreakdown(
                HousingId: housing.Id,
                HousingName: housing.Name,
                DailyOrdersCount: totalDailyOrders,
                CompletedOrdersCount: completedOrders,
                RejectedOrdersCount: rejectedOrders,
                CompletionRate: completionRate,
                RiderCount: riderIds.Count,
                RiderAssignments: riderAssignments,
                HousingContribution: 0, // Will be calculated below
                ProblematicOrdersCount: problematicOrders,
                AverageOrdersPerRider: avgOrdersPerRider
            ));
        }

        // Calculate housing contributions
        housingBreakdowns = housingBreakdowns
            .Select(h => h with
            {
                HousingContribution = totalOrders > 0
                    ? (decimal)h.DailyOrdersCount / totalOrders * 100
                    : 0
            })
            .OrderByDescending(h => h.CompletionRate)
            .ToList();

        var topPerforming = housingBreakdowns.FirstOrDefault();
        var lowestPerforming = housingBreakdowns.LastOrDefault();

        var analysis = new PeriodHousingAnalysis(
            StartDate: startDate,
            EndDate: endDate,
            HousingBreakdowns: housingBreakdowns,
            TotalOrders: totalOrders,
            TotalRiders: allRiderIds.Count,
            TopPerformingHousing: topPerforming != null
                ? new HousingPerformanceRanking(
                    topPerforming.HousingId,
                    topPerforming.HousingName,
                    topPerforming.CompletionRate,
                    topPerforming.DailyOrdersCount,
                    topPerforming.RiderCount)
                : null,
            LowestPerformingHousing: lowestPerforming != null
                ? new HousingPerformanceRanking(
                    lowestPerforming.HousingId,
                    lowestPerforming.HousingName,
                    lowestPerforming.CompletionRate,
                    lowestPerforming.DailyOrdersCount,
                    lowestPerforming.RiderCount)
                : null
        );

        return Result.Success(analysis);
    }

    public async Task<Result<HousingPeriodComparison>> CompareSpecificHousingAsync(
        string housingName,
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default)
    {

        var housing = await _dbcontext.Housings
            .FirstOrDefaultAsync(h => h.Name == housingName, cancellationToken);

        if (housing == null)
            return Result.Failure<HousingPeriodComparison>(
                new Error($"Housing with  {housingName} not found", "not_found", 404));

        var period1Result = await GetHousingAnalysisForPeriodAsync(
            period1Start, period1End, cancellationToken);

        var period2Result = await GetHousingAnalysisForPeriodAsync(
            period2Start, period2End, cancellationToken);

        var p1Housing = period1Result.IsSuccess
            ? period1Result.Value.HousingBreakdowns.FirstOrDefault(h => h.HousingName == housingName)
            : null;

        var p2Housing = period2Result.IsSuccess
            ? period2Result.Value.HousingBreakdowns.FirstOrDefault(h => h.HousingName == housingName)
            : null;

        if (p1Housing == null || p2Housing == null)
            return Result.Failure<HousingPeriodComparison>(
                new Error($"Housing data not found for one or both periods", "no_data", 404));

        var metrics = CalculateHousingComparisonMetrics(p1Housing, p2Housing);
        var insights = GenerateHousingInsights(p1Housing, p2Housing, metrics);

        var comparison = new HousingPeriodComparison(
            HousingName: housing.Name,
            Period1Breakdown: p1Housing,
            Period2Breakdown: p2Housing,
            Comparison: metrics,
            Insights: insights
        );

        return Result.Success(comparison);
    }

    public async Task<Result<List<RiderHousingAssignment>>> GetRidersForHousingAsync(
        string housingName,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {

        if (endDate < startDate)
            return Result.Failure<List<RiderHousingAssignment>>(
                new Error("End date must be after start date", "invalid_input", 400));

        var housing = await _dbcontext.Housings
            .FirstOrDefaultAsync(h => h.Name == housingName, cancellationToken);

        if (housing == null)
            return Result.Failure<List<RiderHousingAssignment>>(
                new Error($"Housing with {housingName} not found", "not_found", 404));

        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
                .ThenInclude(e => e.Housing)    
            .Where(s => s.Rider.Employee.Housing.Name == housingName
                   && s.ShiftDate >= startDate
                   && s.ShiftDate <= endDate)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
            return Result.Failure<List<RiderHousingAssignment>>(
                new Error($"No shifts found for housing '{housing.Name}' in the specified period", "no_data", 404));

        var riderGroups = shifts.GroupBy(s => s.RiderId);
        var assignments = new List<RiderHousingAssignment>();

        foreach (var group in riderGroups)
        {
            var rider = group.First().Rider;
            if (rider == null) continue;

            var riderShifts = group.ToList();
            var completed = riderShifts.Sum(s => s.AcceptedDailyOrders);
            var rejected = riderShifts.Sum(s => s.RejectedDailyOrders);
            var total = completed + rejected;

            var completionRate = total > 0
                ? (decimal)completed / total * 100
                : 0;

            assignments.Add(new RiderHousingAssignment(
                RiderId: rider.Id,
                RiderName: rider.Employee.NameAR,
                WorkingId: rider.WorkingId ?? 0,
                ShiftsCount: riderShifts.Count,
                OrdersCompleted: completed,
                OrdersRejected: rejected,
                CompletionRate: completionRate,
                TotalWorkingHours: riderShifts.Sum(s => s.WorkingHours)
            ));
        }

        return Result.Success(assignments.OrderByDescending(a => a.OrdersCompleted).ToList());
    }
    private List<RiderHousingAssignment> GetRiderAssignmentsForHousingFromShifts(
        List<int> riderIds,
        List<RiderShift> shifts)
    {
        var assignments = new List<RiderHousingAssignment>();
        var riderGroups = shifts.GroupBy(s => s.RiderId);

        foreach (var group in riderGroups)
        {
            var rider = group.First().Rider;
            if (rider == null) continue;

            var riderShifts = group.ToList();
            var completed = riderShifts.Sum(s => s.AcceptedDailyOrders);
            var rejected = riderShifts.Sum(s => s.RejectedDailyOrders);
            var total = completed + rejected;

            var completionRate = total > 0
                ? (decimal)completed / total * 100
                : 0;

            assignments.Add(new RiderHousingAssignment(
                RiderId: rider.Id,
                RiderName: rider.Employee.NameAR,
                WorkingId: rider.WorkingId ?? 0,
                ShiftsCount: riderShifts.Count,
                OrdersCompleted: completed,
                OrdersRejected: rejected,
                CompletionRate: completionRate,
                TotalWorkingHours: riderShifts.Sum(s => s.WorkingHours)
            ));
        }

        return assignments.OrderByDescending(a => a.OrdersCompleted).ToList();
    }

    private HousingComparisonMetrics CalculateHousingComparisonMetrics(
        HousingPeriodBreakdown period1,
        HousingPeriodBreakdown period2)
    {
        return new HousingComparisonMetrics(
            DailyOrdersDifference: period2.DailyOrdersCount - period1.DailyOrdersCount,
            DailyOrdersChangePercent: CalculatePercentChange(period1.DailyOrdersCount, period2.DailyOrdersCount),
            CompletedOrdersDifference: period2.CompletedOrdersCount - period1.CompletedOrdersCount,
            CompletedOrdersChangePercent: CalculatePercentChange(period1.CompletedOrdersCount, period2.CompletedOrdersCount),
            CompletionRateDifference: period2.CompletionRate - period1.CompletionRate,
            CompletionRateChangePercent: CalculatePercentChange(period1.CompletionRate, period2.CompletionRate),
            RiderCountDifference: period2.RiderCount - period1.RiderCount,
            RiderCountChangePercent: CalculatePercentChange(period1.RiderCount, period2.RiderCount),
            RejectedOrdersDifference: period2.RejectedOrdersCount - period1.RejectedOrdersCount,
            RejectionRateChangePercent: CalculatePercentChange(
                CalculateRejectionRate(period1),
                CalculateRejectionRate(period2)),
            HousingContributionDifference: period2.HousingContribution - period1.HousingContribution
        );
    }

    private decimal CalculatePercentChange(decimal oldValue, decimal newValue)
    {
        if (oldValue == 0)
            return newValue > 0 ? 100 : 0;

        return Math.Round(((newValue - oldValue) / oldValue) * 100, 2);
    }

    private decimal CalculateRejectionRate(HousingPeriodBreakdown housing)
    {
        var total = housing.CompletedOrdersCount + housing.RejectedOrdersCount;
        return total > 0
            ? (decimal)housing.RejectedOrdersCount / total * 100
            : 0;
    }
    public async Task<Result<TopRidersReport>> GetTopRidersInPeriodAsync(
        TopRidersRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        if (request.EndDate < request.StartDate)
            return Result.Failure<TopRidersReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        if (request.TopCount <= 0)
            return Result.Failure<TopRidersReport>(
                new Error("Top count must be greater than 0", "invalid_input", 400));

        try
        {
            // Load all shifts in period with necessary includes
            var shiftsQuery = _dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(s => s.Company)
                .Where(s => s.ShiftDate >= request.StartDate &&
                           s.ShiftDate <= request.EndDate);

            // Apply company filter if specified
            if (!string.IsNullOrWhiteSpace(request.CompanyFilter))
            {
                shiftsQuery = shiftsQuery.Where(s => s.Company.Name == request.CompanyFilter);
            }

            var shifts = await shiftsQuery.ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<TopRidersReport>(
                    new Error("No shifts found in the specified period", "no_data", 404));
            }

            // Load active substitutions to mark riders correctly
            var activeSubstitutions = await _dbcontext.Set<RiderShiftSubstitution>()
                .Where(s => s.IsActive)
                .Include(s => s.ActualRider)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var substitutionDict = activeSubstitutions
                .ToDictionary(s => s.ActualRiderId, s => s);

            // Group shifts by rider
            var riderGroups = shifts
                .GroupBy(s => s.RiderId)
                .ToList();

            // Filter by minimum shifts if specified
            if (request.MinimumShifts > 0)
            {
                riderGroups = riderGroups
                    .Where(g => g.Count() >= request.MinimumShifts)
                    .ToList();
            }

            if (!riderGroups.Any())
            {
                return Result.Failure<TopRidersReport>(
                    new Error($"No riders found with at least {request.MinimumShifts} shifts", "no_data", 404));
            }

            // Calculate metrics for each rider
            var riderDetails = new List<TopRiderDetail>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var riderShifts = group.ToList();
                var totalAccepted = riderShifts.Sum(s => s.AcceptedDailyOrders);
                var totalRejected = riderShifts.Sum(s => s.RejectedDailyOrders);
                var totalRealRejected = riderShifts.Sum(s => s.RealRejectedDailyOrders);
                var totalHours = riderShifts.Sum(s => s.WorkingHours);
                var totalShifts = riderShifts.Count;

                var completedShifts = riderShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString());
                var incompleteShifts = riderShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString());
                var failedShifts = riderShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString());

                var completionRate = totalShifts > 0
                    ? (decimal)completedShifts / totalShifts * 100
                    : 0;

                var avgOrdersPerShift = totalShifts > 0
                    ? (decimal)totalAccepted / totalShifts
                    : 0;

                var totalOrders = totalAccepted + totalRejected;
                var rejectionRate = totalOrders > 0
                    ? (decimal)totalRejected / totalOrders * 100
                    : 0;

                // Calculate performance score
                var companyName = riderShifts.First().Company?.Name ?? "Unknown";
                var dailyTarget = CompanyShiftConfiguration.GetDailyOrderTarget(companyName);
                var expectedOrders = totalShifts * dailyTarget;
                var performanceScore = expectedOrders > 0
                    ? (decimal)totalAccepted / expectedOrders * 100
                    : 0;

                // Calculate penalty
                var totalPenalty = riderShifts.Sum(s => CalculatePenalty(s));
                var problematicCount = riderShifts.Count(s => HasRejectionProblem(s));

                // Determine performance grade
                var grade = DeterminePerformanceGrade(performanceScore);

                // Generate achievements
                var achievements = GenerateRiderAchievements(
                    totalAccepted, avgOrdersPerShift, completionRate,
                    rejectionRate, totalShifts, performanceScore);

                // Check for active substitution
                var hasSubstitution = substitutionDict.ContainsKey(rider.Id);
                var originalWorkingId = hasSubstitution
                    ? substitutionDict[rider.Id].SubstituteWorkingId
                    : (int?)null;

                riderDetails.Add(new TopRiderDetail(
                    RiderId: rider.Id,
                    WorkingId: riderShifts.First().WorkingId,
                    RiderNameEN: rider.Employee.NameEN,
                    RiderNameAR: rider.Employee.NameAR,
                    CompanyName: companyName,
                    TotalShifts: totalShifts,
                    TotalAcceptedOrders: totalAccepted,
                    TotalRejectedOrders: totalRejected,
                    TotalRealRejectedOrders: totalRealRejected,
                    TotalWorkingHours: totalHours,
                    CompletedShifts: completedShifts,
                    IncompleteShifts: incompleteShifts,
                    FailedShifts: failedShifts,
                    CompletionRate: completionRate,
                    AverageOrdersPerShift: avgOrdersPerShift,
                    RejectionRate: rejectionRate,
                    PerformanceScore: performanceScore,
                    TotalPenalty: totalPenalty,
                    ProblematicShiftsCount: problematicCount,
                    Rank: 0, // Will be assigned after sorting
                    PerformanceGrade: grade,
                    Achievements: achievements,
                    IsSubstitutionActive: hasSubstitution,
                    OriginalWorkingId: originalWorkingId
                ));
            }

            // Sort by requested criteria
            riderDetails = SortRiderDetails(riderDetails, request.SortBy);

            // Assign ranks
            for (int i = 0; i < riderDetails.Count; i++)
            {
                riderDetails[i] = riderDetails[i] with { Rank = i + 1 };
            }

            // Take top N
            var topRiders = riderDetails.Take(request.TopCount).ToList();

            // Calculate company breakdown
            var companyBreakdown = CalculateCompanyBreakdown(
                shifts, riderDetails, request.IncludeAllCompanies);

            var report = new TopRidersReport(
                StartDate: request.StartDate,
                EndDate: request.EndDate,
                TotalRiders: riderGroups.Count,
                TotalShifts: shifts.Count,
                TotalOrders: shifts.Sum(s => s.AcceptedDailyOrders),
                TopRiders: topRiders,
                CompanyBreakdown: companyBreakdown
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<TopRidersReport>(
                new Error($"Error generating top riders report: {ex.Message}", "server_error", 500));
        }
    }


    public async Task<Result<TopRidersReport>> GetTopRidersForMonthAsync(
        int year,
        int month,
        int topCount = 100,
        string? companyFilter = null,
        CancellationToken cancellationToken = default)
    {
        if (month < 1 || month > 12)
            return Result.Failure<TopRidersReport>(
                new Error("Month must be between 1 and 12", "invalid_input", 400));

        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var request = new TopRidersRequest(
            StartDate: startDate,
            EndDate: endDate,
            TopCount: topCount,
            CompanyFilter: companyFilter,
            SortBy: TopRidersSortBy.TotalOrders,
            IncludeAllCompanies: true,
            MinimumShifts: 0
        );

        return await GetTopRidersInPeriodAsync(request, cancellationToken);
    }


    public async Task<Result<TopRidersReport>> GetTopRidersForYearAsync(
        int year,
        int topCount = 100,
        string? companyFilter = null,
        CancellationToken cancellationToken = default)
    {
        var startDate = new DateOnly(year, 1, 1);
        var endDate = new DateOnly(year, 12, 31);

        var request = new TopRidersRequest(
            StartDate: startDate,
            EndDate: endDate,
            TopCount: topCount,
            CompanyFilter: companyFilter,
            SortBy: TopRidersSortBy.TotalOrders,
            IncludeAllCompanies: true,
            MinimumShifts: 5
        );

        return await GetTopRidersInPeriodAsync(request, cancellationToken);
    }

    public async Task<Result<Dictionary<string, List<TopRiderDetail>>>> GetTopRidersPerCompanyAsync(
        DateOnly startDate,
        DateOnly endDate,
        int topCountPerCompany = 100,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<Dictionary<string, List<TopRiderDetail>>>(
                new Error("End date must be after start date", "invalid_input", 400));

        try
        {
            // Get all companies
            var companies = await _dbcontext.Companies
                .Select(c => c.Name)
                .Distinct()
                .ToListAsync(cancellationToken);

            var result = new Dictionary<string, List<TopRiderDetail>>();

            foreach (var company in companies)
            {
                var request = new TopRidersRequest(
                    StartDate: startDate,
                    EndDate: endDate,
                    TopCount: topCountPerCompany,
                    CompanyFilter: company,
                    SortBy: TopRidersSortBy.PerformanceScore,
                    IncludeAllCompanies: false,
                    MinimumShifts: 1
                );

                var companyReport = await GetTopRidersInPeriodAsync(request, cancellationToken);

                if (companyReport.IsSuccess && companyReport.Value.TopRiders.Any())
                {
                    result[company] = companyReport.Value.TopRiders;
                }
            }

            if (!result.Any())
            {
                return Result.Failure<Dictionary<string, List<TopRiderDetail>>>(
                    new Error("No data found for any company", "no_data", 404));
            }

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<Dictionary<string, List<TopRiderDetail>>>(
                new Error($"Error generating company rankings: {ex.Message}", "server_error", 500));
        }
    }


    private List<TopRiderDetail> SortRiderDetails(
        List<TopRiderDetail> riders,
        TopRidersSortBy sortBy)
    {
        return sortBy switch
        {
            TopRidersSortBy.TotalOrders => riders
                .OrderByDescending(r => r.TotalAcceptedOrders)
                .ThenByDescending(r => r.CompletionRate)
                .ToList(),

            TopRidersSortBy.CompletionRate => riders
                .OrderByDescending(r => r.CompletionRate)
                .ThenByDescending(r => r.TotalAcceptedOrders)
                .ToList(),

            TopRidersSortBy.PerformanceScore => riders
                .OrderByDescending(r => r.PerformanceScore)
                .ThenByDescending(r => r.TotalAcceptedOrders)
                .ToList(),

            TopRidersSortBy.AverageOrdersPerShift => riders
                .OrderByDescending(r => r.AverageOrdersPerShift)
                .ThenByDescending(r => r.CompletionRate)
                .ToList(),

            TopRidersSortBy.TotalShifts => riders
                .OrderByDescending(r => r.TotalShifts)
                .ThenByDescending(r => r.TotalAcceptedOrders)
                .ToList(),

            TopRidersSortBy.WorkingHours => riders
                .OrderByDescending(r => r.TotalWorkingHours)
                .ThenByDescending(r => r.TotalAcceptedOrders)
                .ToList(),

            _ => riders
                .OrderByDescending(r => r.TotalAcceptedOrders)
                .ToList()
        };
    }

    private string DeterminePerformanceGrade(decimal performanceScore)
    {
        return performanceScore switch
        {
            >= 95m => PerformanceGrade.Exceptional.ToString(),
            >= 85m => PerformanceGrade.Excellent.ToString(),
            >= 75m => PerformanceGrade.Good.ToString(),
            >= 65m => PerformanceGrade.Average.ToString(),
            >= 50m => PerformanceGrade.BelowAverage.ToString(),
            _ => PerformanceGrade.Poor.ToString()
        };
    }

    private List<string> GenerateRiderAchievements(
        int totalOrders,
        decimal avgOrdersPerShift,
        decimal completionRate,
        decimal rejectionRate,
        int totalShifts,
        decimal performanceScore)
    {
        var achievements = new List<string>();

        // Order-based achievements
        if (totalOrders >= 1000)
            achievements.Add("🏆 1000+ Orders Club");
        else if (totalOrders >= 500)
            achievements.Add("⭐ 500+ Orders Milestone");
        else if (totalOrders >= 250)
            achievements.Add("✨ 250+ Orders Achievement");

        // Consistency achievements
        if (totalShifts >= 30 && completionRate >= 90m)
            achievements.Add("💎 Consistency Champion");
        else if (totalShifts >= 20 && completionRate >= 85m)
            achievements.Add("🎯 Reliable Performer");

        // Average performance
        if (avgOrdersPerShift >= 25m)
            achievements.Add("🚀 High Volume Expert");
        else if (avgOrdersPerShift >= 20m)
            achievements.Add("📈 Above Average Performer");

        // Low rejection rate
        if (rejectionRate <= 5m && totalOrders >= 100)
            achievements.Add("✅ Quality Master");
        else if (rejectionRate <= 10m && totalOrders >= 100)
            achievements.Add("👍 Quality Focused");

        // Overall performance
        if (performanceScore >= 95m)
            achievements.Add("🌟 Exceptional Rating");
        else if (performanceScore >= 85m)
            achievements.Add("⚡ Excellent Rating");

        // Perfect month
        if (completionRate == 100m && totalShifts >= 15)
            achievements.Add("💯 Perfect Record");

        return achievements;
    }

    private CompanyBreakdownSummary CalculateCompanyBreakdown(
        List<RiderShift> allShifts,
        List<TopRiderDetail> allRiderDetails,
        bool includeAll)
    {
        var companyGroups = allShifts.GroupBy(s => s.Company?.Name ?? "Unknown");
        var companySummaries = new List<CompanyTopRiders>();

        foreach (var companyGroup in companyGroups)
        {
            var companyName = companyGroup.Key;
            var companyShifts = companyGroup.ToList();
            var dailyTarget = CompanyShiftConfiguration.GetDailyOrderTarget(companyName);

            // Get riders for this company
            var companyRiders = allRiderDetails
                .Where(r => r.CompanyName == companyName)
                .OrderByDescending(r => r.PerformanceScore)
                .ToList();

            if (!companyRiders.Any()) continue;

            var topPerformer = companyRiders.First();
            var topPerformersCount = companyRiders.Count(r => r.PerformanceScore >= 85m);

            var totalOrders = companyShifts.Sum(s => s.AcceptedDailyOrders);
            var expectedOrders = companyShifts.Count * dailyTarget;
            var companyScore = expectedOrders > 0
                ? (decimal)totalOrders / expectedOrders * 100
                : 0;

            companySummaries.Add(new CompanyTopRiders(
                CompanyName: companyName,
                DailyOrderTarget: dailyTarget,
                TotalRiders: companyRiders.Count,
                TotalShifts: companyShifts.Count,
                TotalOrders: totalOrders,
                CompanyPerformanceScore: companyScore,
                TopPerformer: topPerformer,
                TopPerformersCount: topPerformersCount
            ));
        }

        return new CompanyBreakdownSummary(
            CompaniesSummary: companySummaries
                .OrderByDescending(c => c.CompanyPerformanceScore)
                .ToList()
        );
    }



    private List<string> GenerateHousingInsights(
        HousingPeriodBreakdown period1,
        HousingPeriodBreakdown period2,
        HousingComparisonMetrics metrics)
    {
        var insights = new List<string>();

        // Orders change
        if (Math.Abs(metrics.DailyOrdersChangePercent) >= 15)
        {
            var emoji = metrics.DailyOrdersChangePercent > 0 ? "📈" : "📉";
            var direction = metrics.DailyOrdersChangePercent > 0 ? "increased" : "decreased";
            insights.Add($"{emoji} Orders {direction} by {Math.Abs(metrics.DailyOrdersChangePercent):F1}% " +
                        $"from {period1.DailyOrdersCount} to {period2.DailyOrdersCount}");
        }

        // Completion rate change
        if (Math.Abs(metrics.CompletionRateDifference) >= 5)
        {
            var emoji = metrics.CompletionRateDifference > 0 ? "✅" : "❌";
            var direction = metrics.CompletionRateDifference > 0 ? "improved" : "declined";
            insights.Add($"{emoji} Completion rate {direction} from {period1.CompletionRate:F1}% to {period2.CompletionRate:F1}%");
        }

        // Rider count change
        if (metrics.RiderCountDifference != 0)
        {
            var direction = metrics.RiderCountDifference > 0 ? "increased" : "decreased";
            insights.Add($"👥 Rider count {direction} from {period1.RiderCount} to {period2.RiderCount}");
        }

        // Rejection rate change
        if (Math.Abs(metrics.RejectionRateChangePercent) >= 10)
        {
            var emoji = metrics.RejectionRateChangePercent < 0 ? "🎯" : "⚠️";
            var direction = metrics.RejectionRateChangePercent < 0 ? "improved" : "increased";
            insights.Add($"{emoji} Rejection rate {direction} by {Math.Abs(metrics.RejectionRateChangePercent):F1}%");
        }

        // Efficiency change
        var avgChange = period2.AverageOrdersPerRider - period1.AverageOrdersPerRider;
        if (Math.Abs(avgChange) >= 2)
        {
            var emoji = avgChange > 0 ? "🚀" : "⚠️";
            var status = avgChange > 0 ? "more efficient" : "less efficient";
            insights.Add($"{emoji} Riders becoming {status}: avg orders per rider " +
                        $"from {period1.AverageOrdersPerRider:F1} to {period2.AverageOrdersPerRider:F1}");
        }

        if (!insights.Any())
        {
            insights.Add("✨ Performance remained relatively stable between periods");
        }

        return insights;
    }



    private List<CompanyPeriodBreakdown> CalculateCompanyBreakdowns(List<RiderShift> shifts)
    {
        var companyGroups = shifts.GroupBy(s => s.Company?.Name ?? "Unknown");
        var breakdowns = new List<CompanyPeriodBreakdown>();

        foreach (var companyGroup in companyGroups)
        {
            var companyName = companyGroup.Key;
            var companyShifts = companyGroup.ToList();
            var companyTarget = CompanyShiftConfiguration.GetDailyOrderTarget(companyName);
            var companyWorkingDays = companyShifts.Count;
            var companyExpected = companyWorkingDays * companyTarget;
            var companyAccepted = companyShifts.Sum(s => s.AcceptedDailyOrders);
            var companyPenalty = companyShifts.Sum(s => CalculatePenalty(s));
            var companyProblematic = companyShifts.Count(s => HasRejectionProblem(s));

            var performanceScore = companyExpected > 0
                ? (decimal)companyAccepted / companyExpected * 100
                : 0;

            breakdowns.Add(new CompanyPeriodBreakdown(
                CompanyName: companyName,
                DailyOrderTarget: companyTarget,
                WorkingDays: companyWorkingDays,
                CompletedShifts: companyShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: companyShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: companyShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString()),
                TotalAcceptedOrders: companyAccepted,
                TotalRejectedOrders: companyShifts.Sum(s => s.RejectedDailyOrders),
                TotalRealRejectedOrders: companyShifts.Sum(s => s.RealRejectedDailyOrders),
                TotalWorkingHours: companyShifts.Sum(s => s.WorkingHours),
                ProblematicShiftsCount: companyProblematic,
                PenaltyAmount: companyPenalty,
                PerformanceScore: performanceScore,
                ExpectedOrders: companyExpected
            ));
        }

        return breakdowns.OrderByDescending(b => b.PerformanceScore).ToList();
    }

    private List<YearlyCompanyBreakdown> CalculateYearlyCompanyBreakdowns(List<RiderShift> shifts)
    {
        var companyGroups = shifts.GroupBy(s => s.Company?.Name ?? "Unknown");
        var breakdowns = new List<YearlyCompanyBreakdown>();

        foreach (var companyGroup in companyGroups)
        {
            var companyName = companyGroup.Key;
            var companyShifts = companyGroup.ToList();
            var monthlyData = companyShifts
                .GroupBy(s => s.ShiftDate.Month)
                .Select(monthGroup => new MonthlyCompanyData(
                    Month: monthGroup.Key,
                    WorkingDays: monthGroup.Count(),
                    AcceptedOrders: monthGroup.Sum(s => s.AcceptedDailyOrders),
                    RejectedOrders: monthGroup.Sum(s => s.RejectedDailyOrders)
                ))
                .OrderBy(m => m.Month)
                .ToList();

            var companyTarget = CompanyShiftConfiguration.GetDailyOrderTarget(companyName);
            var totalWorkingDays = companyShifts.Count;
            var expectedOrders = totalWorkingDays * companyTarget;
            var totalAccepted = companyShifts.Sum(s => s.AcceptedDailyOrders);

            var performanceScore = expectedOrders > 0
                ? (decimal)totalAccepted / expectedOrders * 100
                : 0;

            breakdowns.Add(new YearlyCompanyBreakdown(
                CompanyName: companyName,
                DailyOrderTarget: companyTarget,
                TotalWorkingDays: totalWorkingDays,
                TotalAcceptedOrders: totalAccepted,
                TotalRejectedOrders: companyShifts.Sum(s => s.RejectedDailyOrders),
                AveragePerformanceScore: performanceScore,
                MonthlyDetails: monthlyData
            ));
        }

        return breakdowns.OrderByDescending(b => b.AveragePerformanceScore).ToList();
    }

    private List<MonthlyBreakdown> CalculateMonthlyBreakdowns(List<RiderShift> shifts)
    {
        return shifts
            .GroupBy(s => s.ShiftDate.Month)
            .Select(monthGroup =>
            {
                var monthShifts = monthGroup.ToList();
                var companyBreakdowns = CalculateCompanyBreakdowns(monthShifts);
                var totalWorkingDays = monthShifts.Count;

                var performanceScore = companyBreakdowns.Any() && totalWorkingDays > 0
                    ? companyBreakdowns.Sum(cb => cb.PerformanceScore * cb.WorkingDays) / totalWorkingDays
                    : 0;

                return new MonthlyBreakdown(
                    Month: monthGroup.Key,
                    WorkingDays: totalWorkingDays,
                    CompletedShifts: monthShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                    TotalAcceptedOrders: monthShifts.Sum(s => s.AcceptedDailyOrders),
                    TotalRejectedOrders: monthShifts.Sum(s => s.RejectedDailyOrders),
                    PerformanceScore: performanceScore,
                    CompanyBreakdowns: companyBreakdowns
                );
            })
            .OrderBy(m => m.Month)
            .ToList();
    }

    private List<WorkingIdPeriod> DetectWorkingIdChanges(List<RiderShift> shifts)
    {
        if (!shifts.Any())
            return new List<WorkingIdPeriod>();

        var periods = new List<WorkingIdPeriod>();
        var currentWorkingId = shifts[0].WorkingId;
        var periodStart = shifts[0].ShiftDate;
        var shiftCount = 0;
        DateOnly? lastDate = null;

        foreach (var shift in shifts)
        {
            if (shift.WorkingId != currentWorkingId)
            {
                periods.Add(new WorkingIdPeriod(
                    WorkingId: currentWorkingId,
                    StartDate: periodStart,
                    EndDate: lastDate ?? periodStart,
                    ShiftCount: shiftCount
                ));

                currentWorkingId = shift.WorkingId;
                periodStart = shift.ShiftDate;
                shiftCount = 1;
            }
            else
            {
                shiftCount++;
            }
            lastDate = shift.ShiftDate;
        }

        periods.Add(new WorkingIdPeriod(
            WorkingId: currentWorkingId,
            StartDate: periodStart,
            EndDate: lastDate ?? periodStart,
            ShiftCount: shiftCount
        ));

        return periods;
    }

    private decimal CalculatePenalty(RiderShift shift)
    {
        var excessRejections = Math.Max(0,
            shift.RealRejectedDailyOrders - CompanyShiftConfiguration.RejectionThreshold);
        return excessRejections * CompanyShiftConfiguration.PenaltyPerExcessRejection;
    }

    private bool HasRejectionProblem(RiderShift shift)
    {
        return shift.RealRejectedDailyOrders > CompanyShiftConfiguration.RejectionThreshold;
    }

    private decimal CalculateRiderPerformanceScore(List<RiderShift> shifts, int dailyTarget)
    {
        var totalDays = shifts.Count;
        var expectedOrders = totalDays * dailyTarget;
        var actualOrders = shifts.Sum(s => s.AcceptedDailyOrders);

        return expectedOrders > 0
            ? (decimal)actualOrders / expectedOrders * 100
            : 0;
    }

    private ProblemShiftDetail CreateProblemShiftDetail(RiderShift shift)
    {
        var problems = new List<string>();

        if (shift.ShiftStatus != ShiftStatus.Completed.ToString())
            problems.Add($"Status: {shift.ShiftStatus}");

        if (HasRejectionProblem(shift))
        {
            var excess = shift.RealRejectedDailyOrders - CompanyShiftConfiguration.RejectionThreshold;
            problems.Add($"Excess rejections: {excess} (Total: {shift.RealRejectedDailyOrders})");
        }

        return new ProblemShiftDetail(
            RiderId: shift.RiderId,
            RiderName: shift.Rider?.Employee.NameAR ?? "Unknown",
            WorkingId: shift.WorkingId,
            ShiftDate: shift.ShiftDate,
            CompanyName: shift.Company?.Name ?? "Unknown",
            AcceptedOrders: shift.AcceptedDailyOrders,
            RejectedOrders: shift.RejectedDailyOrders,
            RealRejectedOrders: shift.RealRejectedDailyOrders,
            Status: shift.ShiftStatus,
            PenaltyAmount: CalculatePenalty(shift),
            ProblemDescription: string.Join(", ", problems)
        );
    }

    private MonthlyRiderReport CreateEmptyMonthlyReport(
        int riderId, string riderName, int workingId, int year, int month)
    {
        return new MonthlyRiderReport(
            RiderId: riderId,
            RiderName: riderName,
            WorkingId: workingId,
            Year: year,
            Month: month,
            TotalWorkingDays: 0,
            CompletedShifts: 0,
            IncompleteShifts: 0,
            FailedShifts: 0,
            TotalAcceptedOrders: 0,
            TotalRejectedOrders: 0,
            TotalRealRejectedOrders: 0,
            TotalWorkingHours: 0,
            ProblematicShiftsCount: 0,
            TotalPenaltyAmount: 0,
            OverallPerformanceScore: 0,
            CompanyBreakdowns: new List<CompanyPeriodBreakdown>(),
            ProblematicShifts: new List<ProblemShiftDetail>(),
            WorkingIdHistory: new List<WorkingIdPeriod>()
        );
    }

    private YearlyRiderReport CreateEmptyYearlyReport(
        int riderId, string riderName, int workingId, int year)
    {
        return new YearlyRiderReport(
            RiderId: riderId,
            RiderName: riderName,
            WorkingId: workingId,
            Year: year,
            TotalWorkingDays: 0,
            CompletedShifts: 0,
            IncompleteShifts: 0,
            FailedShifts: 0,
            TotalAcceptedOrders: 0,
            TotalRejectedOrders: 0,
            TotalRealRejectedOrders: 0,
            TotalWorkingHours: 0,
            ProblematicShiftsCount: 0,
            TotalPenaltyAmount: 0,
            AveragePerformanceScore: 0,
            YearlyCompanyBreakdowns: new List<YearlyCompanyBreakdown>(),
            MonthlyBreakdowns: new List<MonthlyBreakdown>(),
            WorkingIdHistory: new List<WorkingIdPeriod>()
        );
    }

    private DateRangeReport CreateEmptyDateRangeReport(
        int riderId, string riderName, int workingId, DateOnly startDate, DateOnly endDate)
    {
        return new DateRangeReport(
            RiderId: riderId,
            RiderName: riderName,
            WorkingId: workingId,
            StartDate: startDate,
            EndDate: endDate,
            TotalWorkingDays: 0,
            CompletedShifts: 0,
            IncompleteShifts: 0,
            FailedShifts: 0,
            TotalAcceptedOrders: 0,
            TotalRejectedOrders: 0,
            TotalRealRejectedOrders: 0,
            TotalWorkingHours: 0,
            ProblematicShiftsCount: 0,
            TotalPenaltyAmount: 0,
            OverallPerformanceScore: 0,
            CompanyBreakdowns: new List<CompanyPeriodBreakdown>(),
            ProblematicShifts: new List<ProblemShiftDetail>(),
            WorkingIdHistory: new List<WorkingIdPeriod>()
        );
    }
}