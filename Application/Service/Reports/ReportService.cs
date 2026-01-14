using Application.Abstraction;
using Application.Service.Member;
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

    // Add these implementations to ReportService.cs class

    /// <summary>
    /// Compare orders between two time periods (e.g., previous month vs current month)
    /// Period 1 is automatically calculated as the previous month of Period 2
    /// </summary>
    /// 
    private const float TARGET_HOURS_PER_DAY = 9f;
    private const float TARGET_HOURS_PER_DAY2 = 10.5f;
    private const int TARGET_ORDERS_PER_DAY = 14;
    private const int TARGET_ORDERS_PER_DAY2 = 12;


    // Add this method to the ReportService class
    // Replace the following methods in ReportService.cs

    private const float MIN_WORKING_HOURS_PER_DAY = 10f;
    private const int MAX_ALLOWED_MISSING_DAYS = 4;
    private const int FULL_MONTH_TARGET_ORDERS = 300;
    private const int FIRST_CRITICAL_DAYS = 3;
    private const int LAST_CRITICAL_DAYS = 4;

    public async Task<Result<MonthlyRiderValidationReport>> GetCompany2MonthlyRiderValidationAsync(
       int year,
       int month,
       CancellationToken cancellationToken = default)
    {
        if (month < 1 || month > 12)
            return Result.Failure<MonthlyRiderValidationReport>(
                new Error("Month must be between 1 and 12", "invalid_input", 400));

        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
            var yesterday = today.AddDays(-1);
            var startDate = new DateOnly(year, month, 1);

            // If it's the current month, use yesterday as end date, otherwise use last day of month
            var isCurrentMonth = year == today.Year && month == today.Month;
            var endDate = isCurrentMonth ? yesterday : startDate.AddMonths(1).AddDays(-1);

            var totalExpectedDays = endDate.Day; // Days from 1 to current day or end of month
            var currentDayOfMonth = endDate.Day;
            var lastDayOfMonth = startDate.AddMonths(1).AddDays(-1).Day;

            // Calculate target orders based on current day
            var targetOrders = isCurrentMonth
                ? (int)Math.Ceiling((decimal)currentDayOfMonth / lastDayOfMonth * FULL_MONTH_TARGET_ORDERS)
                : FULL_MONTH_TARGET_ORDERS;

            // Get all shifts for company 2 in this period
            var shifts = await _dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.CompanyId == 2 &&
                           s.ShiftDate >= startDate &&
                           s.ShiftDate <= endDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<MonthlyRiderValidationReport>(
                    new Error($"No shifts found for Company 2 in {year}-{month:D2}", "no_data", 404));
            }

            // Group by rider
            var riderGroups = shifts.GroupBy(s => s.RiderId);
            var validationResults = new List<RiderMonthlyValidation>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var validation = ValidateRider(
                    rider,
                    group.OrderBy(s => s.ShiftDate).ToList(),
                    year,
                    month,
                    currentDayOfMonth,
                    lastDayOfMonth,
                    targetOrders);

                validationResults.Add(validation);
            }

            // Sort: valid riders first (by total orders desc), then invalid riders (by missing days asc)
            var sortedResults = validationResults
                .OrderByDescending(r => r.IsValidForMonth)
                .ThenByDescending(r => r.TotalOrders)
                .ThenBy(r => r.MissingDays)
                .ToList();

            var report = new MonthlyRiderValidationReport(
                Year: year,
                Month: month,
                StartDate: startDate,
                EndDate: endDate,
                IsCurrentMonth: isCurrentMonth,
                CurrentDay: currentDayOfMonth,
                TotalExpectedDays: totalExpectedDays,
                TargetOrders: targetOrders,
                TotalRiders: validationResults.Count,
                ValidRiders: validationResults.Count(r => r.IsValidForMonth),
                InvalidRiders: validationResults.Count(r => !r.IsValidForMonth),
                RiderValidations: sortedResults
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<MonthlyRiderValidationReport>(
                new Error($"Error generating monthly validation report: {ex.Message}", "server_error", 500));
        }
    }

    private RiderMonthlyValidation ValidateRider(
        RiderDetails rider,
        List<RiderShift> riderShifts,
        int year,
        int month,
        int currentDayOfMonth,
        int lastDayOfMonth,
        int targetOrders)
    {
        // Create a dictionary of shifts by date for easy lookup
        var shiftsByDate = riderShifts.ToDictionary(s => s.ShiftDate);

        var goodDays = 0;
        var missingDays = new List<int>();
        var daysWithLessThan10Hours = new List<int>();
        var dailyDetails = new List<DailyValidationDetail>();

        // Check each day from start to current day
        for (int day = 1; day <= currentDayOfMonth; day++)
        {
            var currentDate = new DateOnly(year, month, day);

            if (shiftsByDate.TryGetValue(currentDate, out var shift))
            {
                // Check if working hours is less than 10
                if (shift.WorkingHours < MIN_WORKING_HOURS_PER_DAY)
                {
                    daysWithLessThan10Hours.Add(day);
                    missingDays.Add(day);
                    dailyDetails.Add(new DailyValidationDetail(
                        Day: day,
                        Date: currentDate,
                        HasShift: true,
                        WorkingHours: shift.WorkingHours,
                        AcceptedOrders: shift.AcceptedDailyOrders,
                        IsValid: false,
                        Reason: $"Working hours ({shift.WorkingHours:F1}h) less than {MIN_WORKING_HOURS_PER_DAY}h"
                    ));
                }
                else
                {
                    goodDays++;
                    dailyDetails.Add(new DailyValidationDetail(
                        Day: day,
                        Date: currentDate,
                        HasShift: true,
                        WorkingHours: shift.WorkingHours,
                        AcceptedOrders: shift.AcceptedDailyOrders,
                        IsValid: true,
                        Reason: "✓ Valid"
                    ));
                }
            }
            else
            {
                // No shift for this day
                missingDays.Add(day);
                dailyDetails.Add(new DailyValidationDetail(
                    Day: day,
                    Date: currentDate,
                    HasShift: false,
                    WorkingHours: 0,
                    AcceptedOrders: 0,
                    IsValid: false,
                    Reason: "No shift"
                ));
            }
        }

        var totalMissingDays = missingDays.Count;
        var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders);
        var totalHours = riderShifts.Sum(s => s.WorkingHours);
        var totalWorkingDays = riderShifts.Count;

        // Perform validation
        var validationResult = PerformValidation(
            totalMissingDays,
            missingDays,
            daysWithLessThan10Hours,
            totalOrders,
            targetOrders,
            currentDayOfMonth,
            lastDayOfMonth);

        return new RiderMonthlyValidation(
            RiderId: rider.Id,
            IqamaNo: rider.EmployeeIqamaNo,
            RiderNameAR: rider.Employee.NameAR,
            RiderNameEN: rider.Employee.NameEN,
            WorkingId: rider.WorkingId ?? "0",
            TotalExpectedDays: currentDayOfMonth,
            TotalWorkingDays: totalWorkingDays,
            GoodDays: goodDays,
            MissingDays: totalMissingDays,
            MissingDaysList: missingDays,
            DaysWithLessThan10Hours: daysWithLessThan10Hours,
            TotalOrders: totalOrders,
            TargetOrders: targetOrders,
            TotalWorkingHours: totalHours,
            AverageHoursPerDay: totalWorkingDays > 0 ? totalHours / totalWorkingDays : 0,
            IsValidForMonth: validationResult.IsValid,
            ValidationErrors: validationResult.Errors,
            DailyDetails: dailyDetails
        );
    }

    private ValidationResult PerformValidation(
        int totalMissingDays,
        List<int> missingDays,
        List<int> daysWithLessThan10Hours,
        int totalOrders,
        int targetOrders,
        int currentDayOfMonth,
        int lastDayOfMonth)
    {
        var isValid = true;
        var errors = new List<string>();

        // Rule 1: Total missing days should not be more than 4
        if (totalMissingDays > MAX_ALLOWED_MISSING_DAYS)
        {
            isValid = false;
            errors.Add($"❌ Too many missing days: {totalMissingDays} (max allowed: {MAX_ALLOWED_MISSING_DAYS})");
        }

        // Rule 2: Check first 3 days (only if they fall within the current period)
        var first3Days = Enumerable.Range(1, Math.Min(FIRST_CRITICAL_DAYS, currentDayOfMonth)).ToList();
        var missingInFirst3 = first3Days.Intersect(missingDays).ToList();

        // Rule 3: Check last 4 days (only if we're at the end of month or past those days)
        var last4DaysStart = Math.Max(1, lastDayOfMonth - LAST_CRITICAL_DAYS + 1);
        var last4Days = Enumerable.Range(last4DaysStart, Math.Min(LAST_CRITICAL_DAYS, lastDayOfMonth - last4DaysStart + 1))
            .Where(d => d <= currentDayOfMonth)
            .ToList();
        var missingInLast4 = last4Days.Intersect(missingDays).ToList();

        // Exception: If all other days are correct, allow 1 missing day from the critical 7 days
        var criticalDays = first3Days.Concat(last4Days).Distinct().ToList();
        var missingInCritical = criticalDays.Intersect(missingDays).ToList();
        var nonCriticalDays = Enumerable.Range(1, currentDayOfMonth)
            .Except(criticalDays)
            .ToList();
        var missingInNonCritical = nonCriticalDays.Intersect(missingDays).ToList();

        // Check critical days violation
        if (missingInNonCritical.Count == 0 && missingInCritical.Count > 1)
        {
            // All non-critical days are present, but more than 1 critical day is missing
            isValid = false;
            errors.Add($"❌ Missing {missingInCritical.Count} critical days (first {FIRST_CRITICAL_DAYS} or last {LAST_CRITICAL_DAYS}), only 1 allowed when all other days are present: Days {string.Join(", ", missingInCritical)}");
        }
        else if (missingInCritical.Count > 0 && missingInNonCritical.Count > 0)
        {
            // Has missing days in both critical and non-critical
            isValid = false;
            if (missingInFirst3.Any())
            {
                errors.Add($"❌ Missing days in first {FIRST_CRITICAL_DAYS} days: Days {string.Join(", ", missingInFirst3)}");
            }
            if (missingInLast4.Any())
            {
                errors.Add($"❌ Missing days in last {LAST_CRITICAL_DAYS} days: Days {string.Join(", ", missingInLast4)}");
            }
        }

        // Rule 4: Total orders should be >= target
        if (totalOrders < targetOrders)
        {
            isValid = false;
            var shortage = targetOrders - totalOrders;
            errors.Add($"❌ Insufficient orders: {totalOrders} (required: {targetOrders}, shortage: {shortage})");
        }

        // Add details about days with less than 10 hours
        if (daysWithLessThan10Hours.Any())
        {
            errors.Add($"⚠️ Days with less than {MIN_WORKING_HOURS_PER_DAY} working hours (counted as missing): Days {string.Join(", ", daysWithLessThan10Hours)}");
        }

        // Add general missing days info if any and not already mentioned
        if (missingDays.Any() && !errors.Any(e => e.Contains("Missing days in")))
        {
            var regularMissingDays = missingDays.Except(daysWithLessThan10Hours).ToList();
            if (regularMissingDays.Any())
            {
                errors.Add($"⚠️ Days with no shifts: {string.Join(", ", regularMissingDays)}");
            }
        }

        // If no errors, add success message
        if (!errors.Any())
        {
            errors.Add("✅ All validation criteria met");
        }

        return new ValidationResult(isValid, errors);
    }

    private record ValidationResult(bool IsValid, List<string> Errors);


// Records for Validation Results
public record MonthlyRiderValidationReport(
    int Year,
    int Month,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsCurrentMonth,
    int CurrentDay,
    int TotalExpectedDays,
    int TargetOrders,
    int TotalRiders,
    int ValidRiders,
    int InvalidRiders,
    List<RiderMonthlyValidation> RiderValidations
);

public record RiderMonthlyValidation(
    int RiderId,
    long IqamaNo,
    string RiderNameAR,
    string RiderNameEN,
    string WorkingId,
    int TotalExpectedDays,
    int TotalWorkingDays,
    int GoodDays,
    int MissingDays,
    List<int> MissingDaysList,
    List<int> DaysWithLessThan10Hours,
    int TotalOrders,
    int TargetOrders,
    float TotalWorkingHours,
    float AverageHoursPerDay,
    bool IsValidForMonth,
    List<string> ValidationErrors,
    List<DailyValidationDetail> DailyDetails
);

public record DailyValidationDetail(
    int Day,
    DateOnly Date,
    bool HasShift,
    float WorkingHours,
    int AcceptedOrders,
    bool IsValid,
    string Reason
);

