using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Authentication;
using Application.Service.Empolyee;
using Application.Service.Reports;
using Domain;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using static Application.Service.Member.IMemberService;

namespace Application.Service.Member;

public class MemberService(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IJwtProvider jwtProvider, ApplicationDbcontext context , IReportService reportService) : IMemberService
{
    private readonly UserManager<ApplicationUser> userManager = userManager;
    private readonly SignInManager<ApplicationUser> signInManager = signInManager;
    private readonly IJwtProvider jwtProvider = jwtProvider;
    private readonly ApplicationDbcontext context = context;
    private readonly IReportService reportService = reportService;

    private const float TARGET_HOURS_PER_DAY = 9f;
    private const int TARGET_ORDERS_PER_DAY = 14;


    // Add this method to the MemberService class
    public async Task<Result> CancelVehicleOperationRequestAsync(
    long managerIqamaNo,
    int requestId)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return housingResult;

        var housing = housingResult.Value;

        // Find the request
        var request = await context.TempVehicleOperations
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return Result.Failure(HousingMemberErrors.RequestNotFound);

        // Check if already resolved
        if (request.IsResolved)
            return Result.Failure(HousingMemberErrors.RequestAlreadyResolved);

        // Get manager name to verify they made the request
        var manager = await context.Employees
            .FirstOrDefaultAsync(e => e.IqamaNo == managerIqamaNo);

        if (manager == null)
            return Result.Failure(UserErrors.UserNotFound);

        // Verify this manager made the request
        if (request.RequestedBy != manager.NameAR)
            return Result.Failure(HousingMemberErrors.UnauthorizedToCancel);

        // Verify the rider belongs to this housing
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();
        if (request.RiderIqamaNo != 0 && !employeeIqamas.Contains(request.RiderIqamaNo))
            return Result.Failure(HousingMemberErrors.UnauthorizedToCancel);

        // Mark as resolved/cancelled
        request.IsResolved = true;
        request.Resolution = "Cancelled";
        request.ResolvedBy = manager.NameAR;
        request.ResolvedAt = DateTime.UtcNow.AddHours(3);
        request.AdminNotes = "Cancelled by housing manager";

        await context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result> CancelEmployeeStatusChangeRequestAsync(
        long managerIqamaNo,
        int requestId)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return housingResult;

        var housing = housingResult.Value;

        // Find the request
        var request = await context.TempEmployeeStatusChanges
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return Result.Failure(HousingMemberErrors.RequestNotFound);

        // Check if already resolved
        if (request.IsResolved)
            return Result.Failure(HousingMemberErrors.RequestAlreadyResolved);

        // Get manager name to verify they made the request
        var manager = await context.Employees
            .FirstOrDefaultAsync(e => e.IqamaNo == managerIqamaNo);

        if (manager == null)
            return Result.Failure(UserErrors.UserNotFound);

        // Verify this manager made the request
        if (request.RequestedBy != manager.NameAR)
            return Result.Failure(HousingMemberErrors.UnauthorizedToCancel);

        // Verify the employee belongs to this housing
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();
        if (!employeeIqamas.Contains(request.EmployeeIqamaNo))
            return Result.Failure(HousingMemberErrors.UnauthorizedToCancel);

        // Mark as resolved/cancelled
        request.IsResolved = true;
        request.Resolution = "Cancelled";
        request.ResolvedBy = manager.NameAR;
        request.ResolvedAt = DateTime.UtcNow.AddHours(3);
        request.AdminNotes = "Cancelled by housing manager";