public async Task<Result<HousingDetailedDailyPerformanceReport>> GetHousingDetailedDailyPerformanceAsync(
    DateOnly startDate,
    DateOnly endDate,
    CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<HousingDetailedDailyPerformanceReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        try
        {
            var totalExpectedDays = endDate.DayNumber - startDate.DayNumber + 1;

            // Get all shifts for company 1 with housing data
            var shifts = await _dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                        .ThenInclude(e => e.Housing)
                .Where(s => s.CompanyId == 1 &&
                           s.ShiftDate >= startDate &&
                           s.ShiftDate <= endDate &&
                           s.Rider.Employee.Housing != null)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<HousingDetailedDailyPerformanceReport>(
                    new Error("No shifts found for the specified period", "no_data", 404));
            }

            // Group by housing
            var housingGroups = shifts
                .GroupBy(s => new {
                    HousingId = s.Rider.Employee.Housing.Id,
                    HousingName = s.Rider.Employee.Housing.Name
                });

            var housingDetails = new List<HousingPerformanceDetail>();

            // Track global metrics
            int globalTotalWorkingDays = 0;
            int globalTotalAbsentDays = 0;
            float globalTotalHours = 0;
            float globalTotalTargetHours = 0;
            int globalTotalOrders = 0;
            int globalTotalTargetOrders = 0;

            foreach (var housingGroup in housingGroups)
            {
                var housingShifts = housingGroup.ToList();
                var riderGroups = housingShifts.GroupBy(s => s.RiderId);
                var riderDetails = new List<RiderDailyPerformanceDetail>();

                // Housing-level metrics
                int housingTotalWorkingDays = 0;
                int housingTotalAbsentDays = 0;
                float housingTotalHours = 0;
                float housingTotalTargetHours = 0;
                int housingTotalOrders = 0;
                int housingTotalTargetOrders = 0;
                var attendanceRates = new List<decimal>();
                var hoursCompletionRates = new List<decimal>();
                var ordersCompletionRates = new List<decimal>();

                foreach (var riderGroup in riderGroups)
                {
                    var rider = riderGroup.First().Rider;
                    if (rider?.Employee == null) continue;

                    var riderShifts = riderGroup.ToList();
                    var shiftDictionary = riderShifts.ToDictionary(s => s.ShiftDate);

                    // Build daily entries
                    var dailyEntries = new List<DailyPerformanceEntry>();
                    var currentDate = startDate;
                    int workingDays = 0;
                    int absentDays = 0;
                    float totalHours = 0;
                    int totalOrders = 0;
                    int totalRejected = 0;

                    while (currentDate <= endDate)
                    {
                        if (shiftDictionary.TryGetValue(currentDate, out var shift))
                        {
                            workingDays++;
                            totalHours += shift.WorkingHours;
                            totalOrders += shift.AcceptedDailyOrders;
                            totalRejected += shift.RejectedDailyOrders;

                            var hoursDiff = shift.WorkingHours - TARGET_HOURS_PER_DAY;
                            var ordersDiff = shift.AcceptedDailyOrders - TARGET_ORDERS_PER_DAY;

                            dailyEntries.Add(new DailyPerformanceEntry(
                                Date: currentDate,
                                IsPresent: true,
                                WorkingHours: shift.WorkingHours,
                                TargetHours: TARGET_HOURS_PER_DAY,
                                HoursDifference: hoursDiff,
                                AcceptedOrders: shift.AcceptedDailyOrders,
                                RejectedOrders: shift.RejectedDailyOrders,
                                TargetOrders: TARGET_ORDERS_PER_DAY,
                                OrdersDifference: ordersDiff,
                                ShiftStatus: shift.ShiftStatus,
                                PerformanceLevel: DeterminePerformanceLevel(
                                    shift.WorkingHours,
                                    shift.AcceptedDailyOrders,
                                    TARGET_HOURS_PER_DAY,
                                    TARGET_ORDERS_PER_DAY)
                            ));
                        }
                        else
                        {
                            absentDays++;
                            dailyEntries.Add(new DailyPerformanceEntry(
                                Date: currentDate,
                                IsPresent: false,
                                WorkingHours: 0,
                                TargetHours: TARGET_HOURS_PER_DAY,
                                HoursDifference: -TARGET_HOURS_PER_DAY,
                                AcceptedOrders: 0,
                                RejectedOrders: 0,
                                TargetOrders: TARGET_ORDERS_PER_DAY,
                                OrdersDifference: -TARGET_ORDERS_PER_DAY,
                                ShiftStatus: "Absent",
                                PerformanceLevel: "Absent"
                            ));
                        }
                        currentDate = currentDate.AddDays(1);
                    }

                    // Calculate rider summary metrics
                    var targetHours = totalExpectedDays * TARGET_HOURS_PER_DAY;
                    var targetOrders = totalExpectedDays * TARGET_ORDERS_PER_DAY;
                    var attendanceRate = (decimal)workingDays / totalExpectedDays * 100;
                    var hoursCompletionRate = targetHours > 0 ? (decimal)totalHours / (decimal)targetHours * 100 : 0;
                    var ordersCompletionRate = targetOrders > 0 ? (decimal)totalOrders / targetOrders * 100 : 0;
                    var overallScore = (attendanceRate + hoursCompletionRate + ordersCompletionRate) / 3;

                    var periodSummary = new RiderPeriodSummary(
                        TotalWorkingDays: workingDays,
                        TotalAbsentDays: absentDays,
                        TotalWorkingHours: totalHours,
                        TotalTargetHours: targetHours,
                        TotalHoursDifference: totalHours - targetHours,
                        TotalAcceptedOrders: totalOrders,
                        TotalRejectedOrders: totalRejected,
                        TotalTargetOrders: targetOrders,
                        TotalOrdersDifference: totalOrders - targetOrders,
                        AverageHoursPerDay: workingDays > 0 ? totalHours / workingDays : 0,
                        AverageOrdersPerDay: workingDays > 0 ? (decimal)totalOrders / workingDays : 0,
                        AttendanceRate: attendanceRate,
                        HoursCompletionRate: hoursCompletionRate,
                        OrdersCompletionRate: ordersCompletionRate,
                        OverallPerformanceScore: overallScore
                    );

                    riderDetails.Add(new RiderDailyPerformanceDetail(
                        RiderId: rider.Id,
                        IqamaNo: rider.EmployeeIqamaNo,
                        RiderNameAR: rider.Employee.NameAR,
                        RiderNameEN: rider.Employee.NameEN,
                        WorkingId: rider.WorkingId ?? "0",
                        DailyEntries: dailyEntries,
                        PeriodSummary: periodSummary
                    ));

                    // Accumulate housing metrics
                    housingTotalWorkingDays += workingDays;
                    housingTotalAbsentDays += absentDays;
                    housingTotalHours += totalHours;
                    housingTotalTargetHours += targetHours;
                    housingTotalOrders += totalOrders;
                    housingTotalTargetOrders += targetOrders;
                    attendanceRates.Add(attendanceRate);
                    hoursCompletionRates.Add(hoursCompletionRate);
                    ordersCompletionRates.Add(ordersCompletionRate);
                }

                // Sort riders by overall performance score
                riderDetails = riderDetails
                    .OrderByDescending(r => r.PeriodSummary.OverallPerformanceScore)
                    .ToList();

                // Calculate housing summary
                var housingSummary = new HousingSummaryMetrics(
                    TotalRiders: riderDetails.Count,
                    TotalWorkingDays: housingTotalWorkingDays,
                    TotalAbsentDays: housingTotalAbsentDays,
                    TotalWorkingHours: housingTotalHours,
                    TotalTargetHours: housingTotalTargetHours,
                    TotalHoursDifference: housingTotalHours - housingTotalTargetHours,
                    TotalAcceptedOrders: housingTotalOrders,
                    TotalTargetOrders: housingTotalTargetOrders,
                    TotalOrdersDifference: housingTotalOrders - housingTotalTargetOrders,
                    AverageAttendanceRate: attendanceRates.Any() ? attendanceRates.Average() : 0,
                    AverageHoursCompletionRate: hoursCompletionRates.Any() ? hoursCompletionRates.Average() : 0,
                    AverageOrdersCompletionRate: ordersCompletionRates.Any() ? ordersCompletionRates.Average() : 0,
                    OverallHousingScore: attendanceRates.Any()
                        ? (attendanceRates.Average() + hoursCompletionRates.Average() + ordersCompletionRates.Average()) / 3
                        : 0
                );

                housingDetails.Add(new HousingPerformanceDetail(
                    HousingId: housingGroup.Key.HousingId,
                    HousingName: housingGroup.Key.HousingName,
                    Riders: riderDetails,
                    HousingSummary: housingSummary
                ));

                // Accumulate global metrics
                globalTotalWorkingDays += housingTotalWorkingDays;
                globalTotalAbsentDays += housingTotalAbsentDays;
                globalTotalHours += housingTotalHours;
                globalTotalTargetHours += housingTotalTargetHours;
                globalTotalOrders += housingTotalOrders;
                globalTotalTargetOrders += housingTotalTargetOrders;
            }

            // Sort housings by overall score
            housingDetails = housingDetails
                .OrderByDescending(h => h.HousingSummary.OverallHousingScore)
                .ToList();

            // Calculate report summary
            var totalRiders = housingDetails.Sum(h => h.Riders.Count);
            var companyAttendanceRate = globalTotalTargetHours > 0
                ? (decimal)(globalTotalWorkingDays) / (totalRiders * totalExpectedDays) * 100
                : 0;
            var companyHoursRate = globalTotalTargetHours > 0
                ? (decimal)globalTotalHours / (decimal)globalTotalTargetHours * 100
                : 0;
            var companyOrdersRate = globalTotalTargetOrders > 0
                ? (decimal)globalTotalOrders / globalTotalTargetOrders * 100
                : 0;

            var summary = new ReportSummary(
                TotalHousings: housingDetails.Count,
                TotalRiders: totalRiders,
                TotalWorkingDays: globalTotalWorkingDays,
                TotalAbsentDays: globalTotalAbsentDays,
                GrandTotalHours: globalTotalHours,
                GrandTotalTargetHours: globalTotalTargetHours,
                GrandTotalOrders: globalTotalOrders,
                GrandTotalTargetOrders: globalTotalTargetOrders,
                CompanyWideAttendanceRate: companyAttendanceRate,
                CompanyWideHoursCompletionRate: companyHoursRate,
                CompanyWideOrdersCompletionRate: companyOrdersRate
            );

            var report = new HousingDetailedDailyPerformanceReport(
                StartDate: startDate,
                EndDate: endDate,
                TotalExpectedDays: totalExpectedDays,
                HousingDetails: housingDetails,
                Summary: summary
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<HousingDetailedDailyPerformanceReport>(
                new Error($"Error generating detailed performance report: {ex.Message}", "server_error", 500));
        }
    }

    // Helper method to determine performance level
    private string DeterminePerformanceLevel(
        float actualHours,
        int actualOrders,
        float targetHours,
        int targetOrders)
    {
        var hoursPercentage = actualHours / targetHours * 100;
        var ordersPercentage = (decimal)actualOrders / targetOrders * 100;
        var averagePercentage = (decimal)(hoursPercentage + (float)ordersPercentage) / 2;

        return averagePercentage switch
        {
            >= 110m => "Excellent",
            >= 90m => "Good",
            >= 70m => "Average",
            >= 50m => "Below Average",
            _ => "Poor"
        };
    }

    public async Task<Result<List<HousingRiderDailyDetailReport>>> GetAllHousingsRiderDailyDetailReportAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        // Get all shifts with rider and housing information
        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
                    .ThenInclude(e => e.Housing)
            .Where(s => s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate &&
                       !string.IsNullOrWhiteSpace(s.Rider.WorkingId))
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
            return Result.Success(new List<HousingRiderDailyDetailReport>());

        // Group by housing (from shift data)
        var housingGroups = shifts
            .Where(s => s.Rider?.Employee?.Housing != null)
            .GroupBy(s => new {
                HousingId = s.Rider.Employee.Housing.Id,
                HousingName = s.Rider.Employee.Housing.Name
            });

        var reports = new List<HousingRiderDailyDetailReport>();

        foreach (var housingGroup in housingGroups)
        {
            var riderGroups = housingGroup.GroupBy(s => s.RiderId);

            foreach (var riderGroup in riderGroups)
            {
                var rider = riderGroup.First().Rider;
                if (rider?.WorkingId == null) continue;

                var reportResult = await GetRiderDailyDetailReportAsync(
                    rider.WorkingId, startDate, endDate, cancellationToken);

                if (reportResult.IsSuccess)
                {
                    reports.Add(new HousingRiderDailyDetailReport(
                        HousingName: housingGroup.Key.HousingName,
                        RiderReport: reportResult.Value
                    ));
                }
            }
        }

        return Result.Success(reports);
    }

    public async Task<Result<List<HousingAllRidersSummaryReport>>> GetAllHousingsSummaryReportAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var totalExpectedDays = endDate.DayNumber - startDate.DayNumber + 1;

        // Get all shifts for company 1 with housing data from shift
        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
                    .ThenInclude(e => e.Housing)
            .Where(s => s.CompanyId == 1 &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate &&
                       s.Rider.Employee.Housing != null)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
            return Result.Success(new List<HousingAllRidersSummaryReport>());

        // Group by housing from shift data
        var housingGroups = shifts.GroupBy(s => new {
            HousingId = s.Rider.Employee.Housing.Id,
            HousingName = s.Rider.Employee.Housing.Name
        });

        var reports = new List<HousingAllRidersSummaryReport>();

        foreach (var housingGroup in housingGroups)
        {
            var housingShifts = housingGroup.ToList();
            var riderGroups = housingShifts.GroupBy(s => s.RiderId);
            var riderSummaries = new List<RiderSummaryDetail>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var riderShifts = group.ToList();
                var actualWorkingDays = riderShifts.Count;
                var missingDays = totalExpectedDays - actualWorkingDays;

                var totalWorkingHours = riderShifts.Sum(s => s.WorkingHours);
                var targetWorkingHours = totalExpectedDays * TARGET_HOURS_PER_DAY;
                var hoursDifference = totalWorkingHours - targetWorkingHours;

                var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders);
                var targetOrders = totalExpectedDays * TARGET_ORDERS_PER_DAY;
                var ordersDifference = totalOrders - targetOrders;

                riderSummaries.Add(new RiderSummaryDetail(
                    RiderId: rider.Id,
                    IqamaNo: rider.EmployeeIqamaNo,
                    RiderNameAR: rider.Employee.NameAR,
                    RiderNameEN: rider.Employee.NameEN,
                    WorkingId: riderShifts.First().WorkingId,
                    ActualWorkingDays: actualWorkingDays,
                    MissingDays: missingDays > 0 ? -missingDays : 0,
                    TotalWorkingHours: totalWorkingHours,
                    TargetWorkingHours: targetWorkingHours,
                    HoursDifference: hoursDifference,
                    TotalOrders: totalOrders,
                    TargetOrders: targetOrders,
                    OrdersDifference: ordersDifference
                ));
            }

            riderSummaries = riderSummaries.OrderByDescending(r => r.TotalOrders).ToList();

            var totals = new SummaryTotals(
                TotalRiders: riderSummaries.Count,
                TotalWorkingDays: riderSummaries.Sum(r => r.ActualWorkingDays),
                TotalMissingDays: riderSummaries.Sum(r => Math.Abs(r.MissingDays)),
                TotalWorkingHours: riderSummaries.Sum(r => r.TotalWorkingHours),
                TotalTargetHours: riderSummaries.Sum(r => r.TargetWorkingHours),
                HoursDifference: riderSummaries.Sum(r => r.HoursDifference),
                TotalOrders: riderSummaries.Sum(r => r.TotalOrders),
                TotalTargetOrders: riderSummaries.Sum(r => r.TargetOrders),
                OrdersDifference: riderSummaries.Sum(r => r.OrdersDifference)
            );

            var summaryReport = new AllRidersSummaryReport(
                StartDate: startDate,
                EndDate: endDate,
                TotalExpectedDays: totalExpectedDays,
                RiderSummaries: riderSummaries,
                Totals: totals
            );

            reports.Add(new HousingAllRidersSummaryReport(
                HousingName: housingGroup.Key.HousingName,
                SummaryReport: summaryReport
            ));
        }

        return Result.Success(reports);
    }

    public async Task<Result<List<HousingAllRidersSummaryReport>>> GetAllHousingsSummaryReportAsync2(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var totalExpectedDays = endDate.DayNumber - startDate.DayNumber + 1;

        // Get all shifts for company 2 with housing data from shift
        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
                    .ThenInclude(e => e.Housing)
            .Where(s => s.CompanyId == 2 &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate &&
                       s.Rider.Employee.Housing != null)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
            return Result.Success(new List<HousingAllRidersSummaryReport>());

        // Group by housing from shift data
        var housingGroups = shifts.GroupBy(s => new {
            HousingId = s.Rider.Employee.Housing.Id,
            HousingName = s.Rider.Employee.Housing.Name
        });

        var reports = new List<HousingAllRidersSummaryReport>();

        foreach (var housingGroup in housingGroups)
        {
            var housingShifts = housingGroup.ToList();
            var riderGroups = housingShifts.GroupBy(s => s.RiderId);
            var riderSummaries = new List<RiderSummaryDetail>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var riderShifts = group.ToList();
                var actualWorkingDays = riderShifts.Count;

                // Count days with less than 10 working hours
                var daysWithLessThan10Hours = riderShifts.Count(s => s.WorkingHours < 10);
                var missingDays = (totalExpectedDays - actualWorkingDays) + daysWithLessThan10Hours;

                var totalWorkingHours = riderShifts.Sum(s => s.WorkingHours);
                var targetWorkingHours = totalExpectedDays * TARGET_HOURS_PER_DAY2;
                var hoursDifference = totalWorkingHours - targetWorkingHours;

                var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders);
                var targetOrders = totalExpectedDays * TARGET_ORDERS_PER_DAY2;
                var ordersDifference = totalOrders - targetOrders;

                riderSummaries.Add(new RiderSummaryDetail(
                    RiderId: rider.Id,
                    IqamaNo: rider.EmployeeIqamaNo,
                    RiderNameAR: rider.Employee.NameAR,
                    RiderNameEN: rider.Employee.NameEN,
                    WorkingId: riderShifts.First().WorkingId,
                    ActualWorkingDays: actualWorkingDays,
                    MissingDays: missingDays > 0 ? -missingDays : 0,
                    TotalWorkingHours: totalWorkingHours,
                    TargetWorkingHours: targetWorkingHours,
                    HoursDifference: hoursDifference,
                    TotalOrders: totalOrders,
                    TargetOrders: targetOrders,
                    OrdersDifference: ordersDifference
                ));
            }

            riderSummaries = riderSummaries.OrderByDescending(r => r.TotalOrders).ToList();

            var totals = new SummaryTotals(
                TotalRiders: riderSummaries.Count,
                TotalWorkingDays: riderSummaries.Sum(r => r.ActualWorkingDays),
                TotalMissingDays: riderSummaries.Sum(r => Math.Abs(r.MissingDays)),
                TotalWorkingHours: riderSummaries.Sum(r => r.TotalWorkingHours),
                TotalTargetHours: riderSummaries.Sum(r => r.TargetWorkingHours),
                HoursDifference: riderSummaries.Sum(r => r.HoursDifference),
                TotalOrders: riderSummaries.Sum(r => r.TotalOrders),
                TotalTargetOrders: riderSummaries.Sum(r => r.TargetOrders),
                OrdersDifference: riderSummaries.Sum(r => r.OrdersDifference)
            );

            var summaryReport = new AllRidersSummaryReport(
                StartDate: startDate,
                EndDate: endDate,
                TotalExpectedDays: totalExpectedDays,
                RiderSummaries: riderSummaries,
                Totals: totals
            );

            reports.Add(new HousingAllRidersSummaryReport(
                HousingName: housingGroup.Key.HousingName,
                SummaryReport: summaryReport
            ));
        }

        return Result.Success(reports);
    }

    public async Task<Result<List<HousingRejectionReport>>> GetAllHousingsRejectionReportAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var totalDays = endDate.DayNumber - startDate.DayNumber + 1;

        // Get all shifts with housing data from shift
        var shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
                    .ThenInclude(e => e.Housing)
            .Where(s => s.Rider.CompanyId == 1 &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate &&
                       s.Rider.Employee.Housing != null)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
            return Result.Success(new List<HousingRejectionReport>());

        // Group by housing from shift data
        var housingGroups = shifts.GroupBy(s => new {
            HousingId = s.Rider.Employee.Housing.Id,
            HousingName = s.Rider.Employee.Housing.Name
        });

        var reports = new List<HousingRejectionReport>();

        foreach (var housingGroup in housingGroups)
        {
            var housingShifts = housingGroup.ToList();
            var riderGroups = housingShifts.GroupBy(s => s.RiderId);
            var riderDetails = new List<RiderRejectionDetail>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var riderShifts = group.ToList();
                var totalShifts = riderShifts.Count;
                var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders);
                var targetOrders = totalDays * TARGET_ORDERS_PER_DAY;
                var totalRejections = riderShifts.Sum(s => s.RejectedDailyOrders);
                var totalRealRejections = riderShifts.Sum(s => s.RealRejectedDailyOrders);

                var rejectionRate = totalOrders > 0
                    ? Math.Round((decimal)totalRejections / totalOrders * 100, 2)
                    : 0;

                var realRejectionRate = totalOrders > 0
                    ? Math.Round((decimal)totalRealRejections / totalOrders * 100, 2)
                    : 0;

                riderDetails.Add(new RiderRejectionDetail(
                    RiderId: rider.Id,
                    IqamaNo: rider.EmployeeIqamaNo,
                    RiderNameAR: rider.Employee.NameAR,
                    RiderNameEN: rider.Employee.NameEN,
                    WorkingId: riderShifts.First().WorkingId,
                    TotalShifts: totalShifts,
                    TotalOrders: totalOrders,
                    TargetOrders: targetOrders,
                    TotalRejections: totalRejections,
                    TotalRealRejections: totalRealRejections,
                    RejectionRate: rejectionRate,
                    RealRejectionRate: realRejectionRate
                ));
            }

            riderDetails = riderDetails.OrderByDescending(r => r.TotalRealRejections).ToList();

            var totalAllOrders = riderDetails.Sum(r => r.TotalOrders);
            var totalAllRejections = riderDetails.Sum(r => r.TotalRejections);
            var totalAllRealRejections = riderDetails.Sum(r => r.TotalRealRejections);

            var overallRejectionRate = totalAllOrders > 0
                ? Math.Round((decimal)totalAllRejections / totalAllOrders * 100, 2)
                : 0;

            var overallRealRejectionRate = totalAllOrders > 0
                ? Math.Round((decimal)totalAllRealRejections / totalAllOrders * 100, 2)
                : 0;

            var totals = new RejectionTotals(
                TotalRiders: riderDetails.Count,
                TotalShifts: riderDetails.Sum(r => r.TotalShifts),
                TotalOrders: totalAllOrders,
                TotalTargetOrders: riderDetails.Sum(r => r.TargetOrders),
                TotalRejections: totalAllRejections,
                TotalRealRejections: totalAllRealRejections,
                OverallRejectionRate: overallRejectionRate,
                OverallRealRejectionRate: overallRealRejectionRate
            );

            var rejectionReport = new RejectionReport(
                StartDate: startDate,
                EndDate: endDate,
                TotalDays: totalDays,
                RiderDetails: riderDetails,
                Totals: totals
            );

            reports.Add(new HousingRejectionReport(
                HousingName: housingGroup.Key.HousingName,
                RejectionReport: rejectionReport
            ));
        }

        return Result.Success(reports);
    }

    // Note: GetComprehensiveDashboardAsync already uses housing from shift data
    // via the GetHousingStatistics method which correctly filters shifts where
    // s.Rider?.Employee?.Housing != null
    public async Task<Result<RiderMonthlyHistory>> GetRiderMonthlyHistoryAsync(
        long riderIqamaNo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get rider details
            var rider = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == riderIqamaNo, cancellationToken);

            if (rider == null)
            {
                return Result.Failure<RiderMonthlyHistory>(
                    new Error($"Rider with Iqama number {riderIqamaNo} not found", "not_found", 404));
            }

            // Get all shifts for this rider
            var shifts = await _dbcontext.RiderShifts
                .Where(s => s.RiderId == rider.Id)
                .OrderBy(s => s.ShiftDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<RiderMonthlyHistory>(
                    new Error("No shift history found for this rider", "no_data", 404));
            }

            // Calculate monthly summaries
            var firstShiftDate = shifts.First().ShiftDate;
            var lastShiftDate = shifts.Last().ShiftDate;
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

            // Use the later of last shift date or today
            var endDate = lastShiftDate > today ? lastShiftDate : today;

            var monthlyData = GenerateMonthlyShiftSummaries(shifts, firstShiftDate, endDate);

            var history = new RiderMonthlyHistory(
                IqamaNo: riderIqamaNo,
                RiderName: rider.Employee.NameAR,
                WorkingId: rider.WorkingId ?? "0",
                FirstShiftDate: firstShiftDate,
                LastShiftDate: lastShiftDate,
                TotalMonths: monthlyData.Count,
                MonthlyData: monthlyData
            );

            return Result.Success(history);
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderMonthlyHistory>(
                new Error($"Error generating rider monthly history: {ex.Message}", "server_error", 500));
        }
    }

    // Helper method for generating monthly summaries
    private List<MonthlyShiftSummary> GenerateMonthlyShiftSummaries(
        List<RiderShift> shifts,
        DateOnly startDate,
        DateOnly endDate)
    {
        var monthlyData = new List<MonthlyShiftSummary>();
        var currentDate = new DateOnly(startDate.Year, startDate.Month, 1);
        var finalDate = new DateOnly(endDate.Year, endDate.Month, 1);

        // Group shifts by year and month
        var shiftsByMonth = shifts
            .GroupBy(s => new { s.ShiftDate.Year, s.ShiftDate.Month })
            .ToDictionary(g => (g.Key.Year, g.Key.Month), g => g.ToList());

        // Iterate through each month from start to end
        while (currentDate <= finalDate)
        {
            var year = currentDate.Year;
            var month = currentDate.Month;

            if (shiftsByMonth.TryGetValue((year, month), out var monthShifts))
            {
                var totalShifts = monthShifts.Count;
                var completedShifts = monthShifts.Count(s => s.ShiftStatus == "Completed");
                var incompleteShifts = monthShifts.Count(s => s.ShiftStatus == "Incomplete");
                var failedShifts = monthShifts.Count(s => s.ShiftStatus == "Failed");

                var completionRate = totalShifts > 0
                    ? (decimal)completedShifts / totalShifts * 100
                    : 0;

                monthlyData.Add(new MonthlyShiftSummary(
                    Year: year,
                    Month: month,
                    MonthName: new DateTime(year, month, 1).ToString("MMMM"),
                    TotalShifts: totalShifts,
                    TotalAcceptedOrders: monthShifts.Sum(s => s.AcceptedDailyOrders),
                    TotalRejectedOrders: monthShifts.Sum(s => s.RejectedDailyOrders),
                    TotalRealRejectedOrders: monthShifts.Sum(s => s.RealRejectedDailyOrders),
                    TotalWorkingHours: monthShifts.Sum(s => s.WorkingHours),
                    CompletedShifts: completedShifts,
                    IncompleteShifts: incompleteShifts,
                    FailedShifts: failedShifts,
                    CompletionRate: completionRate
                ));
            }
            else
            {
                // Month with no shifts
                monthlyData.Add(new MonthlyShiftSummary(
                    Year: year,
                    Month: month,
                    MonthName: new DateTime(year, month, 1).ToString("MMMM"),
                    TotalShifts: 0,
                    TotalAcceptedOrders: 0,
                    TotalRejectedOrders: 0,
                    TotalRealRejectedOrders: 0,
                    TotalWorkingHours: 0,
                    CompletedShifts: 0,
                    IncompleteShifts: 0,
                    FailedShifts: 0,
                    CompletionRate: 0
                ));
            }

            currentDate = currentDate.AddMonths(1);
        }

        return monthlyData;
    }

    //public async Task<Result<List<HousingRiderDailyDetailReport>>> GetAllHousingsRiderDailyDetailReportAsync(
    //DateOnly startDate,
    //DateOnly endDate,
    //CancellationToken cancellationToken = default)
    //{
    //    var housings = await _dbcontext.Housings
    //        .Include(h => h.Employees)
    //        .ToListAsync(cancellationToken);

    //    var reports = new List<HousingRiderDailyDetailReport>();

    //    foreach (var housing in housings)
    //    {
    //        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();
    //        var riders = await _dbcontext.RiderDetails
    //            .Include(r => r.Employee)
    //            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo) &&
    //                       !string.IsNullOrWhiteSpace(r.WorkingId))
    //            .ToListAsync(cancellationToken);

    //        foreach (var rider in riders)
    //        {
    //            var reportResult = await GetRiderDailyDetailReportAsync(
    //                rider.WorkingId!, startDate, endDate, cancellationToken);

    //            if (reportResult.IsSuccess)
    //            {
    //                reports.Add(new HousingRiderDailyDetailReport(
    //                    HousingName: housing.Name,
    //                    RiderReport: reportResult.Value
    //                ));
    //            }
    //        }
    //    }

    //    return Result.Success(reports);
    //}

    //public async Task<Result<List<HousingAllRidersSummaryReport>>> GetAllHousingsSummaryReportAsync(
    //    DateOnly startDate,
    //    DateOnly endDate,
    //    CancellationToken cancellationToken = default)
    //{
    //    var housings = await _dbcontext.Housings
    //        .Include(h => h.Employees)
    //        .ToListAsync(cancellationToken);

    //    var reports = new List<HousingAllRidersSummaryReport>();

    //    foreach (var housing in housings)
    //    {
    //        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();
    //        var riderIds = await _dbcontext.RiderDetails
    //            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
    //            .Select(r => r.Id)
    //            .ToListAsync(cancellationToken);

    //        if (!riderIds.Any()) continue;

    //        var totalExpectedDays = endDate.DayNumber - startDate.DayNumber + 1;

    //        var shifts = await _dbcontext.RiderShifts
    //            .Include(s => s.Rider)
    //                .ThenInclude(r => r.Employee)
    //            .Where(s => riderIds.Contains(s.RiderId) &&
    //                       s.CompanyId == 1 &&
    //                       s.ShiftDate >= startDate &&
    //                       s.ShiftDate <= endDate)
    //            .ToListAsync(cancellationToken);

    //        if (!shifts.Any()) continue;

    //        var riderGroups = shifts.GroupBy(s => s.RiderId);
    //        var riderSummaries = new List<RiderSummaryDetail>();

    //        foreach (var group in riderGroups)
    //        {
    //            var rider = group.First().Rider;
    //            if (rider?.Employee == null) continue;

    //            var riderShifts = group.ToList();
    //            var actualWorkingDays = riderShifts.Count;
    //            var missingDays = totalExpectedDays - actualWorkingDays;

    //            var totalWorkingHours = riderShifts.Sum(s => s.WorkingHours);
    //            var targetWorkingHours = totalExpectedDays * TARGET_HOURS_PER_DAY;
    //            var hoursDifference = totalWorkingHours - targetWorkingHours;

    //            var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders);
    //            var targetOrders = totalExpectedDays * TARGET_ORDERS_PER_DAY;
    //            var ordersDifference = totalOrders - targetOrders;

    //            riderSummaries.Add(new RiderSummaryDetail(
    //                RiderId: rider.Id,
    //                IqamaNo: rider.EmployeeIqamaNo,
    //                RiderNameAR: rider.Employee.NameAR,
    //                RiderNameEN: rider.Employee.NameEN,
    //                WorkingId: riderShifts.First().WorkingId,
    //                ActualWorkingDays: actualWorkingDays,
    //                MissingDays: missingDays > 0 ? -missingDays : 0,
    //                TotalWorkingHours: totalWorkingHours,
    //                TargetWorkingHours: targetWorkingHours,
    //                HoursDifference: hoursDifference,
    //                TotalOrders: totalOrders,
    //                TargetOrders: targetOrders,
    //                OrdersDifference: ordersDifference
    //            ));
    //        }

    //        riderSummaries = riderSummaries.OrderByDescending(r => r.TotalOrders).ToList();

    //        var totals = new SummaryTotals(
    //            TotalRiders: riderSummaries.Count,
    //            TotalWorkingDays: riderSummaries.Sum(r => r.ActualWorkingDays),
    //            TotalMissingDays: riderSummaries.Sum(r => Math.Abs(r.MissingDays)),
    //            TotalWorkingHours: riderSummaries.Sum(r => r.TotalWorkingHours),
    //            TotalTargetHours: riderSummaries.Sum(r => r.TargetWorkingHours),
    //            HoursDifference: riderSummaries.Sum(r => r.HoursDifference),
    //            TotalOrders: riderSummaries.Sum(r => r.TotalOrders),
    //            TotalTargetOrders: riderSummaries.Sum(r => r.TargetOrders),
    //            OrdersDifference: riderSummaries.Sum(r => r.OrdersDifference)
    //        );

    //        var summaryReport = new AllRidersSummaryReport(
    //            StartDate: startDate,
    //            EndDate: endDate,
    //            TotalExpectedDays: totalExpectedDays,
    //            RiderSummaries: riderSummaries,
    //            Totals: totals
    //        );

    //        reports.Add(new HousingAllRidersSummaryReport(
    //            HousingName: housing.Name,
    //            SummaryReport: summaryReport
    //        ));
    //    }

    //    return Result.Success(reports);
    //}
    //public async Task<Result<List<HousingAllRidersSummaryReport>>> GetAllHousingsSummaryReportAsync2(
    //    DateOnly startDate,
    //    DateOnly endDate,
    //    CancellationToken cancellationToken = default)
    //{
    //    var housings = await _dbcontext.Housings
    //        .Include(h => h.Employees)
    //        .ToListAsync(cancellationToken);

    //    var reports = new List<HousingAllRidersSummaryReport>();

    //    foreach (var housing in housings)
    //    {
    //        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();
    //        var riderIds = await _dbcontext.RiderDetails
    //            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
    //            .Select(r => r.Id)
    //            .ToListAsync(cancellationToken);

    //        if (!riderIds.Any()) continue;

    //        var totalExpectedDays = endDate.DayNumber - startDate.DayNumber + 1;

    //        var shifts = await _dbcontext.RiderShifts
    //            .Include(s => s.Rider)
    //                .ThenInclude(r => r.Employee)
    //            .Where(s => riderIds.Contains(s.RiderId) &&
    //                       s.CompanyId == 2 &&
    //                       s.ShiftDate >= startDate &&
    //                       s.ShiftDate <= endDate)
    //            .ToListAsync(cancellationToken);

    //        if (!shifts.Any()) continue;

    //        var riderGroups = shifts.GroupBy(s => s.RiderId);
    //        var riderSummaries = new List<RiderSummaryDetail>();

    //        foreach (var group in riderGroups)
    //        {
    //            var rider = group.First().Rider;
    //            if (rider?.Employee == null) continue;

    //            var riderShifts = group.ToList();
    //            var actualWorkingDays = riderShifts.Count;

    //            // Count days with less than 10 working hours
    //            var daysWithLessThan10Hours = riderShifts.Count(s => s.WorkingHours < 10);

    //            // Calculate missing days: days with no shifts + days with less than 10 hours
    //            var missingDays = (totalExpectedDays - actualWorkingDays) + daysWithLessThan10Hours;

    //            var totalWorkingHours = riderShifts.Sum(s => s.WorkingHours);
    //            var targetWorkingHours = totalExpectedDays * TARGET_HOURS_PER_DAY2;
    //            var hoursDifference = totalWorkingHours - targetWorkingHours;

    //            var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders);
    //            var targetOrders = totalExpectedDays * TARGET_ORDERS_PER_DAY2;
    //            var ordersDifference = totalOrders - targetOrders;

    //            riderSummaries.Add(new RiderSummaryDetail(
    //                RiderId: rider.Id,
    //                IqamaNo: rider.EmployeeIqamaNo,
    //                RiderNameAR: rider.Employee.NameAR,
    //                RiderNameEN: rider.Employee.NameEN,
    //                WorkingId: riderShifts.First().WorkingId,
    //                ActualWorkingDays: actualWorkingDays,
    //                MissingDays: missingDays > 0 ? -missingDays : 0,
    //                TotalWorkingHours: totalWorkingHours,
    //                TargetWorkingHours: targetWorkingHours,
    //                HoursDifference: hoursDifference,
    //                TotalOrders: totalOrders,
    //                TargetOrders: targetOrders,
    //                OrdersDifference: ordersDifference
    //            ));
    //        }

    //        riderSummaries = riderSummaries.OrderByDescending(r => r.TotalOrders).ToList();

    //        var totals = new SummaryTotals(
    //            TotalRiders: riderSummaries.Count,
    //            TotalWorkingDays: riderSummaries.Sum(r => r.ActualWorkingDays),
    //            TotalMissingDays: riderSummaries.Sum(r => Math.Abs(r.MissingDays)),
    //            TotalWorkingHours: riderSummaries.Sum(r => r.TotalWorkingHours),
    //            TotalTargetHours: riderSummaries.Sum(r => r.TargetWorkingHours),
    //            HoursDifference: riderSummaries.Sum(r => r.HoursDifference),
    //            TotalOrders: riderSummaries.Sum(r => r.TotalOrders),
    //            TotalTargetOrders: riderSummaries.Sum(r => r.TargetOrders),
    //            OrdersDifference: riderSummaries.Sum(r => r.OrdersDifference)
    //        );

    //        var summaryReport = new AllRidersSummaryReport(
    //            StartDate: startDate,
    //            EndDate: endDate,
    //            TotalExpectedDays: totalExpectedDays,
    //            RiderSummaries: riderSummaries,
    //            Totals: totals
    //        );

    //        reports.Add(new HousingAllRidersSummaryReport(
    //            HousingName: housing.Name,
    //            SummaryReport: summaryReport
    //        ));
    //    }

    //    return Result.Success(reports);
    //}
    //public async Task<Result<List<HousingRejectionReport>>> GetAllHousingsRejectionReportAsync(
    //    DateOnly startDate,
    //    DateOnly endDate,
    //    CancellationToken cancellationToken = default)
    //{
    //    var housings = await _dbcontext.Housings
    //        .Include(h => h.Employees)
    //        .ToListAsync(cancellationToken);

    //    var reports = new List<HousingRejectionReport>();
    //    var totalDays = endDate.DayNumber - startDate.DayNumber + 1;

    //    foreach (var housing in housings)
    //    {
    //        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();
    //        var riderIds = await _dbcontext.RiderDetails
    //            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo) && r.CompanyId == 1)
    //            .Select(r => r.Id)
    //            .ToListAsync(cancellationToken);

    //        if (!riderIds.Any()) continue;

    //        var shifts = await _dbcontext.RiderShifts
    //            .Include(s => s.Rider)
    //                .ThenInclude(r => r.Employee)
    //            .Where(s => riderIds.Contains(s.RiderId) &&
    //                       s.ShiftDate >= startDate &&
    //                       s.ShiftDate <= endDate)
    //            .ToListAsync(cancellationToken);

    //        if (!shifts.Any()) continue;

    //        var riderGroups = shifts.GroupBy(s => s.RiderId);
    //        var riderDetails = new List<RiderRejectionDetail>();

    //        foreach (var group in riderGroups)
    //        {
    //            var rider = group.First().Rider;
    //            if (rider?.Employee == null) continue;

    //            var riderShifts = group.ToList();
    //            var totalShifts = riderShifts.Count;
    //            var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders);
    //            var targetOrders = totalDays * TARGET_ORDERS_PER_DAY;
    //            var totalRejections = riderShifts.Sum(s => s.RejectedDailyOrders);
    //            var totalRealRejections = riderShifts.Sum(s => s.RealRejectedDailyOrders);

    //            var rejectionRate = totalOrders > 0
    //                ? Math.Round((decimal)totalRejections / totalOrders * 100, 2)
    //                : 0;

    //            var realRejectionRate = totalOrders > 0
    //                ? Math.Round((decimal)totalRealRejections / totalOrders * 100, 2)
    //                : 0;

    //            riderDetails.Add(new RiderRejectionDetail(
    //                RiderId: rider.Id,
    //                IqamaNo: rider.EmployeeIqamaNo,
    //                RiderNameAR: rider.Employee.NameAR,
    //                RiderNameEN: rider.Employee.NameEN,
    //                WorkingId: riderShifts.First().WorkingId,
    //                TotalShifts: totalShifts,
    //                TotalOrders: totalOrders,
    //                TargetOrders: targetOrders,
    //                TotalRejections: totalRejections,
    //                TotalRealRejections: totalRealRejections,
    //                RejectionRate: rejectionRate,
    //                RealRejectionRate: realRejectionRate
    //            ));
    //        }

    //        riderDetails = riderDetails.OrderByDescending(r => r.TotalRealRejections).ToList();

    //        var totalAllOrders = riderDetails.Sum(r => r.TotalOrders);
    //        var totalAllRejections = riderDetails.Sum(r => r.TotalRejections);
    //        var totalAllRealRejections = riderDetails.Sum(r => r.TotalRealRejections);

    //        var overallRejectionRate = totalAllOrders > 0
    //            ? Math.Round((decimal)totalAllRejections / totalAllOrders * 100, 2)
    //            : 0;

    //        var overallRealRejectionRate = totalAllOrders > 0
    //            ? Math.Round((decimal)totalAllRealRejections / totalAllOrders * 100, 2)
    //            : 0;

    //        var totals = new RejectionTotals(
    //            TotalRiders: riderDetails.Count,
    //            TotalShifts: riderDetails.Sum(r => r.TotalShifts),
    //            TotalOrders: totalAllOrders,
    //            TotalTargetOrders: riderDetails.Sum(r => r.TargetOrders),
    //            TotalRejections: totalAllRejections,
    //            TotalRealRejections: totalAllRealRejections,
    //            OverallRejectionRate: overallRejectionRate,
    //            OverallRealRejectionRate: overallRealRejectionRate
    //        );

    //        var rejectionReport = new RejectionReport(
    //            StartDate: startDate,
    //            EndDate: endDate,
    //            TotalDays: totalDays,
    //            RiderDetails: riderDetails,
    //            Totals: totals
    //        );

    //        reports.Add(new HousingRejectionReport(
    //            HousingName: housing.Name,
    //            RejectionReport: rejectionReport
    //        ));
    //    }

    //    return Result.Success(reports);
    //}
    public async Task<Result<RiderDailyDetailReport>> GetRiderDailyDetailReportAsync(
    string workingId,
    DateOnly startDate,
    DateOnly endDate,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workingId))
            return Result.Failure<RiderDailyDetailReport>(
                new Error("Invalid working ID", "invalid_input", 400));

        if (endDate < startDate)
            return Result.Failure<RiderDailyDetailReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        try
        {
            var rider = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.WorkingId == workingId , cancellationToken);

            if (rider == null)
                return Result.Failure<RiderDailyDetailReport>(
                    new Error($"Rider with working ID {workingId} not found", "not_found", 404));

            var shifts = await _dbcontext.RiderShifts
                .Where(s => s.RiderId == rider.Id &&
                            s.CompanyId == 1 &&
                           s.ShiftDate >= startDate &&
                           s.ShiftDate <= endDate)
                .OrderBy(s => s.ShiftDate)
                .ToListAsync(cancellationToken);

            var shiftDictionary = shifts.ToDictionary(s => s.ShiftDate, s => s);

            var dailyDetails = new List<DailyShiftDetail>();
            var totalDays = endDate.DayNumber - startDate.DayNumber + 1;
            var currentDate = startDate;

            while (currentDate <= endDate)
            {
                if (shiftDictionary.TryGetValue(currentDate, out var shift))
                {
                    var hoursDiff = shift.WorkingHours - TARGET_HOURS_PER_DAY;
                    dailyDetails.Add(new DailyShiftDetail(
                        Date: currentDate,
                        HasShift: true,
                        AcceptedOrders: shift.AcceptedDailyOrders,
                        RejectedOrders: shift.RejectedDailyOrders,
                        RealRejectedOrders: shift.RealRejectedDailyOrders,
                        WorkingHours: shift.WorkingHours,
                        TargetHours: TARGET_HOURS_PER_DAY,
                        HoursDifference: hoursDiff,
                        ShiftStatus: shift.ShiftStatus
                    ));
                }
                else
                {
                    dailyDetails.Add(new DailyShiftDetail(
                        Date: currentDate,
                        HasShift: false,
                        AcceptedOrders: 0,
                        RejectedOrders: 0,
                        RealRejectedOrders: 0,
                        WorkingHours: 0,
                        TargetHours: TARGET_HOURS_PER_DAY,
                        HoursDifference: -TARGET_HOURS_PER_DAY,
                        ShiftStatus: "Missing"
                    ));
                }
                currentDate = currentDate.AddDays(1);
            }

            var totalWorkingDays = shifts.Count;
            var missingDays = totalDays - totalWorkingDays;
            var totalWorkingHours = shifts.Sum(s => s.WorkingHours);
            var targetWorkingHours = totalDays * TARGET_HOURS_PER_DAY;
            var hoursDifference = totalWorkingHours - targetWorkingHours;
            var totalOrders = shifts.Sum(s => s.AcceptedDailyOrders);
            var totalRejections = shifts.Sum(s => s.RejectedDailyOrders);
            var totalRealRejections = shifts.Sum(s => s.RealRejectedDailyOrders);

            var report = new RiderDailyDetailReport(
                RiderId: rider.Id,
                IqamaNo: rider.EmployeeIqamaNo,
                RiderNameAR: rider.Employee.NameAR,
                RiderNameEN: rider.Employee.NameEN,
                WorkingId: workingId,
                StartDate: startDate,
                EndDate: endDate,
                DailyDetails: dailyDetails,
                TotalWorkingDays: totalWorkingDays,
                MissingDays: missingDays,
                TotalWorkingHours: totalWorkingHours,
                TargetWorkingHours: targetWorkingHours,
                HoursDifference: hoursDifference,
                IsAboveTarget: hoursDifference >= 0,
                TotalOrders: totalOrders,
                TotalRejections: totalRejections,
                TotalRealRejections: totalRealRejections
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderDailyDetailReport>(
                new Error($"Error generating daily detail report: {ex.Message}", "server_error", 500));
        }
    }
    public async Task<Result<RiderDailyDetailReport>> GetRiderDailyDetailReportAsync2(
    string workingId,
    DateOnly startDate,
    DateOnly endDate,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workingId))
            return Result.Failure<RiderDailyDetailReport>(
                new Error("Invalid working ID", "invalid_input", 400));

        if (endDate < startDate)
            return Result.Failure<RiderDailyDetailReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        try
        {
            var rider = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.WorkingId == workingId , cancellationToken);

            if (rider == null)
                return Result.Failure<RiderDailyDetailReport>(
                    new Error($"Rider with working ID {workingId} not found", "not_found", 404));

            var shifts = await _dbcontext.RiderShifts
                .Where(s => s.RiderId == rider.Id &&
                            s.CompanyId == 2 &&
                           s.ShiftDate >= startDate &&
                           s.ShiftDate <= endDate)
                .OrderBy(s => s.ShiftDate)
                .ToListAsync(cancellationToken);

            var shiftDictionary = shifts.ToDictionary(s => s.ShiftDate, s => s);

            var dailyDetails = new List<DailyShiftDetail>();
            var totalDays = endDate.DayNumber - startDate.DayNumber + 1;
            var currentDate = startDate;

            while (currentDate <= endDate)
            {
                if (shiftDictionary.TryGetValue(currentDate, out var shift))
                {
                    var hoursDiff = shift.WorkingHours - TARGET_HOURS_PER_DAY2;
                    dailyDetails.Add(new DailyShiftDetail(
                        Date: currentDate,
                        HasShift: true,
                        AcceptedOrders: shift.AcceptedDailyOrders,
                        RejectedOrders: shift.RejectedDailyOrders,
                        RealRejectedOrders: shift.RealRejectedDailyOrders,
                        WorkingHours: shift.WorkingHours,
                        TargetHours: TARGET_HOURS_PER_DAY2,
                        HoursDifference: hoursDiff,
                        ShiftStatus: shift.ShiftStatus
                    ));
                }
                else
                {
                    dailyDetails.Add(new DailyShiftDetail(
                        Date: currentDate,
                        HasShift: false,
                        AcceptedOrders: 0,
                        RejectedOrders: 0,
                        RealRejectedOrders: 0,
                        WorkingHours: 0,
                        TargetHours: TARGET_HOURS_PER_DAY2,
                        HoursDifference: -TARGET_HOURS_PER_DAY2,
                        ShiftStatus: "Missing"
                    ));
                }
                currentDate = currentDate.AddDays(1);
            }

            var totalWorkingDays = shifts.Count;
            var missingDays = totalDays - totalWorkingDays;
            var totalWorkingHours = shifts.Sum(s => s.WorkingHours);
            var targetWorkingHours = totalDays * TARGET_HOURS_PER_DAY2;
            var hoursDifference = totalWorkingHours - targetWorkingHours;
            var totalOrders = shifts.Sum(s => s.AcceptedDailyOrders);
            var totalRejections = shifts.Sum(s => s.RejectedDailyOrders);
            var totalRealRejections = shifts.Sum(s => s.RealRejectedDailyOrders);

            var report = new RiderDailyDetailReport(
                RiderId: rider.Id,
                IqamaNo: rider.EmployeeIqamaNo,
                RiderNameAR: rider.Employee.NameAR,
                RiderNameEN: rider.Employee.NameEN,
                WorkingId: workingId,
                StartDate: startDate,
                EndDate: endDate,
                DailyDetails: dailyDetails,
                TotalWorkingDays: totalWorkingDays,
                MissingDays: missingDays,
                TotalWorkingHours: totalWorkingHours,
                TargetWorkingHours: targetWorkingHours,
                HoursDifference: hoursDifference,
                IsAboveTarget: hoursDifference >= 0,
                TotalOrders: totalOrders,
                TotalRejections: totalRejections,
                TotalRealRejections: totalRealRejections
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<RiderDailyDetailReport>(
                new Error($"Error generating daily detail report: {ex.Message}", "server_error", 500));
        }
    }

    // ============================================
    // 2. ALL RIDERS SUMMARY REPORT
    // ============================================

    public async Task<Result<AllRidersSummaryReport>> GetAllRidersSummaryReportAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<AllRidersSummaryReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        try
        {
            var totalExpectedDays = endDate.DayNumber - startDate.DayNumber + 1;

            var shifts = await _dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Success(new AllRidersSummaryReport(
                    StartDate: startDate,
                    EndDate: endDate,
                    TotalExpectedDays: totalExpectedDays,
                    RiderSummaries: new List<RiderSummaryDetail>(),
                    Totals: new SummaryTotals(0, 0, 0, 0, 0, 0, 0, 0, 0)
                ));
            }

            var riderGroups = shifts.GroupBy(s => s.RiderId);
            var riderSummaries = new List<RiderSummaryDetail>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var riderShifts = group.ToList();
                var actualWorkingDays = riderShifts.Count;
                var missingDays = totalExpectedDays - actualWorkingDays;

                var totalWorkingHours = riderShifts.Sum(s => s.WorkingHours);
                var targetWorkingHours = totalExpectedDays * TARGET_HOURS_PER_DAY;
                var hoursDifference = totalWorkingHours - targetWorkingHours;

                var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders);
                var targetOrders = totalExpectedDays * TARGET_ORDERS_PER_DAY;
                var ordersDifference = totalOrders - targetOrders;

                riderSummaries.Add(new RiderSummaryDetail(
                    RiderId: rider.Id,
                    IqamaNo: rider.EmployeeIqamaNo,
                    RiderNameAR: rider.Employee.NameAR,
                    RiderNameEN: rider.Employee.NameEN,
                    WorkingId: riderShifts.First().WorkingId,
                    ActualWorkingDays: actualWorkingDays,
                    MissingDays: missingDays > 0 ? -missingDays : 0, // Negative if missing, 0 otherwise
                    TotalWorkingHours: totalWorkingHours,
                    TargetWorkingHours: targetWorkingHours,
                    HoursDifference: hoursDifference,
                    TotalOrders: totalOrders,
                    TargetOrders: targetOrders,
                    OrdersDifference: ordersDifference
                ));
            }

            riderSummaries = riderSummaries
                .OrderByDescending(r => r.TotalOrders)
                .ToList();

            var totals = new SummaryTotals(
                TotalRiders: riderSummaries.Count,
                TotalWorkingDays: riderSummaries.Sum(r => r.ActualWorkingDays),
                TotalMissingDays: riderSummaries.Sum(r => Math.Abs(r.MissingDays)),
                TotalWorkingHours: riderSummaries.Sum(r => r.TotalWorkingHours),
                TotalTargetHours: riderSummaries.Sum(r => r.TargetWorkingHours),
                HoursDifference: riderSummaries.Sum(r => r.HoursDifference),
                TotalOrders: riderSummaries.Sum(r => r.TotalOrders),
                TotalTargetOrders: riderSummaries.Sum(r => r.TargetOrders),
                OrdersDifference: riderSummaries.Sum(r => r.OrdersDifference)
            );

            var report = new AllRidersSummaryReport(
                StartDate: startDate,
                EndDate: endDate,
                TotalExpectedDays: totalExpectedDays,
                RiderSummaries: riderSummaries,
                Totals: totals
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<AllRidersSummaryReport>(
                new Error($"Error generating summary report: {ex.Message}", "server_error", 500));
        }
    }

    // ============================================
    // 3. REJECTION REPORT
    // ============================================

    public async Task<Result<RejectionReport>> GetRejectionReportAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<RejectionReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        try
        {
            var totalDays = endDate.DayNumber - startDate.DayNumber + 1;

            var shifts = await _dbcontext.RiderShifts
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Success(new RejectionReport(
                    StartDate: startDate,
                    EndDate: endDate,
                    TotalDays: totalDays,
                    RiderDetails: new List<RiderRejectionDetail>(),
                    Totals: new RejectionTotals(0, 0, 0, 0, 0, 0, 0, 0)
                ));
            }

            var riderGroups = shifts.GroupBy(s => s.RiderId);
            var riderDetails = new List<RiderRejectionDetail>();

            foreach (var group in riderGroups)
            {
                var rider = group.First().Rider;
                if (rider?.Employee == null) continue;

                var riderShifts = group.ToList();
                var totalShifts = riderShifts.Count;
                var totalOrders = riderShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders);
                var targetOrders = totalDays * TARGET_ORDERS_PER_DAY;
                var totalRejections = riderShifts.Sum(s => s.RejectedDailyOrders);
                var totalRealRejections = riderShifts.Sum(s => s.RealRejectedDailyOrders);

                var rejectionRate = totalOrders > 0
                    ? Math.Round((decimal)totalRejections / totalOrders * 100, 2)
                    : 0;

                var realRejectionRate = totalOrders > 0
                    ? Math.Round((decimal)totalRealRejections / totalOrders * 100, 2)
                    : 0;

                riderDetails.Add(new RiderRejectionDetail(
                    RiderId: rider.Id,
                    IqamaNo: rider.EmployeeIqamaNo,
                    RiderNameAR: rider.Employee.NameAR,
                    RiderNameEN: rider.Employee.NameEN,
                    WorkingId: riderShifts.First().WorkingId,
                    TotalShifts: totalShifts,
                    TotalOrders: totalOrders,
                    TargetOrders: targetOrders,
                    TotalRejections: totalRejections,
                    TotalRealRejections: totalRealRejections,
                    RejectionRate: rejectionRate,
                    RealRejectionRate: realRejectionRate
                ));
            }

            riderDetails = riderDetails
                .OrderByDescending(r => r.TotalRealRejections)
                .ToList();

            var totalAllOrders = riderDetails.Sum(r => r.TotalOrders);
            var totalAllRejections = riderDetails.Sum(r => r.TotalRejections);
            var totalAllRealRejections = riderDetails.Sum(r => r.TotalRealRejections);

            var overallRejectionRate = totalAllOrders > 0
                ? Math.Round((decimal)totalAllRejections / totalAllOrders * 100, 2)
                : 0;

            var overallRealRejectionRate = totalAllOrders > 0
                ? Math.Round((decimal)totalAllRealRejections / totalAllOrders * 100, 2)
                : 0;

            var totals = new RejectionTotals(
                TotalRiders: riderDetails.Count,
                TotalShifts: riderDetails.Sum(r => r.TotalShifts),
                TotalOrders: totalAllOrders,
                TotalTargetOrders: riderDetails.Sum(r => r.TargetOrders),
                TotalRejections: totalAllRejections,
                TotalRealRejections: totalAllRealRejections,
                OverallRejectionRate: overallRejectionRate,
                OverallRealRejectionRate: overallRealRejectionRate
            );

            var report = new RejectionReport(
                StartDate: startDate,
                EndDate: endDate,
                TotalDays: totalDays,
                RiderDetails: riderDetails,
                Totals: totals
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<RejectionReport>(
                new Error($"Error generating rejection report: {ex.Message}", "server_error", 500));
        }
    }
    public async Task<Result<PeriodOrdersComparison>> ComparePeriodOrdersAsync(
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        // Validate period 2 dates
        if (period2End < period2Start)
            return Result.Failure<PeriodOrdersComparison>(
                new Error("Period 2: End date must be after or equal to start date", "invalid_input", 400));

        // Automatically calculate Period 1 (previous month of Period 2)
        var period1Start = period2Start.AddMonths(-1);
        var period1End = period2End.AddMonths(-1);

        try
        {
            // Get shifts for period 1
            var period1Shifts = await _dbcontext.RiderShifts
                .Where(s => s.ShiftDate >= period1Start && s.ShiftDate <= period1End && s.CompanyId == 1)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Get shifts for period 2
            var period2Shifts = await _dbcontext.RiderShifts
                .Where(s => s.ShiftDate >= period2Start && s.ShiftDate <= period2End && s.CompanyId == 1)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Calculate total orders for each period
            var period1TotalOrders = period1Shifts.Sum(s => s.AcceptedDailyOrders);
            var period2TotalOrders = period2Shifts.Sum(s => s.AcceptedDailyOrders);

            // Calculate difference and percentage
            var ordersDifference = period2TotalOrders - period1TotalOrders;
            var changePercentage = period1TotalOrders > 0
                ? Math.Round(((decimal)ordersDifference / period1TotalOrders) * 100, 2)
                : (period2TotalOrders > 0 ? 100m : 0m);

            // Generate trend description
            var trendDescription = GenerateTrendDescription(
                ordersDifference, changePercentage, period1TotalOrders, period2TotalOrders);

            var comparison = new PeriodOrdersComparison(
                Period1Start: period1Start,
                Period1End: period1End,
                Period2Start: period2Start,
                Period2End: period2End,
                Period1TotalOrders: period1TotalOrders,
                Period2TotalOrders: period2TotalOrders,
                OrdersDifference: ordersDifference,
                ChangePercentage: changePercentage,
                TrendDescription: trendDescription
            );

            return Result.Success(comparison);
        }
        catch (Exception ex)
        {
            return Result.Failure<PeriodOrdersComparison>(
                new Error($"Error comparing periods: {ex.Message}", "server_error", 500));
        }
    }

    /// <summary>
    /// Get daily summary report grouped by housing
    /// </summary>
    public async Task<Result<HousingDailySummaryReport>> GetHousingDailySummaryAsync(
        DateOnly reportDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all shifts for the specified date with housing information
            var shifts = await _dbcontext.RiderShifts
                .Include(m=>m.Housing)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.ShiftDate == reportDate && s.CompanyId == 1)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<HousingDailySummaryReport>(
                    new Error($"No shifts found for date {reportDate:yyyy-MM-dd}", "no_data", 404));
            }

            // Filter shifts with valid housing
            var validShifts = shifts
                .Where(s => s.Rider?.Employee?.Housing != null)
                .ToList();

            if (!validShifts.Any())
            {
                return Result.Failure<HousingDailySummaryReport>(
                    new Error($"No shifts with housing information found for {reportDate:yyyy-MM-dd}", "no_data", 404));
            }

            // Calculate totals
            var totalOrders = validShifts.Sum(s => s.AcceptedDailyOrders);
            var totalRiders = validShifts.Select(s => s.RiderId).Distinct().Count();

            // Group by housing
            var housingGroups = validShifts
                .GroupBy(s => new {
                    HousingId = s.HousingId,
                    HousingName = s.Housing?.Name
                });

            var housingSummaries = new List<HousingDailySummary>();

            foreach (var group in housingGroups)
            {
                var housingShifts = group.ToList();
                var housingOrders = housingShifts.Sum(s => s.AcceptedDailyOrders);
                var activeRiders = housingShifts.Select(s => s.RiderId).Distinct().Count();
                var avgOrdersPerRider = activeRiders > 0
                    ? Math.Round((decimal)housingOrders / activeRiders, 2)
                    : 0;
                var percentageOfTotal = totalOrders > 0
                    ? Math.Round((decimal)housingOrders / totalOrders * 100, 2)
                    : 0;

                housingSummaries.Add(new HousingDailySummary(
                    HousingId: group.Key.HousingId ?? 1,
                    HousingName: group.Key.HousingName!,
                    TotalOrders: housingOrders,
                    ActiveRiders: activeRiders,
                    AverageOrdersPerRider: avgOrdersPerRider,
                    PercentageOfTotalOrders: percentageOfTotal
                ));
            }

            // Sort by total orders descending
            housingSummaries = housingSummaries
                .OrderByDescending(h => h.TotalOrders)
                .ToList();

            var avgOrdersPerRiderOverall = totalRiders > 0
                ? Math.Round((decimal)totalOrders / totalRiders, 2)
                : 0;

            var report = new HousingDailySummaryReport(
                ReportDate: reportDate,
                HousingSummaries: housingSummaries,
                TotalOrders: totalOrders,
                TotalRiders: totalRiders,
                AverageOrdersPerRider: avgOrdersPerRiderOverall
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<HousingDailySummaryReport>(
                new Error($"Error generating housing daily summary: {ex.Message}", "server_error", 500));
        }
    }

    /// <summary>
    /// Get detailed daily report with individual riders grouped by housing
    /// </summary>
    public async Task<Result<HousingDailyDetailedReport>> GetHousingDailyDetailedReportAsync(
        DateOnly reportDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all shifts for the specified date with full details
            var shifts = await _dbcontext.RiderShifts
                .Include(m=>m.Housing)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                .Where(s => s.ShiftDate == reportDate && s.CompanyId == 1)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!shifts.Any())
            {
                return Result.Failure<HousingDailyDetailedReport>(
                    new Error($"No shifts found for date {reportDate:yyyy-MM-dd}", "no_data", 404));
            }

            // Filter shifts with valid housing
            var validShifts = shifts
                .Where(s => s.Rider?.Employee?.Housing != null)
                .ToList();

            if (!validShifts.Any())
            {
                return Result.Failure<HousingDailyDetailedReport>(
                    new Error($"No shifts with housing information found for {reportDate:yyyy-MM-dd}", "no_data", 404));
            }

            // Calculate grand totals
            var grandTotalOrders = validShifts.Sum(s => s.AcceptedDailyOrders);
            var grandTotalRiders = validShifts.Select(s => s.RiderId).Distinct().Count();

            // Group by housing
            var housingGroups = validShifts
                .GroupBy(s => new {
                    HousingId = s.Rider.Employee.Housing.Id,
                    HousingName = s.Rider.Employee.Housing.Name
                });

            var housingDetails = new List<HousingDailyDetails>();

            foreach (var group in housingGroups)
            {
                var housingShifts = group.ToList();
                var housingTotalOrders = housingShifts.Sum(s => s.AcceptedDailyOrders);
                var housingRiderCount = housingShifts.Select(s => s.RiderId).Distinct().Count();
                var percentageOfCompany = grandTotalOrders > 0
                    ? Math.Round((decimal)housingTotalOrders / grandTotalOrders * 100, 2)
                    : 0;

                // Get individual rider performances
                var riderPerformances = housingShifts
                    .Select(s => new RiderDailyPerformance(
                        RiderId: s.RiderId,
                        RiderName: s.Rider?.Employee.NameAR ?? "Unknown",
                        RiderNameE: s.Rider?.Employee.NameEN ?? "Unknown",
                        s.Rider?.Employee.Phone ?? "050",
                        WorkingId: s.WorkingId ?? "0",
                        AcceptedOrders: s.AcceptedDailyOrders,
                        ShiftDate: s.ShiftDate
                    ))
                    .OrderByDescending(r => r.AcceptedOrders)
                    .ToList();

                housingDetails.Add(new HousingDailyDetails(
                    HousingId: group.Key.HousingId,
                    HousingName: group.Key.HousingName,
                    Riders: riderPerformances,
                    HousingTotalOrders: housingTotalOrders,
                    HousingRiderCount: housingRiderCount,
                    PercentageOfCompanyTotal: percentageOfCompany
                ));
            }

            // Sort by total orders descending
            housingDetails = housingDetails
                .OrderByDescending(h => h.HousingTotalOrders)
                .ToList();

            var report = new HousingDailyDetailedReport(
                ReportDate: reportDate,
                HousingDetails: housingDetails,
                GrandTotalOrders: grandTotalOrders,
                GrandTotalRiders: grandTotalRiders
            );

            return Result.Success(report);
        }
        catch (Exception ex)
        {
            return Result.Failure<HousingDailyDetailedReport>(
                new Error($"Error generating housing daily detailed report: {ex.Message}", "server_error", 500));
        }
    }

    // Helper method for trend description
    private string GenerateTrendDescription(
        int difference,
        decimal changePercentage,
        int period1Total,
        int period2Total)
    {
        if (difference == 0)
            return "📊 Orders remained stable between periods";

        if (difference > 0)
        {
            if (changePercentage >= 50)
                return $"🚀 Significant increase of {difference:N0} orders (+{changePercentage:F1}%) - Excellent growth!";
            else if (changePercentage >= 20)
                return $"📈 Strong increase of {difference:N0} orders (+{changePercentage:F1}%) - Good performance!";
            else if (changePercentage >= 10)
                return $"✅ Moderate increase of {difference:N0} orders (+{changePercentage:F1}%)";
            else
                return $"↗️ Slight increase of {difference:N0} orders (+{changePercentage:F1}%)";
        }
        else
        {
            var absChange = Math.Abs(changePercentage);
            if (absChange >= 50)
                return $"📉 Significant decrease of {Math.Abs(difference):N0} orders ({changePercentage:F1}%) - Needs urgent attention!";
            else if (absChange >= 20)
                return $"⚠️ Notable decrease of {Math.Abs(difference):N0} orders ({changePercentage:F1}%) - Review required";
            else if (absChange >= 10)
                return $"↘️ Moderate decrease of {Math.Abs(difference):N0} orders ({changePercentage:F1}%)";
            else
                return $"➡️ Slight decrease of {Math.Abs(difference):N0} orders ({changePercentage:F1}%)";
        }
    }
    public async Task<Result<ComprehensiveDashboard>> GetComprehensiveDashboardAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var effectiveEndDate = endDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
            var effectiveStartDate = startDate ?? effectiveEndDate.AddDays(-30);

            if (effectiveEndDate < effectiveStartDate)
                return Result.Failure<ComprehensiveDashboard>(
                    new Error("End date must be after start date", "invalid_input", 400));

            var allCompanies = await _dbcontext.Companies
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var allRiders = await _dbcontext.RiderDetails
                .Include(r => r.Employee)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var shifts = await _dbcontext.RiderShifts
                .Include(s => s.Company)
                .Include(s => s.Rider)
                    .ThenInclude(r => r.Employee)
                    .ThenInclude(e => e.Housing)
                .Where(s => s.ShiftDate >= effectiveStartDate && s.ShiftDate <= effectiveEndDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var allHousings = await _dbcontext.Housings
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var substitutions = await _dbcontext.Set<RiderShiftSubstitution>()
                .Where(s => s.IsActive)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var allVehicles = await _dbcontext.Vehicles
            .Include(v => v.RiderDetails)
            .Include(v => v.RiderVehicleStatuses)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

            // Now process everything in memory - no more DB calls
            var companies = GetCompaniesStatistics(allCompanies, shifts);
            var riders = GetRidersStatistics(allRiders, shifts, substitutions);
            var shiftsStats = GetShiftsStatistics(shifts, effectiveStartDate, effectiveEndDate);
            var orders = GetOrdersStatistics(shifts);
            var performance = GetPerformanceMetrics(shifts);
            var housing = GetHousingStatistics(allHousings, shifts);
            var trends = GetTrendsAnalysis(shifts, effectiveStartDate, effectiveEndDate);

            var vehicles = GetVehicleStatistics(allVehicles, allRiders, effectiveStartDate, effectiveEndDate);


            var dashboard = new ComprehensiveDashboard(
                GeneratedAt: DateTime.UtcNow.AddHours(3),
                PeriodStart: effectiveStartDate,
                PeriodEnd: effectiveEndDate,
                Companies: companies,
                Riders: riders,
                Shifts: shiftsStats,
                Orders: orders,
                Performance: performance,
                Housing: housing,
                Trends: trends,
                Vehicle: vehicles  

            );

            return Result.Success(dashboard);
        }
        catch (Exception ex)
        {
            return Result.Failure<ComprehensiveDashboard>(
                new Error($"Error generating dashboard: {ex.Message}", "server_error", 500));
        }
    }
    private VehicleStatistics GetVehicleStatistics(
    List<Vehicle> vehicles,
    List<RiderDetails> riders,
    DateOnly startDate,
    DateOnly endDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        // Total vehicles count
        var totalVehicles = vehicles.Count;

        // Vehicles by type
        var byType = vehicles
            .GroupBy(v => v.VehicleType)
            .Select(g => new VehicleTypeCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        // Vehicles by manufacturer
        var byManufacturer = vehicles
            .Where(v => !string.IsNullOrEmpty(v.Manufacturer))
            .GroupBy(v => v.Manufacturer)
            .Select(g => new ManufacturerCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        // License expiry analysis
        var expiredLicenses = vehicles.Count(v => v.LicenseExpiryDate < today);
        var expiringIn30Days = vehicles.Count(v =>
            v.LicenseExpiryDate >= today &&
            v.LicenseExpiryDate <= today.AddDays(30));
        var expiringIn90Days = vehicles.Count(v =>
            v.LicenseExpiryDate > today.AddDays(30) &&
            v.LicenseExpiryDate <= today.AddDays(90));

        // Assigned vs unassigned vehicles
        var assignedVehicles = vehicles.Count(v => v.RiderDetails != null);
        var unassignedVehicles = totalVehicles - assignedVehicles;

        // Average vehicle age
        var currentYear = DateTime.Now.Year;
        var averageAge = vehicles.Any()
            ? vehicles.Average(v => currentYear - v.ManufactureYear)
            : 0;

        // Vehicles by location
        var byLocation = vehicles
            .Where(v => !string.IsNullOrEmpty(v.Location))
            .GroupBy(v => v.Location)
            .Select(g => new LocationCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        // Vehicles with complete documentation
        var withCompleteDocumentation = vehicles.Count(v =>
            !string.IsNullOrEmpty(v.VehicleImagePath) &&
            !string.IsNullOrEmpty(v.LicenseImagePath));

        // Recent registrations (within the selected period)
        var recentRegistrations = vehicles.Count(v =>
            DateOnly.FromDateTime(v.CreatedAt) >= startDate &&
            DateOnly.FromDateTime(v.CreatedAt) <= endDate);

        return new VehicleStatistics(
            TotalVehicles: totalVehicles,
            AssignedVehicles: assignedVehicles,
            UnassignedVehicles: unassignedVehicles,
            ExpiredLicenses: expiredLicenses,
            ExpiringIn30Days: expiringIn30Days,
            ExpiringIn90Days: expiringIn90Days,
            AverageVehicleAge: Math.Round(averageAge, 1),
            WithCompleteDocumentation: withCompleteDocumentation,
            RecentRegistrations: recentRegistrations,
            ByType: byType,
            ByManufacturer: byManufacturer,
            ByLocation: byLocation
        );
    }

    public record VehicleStatistics(
        int TotalVehicles,
        int AssignedVehicles,
        int UnassignedVehicles,
        int ExpiredLicenses,
        int ExpiringIn30Days,
        int ExpiringIn90Days,
        double AverageVehicleAge,
        int WithCompleteDocumentation,
        int RecentRegistrations,
        List<VehicleTypeCount> ByType,
        List<ManufacturerCount> ByManufacturer,
        List<LocationCount> ByLocation
    );

    public record VehicleTypeCount(string Type, int Count);
    public record ManufacturerCount(string Manufacturer, int Count);
    public record LocationCount(string Location, int Count);
    private CompaniesStatistics GetCompaniesStatistics(
        List<Company> allCompanies,
        List<RiderShift> shifts)
    {
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

    private RidersStatistics GetRidersStatistics(
        List<RiderDetails> allRiders,
        List<RiderShift> shifts,
        List<RiderShiftSubstitution> substitutions)
    {
        var activeRiderIds = shifts.Select(s => s.RiderId).Distinct().ToList();
        var activeRiders = dbcontext
                   .Employees
                   .AsNoTracking()
                   .Where(r => r.RiderDetails != null && r.Status.ToLower() == "enable")
                   .Include(e => e.Housing)
                   .Include(e => e.RiderDetails)
                   .ToList();


        return new RidersStatistics(
            TotalRiders: allRiders.Count,
            ActiveRiders: activeRiders.Count,
            InactiveRiders: allRiders.Count - activeRiders.Count,
            RidersWithWorkingId: allRiders.Count(r => !string.IsNullOrWhiteSpace(r.WorkingId) && r.WorkingId != "0"),
            RidersWithSubstitution: substitutions.Count,
            AverageShiftsPerRider: activeRiders.Any() ? (decimal)shifts.Count / activeRiders.Count : 0,
            TotalWorkingHours: shifts.Sum(s => s.WorkingHours)
        );
    }

    private ShiftsStatistics GetShiftsStatistics(
        List<RiderShift> shifts,
        DateOnly startDate,
        DateOnly endDate)
    {
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
                RejectedOrders: g.Sum(s => s.RejectedDailyOrders),
                StackedDeliveries: g.Sum(s => s.StackedDeliveries) // ADD THIS

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

    private OrdersStatistics GetOrdersStatistics(List<RiderShift> shifts)
    {
        var totalAccepted = shifts.Sum(s => s.AcceptedDailyOrders);
        var totalRejected = shifts.Sum(s => s.RejectedDailyOrders);
        var totalStacked = shifts.Sum(s => s.StackedDeliveries); // ADD THIS
        var totalRealRejected = shifts.Sum(s => s.RealRejectedDailyOrders);
        var totalOrders = totalAccepted + totalRejected;

        var acceptanceRate = totalOrders > 0 ? (decimal)totalAccepted / totalOrders * 100 : 0;
        var rejectionRate = totalOrders > 0 ? (decimal)totalRejected / totalOrders * 100 : 0;
        var stackedRate = totalAccepted > 0 ? (decimal)totalStacked / totalAccepted * 100 : 0; // ADD THIS


        var avgOrdersPerShift = shifts.Count > 0 ? (decimal)totalAccepted / shifts.Count : 0;
        var avgStackedPerShift = shifts.Count > 0 ? (decimal)totalStacked / shifts.Count : 0; // ADD THIS

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
            TotalPenaltyAmount: shifts.Sum(s => CalculatePenalty(s)),
                    TotalStackedDeliveries: totalStacked, // ADD THIS
                    StackedDeliveryRate: stackedRate, // ADD THIS
        AverageStackedPerShift: avgStackedPerShift // ADD THIS

        );
    }

    private PerformanceMetrics GetPerformanceMetrics(List<RiderShift> shifts)
    {
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

        var totalDays = shifts.Select(s => s.ShiftDate).Distinct().Count();
        var avgOrdersPerDay = totalDays > 0 ? (decimal)shifts.Sum(s => s.AcceptedDailyOrders) / totalDays : 0;

        return new PerformanceMetrics(
            OverallPerformanceScore: overallScore,
            TopPerformers: riderPerformances,
            AverageCompletionRate: shifts.Any()
                ? (decimal)shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()) / shifts.Count * 100
                : 0,
            AverageOrdersPerDay: avgOrdersPerDay
        );
    }

    private HousingStatistics GetHousingStatistics(
        List<Housing> allHousings,
        List<RiderShift> shifts)
    {
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

    private TrendsAnalysis GetTrendsAnalysis(
        List<RiderShift> shifts,
        DateOnly startDate,
        DateOnly endDate)
    {
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

    //// Helper methods
    private decimal CalculatePenalty(RiderShift shift)
    {
        var excessRejections = Math.Max(0,
            shift.RealRejectedDailyOrders - CompanyShiftConfiguration.RejectionThreshold);
        return excessRejections * CompanyShiftConfiguration.PenaltyPerExcessRejection;
    }

    private decimal CalculateCompletionRate(List<RiderShift> shifts)
    {
        var total = shifts.Count;
        var completed = shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString());
        return total > 0 ? (decimal)completed / total * 100 : 0;
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




    public async Task<Result<PreviousDayCompanySummary>> GetPreviousDayCompanySummaryAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get yesterday's date and current month range
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
            var yesterday = today.AddDays(-1);
            var monthStart = new DateOnly(today.Year, today.Month, 1);

            // Get all shifts for yesterday
            var yesterdayShifts = await _dbcontext.RiderShifts
                .Include(s => s.Company)
                .Where(s => s.ShiftDate == yesterday)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Get all shifts for the current month up to today
            var monthShifts = await _dbcontext.RiderShifts
                .Include(s => s.Company)
                .Where(s => s.ShiftDate >= monthStart && s.ShiftDate <= yesterday)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!yesterdayShifts.Any() && !monthShifts.Any())
            {
                return Result.Failure<PreviousDayCompanySummary>(
                    new Error($"No shifts found for {yesterday:yyyy-MM-dd} or current month", "no_data", 404));
            }

            // ===== YESTERDAY'S DATA =====

            // Filter shifts for Hunger company (yesterday)
            var hungerYesterdayShifts = yesterdayShifts
                .Where(s => s.Company?.Name?.Equals("Hunger", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Filter shifts for Keta company (yesterday)
            var ketaYesterdayShifts = yesterdayShifts
                .Where(s => s.Company?.Name?.Equals("Keta", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Calculate Hunger summary (yesterday)
            var hungerDaySummary = new CompanyDaySummary(
                CompanyName: "Hunger",
                TotalOrders: hungerYesterdayShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                TotalShifts: hungerYesterdayShifts.Count,
                AcceptedOrders: hungerYesterdayShifts.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: hungerYesterdayShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: hungerYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: hungerYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: hungerYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString())
            );

            // Calculate Keta summary (yesterday)
            var ketaDaySummary = new CompanyDaySummary(
                CompanyName: "Keta",
                TotalOrders: ketaYesterdayShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                TotalShifts: ketaYesterdayShifts.Count,
                AcceptedOrders: ketaYesterdayShifts.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: ketaYesterdayShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: ketaYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: ketaYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: ketaYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString())
            );

            // ===== MONTH-TO-DATE DATA =====

            // Filter shifts for Hunger company (month-to-date)
            var hungerMonthShifts = monthShifts
                .Where(s => s.Company?.Name?.Equals("Hunger", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Filter shifts for Keta company (month-to-date)
            var ketaMonthShifts = monthShifts
                .Where(s => s.Company?.Name?.Equals("Keta", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Calculate number of days with data in the month
            var daysInMonth = monthShifts
                .Select(s => s.ShiftDate)
                .Distinct()
                .Count();

            // Calculate Hunger month-to-date summary
            var hungerMonthSummary = new CompanyMonthToDateSummary(
                CompanyName: "Hunger",
                TotalOrders: hungerMonthShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                TotalShifts: hungerMonthShifts.Count,
                AcceptedOrders: hungerMonthShifts.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: hungerMonthShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: hungerMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: hungerMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: hungerMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString()),
                TotalDays: daysInMonth
            );

            // Calculate Keta month-to-date summary
            var ketaMonthSummary = new CompanyMonthToDateSummary(
                CompanyName: "Keta",
                TotalOrders: ketaMonthShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                TotalShifts: ketaMonthShifts.Count,
                AcceptedOrders: ketaMonthShifts.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: ketaMonthShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: ketaMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: ketaMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: ketaMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString()),
                TotalDays: daysInMonth
            );

            // ===== CALCULATE TOTALS =====

            var totalDayOrders = hungerDaySummary.AcceptedOrders + ketaDaySummary.AcceptedOrders;
            var totalDayShifts = hungerDaySummary.TotalShifts + ketaDaySummary.TotalShifts;

            var totalMonthOrders = hungerMonthSummary.AcceptedOrders + ketaMonthSummary.AcceptedOrders;
            var totalMonthShifts = hungerMonthSummary.TotalShifts + ketaMonthSummary.TotalShifts;

            var summary = new PreviousDayCompanySummary(
                ReportDate: yesterday,
                Hunger: hungerDaySummary,
                Keta: ketaDaySummary,
                TotalDayOrders: totalDayOrders,
                TotalDayShifts: totalDayShifts,
                HungerMonthToDate: hungerMonthSummary,
                KetaMonthToDate: ketaMonthSummary,
                TotalMonthOrders: totalMonthOrders,
                TotalMonthShifts: totalMonthShifts,
                MonthStartDate: monthStart
            );

            return Result.Success(summary);
        }
        catch (Exception ex)
        {
            return Result.Failure<PreviousDayCompanySummary>(
                new Error($"Error generating previous day summary: {ex.Message}", "server_error", 500));
        }
    }




    public async Task<Result<PreviousDayCompanySummary>> GetHousingPreviousDayCompanySummaryAsync(
       long managerIqamaNo,
       CancellationToken cancellationToken = default)
    {
        try
        {
            // Find the housing by manager iqama number
            var housing = await _dbcontext.Set<Housing>()
                .FirstOrDefaultAsync(h => h.ManagerIqamaNo == managerIqamaNo, cancellationToken);

            if (housing == null)
            {
                return Result.Failure<PreviousDayCompanySummary>(
                    new Error($"No housing found for manager iqama number {managerIqamaNo}", "housing_not_found", 404));
            }

            // Get yesterday's date and current month range
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
            var yesterday = today.AddDays(-1);
            var monthStart = new DateOnly(today.Year, today.Month, 1);

            // Get rider IDs for employees in this housing
            var housingRiderIds = await _dbcontext.Set<Employees>()
                .Where(e => e.HousingId == housing.Id)
                .Join(_dbcontext.Set<RiderDetails>(),
                      emp => emp.IqamaNo,
                      rider => rider.EmployeeIqamaNo,
                      (emp, rider) => rider.Id)
                .ToListAsync(cancellationToken);

            if (!housingRiderIds.Any())
            {
                return Result.Failure<PreviousDayCompanySummary>(
                    new Error($"No riders found in housing managed by {managerIqamaNo}", "no_riders", 404));
            }

            // Get all shifts for yesterday for riders in this housing
            var yesterdayShifts = await _dbcontext.RiderShifts
                .Include(s => s.Company)
                .Where(s => s.ShiftDate == yesterday && housingRiderIds.Contains(s.RiderId))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Get all shifts for the current month up to today for riders in this housing
            var monthShifts = await _dbcontext.RiderShifts
                .Include(s => s.Company)
                .Where(s => s.ShiftDate >= monthStart && s.ShiftDate < today && housingRiderIds.Contains(s.RiderId))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!yesterdayShifts.Any() && !monthShifts.Any())
            {
                return Result.Failure<PreviousDayCompanySummary>(
                    new Error($"No shifts found for housing managed by {managerIqamaNo} on {yesterday:yyyy-MM-dd} or current month", "no_data", 404));
            }

            // ===== YESTERDAY'S DATA =====

            // Filter shifts for Hunger company (yesterday)
            var hungerYesterdayShifts = yesterdayShifts
                .Where(s => s.Company?.Name?.Equals("Hunger", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Filter shifts for Keta company (yesterday)
            var ketaYesterdayShifts = yesterdayShifts
                .Where(s => s.Company?.Name?.Equals("Keta", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Calculate Hunger summary (yesterday)
            var hungerDaySummary = new CompanyDaySummary(
                CompanyName: "Hunger",
                TotalOrders: hungerYesterdayShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                TotalShifts: hungerYesterdayShifts.Count,
                AcceptedOrders: hungerYesterdayShifts.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: hungerYesterdayShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: hungerYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: hungerYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: hungerYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString())
            );

            // Calculate Keta summary (yesterday)
            var ketaDaySummary = new CompanyDaySummary(
                CompanyName: "Keta",
                TotalOrders: ketaYesterdayShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                TotalShifts: ketaYesterdayShifts.Count,
                AcceptedOrders: ketaYesterdayShifts.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: ketaYesterdayShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: ketaYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: ketaYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: ketaYesterdayShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString())
            );

            // ===== MONTH-TO-DATE DATA =====

            // Filter shifts for Hunger company (month-to-date)
            var hungerMonthShifts = monthShifts
                .Where(s => s.Company?.Name?.Equals("Hunger", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Filter shifts for Keta company (month-to-date)
            var ketaMonthShifts = monthShifts
                .Where(s => s.Company?.Name?.Equals("Keta", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Calculate number of days with data in the month
            var daysInMonth = monthShifts
                .Select(s => s.ShiftDate)
                .Distinct()
                .Count();

            // Calculate Hunger month-to-date summary
            var hungerMonthSummary = new CompanyMonthToDateSummary(
                CompanyName: "Hunger",
                TotalOrders: hungerMonthShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                TotalShifts: hungerMonthShifts.Count,
                AcceptedOrders: hungerMonthShifts.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: hungerMonthShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: hungerMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: hungerMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: hungerMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString()),
                TotalDays: daysInMonth
            );

            // Calculate Keta month-to-date summary
            var ketaMonthSummary = new CompanyMonthToDateSummary(
                CompanyName: "Keta",
                TotalOrders: ketaMonthShifts.Sum(s => s.AcceptedDailyOrders + s.RejectedDailyOrders),
                TotalShifts: ketaMonthShifts.Count,
                AcceptedOrders: ketaMonthShifts.Sum(s => s.AcceptedDailyOrders),
                RejectedOrders: ketaMonthShifts.Sum(s => s.RejectedDailyOrders),
                CompletedShifts: ketaMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString()),
                IncompleteShifts: ketaMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString()),
                FailedShifts: ketaMonthShifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString()),
                TotalDays: daysInMonth
            );

            // ===== CALCULATE TOTALS =====

            var totalDayOrders = hungerDaySummary.AcceptedOrders + ketaDaySummary.AcceptedOrders;
            var totalDayShifts = hungerDaySummary.TotalShifts + ketaDaySummary.TotalShifts;

            var totalMonthOrders = hungerMonthSummary.AcceptedOrders + ketaMonthSummary.AcceptedOrders;
            var totalMonthShifts = hungerMonthSummary.TotalShifts + ketaMonthSummary.TotalShifts;

            var summary = new PreviousDayCompanySummary(
                ReportDate: yesterday,
                Hunger: hungerDaySummary,
                Keta: ketaDaySummary,
                TotalDayOrders: totalDayOrders,
                TotalDayShifts: totalDayShifts,
                HungerMonthToDate: hungerMonthSummary,
                KetaMonthToDate: ketaMonthSummary,
                TotalMonthOrders: totalMonthOrders,
                TotalMonthShifts: totalMonthShifts,
                MonthStartDate: monthStart
            );

            return Result.Success(summary);
        }
        catch (Exception ex)
        {
            return Result.Failure<PreviousDayCompanySummary>(
                new Error($"Error generating previous day summary for housing: {ex.Message}", "server_error", 500));
        }
    }

    public async Task<Result<MonthlyRiderReport>> GetMonthlyReportByWorkingIdAsync(
        string WorkingId,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(WorkingId) || !int.TryParse(WorkingId, out var id) || id <= 0)
            return Result.Failure<MonthlyRiderReport>(
                new Error("Invalid working ID", "invalid_input", 400));

        if (month < 1 || month > 12)
            return Result.Failure<MonthlyRiderReport>(
                new Error("Month must be between 1 and 12", "invalid_input", 400));

        var rider = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.WorkingId == WorkingId, cancellationToken);

        if (rider == null)
            return Result.Failure<MonthlyRiderReport>(
                new Error($"Rider with WorkingId {WorkingId} not found", "not_found", 404));

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
                rider.Id, rider.Employee.NameAR, WorkingId, year, month));
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
            .Where(s => HasRejectionProblem(s) || s.ShiftStatus == ShiftStatus.Failed.ToString())
            .Select(CreateProblemShiftDetail)
            .ToList();

        var report = new MonthlyRiderReport(
            RiderId: rider.Id,
            RiderName: rider.Employee.NameAR,
            WorkingId: WorkingId,
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
.Where(r => !string.IsNullOrWhiteSpace(r.WorkingId) && r.WorkingId != "0")
.ToListAsync(cancellationToken);

        var reports = new List<MonthlyRiderReport>();

        foreach (var rider in allRiders)
        {
            var result = await GetMonthlyReportByWorkingIdAsync(
                rider.WorkingId!, year, month, cancellationToken);

            if (result.IsSuccess)
            {
                reports.Add(result.Value);
            }
        }

        return Result.Success<IEnumerable<MonthlyRiderReport>>(reports);
    }

    public async Task<Result<YearlyRiderReport>> GetYearlyReportByWorkingIdAsync(
        string WorkingId,
        int year,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(WorkingId) || !int.TryParse(WorkingId, out var id) || id <= 0)
            return Result.Failure<YearlyRiderReport>(
                new Error("Invalid working ID", "invalid_input", 400));

        var rider = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.WorkingId == WorkingId, cancellationToken);

        if (rider == null)
            return Result.Failure<YearlyRiderReport>(
                new Error($"Rider with WorkingId {WorkingId} not found", "not_found", 404));

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
                rider.Id, rider.Employee.NameAR, WorkingId, year));
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
            WorkingId: WorkingId,
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
.Where(r => !string.IsNullOrWhiteSpace(r.WorkingId) && r.WorkingId != "0")
.ToListAsync(cancellationToken);

        var reports = new List<YearlyRiderReport>();

        foreach (var rider in allRiders)
        {
            var result = await GetYearlyReportByWorkingIdAsync(
                rider.WorkingId!, year, cancellationToken);

            if (result.IsSuccess)
            {
                reports.Add(result.Value);
            }
        }

        return Result.Success<IEnumerable<YearlyRiderReport>>(reports);
    }


    public async Task<Result<DateRangeReport>> GetCustomDateRangeReportByWorkingIdAsync(
        string WorkingId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(WorkingId) || !int.TryParse(WorkingId, out var id) || id <= 0)
            return Result.Failure<DateRangeReport>(
                new Error("Invalid working ID", "invalid_input", 400));

        if (endDate < startDate)
            return Result.Failure<DateRangeReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        var rider = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.WorkingId == WorkingId, cancellationToken);

        if (rider == null)
            return Result.Failure<DateRangeReport>(
                new Error($"Rider with WorkingId {WorkingId} not found", "not_found", 404));

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
                rider.Id,rider.EmployeeIqamaNo, rider.Employee.NameAR, WorkingId, startDate, endDate));
        }

        var workingIdHistory = DetectWorkingIdChanges(shifts);
        var totalWorkingDays = shifts.Count;
        var totalPenalty = shifts.Sum(s => CalculatePenalty(s));

        var companyBreakdowns = CalculateCompanyBreakdowns(shifts);
        var overallPerformanceScore = companyBreakdowns.Any()
            ? companyBreakdowns.Sum(cb => cb.PerformanceScore * cb.WorkingDays) / totalWorkingDays
            : 0;

        var problematicShifts = shifts
            .Where(s => HasRejectionProblem(s) || s.ShiftStatus == ShiftStatus.Failed.ToString())
            .Select(CreateProblemShiftDetail)
            .ToList();

        var report = new DateRangeReport(
            RiderId: rider.Id,
            IqamaNo: rider.EmployeeIqamaNo,
            RiderName: rider.Employee.NameAR,
            WorkingId: WorkingId,
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
.Where(r => !string.IsNullOrWhiteSpace(r.WorkingId) && r.WorkingId != "0")
.ToListAsync(cancellationToken);

        var reports = new List<DateRangeReport>();

        foreach (var rider in allRiders)
        {
            var result = await GetCustomDateRangeReportByWorkingIdAsync(
                rider.WorkingId!, startDate, endDate, cancellationToken);

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


    public async Task<Result<RiderPeriodComparison>> CompareRiderPeriodsAsync(
    string WorkingId,
    DateOnly period1Start,
    DateOnly period1End,
    DateOnly period2Start,
    DateOnly period2End,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(WorkingId) || !int.TryParse(WorkingId, out var id) || id <= 0)
            return Result.Failure<RiderPeriodComparison>(
                new Error("Invalid working ID", "invalid_input", 400));

        if (period1End < period1Start)
            return Result.Failure<RiderPeriodComparison>(
                new Error("Period 1: End date must be after start date", "invalid_input", 400));

        if (period2End < period2Start)
            return Result.Failure<RiderPeriodComparison>(
                new Error("Period 2: End date must be after start date", "invalid_input", 400));

        var rider = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.WorkingId == WorkingId, cancellationToken);

        if (rider == null)
            return Result.Failure<RiderPeriodComparison>(
                new Error($"Rider with WorkingId {WorkingId} not found", "not_found", 404));

        // Get shifts for both periods
        var period1Shifts = await _dbcontext.RiderShifts
            .Include(s => s.Company)
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => s.RiderId == rider.Id &&
                       s.ShiftDate >= period1Start &&
                       s.ShiftDate <= period1End)
            .ToListAsync(cancellationToken);

        var period2Shifts = await _dbcontext.RiderShifts
            .Include(s => s.Company)
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => s.RiderId == rider.Id &&
                       s.ShiftDate >= period2Start &&
                       s.ShiftDate <= period2End)
            .ToListAsync(cancellationToken);

        // Build period summaries
        var period1Summary = BuildPeriodSummary(period1Start, period1End, period1Shifts);
        var period2Summary = BuildPeriodSummary(period2Start, period2End, period2Shifts);

        // Calculate comparison metrics
        var comparisonMetrics = CalculateComparisonMetrics(period1Summary, period2Summary);

        // Generate verdict
        var verdict = GeneratePerformanceVerdict(period1Summary, period2Summary, comparisonMetrics);

        // Generate insights and recommendations
        var insights = GenerateComparisonInsights(period1Summary, period2Summary, comparisonMetrics);
        var recommendations = GenerateRecommendations(period2Summary, comparisonMetrics, verdict);

        var comparison = new RiderPeriodComparison(
            RiderId: rider.Id,
            RiderName: rider.Employee.NameAR,
            WorkingId: WorkingId,
            Period1: period1Summary,
            Period2: period2Summary,
            Comparison: comparisonMetrics,
            Verdict: verdict,
            KeyInsights: insights,
            Recommendations: recommendations
        );

        return Result.Success(comparison);
    }

    public async Task<Result<IEnumerable<RiderPeriodComparison>>> CompareAllRidersPeriodsAsync(
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        if (period1End < period1Start)
            return Result.Failure<IEnumerable<RiderPeriodComparison>>(
                new Error("Period 1: End date must be after start date", "invalid_input", 400));

        if (period2End < period2Start)
            return Result.Failure<IEnumerable<RiderPeriodComparison>>(
                new Error("Period 2: End date must be after start date", "invalid_input", 400));

        var allRiders = await _dbcontext.RiderDetails
.Where(r => !string.IsNullOrWhiteSpace(r.WorkingId) && r.WorkingId != "0")
.ToListAsync(cancellationToken);

        var comparisons = new List<RiderPeriodComparison>();

        foreach (var rider in allRiders)
        {
            var result = await CompareRiderPeriodsAsync(
                rider.WorkingId!,
                period1Start,
                period1End,
                period2Start,
                period2End,
                cancellationToken);

            if (result.IsSuccess)
            {
                comparisons.Add(result.Value);
            }
        }

        // Sort by overall improvement
        var sortedComparisons = comparisons
            .OrderByDescending(c => c.Verdict.ImprovementScore)
            .ToList();

        return Result.Success<IEnumerable<RiderPeriodComparison>>(sortedComparisons);
    }

    public async Task<Result<CompanyPeriodComparison>> CompareCompanyPeriodsAsync(
        string companyName,
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyName))
            return Result.Failure<CompanyPeriodComparison>(
                new Error("Company name is required", "invalid_input", 400));

        if (period1End < period1Start)
            return Result.Failure<CompanyPeriodComparison>(
                new Error("Period 1: End date must be after start date", "invalid_input", 400));

        if (period2End < period2Start)
            return Result.Failure<CompanyPeriodComparison>(
                new Error("Period 2: End date must be after start date", "invalid_input", 400));

        var company = await _dbcontext.Companies
            .FirstOrDefaultAsync(c => c.Name == companyName, cancellationToken);

        if (company == null)
            return Result.Failure<CompanyPeriodComparison>(
                new Error($"Company '{companyName}' not found", "not_found", 404));

        // Get shifts for both periods
        var period1Shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Include(s => s.Company)
            .Where(s => s.Company.Name == companyName &&
                       s.ShiftDate >= period1Start &&
                       s.ShiftDate <= period1End)
            .ToListAsync(cancellationToken);

        var period2Shifts = await _dbcontext.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Include(s => s.Company)
            .Where(s => s.Company.Name == companyName &&
                       s.ShiftDate >= period2Start &&
                       s.ShiftDate <= period2End)
            .ToListAsync(cancellationToken);

        if (!period1Shifts.Any() && !period2Shifts.Any())
            return Result.Failure<CompanyPeriodComparison>(
                new Error($"No shifts found for company '{companyName}' in either period", "no_data", 404));

        // Build period summaries
        var period1Summary = BuildPeriodSummary(period1Start, period1End, period1Shifts);
        var period2Summary = BuildPeriodSummary(period2Start, period2End, period2Shifts);

        // Calculate comparison metrics
        var comparisonMetrics = CalculateComparisonMetrics(period1Summary, period2Summary);

        // Get rider comparisons for top improved and declined
        var riderComparisons = await GetRiderComparisonsForCompany(
            companyName, period1Start, period1End, period2Start, period2End, cancellationToken);

        var topImproved = riderComparisons
            .Where(r => r.Verdict.ImprovementScore > 0)
            .OrderByDescending(r => r.Verdict.ImprovementScore)
            .Take(5)
            .ToList();

        var topDeclined = riderComparisons
            .Where(r => r.Verdict.ImprovementScore < 0)
            .OrderBy(r => r.Verdict.ImprovementScore)
            .Take(5)
            .ToList();

        var overallTrend = DetermineOverallTrend(comparisonMetrics, riderComparisons);

        var comparison = new CompanyPeriodComparison(
            CompanyName: companyName,
            Period1: period1Summary,
            Period2: period2Summary,
            Comparison: comparisonMetrics,
            TopImprovedRiders: topImproved,
            TopDeclinedRiders: topDeclined,
            OverallTrend: overallTrend
        );

        return Result.Success(comparison);
    }
        
    public async Task<Result<RiderPeriodComparison>> CompareRiderMonthsAsync(
        string WorkingId,
        int year1,
        int month1,
        int year2,
        int month2,
        CancellationToken cancellationToken = default)
    {
        if (month1 < 1 || month1 > 12)
            return Result.Failure<RiderPeriodComparison>(
                new Error("Month 1 must be between 1 and 12", "invalid_input", 400));

        if (month2 < 1 || month2 > 12)
            return Result.Failure<RiderPeriodComparison>(
                new Error("Month 2 must be between 1 and 12", "invalid_input", 400));

        var period1Start = new DateOnly(year1, month1, 1);
        var period1End = period1Start.AddMonths(1).AddDays(-1);

        var period2Start = new DateOnly(year2, month2, 1);
        var period2End = period2Start.AddMonths(1).AddDays(-1);

        return await CompareRiderPeriodsAsync(
            WorkingId,
            period1Start,
            period1End,
            period2Start,
            period2End,
            cancellationToken);
    }

    public async Task<Result<RiderPeriodComparison>> CompareRiderYearsAsync(
        string WorkingId,
        int year1,
        int year2,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(WorkingId) || !int.TryParse(WorkingId, out var id) || id <= 0)
            return Result.Failure<RiderPeriodComparison>(
                new Error("Invalid working ID", "invalid_input", 400));

        var period1Start = new DateOnly(year1, 1, 1);
        var period1End = new DateOnly(year1, 12, 31);

        var period2Start = new DateOnly(year2, 1, 1);
        var period2End = new DateOnly(year2, 12, 31);

        return await CompareRiderPeriodsAsync(
            WorkingId,
            period1Start,
            period1End,
            period2Start,
            period2End,
            cancellationToken);
    }
    
    
    
    
    
    private string DetermineOverallTrend(
    ComparisonMetrics companyMetrics,
    List<RiderPeriodComparison> riderComparisons)
    {
        if (!riderComparisons.Any())
            return "➡️ No Data Available";

        var totalRiders = riderComparisons.Count;
        var improvingRiders = 0;
        var decliningRiders = 0;
        var stableRiders = 0;

        // Analyze company-level metrics
        var companyImprovements = 0;
        var companyDeclines = 0;

        if (companyMetrics.OrdersChangePercent > 5) companyImprovements++;
        else if (companyMetrics.OrdersChangePercent < -5) companyDeclines++;

        if (companyMetrics.CompletionRateChangePercent > 3) companyImprovements++;
        else if (companyMetrics.CompletionRateChangePercent < -3) companyDeclines++;

        if (companyMetrics.PerformanceScoreChangePercent > 5) companyImprovements++;
        else if (companyMetrics.PerformanceScoreChangePercent < -5) companyDeclines++;

        if (companyMetrics.ProblematicShiftsChangePercent < -10) companyImprovements++;
        else if (companyMetrics.ProblematicShiftsChangePercent > 10) companyDeclines++;

        // Analyze individual rider performance
        foreach (var rider in riderComparisons)
        {
            switch (rider.Verdict.OverallResult)
            {
                case ComparisonResult.Better:
                    improvingRiders++;
                    break;
                case ComparisonResult.Worse:
                    decliningRiders++;
                    break;
                default:
                    stableRiders++;
                    break;
            }
        }

        // Calculate percentages
        var improvingPercent = (decimal)improvingRiders / totalRiders * 100;
        var decliningPercent = (decimal)decliningRiders / totalRiders * 100;
        var stablePercent = (decimal)stableRiders / totalRiders * 100;

        // Determine company-level trend
        string companyTrend;
        if (companyImprovements > companyDeclines + 1)
            companyTrend = "strong improvement";
        else if (companyImprovements > companyDeclines)
            companyTrend = "improving";
        else if (companyDeclines > companyImprovements + 1)
            companyTrend = "declining";
        else if (companyDeclines > companyImprovements)
            companyTrend = "needs attention";
        else
            companyTrend = "stable";

        // Combine company and rider trends for final verdict
        if (companyImprovements > companyDeclines && improvingPercent >= 60)
            return $"📈 Strong Overall Improvement - Company metrics {companyTrend}, {improvingPercent:F0}% of riders improving ({improvingRiders}/{totalRiders})";

        if (companyImprovements > companyDeclines && improvingPercent >= 40)
            return $"✅ Positive Trend - Company {companyTrend}, majority of riders improving ({improvingRiders}/{totalRiders})";

        if (companyDeclines > companyImprovements && decliningPercent >= 60)
            return $"📉 Significant Decline - Company metrics {companyTrend}, {decliningPercent:F0}% of riders declining ({decliningRiders}/{totalRiders})";

        if (companyDeclines > companyImprovements && decliningPercent >= 40)
            return $"⚠️ Needs Attention - Company {companyTrend}, {decliningPercent:F0}% of riders declining ({decliningRiders}/{totalRiders})";

        if (improvingPercent >= 50)
            return $"✅ Generally Improving - Company {companyTrend}, {improvingPercent:F0}% improving vs {decliningPercent:F0}% declining";

        if (decliningPercent >= 50)
            return $"⚠️ Concerning Trend - Company {companyTrend}, {decliningPercent:F0}% declining vs {improvingPercent:F0}% improving";

        if (stablePercent >= 50)
            return $"➡️ Stable Performance - Company {companyTrend}, {stablePercent:F0}% of riders maintaining performance";

        return $"🔄 Mixed Results - Company {companyTrend}, riders split: {improvingPercent:F0}% improving, {decliningPercent:F0}% declining, {stablePercent:F0}% stable";
    }
    private PeriodSummary BuildPeriodSummary(
        DateOnly startDate,
        DateOnly endDate,
        List<RiderShift> shifts)
    {
        var totalDays = endDate.DayNumber - startDate.DayNumber + 1;
        var workingDays = shifts.Count;
        var completedShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Completed.ToString());
        var incompleteShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Incomplete.ToString());
        var failedShifts = shifts.Count(s => s.ShiftStatus == ShiftStatus.Failed.ToString());
        var absentShifts = totalDays - workingDays;

        var totalAccepted = shifts.Sum(s => s.AcceptedDailyOrders);
        var totalRejected = shifts.Sum(s => s.RejectedDailyOrders);
        var totalRealRejected = shifts.Sum(s => s.RealRejectedDailyOrders);
        var totalStacked = shifts.Sum(s => s.StackedDeliveries);
        var totalHours = shifts.Sum(s => s.WorkingHours);

        var problematicCount = shifts.Count(s => HasRejectionProblem(s));
        var totalPenalty = shifts.Sum(s => CalculatePenalty(s));

        var avgOrdersPerDay = workingDays > 0 ? (decimal)totalAccepted / workingDays : 0;
        var avgStackedPerDay = workingDays > 0 ? (decimal)totalStacked / workingDays : 0;
        var completionRate = workingDays > 0 ? (decimal)completedShifts / workingDays * 100 : 0;

        // Calculate performance score
        var companyBreakdowns = CalculateCompanyBreakdowns(shifts);
        var performanceScore = companyBreakdowns.Any() && workingDays > 0
            ? companyBreakdowns.Sum(cb => cb.PerformanceScore * cb.WorkingDays) / workingDays
            : 0;

        return new PeriodSummary(
            StartDate: startDate,
            EndDate: endDate,
            TotalDays: totalDays,
            WorkingDays: workingDays,
            CompletedShifts: completedShifts,
            IncompleteShifts: incompleteShifts,
            FailedShifts: failedShifts,
            AbsentShifts: absentShifts,
            TotalAcceptedOrders: totalAccepted,
            TotalRejectedOrders: totalRejected,
            TotalRealRejectedOrders: totalRealRejected,
            TotalStackedDeliveries: totalStacked,
            TotalWorkingHours: totalHours,
            ProblematicShiftsCount: problematicCount,
            TotalPenaltyAmount: totalPenalty,
            AverageStackedPerDay: avgStackedPerDay,
            AverageOrdersPerDay: avgOrdersPerDay,
            CompletionRate: completionRate,
            PerformanceScore: performanceScore,
            CompanyBreakdowns: companyBreakdowns
        );
    }

    private ComparisonMetrics CalculateComparisonMetrics(
        PeriodSummary period1,
        PeriodSummary period2)
    {
        return new ComparisonMetrics(
            WorkingDaysDifference: period2.WorkingDays - period1.WorkingDays,
            WorkingDaysChangePercent: CalculatePercentChange(period1.WorkingDays, period2.WorkingDays),
            OrdersDifference: period2.TotalAcceptedOrders - period1.TotalAcceptedOrders,
            OrdersChangePercent: CalculatePercentChange(period1.TotalAcceptedOrders, period2.TotalAcceptedOrders),
            AverageOrdersPerDayDifference: period2.AverageOrdersPerDay - period1.AverageOrdersPerDay,
            AverageOrdersPerDayChangePercent: CalculatePercentChange(period1.AverageOrdersPerDay, period2.AverageOrdersPerDay),
            CompletionRateDifference: period2.CompletionRate - period1.CompletionRate,
            CompletionRateChangePercent: CalculatePercentChange(period1.CompletionRate, period2.CompletionRate),
            PerformanceScoreDifference: period2.PerformanceScore - period1.PerformanceScore,
            PerformanceScoreChangePercent: CalculatePercentChange(period1.PerformanceScore, period2.PerformanceScore),
            WorkingHoursDifference: period2.TotalWorkingHours - period1.TotalWorkingHours,
            WorkingHoursChangePercent: CalculatePercentChange((decimal)period1.TotalWorkingHours, (decimal)period2.TotalWorkingHours),
            PenaltyDifference: period2.TotalPenaltyAmount - period1.TotalPenaltyAmount,
            PenaltyChangePercent: CalculatePercentChange(period1.TotalPenaltyAmount, period2.TotalPenaltyAmount),
            ProblematicShiftsDifference: period2.ProblematicShiftsCount - period1.ProblematicShiftsCount,
            ProblematicShiftsChangePercent: CalculatePercentChange(period1.ProblematicShiftsCount, period2.ProblematicShiftsCount),
            RejectionRateDifference: CalculateRejectionRate(period2) - CalculateRejectionRate(period1),
            RejectionRateChangePercent: CalculatePercentChange(CalculateRejectionRate(period1), CalculateRejectionRate(period2))
        );
    }

    private decimal CalculateRejectionRate(PeriodSummary period)
    {
        var totalOrders = period.TotalAcceptedOrders + period.TotalRejectedOrders;
        return totalOrders > 0 ? (decimal)period.TotalRejectedOrders / totalOrders * 100 : 0;
    }

    private PeriodPerformanceVerdict GeneratePerformanceVerdict(
        PeriodSummary period1,
        PeriodSummary period2,
        ComparisonMetrics metrics)
    {
        var improvements = new List<MetricChange>();
        var declines = new List<MetricChange>();

        // Analyze each metric
        AnalyzeMetricChange(
            "Performance Score",
            period1.PerformanceScore, period2.PerformanceScore,
            metrics.PerformanceScoreChangePercent,
            improvements, declines, isHigherBetter: true);

        AnalyzeMetricChange(
            "Completion Rate",
            period1.CompletionRate, period2.CompletionRate,
            metrics.CompletionRateChangePercent,
            improvements, declines, isHigherBetter: true);

        AnalyzeMetricChange(
            "Average Orders/Day",
            period1.AverageOrdersPerDay, period2.AverageOrdersPerDay,
            metrics.AverageOrdersPerDayChangePercent,
            improvements, declines, isHigherBetter: true);

        AnalyzeMetricChange(
            "Rejection Rate",
            CalculateRejectionRate(period1), CalculateRejectionRate(period2),
            metrics.RejectionRateChangePercent,
            improvements, declines, isHigherBetter: false);

        AnalyzeMetricChange(
            "Penalties",
            period1.TotalPenaltyAmount, period2.TotalPenaltyAmount,
            metrics.PenaltyChangePercent,
            improvements, declines, isHigherBetter: false);

        AnalyzeMetricChange(
            "Problematic Shifts",
            period1.ProblematicShiftsCount, period2.ProblematicShiftsCount,
            metrics.ProblematicShiftsChangePercent,
            improvements, declines, isHigherBetter: false);

        // Calculate improvement score
        var improvementScore = CalculateImprovementScore(improvements, declines);

        // Determine overall result
        var overallResult = DetermineOverallResult(improvementScore, improvements.Count, declines.Count);

        // Generate summary
        var summary = GenerateVerdictSummary(overallResult, improvementScore, improvements, declines);

        return new PeriodPerformanceVerdict(
            OverallResult: overallResult,
            Summary: summary,
            ImprovementScore: improvementScore,
            TopImprovements: improvements.OrderByDescending(i => Math.Abs(i.ChangePercent)).Take(3).ToList(),
            TopDeclines: declines.OrderByDescending(d => Math.Abs(d.ChangePercent)).Take(3).ToList()
        );
    }

    private void AnalyzeMetricChange(
        string metricName,
        decimal oldValue,
        decimal newValue,
        decimal changePercent,
        List<MetricChange> improvements,
        List<MetricChange> declines,
        bool isHigherBetter)
    {
        if (Math.Abs(changePercent) < 1) return; // Ignore negligible changes

        var direction = newValue > oldValue ? TrendDirection.Up :
                        newValue < oldValue ? TrendDirection.Down :
                        TrendDirection.Stable;

        var isImprovement = (isHigherBetter && direction == TrendDirection.Up) ||
                            (!isHigherBetter && direction == TrendDirection.Down);

        var change = new MetricChange(
            MetricName: metricName,
            OldValue: FormatMetricValue(oldValue, metricName),
            NewValue: FormatMetricValue(newValue, metricName),
            ChangePercent: changePercent,
            Direction: direction
        );

        if (isImprovement)
            improvements.Add(change);
        else if (direction != TrendDirection.Stable)
            declines.Add(change);
    }

    private string FormatMetricValue(decimal value, string metricName)
    {
        if (metricName.Contains("Rate") || metricName.Contains("Score"))
            return $"{value:F1}%";
        if (metricName.Contains("Penalties"))
            return $"{value:F2} SAR";
        return $"{value:F1}";
    }

    private decimal CalculateImprovementScore(
        List<MetricChange> improvements,
        List<MetricChange> declines)
    {
        var improvementWeight = improvements.Sum(i => Math.Abs(i.ChangePercent));
        var declineWeight = declines.Sum(d => Math.Abs(d.ChangePercent));

        if (improvementWeight + declineWeight == 0)
            return 0;

        return ((improvementWeight - declineWeight) / (improvementWeight + declineWeight)) * 100;
    }

    private ComparisonResult DetermineOverallResult(
        decimal improvementScore,
        int improvementCount,
        int declineCount)
    {
        if (Math.Abs(improvementScore) < 10 && Math.Abs(improvementCount - declineCount) <= 1)
            return ComparisonResult.Same;

        if (improvementCount > 0 && declineCount > 0)
            return improvementScore > 20 ? ComparisonResult.Better :
                   improvementScore < -20 ? ComparisonResult.Worse :
                   ComparisonResult.Mixed;

        return improvementScore > 0 ? ComparisonResult.Better : ComparisonResult.Worse;
    }

    private string GenerateVerdictSummary(
        ComparisonResult result,
        decimal improvementScore,
        List<MetricChange> improvements,
        List<MetricChange> declines)
    {
        return result switch
        {
            ComparisonResult.Better =>
                $"Performance improved significantly with an improvement score of {improvementScore:F1}. " +
                $"{improvements.Count} metrics showed positive changes.",

            ComparisonResult.Worse =>
                $"Performance declined with an improvement score of {improvementScore:F1}. " +
                $"{declines.Count} metrics showed negative changes.",

            ComparisonResult.Mixed =>
                $"Performance showed mixed results (score: {improvementScore:F1}). " +
                $"{improvements.Count} improvements vs {declines.Count} declines.",

            ComparisonResult.Same =>
                $"Performance remained relatively stable with minimal changes (score: {improvementScore:F1}).",

            _ => "Unable to determine performance trend."
        };
    }

    private List<string> GenerateComparisonInsights(
        PeriodSummary period1,
        PeriodSummary period2,
        ComparisonMetrics metrics)
    {
        var insights = new List<string>();

        // Working days insight
        if (Math.Abs(metrics.WorkingDaysChangePercent) >= 20)
        {
            var direction = metrics.WorkingDaysDifference > 0 ? "increased" : "decreased";
            insights.Add($"📅 Working days {direction} by {Math.Abs(metrics.WorkingDaysChangePercent):F1}% " +
                        $"({period1.WorkingDays} → {period2.WorkingDays})");
        }

        // Orders insight
        if (Math.Abs(metrics.AverageOrdersPerDayChangePercent) >= 10)
        {
            var emoji = metrics.AverageOrdersPerDayDifference > 0 ? "📈" : "📉";
            insights.Add($"{emoji} Daily average orders changed by {metrics.AverageOrdersPerDayChangePercent:F1}% " +
                        $"({period1.AverageOrdersPerDay:F1} → {period2.AverageOrdersPerDay:F1})");
        }

        // Completion rate insight
        if (Math.Abs(metrics.CompletionRateDifference) >= 5)
        {
            var emoji = metrics.CompletionRateDifference > 0 ? "✅" : "⚠️";
            insights.Add($"{emoji} Completion rate changed by {metrics.CompletionRateDifference:F1} percentage points " +
                        $"({period1.CompletionRate:F1}% → {period2.CompletionRate:F1}%)");
        }

        // Performance score insight
        if (Math.Abs(metrics.PerformanceScoreDifference) >= 5)
        {
            var emoji = metrics.PerformanceScoreDifference > 0 ? "🌟" : "📊";
            insights.Add($"{emoji} Performance score {(metrics.PerformanceScoreDifference > 0 ? "improved" : "declined")} " +
                        $"by {Math.Abs(metrics.PerformanceScoreDifference):F1} points");
        }

        // Penalty insight
        if (metrics.PenaltyDifference != 0)
        {
            var emoji = metrics.PenaltyDifference < 0 ? "💰" : "⚠️";
            var change = metrics.PenaltyDifference < 0 ? "reduced" : "increased";
            insights.Add($"{emoji} Penalties {change} by {Math.Abs(metrics.PenaltyDifference):F2} SAR");
        }

        // Problematic shifts insight
        if (metrics.ProblematicShiftsDifference != 0)
        {
            var emoji = metrics.ProblematicShiftsDifference < 0 ? "✨" : "🔴";
            insights.Add($"{emoji} Problematic shifts changed from {period1.ProblematicShiftsCount} to {period2.ProblematicShiftsCount}");
        }

        if (!insights.Any())
            insights.Add("📊 Performance metrics remained relatively stable between periods");

        return insights;
    }

    private List<string> GenerateRecommendations(
        PeriodSummary period2,
        ComparisonMetrics metrics,
        PeriodPerformanceVerdict verdict)
    {
        var recommendations = new List<string>();

        // Based on completion rate
        if (period2.CompletionRate < 85)
        {
            recommendations.Add("🎯 Focus on improving shift completion rate - currently below target");
        }

        // Based on rejection rate
        var rejectionRate = CalculateRejectionRate(period2);
        if (rejectionRate > 15)
        {
            recommendations.Add("⚠️ High rejection rate detected - review order acceptance strategy");
        }

        // Based on penalties
        if (period2.TotalPenaltyAmount > 100)
        {
            recommendations.Add("💰 Reduce penalty costs by minimizing excess rejections");
        }

        // Based on performance score
        if (period2.PerformanceScore < 75)
        {
            recommendations.Add("📈 Performance score needs improvement - aim for 85% or higher");
        }

        // Based on trends
        if (metrics.CompletionRateChangePercent < -5)
        {
            recommendations.Add("🔄 Completion rate declining - investigate causes of incomplete shifts");
        }

        if (metrics.AverageOrdersPerDayChangePercent < -10)
        {
            recommendations.Add("📊 Daily order average declining - consider productivity improvements");
        }

        // Positive reinforcement
        if (verdict.OverallResult == ComparisonResult.Better)
        {
            recommendations.Add("⭐ Maintain current positive trend and consistency");
        }

        // If no issues, encourage continued excellence
        if (!recommendations.Any())
        {
            recommendations.Add("✅ Maintain excellent performance and consistency");
        }

        return recommendations;
    }

    private async Task<List<RiderPeriodComparison>> GetRiderComparisonsForCompany(
        string companyName,
        DateOnly period1Start,
        DateOnly period1End,
        DateOnly period2Start,
        DateOnly period2End,
        CancellationToken cancellationToken)
    {
        // Get all riders who worked for this company in either period
        var riderIds = await _dbcontext.RiderShifts
            .Where(s => s.Company.Name == companyName &&
                       ((s.ShiftDate >= period1Start && s.ShiftDate <= period1End) ||
                        (s.ShiftDate >= period2Start && s.ShiftDate <= period2End)))
            .Select(s => s.Rider.WorkingId)
            .Distinct()
.Where(r => !string.IsNullOrWhiteSpace(r) && r != "0").ToListAsync(cancellationToken);

        var comparisons = new List<RiderPeriodComparison>();

        foreach (var workingId in riderIds)
        {
            if (string.IsNullOrEmpty(workingId)) continue;

            var result = await CompareRiderPeriodsAsync(
                workingId,
                period1Start,
                period1End,
                period2Start,
                period2End,
                cancellationToken);

            if (result.IsSuccess)
            {
                // Filter to only include shifts from this company
                var comparison = result.Value;
                var hasCompanyData = comparison.Period1.CompanyBreakdowns.Any(c => c.CompanyName == companyName) ||
                                   comparison.Period2.CompanyBreakdowns.Any(c => c.CompanyName == companyName);

                if (hasCompanyData)
                {
                    comparisons.Add(comparison);
                }
            }
        }

        return comparisons;
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
                WorkingId: rider.WorkingId ?? "0",
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
                WorkingId: rider.WorkingId ?? "0",
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

                var totalStacked = riderShifts.Sum(s => s.StackedDeliveries); // ADD THIS
                var avgStackedPerShift = totalShifts > 0
                ? (decimal)totalStacked / totalShifts
                : 0; // ADD THIS

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
                    rejectionRate, totalShifts, performanceScore, totalStacked,avgStackedPerShift);

                // Check for active substitution
                var hasSubstitution = substitutionDict.ContainsKey(rider.Id);
                var originalWorkingId = hasSubstitution
                    ? substitutionDict[rider.Id].SubstituteWorkingId
                    : (string?)null;

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
                        TotalStackedDeliveries: totalStacked, // ADD THIS
    AverageStackedPerShift: avgStackedPerShift,
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



    public async Task<Result<MonthlyStackedDeliveriesReport>> GetMonthlyStackedDeliveriesByWorkingIdAsync(
    string WorkingId,
    int year,
    int month,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(WorkingId) || !int.TryParse(WorkingId, out var id) || id <= 0)
            return Result.Failure<MonthlyStackedDeliveriesReport>(
                new Error("Invalid working ID", "invalid_input", 400));

        if (month < 1 || month > 12)
            return Result.Failure<MonthlyStackedDeliveriesReport>(
                new Error("Month must be between 1 and 12", "invalid_input", 400));

        var rider = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.WorkingId == WorkingId, cancellationToken);

        if (rider == null)
            return Result.Failure<MonthlyStackedDeliveriesReport>(
                new Error($"Rider with WorkingId {WorkingId} not found", "not_found", 404));

        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var shifts = await _dbcontext.RiderShifts
            .Where(s => s.RiderId == rider.Id &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .OrderBy(s => s.ShiftDate)
            .ToListAsync(cancellationToken);

        if (!shifts.Any())
        {
            return Result.Success(new MonthlyStackedDeliveriesReport(
                RiderId: rider.Id,
                RiderName: rider.Employee.NameAR,
                WorkingId: WorkingId,
                Year: year,
                Month: month,
                TotalStackedDeliveries: 0,
                TotalShifts: 0,
                AverageStackedPerShift: 0,
                MaxStackedInDay: 0,
                MaxStackedDate: null,
                DailyBreakdown: new List<DailyStackedBreakdown>()
            ));
        }

        var totalStacked = shifts.Sum(s => s.StackedDeliveries);
        var totalShifts = shifts.Count;
        var avgStackedPerShift = totalShifts > 0 ? (decimal)totalStacked / totalShifts : 0;

        var maxStackedShift = shifts.OrderByDescending(s => s.StackedDeliveries).First();
        var maxStacked = maxStackedShift.StackedDeliveries;
        var maxStackedDate = maxStackedShift.ShiftDate;

        var dailyBreakdown = shifts.Select(s =>
        {
            var totalOrders = s.AcceptedDailyOrders;
            var stackedPercentage = totalOrders > 0
                ? (decimal)s.StackedDeliveries / totalOrders * 100
                : 0;

            return new DailyStackedBreakdown(
                Date: s.ShiftDate,
                StackedDeliveries: s.StackedDeliveries,
                AcceptedOrders: s.AcceptedDailyOrders,
                StackedPercentage: stackedPercentage
            );
        }).ToList();

        var report = new MonthlyStackedDeliveriesReport(
            RiderId: rider.Id,
            RiderName: rider.Employee.NameAR,
            WorkingId: WorkingId,
            Year: year,
            Month: month,
            TotalStackedDeliveries: totalStacked,
            TotalShifts: totalShifts,
            AverageStackedPerShift: avgStackedPerShift,
            MaxStackedInDay: maxStacked,
            MaxStackedDate: maxStackedDate,
            DailyBreakdown: dailyBreakdown
        );

        return Result.Success(report);
    }

    public async Task<Result<AllRidersStackedDeliveriesReport>> GetAllRidersStackedDeliveriesAsync(
    DateOnly startDate,
    DateOnly endDate,
    CancellationToken cancellationToken = default)
    {
        if (startDate > endDate)
            return Result.Failure<AllRidersStackedDeliveriesReport>(
                new Error("Start date must be before or equal to end date", "invalid_input", 400));

        // Get all riders with their shifts in the date range
        var ridersWithShifts = await _dbcontext.RiderDetails
            .Include(r => r.Employee)
            .Where(r => r.RiderShifts.Any(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate))
            .Select(r => new
            {
                Rider = r,
                Shifts = r.RiderShifts
                    .Where(s => s.ShiftDate >= startDate && s.ShiftDate <= endDate)
                    .OrderBy(s => s.ShiftDate)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        if (!ridersWithShifts.Any())
        {
            return Result.Success(new AllRidersStackedDeliveriesReport(
                StartDate: startDate,
                EndDate: endDate,
                TotalRiders: 0,
                TotalStackedDeliveries: 0,
                TotalShifts: 0,
                AverageStackedPerRider: 0,
                RiderSummaries: new List<RiderStackedSummary>()
            ));
        }

        var riderSummaries = new List<RiderStackedSummary>();
        var grandTotalStacked = 0;
        var grandTotalShifts = 0;

        foreach (var item in ridersWithShifts)
        {
            var shifts = item.Shifts;
            var totalStacked = shifts.Sum(s => s.StackedDeliveries);
            var totalShifts = shifts.Count;
            var totalAcceptedOrders = shifts.Sum(s => s.AcceptedDailyOrders);

            var avgStackedPerShift = totalShifts > 0 ? (decimal)totalStacked / totalShifts : 0;

            var maxStackedShift = shifts.OrderByDescending(s => s.StackedDeliveries).FirstOrDefault();
            var maxStacked = maxStackedShift?.StackedDeliveries ?? 0;
            var maxStackedDate = maxStackedShift?.ShiftDate;

            var stackedPercentage = totalAcceptedOrders > 0
                ? (decimal)totalStacked / totalAcceptedOrders * 100
                : 0;

            riderSummaries.Add(new RiderStackedSummary(
                RiderId: item.Rider.Id,
                RiderName: item.Rider.Employee.NameAR,
                WorkingId: item.Rider.WorkingId ?? "0",
                TotalStackedDeliveries: totalStacked,
                TotalShifts: totalShifts,
                AverageStackedPerShift: avgStackedPerShift,
                MaxStackedInDay: maxStacked,
                MaxStackedDate: maxStackedDate,
                TotalStackedPercentage: stackedPercentage
            ));

            grandTotalStacked += totalStacked;
            grandTotalShifts += totalShifts;
        }

        // Sort by total stacked deliveries descending
        riderSummaries = riderSummaries
            .OrderByDescending(r => r.TotalStackedDeliveries)
            .ToList();

        var avgStackedPerRider = riderSummaries.Count > 0
            ? (decimal)grandTotalStacked / riderSummaries.Count
            : 0;

        var report = new AllRidersStackedDeliveriesReport(
            StartDate: startDate,
            EndDate: endDate,
            TotalRiders: riderSummaries.Count,
            TotalStackedDeliveries: grandTotalStacked,
            TotalShifts: grandTotalShifts,
            AverageStackedPerRider: avgStackedPerRider,
            RiderSummaries: riderSummaries
        );

        return Result.Success(report);
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
        decimal performanceScore, int totalStacked, // ADD THIS PARAMETER
    decimal avgStackedPerShift)
    {
        var achievements = new List<string>();

        // Order-based achievements
        if (totalOrders >= 1000)
            achievements.Add("🏆 1000+ Orders Club");
        else if (totalOrders >= 500)
            achievements.Add("⭐ 500+ Orders Milestone");
        else if (totalOrders >= 250)
            achievements.Add("✨ 250+ Orders Achievement");

        if (totalStacked >= 500)
            achievements.Add("📦 Stacking Master (500+)");
        else if (totalStacked >= 250)
            achievements.Add("📦 Stacking Expert (250+)");
        else if (totalStacked >= 100)
            achievements.Add("📦 Efficient Stacker (100+)");

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
            var companyStacked = companyShifts.Sum(s => s.StackedDeliveries); // ADD THIS

            var performanceScore = companyExpected > 0
                ? (decimal)companyAccepted / companyExpected * 100
                : 0;

            var avgStackedPerShift = companyWorkingDays > 0
         ? (decimal)companyStacked / companyWorkingDays
         : 0; // A

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
                            TotalStackedDeliveries: companyStacked, // ADD THIS
                            AverageStackedPerShift: avgStackedPerShift, // ADD THIS

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
        int riderId, string riderName, string WorkingId, int year, int month)
    {
        return new MonthlyRiderReport(
            RiderId: riderId,
            RiderName: riderName,
            WorkingId: WorkingId,
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
        int riderId, string riderName, string WorkingId, int year)
    {
        return new YearlyRiderReport(
            RiderId: riderId,
            RiderName: riderName,
            WorkingId: WorkingId,
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
        int riderId,long IqamaNo, string riderName, string WorkingId, DateOnly startDate, DateOnly endDate)
    {
        return new DateRangeReport(
            RiderId: riderId,
            IqamaNo: IqamaNo,
            RiderName: riderName,
            WorkingId: WorkingId,
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