        await context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result> CancelRequestAsync(
        long managerIqamaNo,
        RequestType requestType,
        int requestId)
    {
        return requestType switch
        {
            RequestType.VehicleOperation =>
                await CancelVehicleOperationRequestAsync(managerIqamaNo, requestId),
            RequestType.EmployeeStatusChange =>
                await CancelEmployeeStatusChangeRequestAsync(managerIqamaNo, requestId),
            _ => Result.Failure(HousingMemberErrors.InvalidRequestType)
        };
    }
    // Add this method to the MemberService class
    public async Task<Result<List<HousingProblemVehicleResponse>>> GetHousingProblemVehicles(long managerIqamaNo)
    {
        try
        {
            var housingResult = await GetManagedHousing(managerIqamaNo);
            if (housingResult.IsFailure)
                return Result.Failure<List<HousingProblemVehicleResponse>>(housingResult.Error);

            var housing = housingResult.Value;
            var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

            if (!employeeIqamas.Any())
                return Result.Success(new List<HousingProblemVehicleResponse>());

            var riders = await context.RiderDetails
                .Include(r => r.Employee)
                .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
                .ToListAsync();

            var vehicleNumbers = riders
                .Where(r => !string.IsNullOrWhiteSpace(r.VehicleNumber))
                .Select(r => r.VehicleNumber!)
                .Distinct()
                .ToList();

            if (!vehicleNumbers.Any())
                return Result.Success(new List<HousingProblemVehicleResponse>());

            var vehicles = await context.Vehicles
                .Where(v => vehicleNumbers.Contains(v.VehicleNumber))
                .ToListAsync();

            var problemStatuses = await context.RiderVehicleStatus
                .Where(rvs => vehicleNumbers.Contains(rvs.VehicleNumber)
                    && rvs.IsActive
                    && rvs.StatusType == VehicleStatusType.Problem)
                .OrderByDescending(rvs => rvs.Timestamp)
                .ToListAsync();

            if (!problemStatuses.Any())
                return Result.Success(new List<HousingProblemVehicleResponse>());

            // Get employee names for those who reported problems
            var reporterIqamas = problemStatuses
                .Where(s => s.EmployeeIqamaNo.HasValue)
                .Select(s => s.EmployeeIqamaNo!.Value)
                .Distinct()
                .ToList();

            var reporters = await context.Employees
                .Where(e => reporterIqamas.Contains(e.IqamaNo))
                .ToDictionaryAsync(e => e.IqamaNo, e => e.NameAR);

            var problemVehicleNumbers = problemStatuses
                .Select(s => s.VehicleNumber)
                .Distinct()
                .ToHashSet();

            var problemVehicles = vehicles
                .Where(v => problemVehicleNumbers.Contains(v.VehicleNumber))
                .ToList();

            var statusDict = problemStatuses
                .GroupBy(s => s.VehicleNumber)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(s => s.Timestamp).First()
                );

            var response = problemVehicles.Select(v =>
            {
                var status = statusDict[v.VehicleNumber];

                return new HousingProblemVehicleResponse(
                    v.VehicleNumber,
                    v.VehicleType,
                    v.PlateNumberA,
                    v.PlateNumberE,
                    v.ManufactureYear,
                    v.Manufacturer,
                    v.LicenseExpiryDate,
                    v.Location,
                    status.Reason ?? "No reason provided",
                    status.Timestamp,
                    status.EmployeeIqamaNo.HasValue && reporters.ContainsKey(status.EmployeeIqamaNo.Value)
                        ? reporters[status.EmployeeIqamaNo.Value]
                        : null,
                    status.EmployeeIqamaNo,
                    status.Permission,
                    status.PermissionEndDate
                );
            }).ToList();

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<List<HousingProblemVehicleResponse>>(
                new Error("Error", $"Error retrieving problem vehicles: {ex.Message}", 400)
            );
        }
    }

    public record HousingProblemVehicleResponse(
    string VehicleNumber,
    string VehicleType,
    string PlateNumberA,
    string PlateNumberE,
    int ManufactureYear,
    string Manufacturer,
    DateOnly LicenseExpiryDate,
    string Location,
    string ProblemReason,
    DateTime ProblemReportedAt,
    string? ReportedByName,
    long? ReportedByIqamaNo,
    string? Permission,
    DateTime? PermissionEndDate
);
    public async Task<Result> RequestFixVehicleProblemForHousingAsync(
    long managerIqamaNo,
    MemberFixVehicleRequest request)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return housingResult;

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        // Verify vehicle exists
        var vehicle = await context.Vehicles
            .FirstOrDefaultAsync(v => v.PlateNumberA == request.VehiclePlate);

        if (vehicle is null)
        {
            return Result.Failure(new Error(
                "VehicleNotFound",
                "Vehicle not found",
                404
            ));
        }

        // Verify vehicle belongs to housing
        var vehicleInHousing = await context.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .AnyAsync(r => r.VehicleNumber == vehicle.VehicleNumber);

        if (!vehicleInHousing && !string.IsNullOrEmpty(vehicle.Location))
        {
            if (!vehicle.Location.Contains(housing.Name, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure(new Error(
                    "VehicleNotInHousing",
                    "This vehicle does not belong to your housing",
                    403
                ));
            }
        }

        // Check if vehicle has an active problem
        var activeProblem = await context.RiderVehicleStatus
            .FirstOrDefaultAsync(s => s.VehicleNumber == vehicle.VehicleNumber
                && s.IsActive
                && s.StatusType == VehicleStatusType.Problem);

        if (activeProblem is null)
        {
            return Result.Failure(new Error(
                "NoProblemFound",
                "This vehicle does not have an active problem to fix",
                400
            ));
        }

        // Verify vehicle is not currently assigned to any rider
        var vehicleAssigned = await context.RiderDetails
            .AnyAsync(r => r.VehicleNumber == vehicle.VehicleNumber
                && employeeIqamas.Contains(r.EmployeeIqamaNo));

        if (vehicleAssigned)
        {
            return Result.Failure(new Error(
                "VehicleStillAssigned",
                "Vehicle is still assigned to a rider. Please return the vehicle before fixing the problem.",
                400
            ));
        }

        // Check if there's already a pending fix request for this vehicle
        var existingFixRequest = await context.TempVehicleOperations
            .AnyAsync(t => t.VehicleNumber == vehicle.VehicleNumber
                && !t.IsResolved
                && t.VehicleStatusType == VehicleStatusType.Returned);

        if (existingFixRequest)
        {
            return Result.Failure(new Error(
                "FixRequestPending",
                "There is already a pending fix request for this vehicle",
                400
            ));
        }

        // Get the username of the manager
        var manager = await userManager.FindByNameAsync(managerIqamaNo.ToString());
        if (manager is null)
        {
            return Result.Failure(UserErrors.UserNotFound);
        }

        var t = long.Parse(manager.UserName!);
        var name = await context.Employees
            .Where(e => e.IqamaNo == t)
            .Select(e => e.NameAR)
            .FirstOrDefaultAsync();

        // Create the fix vehicle operation request (no rider needed)
        var operation = new TempVehicleOperation
        {
            RiderIqamaNo = 0, // No rider - vehicle was returned when problem was reported
            VehiclePlateNumber = request.VehiclePlate,
            VehicleNumber = vehicle.VehicleNumber,
            VehicleStatusType = VehicleStatusType.Returned, // Request to mark as available (fixed)
            Reason =  $"Problem fixed - Original issue: {activeProblem.Reason}",
            RequestedAt = DateTime.UtcNow.AddHours(3),
            RequestedBy = name!,
            IsResolved = false
        };

        await context.TempVehicleOperations.AddAsync(operation);
        await context.SaveChangesAsync();

        return Result.Success();
    }
    public async Task<Result<RiderMonthlyHistory>> GetRiderMonthlyHistoryForHousingAsync(
        long managerIqamaNo,
        long riderIqamaNo,
        CancellationToken cancellationToken = default)
    {
        // Verify housing manager
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<RiderMonthlyHistory>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        // Verify rider belongs to this housing
        if (!employeeIqamas.Contains(riderIqamaNo))
        {
            return Result.Failure<RiderMonthlyHistory>(
                HousingMemberErrors.RiderNotInHousing);
        }

        // Get rider details
        var rider = await context.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == riderIqamaNo, cancellationToken);

        if (rider == null)
        {
            return Result.Failure<RiderMonthlyHistory>(
                new Error("Rider not found", "not_found", 404));
        }

        // Get all shifts for this rider
        var shifts = await context.RiderShifts
            .Where(s => s.RiderId == rider.Id)
            .OrderBy(s => s.ShiftDate)
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
    public async Task<Result<RiderDailyDetailReport>> GetRiderDailyDetailReportAsync(
    long managerIqamaNo,
    string workingId,
    DateOnly startDate,
    DateOnly endDate)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<RiderDailyDetailReport>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        // Verify rider belongs to this housing
        var rider = await context.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.WorkingId == workingId &&
                                     employeeIqamas.Contains(r.EmployeeIqamaNo));

        if (rider == null)
            return Result.Failure<RiderDailyDetailReport>(
                HousingMemberErrors.RiderNotInHousing);

        var shifts = await context.RiderShifts
            .Where(s => s.RiderId == rider.Id &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .OrderBy(s => s.ShiftDate)
            .ToListAsync();

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

    public async Task<Result<AllRidersSummaryReport>> GetAllRidersSummaryReportAsync(
        long managerIqamaNo,
        DateOnly startDate,
        DateOnly endDate)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<AllRidersSummaryReport>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var riderIds = await context.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .Select(r => r.Id)
            .ToListAsync();

        var totalExpectedDays = endDate.DayNumber - startDate.DayNumber + 1;

        var shifts = await context.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => riderIds.Contains(s.RiderId) &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .ToListAsync();

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
                MissingDays: missingDays > 0 ? -missingDays : 0,
                TotalWorkingHours: totalWorkingHours,
                TargetWorkingHours: targetWorkingHours,
                HoursDifference: hoursDifference,
                TotalOrders: totalOrders,
                TargetOrders: targetOrders,
                OrdersDifference: ordersDifference
            ));
        }

        riderSummaries = riderSummaries.OrderBy(r => r.MissingDays).ToList();

        var totals = new SummaryTotals(
            TotalRiders: riderSummaries.Count,
            TotalWorkingDays: riderSummaries.Sum(r => r.ActualWorkingDays),
            TotalMissingDays: riderSummaries.Sum(r => r.ActualWorkingDays) - totalExpectedDays,
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

    public async Task<Result<RejectionReport>> GetRejectionReportAsync(
        long managerIqamaNo,
        DateOnly startDate,
        DateOnly endDate)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<RejectionReport>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var riderIds = await context.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .Select(r => r.Id)
            .ToListAsync();

        var totalDays = endDate.DayNumber - startDate.DayNumber + 1;

        var shifts = await context.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => riderIds.Contains(s.RiderId) &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .ToListAsync();

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

        var report = new RejectionReport(
            StartDate: startDate,
            EndDate: endDate,
            TotalDays: totalDays,
            RiderDetails: riderDetails,
            Totals: totals
        );

        return Result.Success(report);
    }

    // ============================================
    // EXISTING REPORTS - NOW FOR HOUSING MANAGERS
    // ============================================

    public async Task<Result<PeriodOrdersComparison>> ComparePeriodOrdersAsync(
        long managerIqamaNo,
        DateOnly period2Start,
        DateOnly period2End)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<PeriodOrdersComparison>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var riderIds = await context.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .Select(r => r.Id)
            .ToListAsync();

        if (period2End < period2Start)
            return Result.Failure<PeriodOrdersComparison>(
                new Error("Period 2: End date must be after or equal to start date", "invalid_input", 400));

        var period1Start = period2Start.AddMonths(-1);
        var period1End = period2End.AddMonths(-1);

        var period1Shifts = await context.RiderShifts
            .Where(s => riderIds.Contains(s.RiderId) &&
                       s.ShiftDate >= period1Start &&
                       s.ShiftDate <= period1End)
            .ToListAsync();

        var period2Shifts = await context.RiderShifts
            .Where(s => riderIds.Contains(s.RiderId) &&
                       s.ShiftDate >= period2Start &&
                       s.ShiftDate <= period2End)
            .ToListAsync();

        var period1TotalOrders = period1Shifts.Sum(s => s.AcceptedDailyOrders);
        var period2TotalOrders = period2Shifts.Sum(s => s.AcceptedDailyOrders);

        var ordersDifference = period2TotalOrders - period1TotalOrders;
        var changePercentage = period1TotalOrders > 0
            ? Math.Round(((decimal)ordersDifference / period1TotalOrders) * 100, 2)
            : (period2TotalOrders > 0 ? 100m : 0m);

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

    public async Task<Result<HousingDailySummary>> GetHousingDailySummaryAsync(
        long managerIqamaNo,
        DateOnly reportDate)
    {
        // 1️⃣ Get housing managed by this manager
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<HousingDailySummary>(housingResult.Error);

        var housing = housingResult.Value;

        // 2️⃣ Get employee iqamas in this housing
        var employeeIqamas = housing.Employees
            .Select(e => e.IqamaNo)
            .ToList();

        if (!employeeIqamas.Any())
        {
            return Result.Failure<HousingDailySummary>(
                new Error("No employees found in this housing", "no_employees", 404));
        }

        // 3️⃣ Get rider ids linked to those employees
        var riderIds = await context.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .Select(r => r.Id)
            .ToListAsync();

        if (!riderIds.Any())
        {
            return Result.Failure<HousingDailySummary>(
                new Error("No riders found for this housing", "no_riders", 404));
        }

        // 4️⃣ Get shifts for THIS housing on the given date
        var housingShifts = await context.RiderShifts
            .Where(s =>
                riderIds.Contains(s.RiderId) &&
                s.ShiftDate == reportDate)
            .ToListAsync();

        if (!housingShifts.Any())
        {
            return Result.Failure<HousingDailySummary>(
                new Error(
                    $"No shifts found for date {reportDate:yyyy-MM-dd}",
                    "no_data",
                    404));
        }

        // 5️⃣ Calculate housing stats
        var totalOrders = housingShifts.Sum(s => s.AcceptedDailyOrders);
        var activeRiders = housingShifts
            .Select(s => s.RiderId)
            .Distinct()
            .Count();

        var avgOrdersPerRider = activeRiders > 0
            ? Math.Round((decimal)totalOrders / activeRiders, 2)
            : 0;

        // 6️⃣ Get TOTAL orders across ALL housings for that date
        var allOrdersForDate = await context.RiderShifts
            .Where(s => s.ShiftDate == reportDate)
            .SumAsync(s => s.AcceptedDailyOrders);

        var percentageOfTotalOrders = allOrdersForDate > 0
            ? Math.Round((decimal)totalOrders / allOrdersForDate * 100, 2)
            : 0;

        // 7️⃣ Build summary
        var summary = new HousingDailySummary(
            HousingId: housing.Id,
            HousingName: housing.Name,
            TotalOrders: totalOrders,
            ActiveRiders: activeRiders,
            AverageOrdersPerRider: avgOrdersPerRider,
            PercentageOfTotalOrders: percentageOfTotalOrders
        );

        return Result.Success(summary);
    }


    public async Task<Result<HousingDailyDetailedReport>> GetHousingDailyDetailedReportAsync(
        long managerIqamaNo,
        DateOnly reportDate)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<HousingDailyDetailedReport>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var riderIds = await context.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .Select(r => r.Id)
            .ToListAsync();

        var shifts = await context.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => riderIds.Contains(s.RiderId) && s.ShiftDate == reportDate)
            .ToListAsync();

        if (!shifts.Any())
        {
            return Result.Failure<HousingDailyDetailedReport>(
                new Error($"No shifts found for date {reportDate:yyyy-MM-dd}", "no_data", 404));
        }

        var housingTotalOrders = shifts.Sum(s => s.AcceptedDailyOrders);
        var housingRiderCount = shifts.Select(s => s.RiderId).Distinct().Count();

        var riderPerformances = shifts
            .Select(s => new RiderDailyPerformance(
                RiderId: s.RiderId,
                RiderName: s.Rider?.Employee.NameAR ?? "Unknown",
                RiderNameE: s.Rider?.Employee.NameEN ?? "Unknown",
                s.Rider?.Employee.Phone ?? "050",
                WorkingId: s.WorkingId ?? "0",
                AcceptedOrders: s.AcceptedDailyOrders,
                ShiftDate: s.ShiftDate
            ))
            .OrderBy(r => r.AcceptedOrders)
            .ToList();

        var details = new HousingDailyDetails(
            HousingId: housing.Id,
            HousingName: housing.Name,
            Riders: riderPerformances,
            HousingTotalOrders: housingTotalOrders,
            HousingRiderCount: housingRiderCount,
            PercentageOfCompanyTotal: 100m // Since this is for single housing
        );

        var report = new HousingDailyDetailedReport(
            ReportDate: reportDate,
            HousingDetails: new List<HousingDailyDetails> { details },
            GrandTotalOrders: housingTotalOrders,
            GrandTotalRiders: housingRiderCount
        );

        return Result.Success(report);
    }

    // Helper method
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
    public async Task<Result<HousingRiderDailyDetailReport>> GetHousingRiderDailyDetailReportAsync(
    long managerIqamaNo,
    string workingId,
    DateOnly startDate,
    DateOnly endDate,
    CancellationToken cancellationToken = default)
    {
        var housing = await context.Housings
            .FirstOrDefaultAsync(h => h.ManagerIqamaNo == managerIqamaNo, cancellationToken);

        if (housing == null)
            return Result.Failure<HousingRiderDailyDetailReport>(
                new Error("Housing not found or you are not assigned as manager", "not_found", 404));

        // Verify rider belongs to this housing
        var rider = await context.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.WorkingId == workingId &&
                                     r.Employee.HousingId == housing.Id,
                cancellationToken);

        if (rider == null)
            return Result.Failure<HousingRiderDailyDetailReport>(
                new Error("Rider not found in your housing", "not_found", 404));

        var reportResult = await reportService.GetRiderDailyDetailReportAsync(workingId, startDate, endDate, cancellationToken);

        if (reportResult.IsFailure)
            return Result.Failure<HousingRiderDailyDetailReport>(reportResult.Error);

        return Result.Success(new HousingRiderDailyDetailReport(
            HousingName: housing.Name,
            RiderReport: reportResult.Value
        ));
    }

    public async Task<Result<HousingAllRidersSummaryReport>> GetHousingAllRidersSummaryReportAsync(
        long managerIqamaNo,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var housing = await context.Housings
            .Include(h => h.Employees)
            .FirstOrDefaultAsync(h => h.ManagerIqamaNo == managerIqamaNo, cancellationToken);

        if (housing == null)
            return Result.Failure<HousingAllRidersSummaryReport>(
                new Error("Housing not found or you are not assigned as manager", "not_found", 404));

        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();
        var riderIds = await context.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var totalExpectedDays = endDate.DayNumber - startDate.DayNumber + 1;

        var shifts = await context.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => riderIds.Contains(s.RiderId) &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .ToListAsync(cancellationToken);

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

        return Result.Success(new HousingAllRidersSummaryReport(
            HousingName: housing.Name,
            SummaryReport: summaryReport
        ));
    }

    public async Task<Result<HousingRejectionReport>> GetHousingRejectionReportAsync(
        long managerIqamaNo,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var housing = await context.Housings
            .Include(h => h.Employees)
            .FirstOrDefaultAsync(h => h.ManagerIqamaNo == managerIqamaNo, cancellationToken);

        if (housing == null)
            return Result.Failure<HousingRejectionReport>(
                new Error("Housing not found or you are not assigned as manager", "not_found", 404));

        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();
        var riderIds = await context.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var totalDays = endDate.DayNumber - startDate.DayNumber + 1;

        var shifts = await context.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => riderIds.Contains(s.RiderId) &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .ToListAsync(cancellationToken);

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

        return Result.Success(new HousingRejectionReport(
            HousingName: housing.Name,
            RejectionReport: rejectionReport
        ));
    }
    public async Task<Result<MemberAuthResponse>> MemberSignInAsync(MemberAuthRequest request)
    {
        // Find user by Iqama number (username)
        var user = await userManager.FindByNameAsync(request.IqamaNo.ToString());

        if (user is null)
            return Result.Failure<MemberAuthResponse>(UserErrors.InvalidCredentials);

        if (user.IsDisable)
            return Result.Failure<MemberAuthResponse>(UserErrors.Disableuser);

        // Verify password
        var result = await signInManager.PasswordSignInAsync(user, request.Password, false, true);

        if (!result.Succeeded)
        {
            var error = result.IsNotAllowed
                ? UserErrors.EmailNotConfirmed
                : result.IsLockedOut
                ? UserErrors.userLockedout
                : UserErrors.InvalidCredentials;

            return Result.Failure<MemberAuthResponse>(error);
        }

        // Check if user has Member role
        var userRoles = await userManager.GetRolesAsync(user);
        if (!userRoles.Contains("Member"))
        {
            return Result.Failure<MemberAuthResponse>(
                new Error("Unauthorized", "This login is only for housing members", 403)
            );
        }

        // Get employee and housing information
        var employee = await context.Employees
            .Include(e => e.Housing)
            .FirstOrDefaultAsync(e => e.IqamaNo == request.IqamaNo);

        if (employee is null)
        {
            return Result.Failure<MemberAuthResponse>(
                new Error("NotFound", "Employee record not found", 404)
            );
        }

        // Check if employee is a housing manager
        var housing = await context.Housings
            .Include(h => h.Employees)
            .FirstOrDefaultAsync(h => h.ManagerIqamaNo == request.IqamaNo);

        if (housing is null)
        {
            return Result.Failure<MemberAuthResponse>(
                new Error("Unauthorized", "You are not assigned as a housing manager", 403)
            );
        }

        // Generate JWT token
        var (token, expiresIn) = jwtProvider.GenerateToken(user, userRoles);

        var housingInfo = new HousingBasicInfo(
            housing.Id,
            housing.Name,
            housing.Address,
            housing.Capacity,
            housing.Employees.Count
        );

        var response = new MemberAuthResponse(
            user.Id,
            request.IqamaNo,
            employee.NameEN,
            token,
            expiresIn,
            housingInfo
        );

        return Result.Success(response);
    }

    // Helper method to verify housing manager
    private async Task<Result<Housing>> GetManagedHousing(long managerIqamaNo)
    {
        var housing = await context.Housings
            .Include(h => h.Employees)
            .FirstOrDefaultAsync(h => h.ManagerIqamaNo == managerIqamaNo);

        if (housing is null)
        {
            return Result.Failure<Housing>(
                new Error("Unauthorized", "You are not assigned as a housing manager", 403)
            );
        }

        return Result.Success(housing);
    }

    public async Task<Result<HousingDashboardResponse>> GetHousingDashboard(long managerIqamaNo)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<HousingDashboardResponse>(housingResult.Error);

        var housing = housingResult.Value;
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        // Get statistics
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        // OPTIMIZATION: Load riders once with all related data
        var riders = await context.RiderDetails
            .Include(r => r.Employee)
            .Include(r => r.Company)
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo) && !r.Employee.IsEmployee)
            .ToListAsync();

        var activeRiders = riders.Count(r => r.Employee.Status.ToLower() == "enable");
        var inactiveRiders = riders.Count - activeRiders;

        // FIX: Process in batches to avoid SQL parameter limit (2100 max)
        const int batchSize = 500;
        var riderIds = riders.Select(r => r.Id).ToList();
        var vehicleNumbers = riders.Select(r => r.VehicleNumber).Distinct().ToList();
        var riderIqamas = riders.Select(r => r.EmployeeIqamaNo).ToList();

        // Get vehicles
        var vehicles = new List<Vehicle>();
        for (int i = 0; i < vehicleNumbers.Count; i += batchSize)
        {
            var batch = vehicleNumbers.Skip(i).Take(batchSize).ToList();
            var batchVehicles = await context.Vehicles
                .Where(v => batch.Contains(v.VehicleNumber))
                .ToListAsync();
            vehicles.AddRange(batchVehicles);
        }

        // Get vehicles in use
        var vehiclesInUse = 0;
        var vehicleNumbersList = vehicles.Select(v => v.VehicleNumber).ToList();
        for (int i = 0; i < vehicleNumbersList.Count; i += batchSize)
        {
            var batch = vehicleNumbersList.Skip(i).Take(batchSize).ToList();
            var count = await context.RiderVehicleStatus
                .Where(rvs => batch.Contains(rvs.VehicleNumber)
                    && rvs.IsActive
                    && rvs.StatusType == VehicleStatusType.Taken)
                .CountAsync();
            vehiclesInUse += count;
        }

        // Get pending vehicle operations
        var pendingVehicleOps = 0;
        for (int i = 0; i < riderIqamas.Count; i += batchSize)
        {
            var batch = riderIqamas.Skip(i).Take(batchSize).ToList();
            var count = await context.TempVehicleOperations
                .Where(t => !t.IsResolved && batch.Contains(t.RiderIqamaNo))
                .CountAsync();
            pendingVehicleOps += count;
        }

        // Get pending employee updates
        var pendingEmpUpdates = 0;
        for (int i = 0; i < employeeIqamas.Count; i += batchSize)
        {
            var batch = employeeIqamas.Skip(i).Take(batchSize).ToList();
            var count = await context.TempEmployeeUpdates
                .Where(t => !t.IsResolved && batch.Contains(t.IqamaNo))
                .CountAsync();
            pendingEmpUpdates += count;
        }

        // Get pending status changes
        var pendingStatusChanges = 0;
        for (int i = 0; i < employeeIqamas.Count; i += batchSize)
        {
            var batch = employeeIqamas.Skip(i).Take(batchSize).ToList();
            var count = await context.TempEmployeeStatusChanges
                .Where(t => !t.IsResolved && batch.Contains(t.EmployeeIqamaNo))
                .CountAsync();
            pendingStatusChanges += count;
        }

        // Get active disabilities
        var activeDisabilities = 0;
        for (int i = 0; i < riderIds.Count; i += batchSize)
        {
            var batch = riderIds.Skip(i).Take(batchSize).ToList();
            var count = await context.Set<HungerDisability>()
                .Where(h => batch.Contains(h.ActualRiderId) && h.ShiftDate >= today)
                .CountAsync();
            activeDisabilities += count;
        }

        // Get today's shifts
        var todayShifts = 0;
        for (int i = 0; i < riderIds.Count; i += batchSize)
        {
            var batch = riderIds.Skip(i).Take(batchSize).ToList();
            var count = await context.RiderShifts
                .Where(rs => batch.Contains(rs.RiderId) && rs.ShiftDate == today)
                .CountAsync();
            todayShifts += count;
        }

        // Get recent activities
        var recentActivities = new List<RecentActivityItem>();
        for (int i = 0; i < riderIqamas.Count; i += batchSize)
        {
            var batch = riderIqamas.Skip(i).Take(batchSize).ToList();
            var batchActivities = await context.TempVehicleOperations
                .Where(t => batch.Contains(t.RiderIqamaNo))
                .OrderByDescending(t => t.RequestedAt)
                .Take(5)
                .Select(t => new RecentActivityItem(
                    "VehicleOperation",
                    $"Vehicle operation request: {t.VehicleStatusType}",
                    t.RequestedAt
                ))
                .ToListAsync();
            recentActivities.AddRange(batchActivities);
        }

        var total = housing.Employees.Where(e => e.Status.ToLower() != "vacation").ToList().Count;
        var inca = total - activeRiders;

        var stats = new Statistics(
            total,
            activeRiders,
            inca,
            vehicles.Count,
            vehiclesInUse,
            vehicles.Count - vehiclesInUse,
            pendingVehicleOps + pendingEmpUpdates + pendingStatusChanges,
            activeDisabilities,
            todayShifts
        );

        var housingInfo = new HousingInfo(
            housing.Id,
            housing.Name,
            housing.Address,
            housing.Capacity,
            housing.Employees.Count,
            housing.Capacity - housing.Employees.Count
        );

        // FIX: Check if summary report succeeded before accessing Value
        var summaryReport = await reportService.GetHousingPreviousDayCompanySummaryAsync(managerIqamaNo);

        // If summary fails, use null (frontend already handles this with ?. operators)
        var summaryValue = summaryReport.IsSuccess ? summaryReport.Value : null;

        var response = new HousingDashboardResponse(
            housingInfo,
            stats,
            recentActivities.OrderByDescending(a => a.Timestamp).Take(10).ToList(),
            summaryValue
        );

        return Result.Success(response);
    }
    public async Task<Result<HousingDetailResponse>> GetHousingDetails(long managerIqamaNo)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<HousingDetailResponse>(housingResult.Error);

        var housing = housingResult.Value;

        var manager = await context.Employees
            .FirstOrDefaultAsync(e => e.IqamaNo == housing.ManagerIqamaNo);

        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var riders = await context.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo) && !r.Employee.IsEmployee)
            .ToDictionaryAsync(r => r.EmployeeIqamaNo, r => r);

        var employees = housing.Employees.Where(c => c.IsEmployee).Select(e => new EmployeeSummary(
            e.IqamaNo,
            e.NameEN,
            e.NameAR,
            e.JobTitle,
            e.Status,
            riders.ContainsKey(e.IqamaNo),
            riders.ContainsKey(e.IqamaNo) ? riders[e.IqamaNo].WorkingId : null
        )).ToList();

        var response = new HousingDetailResponse(
            housing.Id,
            housing.Name,
            housing.Address,
            housing.Capacity,
            housing.Employees.Count,
            housing.ManagerIqamaNo,
            manager?.NameAR,
            employees
        );

        return Result.Success(response);
    }

    public async Task<Result<List<HousingEmployeeResponse>>> GetHousingEmployees(long managerIqamaNo)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<List<HousingEmployeeResponse>>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var employeesWithRiders = await context.Employees
            .Where(e => employeeIqamas.Contains(e.IqamaNo))
            .GroupJoin(
                context.RiderDetails.Include(r => r.Company),
                e => e.IqamaNo,
                r => r.EmployeeIqamaNo,
                (e, riders) => new { Employee = e, Rider = riders.FirstOrDefault() }
            )
            .ToListAsync();

        var response = employeesWithRiders.Select(x => new HousingEmployeeResponse(
            x.Employee.IqamaNo,
            x.Employee.NameEN,
            x.Employee.NameAR,
            x.Employee.JobTitle,
            x.Employee.Country,
            x.Employee.Phone,
            x.Employee.Status,
            x.Employee.IqamaEndM,
            x.Employee.IqamaEndH,
            x.Rider != null,
            x.Rider?.WorkingId,
            x.Rider?.CompanyId,
            x.Rider?.Company?.Name
        )).ToList();

        return Result.Success(response);
    }

    public async Task<Result<List<HousingRiderResponses>>> GetHousingRiders(long managerIqamaNo)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<List<HousingRiderResponses>>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var riders = await context.RiderDetails
            .Include(r => r.Employee)
            .Include(r => r.Company)
            .Include(r => r.Vehicle)
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo) && !r.Employee.IsEmployee)
            .ToListAsync();

        // Get the most recent status change reasons for riders whose status is not "enable"
        var disabledRiderIqamas = riders
            .Where(r => r.Employee.Status.ToLower() != "enable")
            .Select(r => r.EmployeeIqamaNo)
            .ToList();

        // Fetch the most recent TempEmployeeStatusChange for each disabled rider
        var statusChangeReasons = new Dictionary<long, string>();

        if (disabledRiderIqamas.Any())
        {
            var statusChanges = await context.TempEmployeeStatusChanges
                .Where(t => disabledRiderIqamas.Contains(t.EmployeeIqamaNo))
                .OrderByDescending(t => t.RequestedAt)
                .ToListAsync();

            // Group by EmployeeIqamaNo and take the most recent reason for each
            statusChangeReasons = statusChanges
                .GroupBy(t => t.EmployeeIqamaNo)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(t => t.RequestedAt).First().Reason
                );
        }

        var response = riders.Select(r => new HousingRiderResponses(
            r.Id,
            r.EmployeeIqamaNo,
            r.Employee.NameEN,
            r.Employee.NameAR,
            r.WorkingId,
            r.CompanyId,
            r.Company.Name,
            r.VehicleNumber,
            r.Vehicle?.PlateNumberA,
            r.Employee.Status,
            r.Employee.Phone,
            r.CreatedAt,
            r.Employee.Status.ToLower() != "enable" && statusChangeReasons.ContainsKey(r.EmployeeIqamaNo)
                ? statusChangeReasons[r.EmployeeIqamaNo]
                : null
        )).ToList();

        return Result.Success(response);
    }
    // Continuation of HousingMemberService class

    public async Task<Result<EmployeeDetailResponse>> GetEmployeeDetails(long managerIqamaNo, long employeeIqamaNo)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<EmployeeDetailResponse>(housingResult.Error);

        var housing = housingResult.Value;

        if (!housing.Employees.Any(e => e.IqamaNo == employeeIqamaNo))
        {
            return Result.Failure<EmployeeDetailResponse>(
                new Error("Unauthorized", "This employee is not in your housing", 403)
            );
        }

        var employee = await context.Employees
            .Include(e => e.Housing)
            .Include(e => e.RiderDetails!)
                .ThenInclude(r => r.Company)
            .Include(e => e.EmployeeDocuments)
            .FirstOrDefaultAsync(e => e.IqamaNo == employeeIqamaNo);

        if (employee is null)
            return Result.Failure<EmployeeDetailResponse>(UserErrors.UserNotFound);

        RiderInfo? riderInfo = null;
        if (employee.RiderDetails is not null)
        {
            riderInfo = new RiderInfo(
                employee.RiderDetails.Id,
                employee.RiderDetails.WorkingId,
                employee.RiderDetails.TshirtSize,
                employee.RiderDetails.LicenseNumber,
                employee.RiderDetails.CompanyId,
                employee.RiderDetails.Company.Name,
                employee.RiderDetails.VehicleNumber,
                employee.RiderDetails.CreatedAt
            );
        }

        DocumentInfo? docInfo = null;
        if (employee.EmployeeDocuments is not null)
        {
            docInfo = new DocumentInfo(
                employee.EmployeeDocuments.ProfileImagePath,
                employee.EmployeeDocuments.PassportImagePath,
                employee.EmployeeDocuments.IqamaImagePath,
                employee.EmployeeDocuments.LicenseImagePath,
                employee.EmployeeDocuments.WorkPermitImagePath
            );
        }

        var response = new EmployeeDetailResponse(
            employee.IqamaNo,
            employee.NameEN,
            employee.NameAR,
            employee.JobTitle,
            employee.Country,
            employee.Phone,
            employee.DateOfBirth,
            employee.Status,
            employee.IqamaEndM,
            employee.IqamaEndH,
            employee.PassportNo,
            employee.PassportEnd,
            employee.Sponsor,
            employee.sponsorNo,
            employee.IBAN,
            employee.INKSA,
            employee.IsEmployee,
            employee.HousingId,
            employee.Housing?.Name,
            riderInfo,
            docInfo
        );

        return Result.Success(response);
    }

    public async Task<Result<List<RiderShiftResponse>>> GetRiderShifts(
        long managerIqamaNo,
        DateOnly? startDate,
        DateOnly? endDate)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<List<RiderShiftResponse>>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var riderIds = await context.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .Select(r => r.Id)
            .ToListAsync();

        var query = context.RiderShifts
            .Include(rs => rs.Rider)
                .ThenInclude(r => r.Employee)
            .Include(rs => rs.Company)
            .Where(rs => riderIds.Contains(rs.RiderId));

        if (startDate.HasValue)
            query = query.Where(rs => rs.ShiftDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(rs => rs.ShiftDate <= endDate.Value);

        var shifts = await query
            .OrderByDescending(rs => rs.ShiftDate)
            .ToListAsync();

        var response = shifts.Select(rs => new RiderShiftResponse(
            rs.RiderId,
            rs.WorkingId,
            rs.Rider.Employee.NameAR,
            rs.ShiftDate,
            rs.AcceptedDailyOrders,
            rs.RejectedDailyOrders,
            rs.StackedDeliveries,
            rs.RealRejectedDailyOrders,
            rs.WorkingHours,
            rs.ShiftStatus,
            rs.CompanyId,
            rs.Company.Name,
            rs.CreatedAt
        )).ToList();

        return Result.Success(response);
    }

    public async Task<Result<RiderPerformanceResponse>> GetRiderPerformance(
        long managerIqamaNo,
        int riderId,
        DateOnly startDate,
        DateOnly endDate)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<RiderPerformanceResponse>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var rider = await context.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.Id == riderId && employeeIqamas.Contains(r.EmployeeIqamaNo));

        if (rider is null)
        {
            return Result.Failure<RiderPerformanceResponse>(
                new Error("NotFound", "Rider not found in your housing", 404)
            );
        }

        var shifts = await context.RiderShifts
            .Where(rs => rs.RiderId == riderId
                && rs.ShiftDate >= startDate
                && rs.ShiftDate <= endDate)
            .OrderBy(rs => rs.ShiftDate)
            .ToListAsync();

        var totalShifts = shifts.Count;
        var totalAcceptedOrders = shifts.Sum(s => s.AcceptedDailyOrders);
        var totalRejectedOrders = shifts.Sum(s => s.RejectedDailyOrders);
        var totalStackedDeliveries = shifts.Sum(s => s.StackedDeliveries);
        var totalWorkingHours = shifts.Sum(s => s.WorkingHours);
        var avgOrdersPerShift = totalShifts > 0 ? (float)totalAcceptedOrders / totalShifts : 0;
        var avgWorkingHours = totalShifts > 0 ? totalWorkingHours / totalShifts : 0;
        var totalOrders = totalAcceptedOrders + totalRejectedOrders;
        var acceptanceRate = totalOrders > 0 ? (float)totalAcceptedOrders / totalOrders * 100 : 0;

        var metrics = new PerformanceMetrics(
            totalShifts,
            totalAcceptedOrders,
            totalRejectedOrders,
            totalStackedDeliveries,
            totalWorkingHours,
            avgOrdersPerShift,
            avgWorkingHours,
            acceptanceRate
        );

        var dailyBreakdown = shifts.Select(s => new DailyPerformance(
            s.ShiftDate,
            s.AcceptedDailyOrders,
            s.RejectedDailyOrders,
            s.WorkingHours,
            s.ShiftStatus
        )).ToList();

        var response = new RiderPerformanceResponse(
            riderId,
            rider.WorkingId ?? string.Empty,
            rider.Employee.NameAR,
            startDate,
            endDate,
            metrics,
            dailyBreakdown
        );

        return Result.Success(response);
    }

    public async Task<Result<HousingShiftSummaryResponse>> GetHousingShiftSummary(
        long managerIqamaNo,
        DateOnly date)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<HousingShiftSummaryResponse>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var riderIds = await context.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .Select(r => r.Id)
            .ToListAsync();

        var shifts = await context.RiderShifts
            .Include(rs => rs.Rider)
                .ThenInclude(r => r.Employee)
            .Where(rs => riderIds.Contains(rs.RiderId) && rs.ShiftDate == date)
            .ToListAsync();

        var totalRiders = riderIds.Count;
        var activeRiders = shifts.Count;
        var totalAcceptedOrders = shifts.Sum(s => s.AcceptedDailyOrders);
        var totalRejectedOrders = shifts.Sum(s => s.RejectedDailyOrders);
        var totalWorkingHours = shifts.Sum(s => s.WorkingHours);

        var riderShifts = shifts.Select(s => new RiderShiftSummary(
            s.RiderId,
            s.WorkingId,
            s.Rider.Employee.NameAR,
            s.AcceptedDailyOrders,
            s.RejectedDailyOrders,
            s.WorkingHours,
            s.ShiftStatus
        )).ToList();

        var response = new HousingShiftSummaryResponse(
            date,
            totalRiders,
            activeRiders,
            totalAcceptedOrders,
            totalRejectedOrders,
            totalWorkingHours,
            riderShifts
        );

        return Result.Success(response);
    }

    public async Task<Result<List<HousingVehicleResponse>>> GetHousingVehicles(long managerIqamaNo)
    {
        try
        {
            // Step 1: Get housing with employees
            var housingResult = await GetManagedHousing(managerIqamaNo);
            if (housingResult.IsFailure)
                return Result.Failure<List<HousingVehicleResponse>>(housingResult.Error);

            var housing = housingResult.Value;
            var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

            if (!employeeIqamas.Any())
                return Result.Success(new List<HousingVehicleResponse>());

            // Step 2: Get riders for these employees
            var riders = await context.RiderDetails
                .Include(r => r.Employee)
                .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
                .ToListAsync();

            // Step 3: Get vehicle numbers (excluding null/empty)
            var vehicleNumbers = riders
                .Where(r => !string.IsNullOrWhiteSpace(r.VehicleNumber))
                .Select(r => r.VehicleNumber!)
                .Distinct()
                .ToList();

            if (!vehicleNumbers.Any())
                return Result.Success(new List<HousingVehicleResponse>());

            // Step 4: Get vehicles
            var vehicles = await context.Vehicles
                .Where(v => vehicleNumbers.Contains(v.VehicleNumber))
                .ToListAsync();

            // Step 5: Get all active statuses for these vehicles and process in memory
            var allStatuses = await context.RiderVehicleStatus
                .Where(rvs => vehicleNumbers.Contains(rvs.VehicleNumber) && rvs.IsActive)
                .OrderByDescending(rvs => rvs.Timestamp)
                .ToListAsync();

            // Step 6: Get the latest status for each vehicle (in memory)
            var statusDict = allStatuses
                .GroupBy(s => s.VehicleNumber)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(s => s.Timestamp).First()
                );

            // Step 7: Handle duplicate vehicle numbers in riders
            var riderDict = riders
                .Where(r => !string.IsNullOrWhiteSpace(r.VehicleNumber))
                .GroupBy(r => r.VehicleNumber!)
                .ToDictionary(g => g.Key, g => g.First());

            // Step 8: Build response
            var response = vehicles.Select(v =>
            {
                statusDict.TryGetValue(v.VehicleNumber, out var status);
                riderDict.TryGetValue(v.VehicleNumber, out var rider);

                return new HousingVehicleResponse(
                    v.VehicleNumber,
                    v.VehicleType,
                    v.PlateNumberA,
                    v.PlateNumberE,
                    v.ManufactureYear,
                    v.Manufacturer,
                    v.LicenseExpiryDate,
                    v.Location,
                    status?.StatusType.ToString(), // This should now work correctly
                    rider?.EmployeeIqamaNo,
                    rider?.Employee?.NameAR,
                    rider?.Employee?.NameEN,
                    status?.Timestamp // This should now have the correct timestamp
                );
            }).ToList();

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<List<HousingVehicleResponse>>(
                new Error("Error", $"Error retrieving housing vehicles: {ex.Message}", 400)
            );
        }
    }

    public async Task<Result<List<VehicleStatusHistoryResponse>>> GetVehicleStatusHistory(
        long managerIqamaNo,
        string vehicleNumber)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<List<VehicleStatusHistoryResponse>>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        // Verify vehicle belongs to housing riders
        var vehicleExists = await context.RiderDetails
            .AnyAsync(r => employeeIqamas.Contains(r.EmployeeIqamaNo)
                && r.VehicleNumber == vehicleNumber);

        if (!vehicleExists)
        {
            return Result.Failure<List<VehicleStatusHistoryResponse>>(
                new Error("Unauthorized", "This vehicle is not assigned to your housing", 403)
            );
        }

        var history = await context.RiderVehicleStatus
            .Include(rvs => rvs.Vehicle)
            .Where(rvs => rvs.VehicleNumber == vehicleNumber)
            .OrderByDescending(rvs => rvs.Timestamp)
            .ToListAsync();

        var employeeNames = await context.Employees
            .Where(e => history.Select(h => h.EmployeeIqamaNo).Contains(e.IqamaNo))
            .ToDictionaryAsync(e => e.IqamaNo, e => e.NameEN);

        var response = history.Select(h => new VehicleStatusHistoryResponse(
            h.Id,
            h.VehicleNumber,
            h.EmployeeIqamaNo,
            h.EmployeeIqamaNo.HasValue && employeeNames.ContainsKey(h.EmployeeIqamaNo.Value)
                ? employeeNames[h.EmployeeIqamaNo.Value]
                : null,
            h.StatusType.ToString(),
            h.Reason,
            h.Permission,
            h.PermissionStartDate,
            h.PermissionEndDate,
            h.Timestamp,
            h.IsActive
        )).ToList();

        return Result.Success(response);
    }

    public async Task<Result<List<PendingVehicleOperationResponse>>> GetPendingVehicleOperations(long managerIqamaNo)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<List<PendingVehicleOperationResponse>>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var pendingOps = await context.TempVehicleOperations
            .Include(t => t.Rider)
                .ThenInclude(r => r.Employee)
            .Include(t => t.Vehicle)
            .Where(t => !t.IsResolved && employeeIqamas.Contains(t.RiderIqamaNo))
            .OrderByDescending(t => t.RequestedAt)
            .ToListAsync();

        var response = pendingOps.Select(op => new PendingVehicleOperationResponse(
            op.Id,
            op.RiderIqamaNo,
            op.Rider.Employee.NameEN,
            op.VehicleNumber,
            op.VehiclePlateNumber,
            op.VehicleStatusType.ToString(),
            op.Reason,
            op.Permission,
            op.PermissionEndDate,
            op.RequestedAt,
            op.RequestedBy
        )).ToList();

        return Result.Success(response);
    }

    public async Task<Result<List<HungerDisabilityResponse>>> GetHousingDisabilities(
        long managerIqamaNo,
        DateOnly? startDate,
        DateOnly? endDate)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<List<HungerDisabilityResponse>>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var riderIds = await context.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .Select(r => r.Id)
            .ToListAsync();

        var query = context.Set<HungerDisability>()
            .Include(h => h.Rider)
                .ThenInclude(r => r.Employee)
            .Include(h => h.Company)
            .Where(h => riderIds.Contains(h.ActualRiderId));

        if (startDate.HasValue)
            query = query.Where(h => h.ShiftDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(h => h.ShiftDate <= endDate.Value);

        var disabilities = await query
            .Where(h => h.AcceptedDailyOrders < 14)
            .OrderByDescending(h => h.ShiftDate)
            .ToListAsync();

        var substituteRiders = await context.RiderDetails
            .Include(r => r.Employee)
            .Where(r => disabilities.Select(d => d.SubstituteRiderId).Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r);

        var response = disabilities.Select(d => new HungerDisabilityResponse(
            d.Id,
            d.ActualRiderId,
            d.ActualWorkingId,
            d.Rider.Employee.NameEN,
            d.SubstituteRiderId,
            d.SubstituteWorkingId,
            d.SubstituteRiderId.HasValue && substituteRiders.ContainsKey(d.SubstituteRiderId.Value)
                ? substituteRiders[d.SubstituteRiderId.Value].Employee.NameEN
                : null,
            d.Days,
            d.ShiftDate,
            d.CompanyId,
            d.Company.Name,
            d.AcceptedDailyOrders,
            d.CreatedAt
        )).ToList();

        return Result.Success(response);
    }

    public async Task<Result<List<ShiftSubstitutionResponse>>> GetActiveSubstitutions(long managerIqamaNo)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<List<ShiftSubstitutionResponse>>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var riderIds = await context.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .Select(r => r.Id)
            .ToListAsync();

        var substitutions = await context.RiderShiftSubstitutions
            .Include(s => s.ActualRider!)
                .ThenInclude(r => r.Employee)
            .Include(s => s.SubstituteRider)
                .ThenInclude(r => r.Employee)
            .Where(s => s.IsActive
                && (riderIds.Contains(s.ActualRiderId ?? 0) || riderIds.Contains(s.SubstituteRiderId)))
            .OrderByDescending(s => s.StartDate)
            .ToListAsync();

        var response = substitutions.Select(s => new ShiftSubstitutionResponse(
            s.Id,
            s.ActualRiderId,
            s.ActualRiderWorkingId,
            s.ActualRider?.Employee.NameEN,
            s.SubstituteRiderId,
            s.SubstituteWorkingId,
            s.SubstituteRider.Employee.NameEN,
            s.StartDate,
            s.EndDate,
            s.Reason,
            s.CreatedBy,
            s.IsActive
        )).ToList();

        return Result.Success(response);
    }

    public async Task<Result<List<PendingEmployeeUpdateResponse>>> GetPendingEmployeeUpdates(long managerIqamaNo)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<List<PendingEmployeeUpdateResponse>>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var pendingUpdates = await context.TempEmployeeUpdates
            .Include(t => t.Employee)
            .Where(t => !t.IsResolved && employeeIqamas.Contains(t.IqamaNo))
            .OrderByDescending(t => t.UploadedAt)
            .ToListAsync();

        var response = pendingUpdates.Select(update =>
        {
            var changes = new List<FieldChange>();

            if (update.OldNameEN != update.NewNameEN)
                changes.Add(new FieldChange("Name (EN)", update.OldNameEN, update.NewNameEN));

            if (update.OldNameAR != update.NewNameAR)
                changes.Add(new FieldChange("Name (AR)", update.OldNameAR, update.NewNameAR));

            if (update.OldPhone != update.NewPhone)
                changes.Add(new FieldChange("Phone", update.OldPhone, update.NewPhone));

            if (update.OldIqamaEndM != update.NewIqamaEndM)
                changes.Add(new FieldChange("Iqama End (M)",
                    update.OldIqamaEndM?.ToString(),
                    update.NewIqamaEndM?.ToString()));

            return new PendingEmployeeUpdateResponse(
                update.Id,
                update.IqamaNo,
                update.Employee?.NameEN ?? "New Employee",
                update.IsNewEmployee,
                changes,
                update.UploadedAt,
                update.UploadedBy
            );
        }).ToList();

        return Result.Success(response);
    }

    public async Task<Result<List<PendingStatusChangeResponse>>> GetPendingStatusChanges(long managerIqamaNo)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<List<PendingStatusChangeResponse>>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var pendingChanges = await context.TempEmployeeStatusChanges
            .Include(t => t.Employee)
            .Where(t => !t.IsResolved && employeeIqamas.Contains(t.EmployeeIqamaNo))
            .OrderByDescending(t => t.RequestedAt)
            .ToListAsync();

        var response = pendingChanges.Select(change => new PendingStatusChangeResponse(
            change.Id,
            change.EmployeeIqamaNo,
            change.Employee.NameEN,
            change.Action,
            change.Reason,
            change.RequestedBy,
            change.RequestedAt
        )).ToList();

        return Result.Success(response);
    }

    public async Task<Result<HousingMonthlyReportResponse>> GetMonthlyReport(
        long managerIqamaNo,
        int year,
        int month)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<HousingMonthlyReportResponse>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var riderDetails = await context.RiderDetails
            .Include(r => r.Employee)
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .ToListAsync();

        var riderIds = riderDetails.Select(r => r.Id).ToList();

        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var shifts = await context.RiderShifts
            .Where(rs => riderIds.Contains(rs.RiderId)
                && rs.ShiftDate >= startDate
                && rs.ShiftDate <= endDate)
            .ToListAsync();

        var disabilities = await context.Set<HungerDisability>()
            .Where(h => riderIds.Contains(h.ActualRiderId)
                && h.ShiftDate >= startDate
                && h.ShiftDate <= endDate)
            .CountAsync();

        var substitutions = await context.RiderShiftSubstitutions
            .Where(s => riderIds.Contains(s.SubstituteRiderId)
                && s.StartDate.Month == month
                && s.StartDate.Year == year)
            .CountAsync();

        var uniqueDays = shifts.Select(s => s.ShiftDate).Distinct().Count();
        var avgRidersPerDay = uniqueDays > 0 ? shifts.Count / uniqueDays : 0;

        var monthlyStats = new MonthlyStatistics(
            shifts.Count,
            shifts.Sum(s => s.AcceptedDailyOrders),
            shifts.Sum(s => s.RejectedDailyOrders),
            shifts.Sum(s => s.WorkingHours),
            disabilities,
            substitutions,
            avgRidersPerDay
        );

        var riderPerformances = shifts
            .GroupBy(s => s.RiderId)
            .Select(g =>
            {
                var rider = riderDetails.First(r => r.Id == g.Key);
                return new RiderMonthlyPerformance(
                    g.Key,
                    rider.WorkingId ?? string.Empty,
                    rider.Employee.NameEN,
                    g.Count(),
                    g.Sum(s => s.AcceptedDailyOrders),
                    g.Sum(s => s.RejectedDailyOrders),
                    g.Sum(s => s.WorkingHours),
                    g.Count() > 0 ? (float)g.Sum(s => s.AcceptedDailyOrders) / g.Count() : 0
                );
            })
            .ToList();

        var vehicleUsage = new List<VehicleUtilization>(); // Simplified for now

        var response = new HousingMonthlyReportResponse(
            housing.Id,
            housing.Name,
            year,
            month,
            monthlyStats,
            riderPerformances,
            vehicleUsage
        );

        return Result.Success(response);
    }

    public async Task<Result<byte[]>> ExportHousingReport(
        long managerIqamaNo,
        DateOnly startDate,
        DateOnly endDate)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<byte[]>(housingResult.Error);

        // This would use ClosedXML to generate Excel report
        // Implementation details omitted for brevity
        return Result.Failure<byte[]>(
            new Error("NotImplemented", "Excel export to be implemented", 501)
        );
    }
    // Add to MemberService class
    public async Task<Result> RequestTakeVehicleForHousingAsync(
        long managerIqamaNo,
        MemberVehicleOperationRequest request)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return housingResult;

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        // Verify rider belongs to this housing
        var rider = await context.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == request.RiderIqamaNo
                && employeeIqamas.Contains(r.EmployeeIqamaNo));

        if (rider is null)
        {
            return Result.Failure(HousingMemberErrors.RiderNotInHousing);
        }

        // Verify vehicle exists and get its assignment
        var vehicle = await context.Vehicles
            .FirstOrDefaultAsync(v => v.PlateNumberA == request.VehiclePlate);

        if (vehicle is null)
        {
            return Result.Failure(new Error(
                "VehicleNotFound",
                "Vehicle not found",
                404
            ));
        }

        // Check if vehicle is currently assigned to any rider in this housing
        var vehicleInHousing = await context.RiderDetails
            .AnyAsync(r => employeeIqamas.Contains(r.EmployeeIqamaNo)
                && r.VehicleNumber == vehicle.VehicleNumber);

        // If vehicle is assigned elsewhere, check if it's available
        if (!vehicleInHousing)
        {
            var isVehicleAvailable = !await context.RiderVehicleStatus
                .AnyAsync(s => s.VehicleNumber == vehicle.VehicleNumber
                    && s.IsActive
                    && (s.StatusType == VehicleStatusType.Taken
                        || s.StatusType == VehicleStatusType.Problem
                        || s.StatusType == VehicleStatusType.Stolen
                        || s.StatusType == VehicleStatusType.BreakUp));

            if (!isVehicleAvailable)
            {
                return Result.Failure(new Error(
                    "VehicleUnavailable",
                    "This vehicle is not available and not assigned to your housing",
                    403
                ));
            }
        }

        // Get the username of the manager for the request
        var manager = await userManager.FindByNameAsync(managerIqamaNo.ToString());
        if (manager is null)
        {
            return Result.Failure(UserErrors.UserNotFound);
        }

        var t = long.Parse(manager.UserName!);
        var name = await context.Employees
            .Where(e => e.IqamaNo == t)
            .Select(e => e.NameAR)
            .FirstOrDefaultAsync();

        // Create the vehicle operation request
        var operation = new TempVehicleOperation
        {
            RiderIqamaNo = request.RiderIqamaNo,
            VehiclePlateNumber = request.VehiclePlate,
            VehicleNumber = vehicle.VehicleNumber,
            VehicleStatusType = VehicleStatusType.Taken,
            Reason = request.Reason ?? "Housing manager request - take vehicle",
            RequestedAt = DateTime.UtcNow.AddHours(3),
            RequestedBy = name!,
            IsResolved = false
        };

        await context.TempVehicleOperations.AddAsync(operation);
        await context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result> RequestReturnVehicleForHousingAsync(
        long managerIqamaNo,
        MemberVehicleOperationRequest request)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return housingResult;

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        // Verify rider belongs to this housing
        var rider = await context.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == request.RiderIqamaNo
                && employeeIqamas.Contains(r.EmployeeIqamaNo));

        if (rider is null)
        {
            return Result.Failure(HousingMemberErrors.RiderNotInHousing);
        }

        // Verify vehicle exists
        var vehicle = await context.Vehicles
            .FirstOrDefaultAsync(v => v.PlateNumberA == request.VehiclePlate);

        if (vehicle is null)
        {
            return Result.Failure(new Error(
                "VehicleNotFound",
                "Vehicle not found",
                404
            ));
        }

        // Verify rider has this vehicle
        if (rider.VehicleNumber != vehicle.VehicleNumber)
        {
            return Result.Failure(new Error(
                "VehicleNotAssigned",
                "This vehicle is not assigned to the specified rider",
                400
            ));
        }

        // Get the username of the manager
        var manager = await userManager.FindByNameAsync(managerIqamaNo.ToString());
        if (manager is null)
        {
            return Result.Failure(UserErrors.UserNotFound);
        }

        var t = long.Parse(manager.UserName!);
        var name = await context.Employees
            .Where(e => e.IqamaNo == t)
            .Select(e => e.NameAR)
            .FirstOrDefaultAsync();

        // Create the vehicle operation request
        var operation = new TempVehicleOperation
        {
            RiderIqamaNo = request.RiderIqamaNo,
            VehiclePlateNumber = request.VehiclePlate,
            VehicleNumber = vehicle.VehicleNumber,
            VehicleStatusType = VehicleStatusType.Returned,
            Reason = request.Reason ?? "Housing manager request - return vehicle",
            RequestedAt = DateTime.UtcNow.AddHours(3),
            RequestedBy = name!,
            IsResolved = false
        };

        await context.TempVehicleOperations.AddAsync(operation);
        await context.SaveChangesAsync();

        return Result.Success();
    }

    // Update RequestReportProblemForHousingAsync method - make rider optional
    public async Task<Result> RequestReportProblemForHousingAsync(
        long managerIqamaNo,
        MemberVehicleOperationRequest request)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return housingResult;

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        RiderDetails? rider = null;

        // Rider is optional - only verify if provided (not 0)
        if (request.RiderIqamaNo != 0)
        {
            rider = await context.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == request.RiderIqamaNo
                    && employeeIqamas.Contains(r.EmployeeIqamaNo));

            if (rider is null)
            {
                return Result.Failure(HousingMemberErrors.RiderNotInHousing);
            }
        }

        // Verify vehicle exists
        var vehicle = await context.Vehicles
            .FirstOrDefaultAsync(v => v.PlateNumberA == request.VehiclePlate);

        if (vehicle is null)
        {
            return Result.Failure(new Error(
                "VehicleNotFound",
                "Vehicle not found",
                404
            ));
        }

        // Verify vehicle belongs to housing
        var vehicleInHousing = await context.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .AnyAsync(r => r.VehicleNumber == vehicle.VehicleNumber);

        if (!vehicleInHousing && !string.IsNullOrEmpty(vehicle.Location))
        {
            var housingLocation = housing.Address;

            if (!vehicle.Location.Contains(housing.Name, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure(new Error(
                    "VehicleNotInHousing",
                    "This vehicle does not belong to your housing",
                    403
                ));
            }
        }

        // Check if vehicle already has an active problem
        var existingProblem = await context.RiderVehicleStatus
            .AnyAsync(s => s.VehicleNumber == vehicle.VehicleNumber
                && s.IsActive
                && s.StatusType == VehicleStatusType.Problem);

        if (existingProblem)
        {
            return Result.Failure(new Error(
                "AlreadyReported",
                "This vehicle already has an active problem reported",
                400
            ));
        }

        // Check if vehicle is stolen or broken up
        var isStolen = await context.RiderVehicleStatus
            .AnyAsync(s => s.VehicleNumber == vehicle.VehicleNumber
                && s.IsActive
                && s.StatusType == VehicleStatusType.Stolen);

        if (isStolen)
        {
            return Result.Failure(new Error(
                "VehicleStolen",
                "Cannot report problem for a stolen vehicle",
                400
            ));
        }

        var isBreakUp = await context.RiderVehicleStatus
            .AnyAsync(s => s.VehicleNumber == vehicle.VehicleNumber
                && s.IsActive
                && s.StatusType == VehicleStatusType.BreakUp);

        if (isBreakUp)
        {
            return Result.Failure(new Error(
                "VehicleBreakUp",
                "Cannot report problem for a broken up vehicle",
                400
            ));
        }

        // Get the username of the manager
        var manager = await userManager.FindByNameAsync(managerIqamaNo.ToString());
        if (manager is null)
        {
            return Result.Failure(UserErrors.UserNotFound);
        }

        // Create the vehicle operation request
        var operation = new TempVehicleOperation
        {
            RiderIqamaNo = request.RiderIqamaNo, // Store as is, even if 0
            VehiclePlateNumber = request.VehiclePlate,
            VehicleNumber = vehicle.VehicleNumber,
            VehicleStatusType = VehicleStatusType.Problem,
            Reason = request.Reason ?? "Housing manager report - vehicle problem",
            RequestedAt = DateTime.UtcNow.AddHours(3),
            RequestedBy = manager.UserName ?? $"Housing Manager ({managerIqamaNo})",
            IsResolved = false
        };

        await context.TempVehicleOperations.AddAsync(operation);
        await context.SaveChangesAsync();

        return Result.Success();
    }
    public async Task<Result> RequestEmployeeStatusChangeForHousingAsync(
        long managerIqamaNo,
        MemberStatusChangeRequest request)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return housingResult;

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        // Verify the employee/rider belongs to this housing
        if (!employeeIqamas.Contains(request.EmployeeIqamaNo))
        {
            return Result.Failure(HousingMemberErrors.EmployeeNotInHousing);
        }

        // Validate status
        if (!EmployeeStatus.IsValid(request.NewStatus))
        {
            return Result.Failure(
                new Error("InvalidStatus",
                    $"Invalid status. Valid statuses are: {string.Join(", ", EmployeeStatus.ValidStatuses)}",
                    400));
        }

        var employee = await context.Employees
            .Include(e => e.RiderDetails)
            .FirstOrDefaultAsync(e => e.IqamaNo == request.EmployeeIqamaNo);

        if (employee is null)
        {
            return Result.Failure(
                new Error("NotFound", "Employee not found", 404));
        }

        // Check if there's already a pending request for this employee
        var existingRequest = await context.TempEmployeeStatusChanges
            .AnyAsync(t => t.EmployeeIqamaNo == request.EmployeeIqamaNo && !t.IsResolved);

        if (existingRequest)
        {
            return Result.Failure(
                new Error("PendingRequest",
                    "There is already a pending status change request for this employee", 400));
        }

        // Check if the status is already set to the requested status
        if (employee.Status.ToLower() == request.NewStatus.ToLower())
        {
            return Result.Failure(
                new Error("SameStatus",
                    $"Employee status is already set to '{request.NewStatus}'", 400));
        }

        // Get the manager's username
        var manager = await userManager.FindByNameAsync(managerIqamaNo.ToString());
        if (manager is null)
        {
            return Result.Failure(UserErrors.UserNotFound);
        }

        var t = long.Parse(manager.UserName!);
        var name = await context.Employees
            .Where(e => e.IqamaNo == t)
            .Select(e => e.NameAR)
            .FirstOrDefaultAsync();

        var statusChange = new TempEmployeeStatusChange
        {
            EmployeeIqamaNo = request.EmployeeIqamaNo,
            Action = request.NewStatus.ToLower(),
            Reason = request.Reason,
            RequestedBy = name!,
            RequestedAt = DateTime.UtcNow.AddHours(3),
            IsResolved = false
        };

        await context.TempEmployeeStatusChanges.AddAsync(statusChange);
        await context.SaveChangesAsync();

        return Result.Success();
    }
}

// Add to IMemberService.cs
public record MemberFixVehicleRequest(
    string VehiclePlate,
    string? FixDescription = null
);