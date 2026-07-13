using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Authentication;
using Application.Contracts.InventoryAudit;
using Application.Contracts.RiderAccessoryCon;
using Application.Contracts.SparePartCo;
using Application.Contracts.SupplierCon;
using Application.Service.Empolyee;
using Application.Service.Reports;
using Domain;
using Domain.Entities;
using Domain.Entities.Spare;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Application.Service.Member.IMemberService;

namespace Application.Service.Member;

public class MemberService(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IJwtProvider jwtProvider, ApplicationDbcontext context, IReportService reportService) : IMemberService
{
    private readonly UserManager<ApplicationUser> userManager = userManager;
    private readonly SignInManager<ApplicationUser> signInManager = signInManager;
    private readonly IJwtProvider jwtProvider = jwtProvider;
    private readonly ApplicationDbcontext context = context;
    private readonly IReportService reportService = reportService;

    public async Task<Result<HousingSpendingReportResponse>> GetHousingSpendingReportAsync(
    long managerIqamaNo,
    DateOnly startDate,
    DateOnly endDate)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<HousingSpendingReportResponse>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees
            .Where(e => !e.IsDeleted)
            .Select(e => e.IqamaNo)
            .ToList();

        // Convert DateOnly to DateTime boundaries for DateTime-typed UsedAt / IssuedAt columns
        var fromUtc = startDate.ToDateTime(TimeOnly.MinValue);
        var toUtc = endDate.ToDateTime(TimeOnly.MaxValue);

        // ── Spare parts ───────────────────────────────────────────────────────


        var sparePartUsages = await context.SparePartUsages
            .Include(u => u.SparePart)
            .Where(u => u.Location == housing.Name
                && u.UsedAt >= fromUtc
                && u.UsedAt <= toUtc)
            .OrderBy(u => u.VehicleNumber)
            .ThenBy(u => u.UsedAt)
            .AsNoTracking()
            .ToListAsync();

        // Resolve plate numbers for vehicles that appear in usages
        var usageVehicleNumbers = sparePartUsages.Select(u => u.VehicleNumber).Distinct().ToList();
        var vehiclePlates = await context.Vehicles
            .Where(v => usageVehicleNumbers.Contains(v.VehicleNumber))
            .ToDictionaryAsync(v => v.VehicleNumber, v => v.PlateNumberA);

        // Group by vehicle → group by spare part
        var vehicleSpending = sparePartUsages
            .GroupBy(u => u.VehicleNumber)
            .Select(vehicleGroup =>
            {
                var sparePartItems = vehicleGroup
                    .GroupBy(u => u.SparePartId)
                    .Select(partGroup =>
                    {
                        var first = partGroup.First();
                        var totalQty = partGroup.Sum(u => u.QuantityUsed);
                        var unitPrice = first.SparePart.Price;

                        return new SparePartSpendingItem(
                            SparePartId: first.SparePartId,
                            SparePartName: first.SparePart.Name,
                            TotalQuantityUsed: totalQty,
                            UnitPrice: unitPrice,
                            TotalCost: partGroup.Sum(u => u.QuantityUsed * unitPrice),
                            UsageDates: partGroup.Select(u => u.UsedAt).ToList()
                        );
                    })
                    .OrderByDescending(p => p.TotalCost)
                    .ToList();

                var vehicleNumber = vehicleGroup.Key;

                return new VehicleSpendingDetail(
                    VehicleNumber: vehicleNumber,
                    VehiclePlate: vehiclePlates.TryGetValue(vehicleNumber, out var plate)
                        ? plate
                        : "N/A",
                    TotalCost: sparePartItems.Sum(p => p.TotalCost),
                    SparePartUsages: sparePartItems
                );
            })
            .OrderByDescending(v => v.TotalCost)
            .ToList();

        // ── Rider accessories ─────────────────────────────────────────────────

        var accessoryUsages = await context.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Include(u => u.Rider)
                .ThenInclude(r => r.Employee)
            .Where(u => u.Location == housing.Name
                && u.IssuedAt >= fromUtc
                && u.IssuedAt <= toUtc)
            .OrderBy(u => u.RiderId)
            .ThenBy(u => u.IssuedAt)
            .AsNoTracking()
            .ToListAsync();


        // Group by rider → group by accessory
        var riderSpending = accessoryUsages
            .GroupBy(u => u.RiderId)
            .Select(riderGroup =>
            {
                var first = riderGroup.First();
                var rider = first.Rider;

                var accessoryItems = riderGroup
                    .GroupBy(u => u.RiderAccessoryId)
                    .Select(accGroup =>
                    {
                        var accFirst = accGroup.First();
                        var qty = accGroup.Count(); // each row = 1 unit issued
                        var unitPrice = accFirst.RiderAccessory.Price;

                        return new AccessorySpendingItem(
                            AccessoryId: accFirst.RiderAccessoryId,
                            AccessoryName: accFirst.RiderAccessory.Name,
                            TotalQuantityIssued: qty,
                            UnitPrice: unitPrice,
                            TotalCost: qty * unitPrice,
                            IssuanceDates: accGroup.Select(u => u.IssuedAt).ToList()
                        );
                    })
                    .OrderByDescending(a => a.TotalCost)
                    .ToList();

                return new RiderSpendingDetail(
                    RiderId: riderGroup.Key,
                    RiderNameAR: rider?.Employee?.NameAR ?? "N/A",
                    RiderNameEN: rider?.Employee?.NameEN ?? "N/A",
                    WorkingId: rider?.WorkingId ?? "N/A",
                    TotalCost: accessoryItems.Sum(a => a.TotalCost),
                    AccessoryUsages: accessoryItems
                );
            })
            .OrderByDescending(r => r.TotalCost)
            .ToList();

        // ── Totals ────────────────────────────────────────────────────────────

        var totalSparePartsCost = vehicleSpending.Sum(v => v.TotalCost);
        var totalAccessoriesCost = riderSpending.Sum(r => r.TotalCost);

        var report = new HousingSpendingReportResponse(
            StartDate: startDate,
            EndDate: endDate,
            HousingName: housing.Name,
            TotalSparePartsCost: totalSparePartsCost,
            TotalAccessoriesCost: totalAccessoriesCost,
            GrandTotal: totalSparePartsCost + totalAccessoriesCost,
            VehicleSpending: vehicleSpending,
            RiderSpending: riderSpending
        );

        return Result.Success(report);
    }


    #region member service
    private const float TARGET_HOURS_PER_DAY = 9f;
    private const int TARGET_ORDERS_PER_DAY = 15;



    private static string DeterminePerformanceLevel(float actualHours, int actualOrders, float targetHours, int targetOrders)
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

    public async Task<Result<HousingDetailedDailyPerformanceReport>> GetHousingDetailedDailyPerformanceForManagerAsync(
    long managerIqamaNo,
    DateOnly startDate,
    DateOnly endDate,
    CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            return Result.Failure<HousingDetailedDailyPerformanceReport>(
                new Error("End date must be after start date", "invalid_input", 400));

        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<HousingDetailedDailyPerformanceReport>(housingResult.Error);

        var housing = housingResult.Value;
        var totalExpectedDays = endDate.DayNumber - startDate.DayNumber + 1;

        var employeeIqamas = housing.Employees
            .Where(e => !e.IsDeleted)
            .Select(e => e.IqamaNo)
            .ToList();

        var riderIds = await context.RiderDetails
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (!riderIds.Any())
            return Result.Failure<HousingDetailedDailyPerformanceReport>(
                new Error("No riders found for this housing", "no_data", 404));

        var shifts = await context.RiderShifts
            .Include(s => s.Rider)
                .ThenInclude(r => r.Employee)
            .Where(s => riderIds.Contains(s.RiderId) &&
                       s.CompanyId == 1 &&
                       s.ShiftDate >= startDate &&
                       s.ShiftDate <= endDate)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        const float TARGET_HOURS = 9f;
        const int TARGET_ORDERS = 15;

        var riderGroups = shifts.GroupBy(s => s.RiderId);
        var riderDetails = new List<RiderDailyPerformanceDetail>();

        int housingTotalWorkingDays = 0;
        int housingTotalAbsentDays = 0;
        float housingTotalHours = 0;
        float housingTotalTargetHours = 0;
        int housingTotalOrders = 0;
        int housingTotalTargetOrders = 0;
        var attendanceRates = new List<decimal>();
        var hoursCompletionRates = new List<decimal>();
        var ordersCompletionRates = new List<decimal>();

        foreach (var group in riderGroups)
        {
            var rider = group.First().Rider;
            if (rider?.Employee == null) continue;

            var riderShifts = group.ToList();
            var shiftDictionary = riderShifts.ToDictionary(s => s.ShiftDate);

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

                    var hoursDiff = shift.WorkingHours - TARGET_HOURS;
                    var ordersDiff = shift.AcceptedDailyOrders - TARGET_ORDERS;
                    var perfLevel = DeterminePerformanceLevel(shift.WorkingHours, shift.AcceptedDailyOrders, TARGET_HOURS, TARGET_ORDERS);

                    dailyEntries.Add(new DailyPerformanceEntry(
                        Date: currentDate,
                        IsPresent: true,
                        WorkingHours: shift.WorkingHours,
                        TargetHours: TARGET_HOURS,
                        HoursDifference: hoursDiff,
                        AcceptedOrders: shift.AcceptedDailyOrders,
                        RejectedOrders: shift.RejectedDailyOrders,
                        TargetOrders: TARGET_ORDERS,
                        OrdersDifference: ordersDiff,
                        ShiftStatus: shift.ShiftStatus,
                        PerformanceLevel: perfLevel
                    ));
                }
                else
                {
                    absentDays++;
                    dailyEntries.Add(new DailyPerformanceEntry(
                        Date: currentDate,
                        IsPresent: false,
                        WorkingHours: 0,
                        TargetHours: TARGET_HOURS,
                        HoursDifference: -TARGET_HOURS,
                        AcceptedOrders: 0,
                        RejectedOrders: 0,
                        TargetOrders: TARGET_ORDERS,
                        OrdersDifference: -TARGET_ORDERS,
                        ShiftStatus: "Absent",
                        PerformanceLevel: "Absent"
                    ));
                }
                currentDate = currentDate.AddDays(1);
            }

            var targetHours = totalExpectedDays * TARGET_HOURS;
            var targetOrders = totalExpectedDays * TARGET_ORDERS;
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
                OverallPerformanceScore: overallScore,
                0
            );

            riderDetails.Add(new RiderDailyPerformanceDetail(
                RiderId: rider.Id,
                IqamaNo: rider.EmployeeIqamaNo,
                RiderNameAR: rider.Employee.NameAR,
                RiderNameEN: rider.Employee.NameEN,
                WorkingId: riderShifts.OrderByDescending(s => s.ShiftDate).First().WorkingId,
                DailyEntries: dailyEntries,
                PeriodSummary: periodSummary
            ));

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

        riderDetails = riderDetails
            .OrderByDescending(r => r.PeriodSummary.OverallPerformanceScore)
            .ToList();

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

        var housingDetail = new HousingPerformanceDetail(
            HousingId: housing.Id,
            HousingName: housing.Name,
            Riders: riderDetails,
            HousingSummary: housingSummary
        );

        var totalRiders = riderDetails.Count;
        var companyAttendanceRate = totalRiders > 0 && totalExpectedDays > 0
            ? (decimal)housingTotalWorkingDays / (totalRiders * totalExpectedDays) * 100
            : 0;
        var companyHoursRate = housingTotalTargetHours > 0
            ? (decimal)housingTotalHours / (decimal)housingTotalTargetHours * 100
            : 0;
        var companyOrdersRate = housingTotalTargetOrders > 0
            ? (decimal)housingTotalOrders / housingTotalTargetOrders * 100
            : 0;

        var summary = new ReportSummary(
            TotalHousings: 1,
            TotalRiders: totalRiders,
            TotalWorkingDays: housingTotalWorkingDays,
            TotalAbsentDays: housingTotalAbsentDays,
            GrandTotalHours: housingTotalHours,
            GrandTotalTargetHours: housingTotalTargetHours,
            GrandTotalOrders: housingTotalOrders,
            GrandTotalTargetOrders: housingTotalTargetOrders,
            CompanyWideAttendanceRate: companyAttendanceRate,
            CompanyWideHoursCompletionRate: companyHoursRate,
            CompanyWideOrdersCompletionRate: companyOrdersRate
        );

        var report = new HousingDetailedDailyPerformanceReport(
            StartDate: startDate,
            EndDate: endDate,
            TotalExpectedDays: totalExpectedDays,
            HousingDetails: new List<HousingPerformanceDetail> { housingDetail },
            Summary: summary
        );

        return Result.Success(report);
    }

    public async Task<Result<UpdateRiderCompanyResponse>> UpdateRiderCompanyAsync(
    long managerIqamaNo,
    MemberUpdateRiderCompanyRequest request)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<UpdateRiderCompanyResponse>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

        // Verify rider belongs to this housing
        var rider = await context.RiderDetails
            .Include(r => r.Employee)
            .Include(r => r.Company)
            .FirstOrDefaultAsync(r => r.Id == request.RiderId
                && employeeIqamas.Contains(r.EmployeeIqamaNo));

        if (rider == null)
            return Result.Failure<UpdateRiderCompanyResponse>(
                HousingMemberErrors.RiderNotFound);

        // Check if already assigned to this company
        if (rider.CompanyId == request.NewCompanyId)
            return Result.Failure<UpdateRiderCompanyResponse>(
                HousingMemberErrors.SameCompanyAssignment);

        // Verify new company exists
        var newCompany = await context.Companies
            .FirstOrDefaultAsync(c => c.Id == request.NewCompanyId);

        if (newCompany == null)
            return Result.Failure<UpdateRiderCompanyResponse>(
                HousingMemberErrors.CompanyNotFound);

        // Get manager name
        var manager = await context.Employees
            .FirstOrDefaultAsync(e => e.IqamaNo == managerIqamaNo);

        if (manager == null)
            return Result.Failure<UpdateRiderCompanyResponse>(UserErrors.UserNotFound);

        var oldCompanyId = rider.CompanyId;
        var oldCompanyName = rider.Company.Name;

        using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            // Create history record
            var history = new RiderCompanyHistory
            {
                RiderId = rider.Id,
                CompanyId = oldCompanyId,
                StartDate = rider.CreatedAt,
                EndDate = DateTime.UtcNow.AddHours(3),
                Reason = request.Reason ?? $"Company changed by housing manager: {manager.NameAR}"
            };
            await context.RiderCompanyHistory.AddAsync(history);

            // Update rider's company
            rider.CompanyId = request.NewCompanyId;

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            var response = new UpdateRiderCompanyResponse(
                RiderId: rider.Id,
                RiderIqamaNo: rider.EmployeeIqamaNo,
                RiderName: rider.Employee.NameAR,
                WorkingId: rider.WorkingId ?? "N/A",
                OldCompanyId: oldCompanyId,
                OldCompanyName: oldCompanyName,
                NewCompanyId: request.NewCompanyId,
                NewCompanyName: newCompany.Name,
                ChangedAt: DateTime.UtcNow.AddHours(3),
                ChangedBy: manager.NameAR,
                Reason: request.Reason
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<UpdateRiderCompanyResponse>(
                new Error("UpdateError", $"Failed to update rider company: {ex.Message}", 500));
        }
    }

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
        if (request.RiderIqamaNo != 0 && !employeeIqamas.Contains(request.RiderIqamaNo ?? 2536361732))
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
            var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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

        //if (!vehicleInHousing && !string.IsNullOrEmpty(vehicle.Location))
        //{
        //    if (!vehicle.Location.Contains(housing.Name, StringComparison.OrdinalIgnoreCase))
        //    {
        //        return Result.Failure(new Error(
        //            "VehicleNotInHousing",
        //            "This vehicle does not belong to your housing",
        //            403
        //        ));
        //    }
        //}

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
            RiderIqamaNo = null, // No rider - vehicle was returned when problem was reported
            VehiclePlateNumber = request.VehiclePlate,
            VehicleNumber = vehicle.VehicleNumber,
            VehicleStatusType = VehicleStatusType.Returned, // Request to mark as available (fixed)
            Reason = $"Problem fixed - Original issue: {activeProblem.Reason}",
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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
                       s.ShiftDate <= endDate
                       && s.CompanyId == 1
                       )
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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted)
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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
            return "📊 عدد الطلبات بقي ثابتًا بين الفترتين";

        if (difference > 0)
        {
            if (changePercentage >= 50)
                return $"🚀 زيادة كبيرة بمقدار {difference:N0} طلب (+{changePercentage:F1}٪) – نمو ممتاز!";
            else if (changePercentage >= 20)
                return $"📈 زيادة قوية بمقدار {difference:N0} طلب (+{changePercentage:F1}٪) – أداء جيد!";
            else if (changePercentage >= 10)
                return $"✅ زيادة متوسطة بمقدار {difference:N0} طلب (+{changePercentage:F1}٪)";
            else
                return $"↗️ زيادة طفيفة بمقدار {difference:N0} طلب (+{changePercentage:F1}٪)";
        }
        else
        {
            var absChange = Math.Abs(changePercentage);

            if (absChange >= 50)
                return $"📉 انخفاض حاد بمقدار {Math.Abs(difference):N0} طلب ({changePercentage:F1}٪) – يحتاج إلى تدخل عاجل!";
            else if (absChange >= 20)
                return $"⚠️ انخفاض ملحوظ بمقدار {Math.Abs(difference):N0} طلب ({changePercentage:F1}٪) – يتطلب المراجعة";
            else if (absChange >= 10)
                return $"↘️ انخفاض متوسط بمقدار {Math.Abs(difference):N0} طلب ({changePercentage:F1}٪)";
            else
                return $"➡️ انخفاض طفيف بمقدار {Math.Abs(difference):N0} طلب ({changePercentage:F1}٪)";
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

        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();
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

        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();
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
                       s.ShiftDate <= endDate
                       && s.CompanyId == 1
                       )
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

        user.LastLogin = DateTime.UtcNow.AddHours(3);

        await userManager.UpdateAsync(user);

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
                .Where(t => !t.IsResolved && batch.Contains(t.RiderIqamaNo ?? 2536361732))
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
                .Where(t => batch.Contains(t.RiderIqamaNo ?? 2536361732))
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

        var emp = housing.Employees.Where(e => e.IsEmployee).ToList().Count;

        var inca = total - (activeRiders + emp);

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

        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
            .Where(e => !e.IsDeleted)
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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
            var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

            if (!employeeIqamas.Any())
                return Result.Success(new List<HousingVehicleResponse>());

            // Step 2: Get riders for these employees
            var riders = await context.RiderDetails
                .Include(r => r.Employee)
                .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
                .ToListAsync();

            // Step 3: Get vehicle numbers from riders (excluding null/empty)
            var vehicleNumbersFromRiders = riders
                .Where(r => !string.IsNullOrWhiteSpace(r.VehicleNumber))
                .Select(r => r.VehicleNumber!)
                .Distinct()
                .ToList();

            // Step 4: Get ALL vehicles that match EITHER:
            // - Assigned to riders in this housing, OR
            // - Location contains the housing name
            var vehicles = await context.Vehicles
                .Where(v => vehicleNumbersFromRiders.Contains(v.VehicleNumber) ||
                            v.Location.Contains(housing.Name))
                .ToListAsync();

            if (!vehicles.Any())
                return Result.Success(new List<HousingVehicleResponse>());

            // Step 5: Get all vehicle numbers for status lookup
            var allVehicleNumbers = vehicles.Select(v => v.VehicleNumber).Distinct().ToList();

            // Step 6: Get all active statuses for these vehicles
            var allStatuses = await context.RiderVehicleStatus
                .Where(rvs => allVehicleNumbers.Contains(rvs.VehicleNumber) && rvs.IsActive)
                .OrderByDescending(rvs => rvs.Timestamp)
                .ToListAsync();

            var statusIqamas = allStatuses
            .Where(s => s.EmployeeIqamaNo.HasValue)
            .Select(s => s.EmployeeIqamaNo!.Value)
            .Distinct()
            .ToList();

            var statusEmployees = await context.Employees
                .Where(e => statusIqamas.Contains(e.IqamaNo))
                .ToDictionaryAsync(e => e.IqamaNo);


            // Step 7: Get the latest status for each vehicle (in memory)
            var statusDict = allStatuses
                .GroupBy(s => s.VehicleNumber)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(s => s.Timestamp).First()
                );

            // Step 8: Handle duplicate vehicle numbers in riders
            var riderDict = riders
                .Where(r => !string.IsNullOrWhiteSpace(r.VehicleNumber))
                .GroupBy(r => r.VehicleNumber!)
                .ToDictionary(g => g.Key, g => g.First());

            // Step 9: Build response
            var response = vehicles.Select(v =>
            {
                statusDict.TryGetValue(v.VehicleNumber, out var status);
                riderDict.TryGetValue(v.VehicleNumber, out var rider);

                var statusType = status?.StatusType.ToString() ?? "Returned";
                var statusTimestamp = status?.Timestamp;

                // Get rider info: prioritize status record for "Taken" vehicles
                long? assignedIqama = null;
                string? assignedNameAR = null;
                string? assignedNameEN = null;

                if (status?.StatusType == VehicleStatusType.Taken && status.EmployeeIqamaNo.HasValue)
                {
                    // Get employee from status record
                    assignedIqama = status.EmployeeIqamaNo;
                    if (statusEmployees.TryGetValue(status.EmployeeIqamaNo.Value, out var statusEmployee))
                    {
                        assignedNameAR = statusEmployee.NameAR;
                        assignedNameEN = statusEmployee.NameEN;
                    }
                }
                else if (rider != null)
                {
                    // Use rider from RiderDetails
                    assignedIqama = rider.EmployeeIqamaNo;
                    assignedNameAR = rider.Employee?.NameAR;
                    assignedNameEN = rider.Employee?.NameEN;
                }

                return new HousingVehicleResponse(
                    v.VehicleNumber,
                    v.VehicleType,
                    v.PlateNumberA,
                    v.PlateNumberE,
                    v.ManufactureYear,
                    v.Manufacturer,
                    v.LicenseExpiryDate,
                    v.Location,
                    statusType,
                    assignedIqama,
                    assignedNameAR,
                    assignedNameEN,
                    statusTimestamp
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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

        var pendingOps = await context.TempVehicleOperations
            .Include(t => t.Rider)
                .ThenInclude(r => r.Employee)
            .Include(t => t.Vehicle)
            .Where(t => !t.IsResolved && employeeIqamas.Contains(t.RiderIqamaNo ?? 2536361732))
            .OrderByDescending(t => t.RequestedAt)
            .ToListAsync();

        var response = pendingOps.Select(op => new PendingVehicleOperationResponse(
            op.Id,
            op.RiderIqamaNo ?? 2536361732,
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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

        var pendingChanges = await context.TempEmployeeStatusChanges
            .Include(t => t.Employee)
            .Where(t => !t.IsResolved && employeeIqamas.Contains(t.EmployeeIqamaNo))
            .OrderByDescending(t => t.RequestedAt)
            .ToListAsync();

        var response = pendingChanges.Select(change => new PendingStatusChangeResponse(
            change.Id,
            change.EmployeeIqamaNo,
            change.Employee.NameAR,
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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

        // Verify rider belongs to this housing
        var rider = await context.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == request.RiderIqamaNo
                && employeeIqamas.Contains(r.EmployeeIqamaNo));

        if (rider is null)
        {
            return Result.Failure(HousingMemberErrors.RiderNotInHousing);
        }

        if (!string.IsNullOrEmpty(rider.VehicleNumber))
            return Result.Failure(new Error(
              "rider has vehicle already",
              "rider has vehicle already",
              404
          ));


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

        // Check if there's already a pending take request for this rider + vehicle
        var existingTakeRequest = await context.TempVehicleOperations
            .AnyAsync(t => t.RiderIqamaNo == request.RiderIqamaNo
                && t.VehicleNumber == vehicle.VehicleNumber
                && t.VehicleStatusType == VehicleStatusType.Taken
                && !t.IsResolved);

        if (existingTakeRequest)
            return Result.Failure(new Error(
                "DuplicateRequest",
                "A pending take request already exists for this rider and vehicle",
                400
            ));

        //// Check if vehicle is currently assigned to any rider in this housing
        //var vehicleInHousing = await context.RiderDetails
        //    .AnyAsync(r => employeeIqamas.Contains(r.EmployeeIqamaNo)
        //        && r.VehicleNumber == vehicle.VehicleNumber);

        //// If vehicle is assigned elsewhere, check if it's available
        //if (!vehicleInHousing)
        //{
        //    var isVehicleAvailable = !await context.RiderVehicleStatus
        //        .AnyAsync(s => s.VehicleNumber == vehicle.VehicleNumber
        //            && s.IsActive
        //            && (s.StatusType == VehicleStatusType.Taken
        //                || s.StatusType == VehicleStatusType.Problem
        //                || s.StatusType == VehicleStatusType.Stolen
        //                || s.StatusType == VehicleStatusType.BreakUp));

        //    if (!isVehicleAvailable)
        //    {
        //        return Result.Failure(new Error(
        //            "VehicleUnavailable",
        //            "This vehicle is not available and not assigned to your housing",
        //            403
        //        ));
        //    }
        //}

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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

        // Check if there's already a pending return request for this rider + vehicle
        var existingReturnRequest = await context.TempVehicleOperations
            .AnyAsync(t => t.RiderIqamaNo == request.RiderIqamaNo
                && t.VehicleNumber == vehicle.VehicleNumber
                && t.VehicleStatusType == VehicleStatusType.Returned
                && !t.IsResolved);

        if (existingReturnRequest)
            return Result.Failure(new Error(
                "DuplicateRequest",
                "A pending return request already exists for this rider and vehicle",
                400
            ));

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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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

        //// Verify vehicle belongs to housing
        //var vehicleInHousing = await context.RiderDetails
        //    .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
        //    .AnyAsync(r => r.VehicleNumber == vehicle.VehicleNumber);

        //if (!vehicleInHousing && !string.IsNullOrEmpty(vehicle.Location))
        //{
        //    var housingLocation = housing.Address;

        //    if (!vehicle.Location.Contains(housing.Name, StringComparison.OrdinalIgnoreCase))
        //    {
        //        return Result.Failure(new Error(
        //            "VehicleNotInHousing",
        //            "This vehicle does not belong to your housing",
        //            403
        //        ));
        //    }
        //}

        // Check if vehicle already has an active problem
        var existingProblem = await context.RiderVehicleStatus
            .AnyAsync(s => s.VehicleNumber == vehicle.VehicleNumber
                && s.IsActive
                && s.StatusType == VehicleStatusType.Problem);

        var existingreport = await context.TempVehicleOperations
            .AnyAsync(s => s.VehicleNumber == vehicle.VehicleNumber
                && !s.IsResolved);

        if (existingProblem)
        {
            return Result.Failure(new Error(
                "AlreadyReported",
                "This vehicle already has an active problem reported",
                400
            ));
        }
        if (existingreport)
        {
            return Result.Failure(new Error(
                "already reported",
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
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

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


    // Add to MemberService class

    public async Task<Result> RequestSwitchVehicleForHousingAsync(
        long managerIqamaNo,
        MemberSwitchVehicleRequest request)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return housingResult;

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

        // Verify rider belongs to this housing
        var rider = await context.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == request.RiderIqamaNo
                && employeeIqamas.Contains(r.EmployeeIqamaNo));

        if (rider is null)
            return Result.Failure(HousingMemberErrors.RiderNotInHousing);

        // Verify rider has a current vehicle
        if (string.IsNullOrEmpty(rider.VehicleNumber))
            return Result.Failure(HousingMemberErrors.NoCurrentVehicle);

        var currentVehicleNumber = rider.VehicleNumber;

        // Get current vehicle details
        var currentVehicle = await context.Vehicles
            .FirstOrDefaultAsync(v => v.VehicleNumber == currentVehicleNumber);

        if (currentVehicle is null)
            return Result.Failure(new Error(
                "CurrentVehicleNotFound",
                "Current vehicle not found in system",
                404
            ));

        // Verify new vehicle exists
        var newVehicle = await context.Vehicles
            .Include(c => c.RiderDetails)
            .FirstOrDefaultAsync(v => v.PlateNumberA == request.NewVehiclePlate);

        if (newVehicle is null)
            return Result.Failure(new Error(
                "VehicleNotFound",
                "New vehicle not found",
                404
            ));

        // Check if trying to switch to the same vehicle
        if (currentVehicleNumber == newVehicle.VehicleNumber)
            return Result.Failure(HousingMemberErrors.SameVehicleSwitch);

        if (newVehicle.RiderDetails is not null)
            return Result.Failure(new Error("vehicle has rider", "vehicle has a rider already", 400));


        // Verify new vehicle belongs to housing or is available
        var newVehicleInHousing = await context.RiderDetails
            .AnyAsync(r => employeeIqamas.Contains(r.EmployeeIqamaNo)
                && r.VehicleNumber == newVehicle.VehicleNumber);

        //if (!newVehicleInHousing && !string.IsNullOrEmpty(newVehicle.Location))
        //{
        //    if (!newVehicle.Location.Contains(housing.Name, StringComparison.OrdinalIgnoreCase))
        //    {
        //        return Result.Failure(new Error(
        //            "VehicleNotInHousing",
        //            "The new vehicle does not belong to your housing",
        //            403
        //        ));
        //    }
        //}

        // Check if new vehicle is available
        var isNewVehicleAvailable = !await context.RiderVehicleStatus
            .AnyAsync(s => s.VehicleNumber == newVehicle.VehicleNumber
                && s.IsActive
                && (s.StatusType == VehicleStatusType.Taken
                    || s.StatusType == VehicleStatusType.Problem
                    || s.StatusType == VehicleStatusType.Stolen
                    || s.StatusType == VehicleStatusType.BreakUp));

        if (!isNewVehicleAvailable)
            return Result.Failure(HousingMemberErrors.NewVehicleNotAvailable);

        // Replace the existing existingSwitchRequest check with this:
        var existingSwitchRequest = await context.TempVehicleOperations
            .AnyAsync(t => t.RiderIqamaNo == request.RiderIqamaNo
                && t.VehicleNumber == currentVehicleNumber
                && t.VehiclePlateNumber == request.NewVehiclePlate
                && t.VehicleStatusType == VehicleStatusType.switched
                && !t.IsResolved);

        if (existingSwitchRequest)
            return Result.Failure(HousingMemberErrors.PendingSwitchRequest);

        // Get the manager name
        var manager = await userManager.FindByNameAsync(managerIqamaNo.ToString());
        if (manager is null)
            return Result.Failure(UserErrors.UserNotFound);

        var t = long.Parse(manager.UserName!);
        var name = await context.Employees
            .Where(e => e.IqamaNo == t)
            .Select(e => e.NameAR)
            .FirstOrDefaultAsync();

        // Create the switch vehicle operation request
        // IMPORTANT: We use a special pattern to indicate this is a switch:
        // - VehicleNumber = Current vehicle (that will be returned)
        // - VehiclePlateNumber = New vehicle plate (that will be taken)
        // - VehicleStatusType = Taken (to indicate taking the new vehicle)
        var operation = new TempVehicleOperation
        {
            RiderIqamaNo = request.RiderIqamaNo,
            VehicleNumber = currentVehicleNumber,  // Current vehicle to be returned
            VehiclePlateNumber = request.NewVehiclePlate,  // New vehicle to be taken
            VehicleStatusType = VehicleStatusType.switched,  // Indicates this is for taking a vehicle
            Reason = $"Switch request: {request.Reason}",
            RequestedAt = DateTime.UtcNow.AddHours(3),
            RequestedBy = name!,
            IsResolved = false
        };

        await context.TempVehicleOperations.AddAsync(operation);
        await context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<List<PendingSwitchVehicleResponse>>> GetPendingSwitchVehicleRequests(
        long managerIqamaNo)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<List<PendingSwitchVehicleResponse>>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Where(e => !e.IsDeleted).Select(e => e.IqamaNo).ToList();

        // Get all pending operations for this housing where:
        // - VehicleNumber is set (current vehicle)
        // - VehiclePlateNumber is set (new vehicle)
        // - This indicates a switch operation
        var pendingSwitches = await context.TempVehicleOperations
            .Include(t => t.Rider)
                .ThenInclude(r => r.Employee)
            .Where(t => !t.IsResolved
                && employeeIqamas.Contains(t.RiderIqamaNo ?? 2536361732)
                && !string.IsNullOrEmpty(t.VehicleNumber)
                && !string.IsNullOrEmpty(t.VehiclePlateNumber)
                && t.VehicleStatusType == VehicleStatusType.Taken)
            .OrderByDescending(t => t.RequestedAt)
            .ToListAsync();

        var responses = new List<PendingSwitchVehicleResponse>();

        foreach (var operation in pendingSwitches)
        {
            // Get current vehicle details
            var currentVehicle = await context.Vehicles
                .FirstOrDefaultAsync(v => v.VehicleNumber == operation.VehicleNumber);

            // Get new vehicle details
            var newVehicle = await context.Vehicles
                .FirstOrDefaultAsync(v => v.PlateNumberA == operation.VehiclePlateNumber);

            if (currentVehicle == null || newVehicle == null)
                continue;

            // Validate the switch operation
            var validation = await ValidateSwitchOperation(
                operation.RiderIqamaNo ?? 2536361732,
                operation.VehicleNumber,
                newVehicle.VehicleNumber);

            responses.Add(new PendingSwitchVehicleResponse(
                operation.Id,
                operation.RiderIqamaNo ?? 2536361732,
                operation.Rider?.Employee?.NameAR ?? "Unknown",
                operation.VehicleNumber,
                currentVehicle.PlateNumberA,
                newVehicle.VehicleNumber,
                operation.VehiclePlateNumber,
                operation.Reason ?? "No reason provided",
                operation.RequestedAt,
                operation.RequestedBy,
                validation
            ));
        }

        return Result.Success(responses);
    }

    // Helper method to validate switch operations
    private async Task<VehicleSwitchValidation> ValidateSwitchOperation(
        long riderIqamaNo,
        string currentVehicleNumber,
        string newVehicleNumber)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Check if rider still has the current vehicle
        var rider = await context.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == riderIqamaNo);

        if (rider == null)
        {
            errors.Add("Rider not found");
            return new VehicleSwitchValidation(false, errors, warnings);
        }

        if (rider.Employee.Status.ToLower() != "enable")
        {
            errors.Add("Rider is not enabled");
        }

        if (rider.VehicleNumber != currentVehicleNumber)
        {
            errors.Add($"Rider no longer has the current vehicle. Current assignment: {rider.VehicleNumber ?? "None"}");
        }

        // Check if current vehicle has active taken status
        var currentVehicleActive = await context.RiderVehicleStatus
            .AnyAsync(s => s.VehicleNumber == currentVehicleNumber
                && s.EmployeeIqamaNo == riderIqamaNo
                && s.IsActive
                && s.StatusType == VehicleStatusType.Taken);

        if (!currentVehicleActive)
        {
            warnings.Add("No active 'Taken' status found for current vehicle");
        }

        // Check if new vehicle is still available
        var newVehicleUnavailable = await context.RiderVehicleStatus
            .AnyAsync(s => s.VehicleNumber == newVehicleNumber
                && s.IsActive
                && (s.StatusType == VehicleStatusType.Taken
                    || s.StatusType == VehicleStatusType.Problem
                    || s.StatusType == VehicleStatusType.Stolen
                    || s.StatusType == VehicleStatusType.BreakUp));

        if (newVehicleUnavailable)
        {
            errors.Add("New vehicle is no longer available");
        }

        // Check if vehicles are the same
        if (currentVehicleNumber == newVehicleNumber)
        {
            errors.Add("Current and new vehicle are the same");
        }

        return new VehicleSwitchValidation(
            IsValid: errors.Count == 0,
            Errors: errors,
            Warnings: warnings
        );
    }

    #endregion

    #region Helper Methods

    private async Task<bool> IsVehicleInHousing(Housing housing, string vehicleNumber)
    {
        var employeeIqamas = housing.Employees
            .Where(e => !e.IsDeleted)
            .Select(e => e.IqamaNo)
            .ToList();

        // Check if vehicle is assigned to any rider in this housing
        var isAssigned = await context.RiderDetails
            .AnyAsync(r => employeeIqamas.Contains(r.EmployeeIqamaNo)
                && r.VehicleNumber == vehicleNumber);

        if (isAssigned)
            return true;

        // Check if vehicle location matches housing
        var vehicle = await context.Vehicles
            .FirstOrDefaultAsync(v => v.VehicleNumber == vehicleNumber);

        if (vehicle != null && !string.IsNullOrEmpty(vehicle.Location))
        {
            return vehicle.Location.Contains(housing.Name, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private async Task<bool> IsRiderInHousing(Housing housing, int riderId)
    {
        var employeeIqamas = housing.Employees
            .Where(e => !e.IsDeleted)
            .Select(e => e.IqamaNo)
            .ToList();

        return await context.RiderDetails
            .AnyAsync(r => r.Id == riderId && employeeIqamas.Contains(r.EmployeeIqamaNo));
    }

    #endregion

    #region Spare Parts Management

    public async Task<Result<IEnumerable<SparePartResponse>>> GetHousingSparePartsAsync(long managerIqamaNo)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<IEnumerable<SparePartResponse>>(housingResult.Error);

        var housing = housingResult.Value;

        var spareParts = await context.SpareParts
            .Where(sp => sp.Location == housing.Name)
            .AsNoTracking()
            .OrderBy(sp => sp.Name)
            .ToListAsync();

        var response = spareParts.Select(sp => new SparePartResponse(
            sp.Id,
            sp.Name,
            sp.Quantity,
            sp.Price,
            sp.Location,
            sp.CreatedAt
        ));

        return Result.Success(response);
    }

    public async Task<Result<SparePartResponse>> GetSparePartByIdAsync(long managerIqamaNo, int id)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<SparePartResponse>(housingResult.Error);

        var housing = housingResult.Value;

        var sparePart = await context.SpareParts
            .AsNoTracking()
            .FirstOrDefaultAsync(sp => sp.Id == id && sp.Location == housing.Name);

        if (sparePart == null)
            return Result.Failure<SparePartResponse>(
                new Error("NotFound", "Spare part not found in your housing inventory", 404));

        return Result.Success(new SparePartResponse(
            sparePart.Id,
            sparePart.Name,
            sparePart.Quantity,
            sparePart.Price,
            sparePart.Location,
            sparePart.CreatedAt
        ));
    }

    public async Task<Result<IEnumerable<SparePartResponse>>> SearchSparePartsAsync(
        long managerIqamaNo,
        string keyword)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<IEnumerable<SparePartResponse>>(housingResult.Error);

        var housing = housingResult.Value;
        keyword = keyword.ToLower();

        var spareParts = await context.SpareParts
            .Where(sp => sp.Location == housing.Name
                && sp.Name.ToLower().Contains(keyword))
            .AsNoTracking()
            .ToListAsync();

        var response = spareParts.Select(sp => new SparePartResponse(
            sp.Id,
            sp.Name,
            sp.Quantity,
            sp.Price,
            sp.Location,
            sp.CreatedAt
        ));

        return Result.Success(response);
    }

    #endregion

    #region Spare Parts Usage

    public async Task<Result<BatchUsageResponse>> RecordBatchSparePartUsageAsync(
        DateTime Date,
        long managerIqamaNo,
        MemberBatchSparePartUsageRequest request)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<BatchUsageResponse>(housingResult.Error);

        var housing = housingResult.Value;
        var details = new List<UsageResultDetail>();
        int successCount = 0;
        int failureCount = 0;

        using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            foreach (var usage in request.Usages)
            {
                try
                {
                    // Validate spare part exists and belongs to housing
                    var sparePart = await context.SpareParts
                        .FirstOrDefaultAsync(sp => sp.Id == usage.SparePartId
                            && sp.Location == housing.Name);

                    if (sparePart == null)
                    {
                        details.Add(new UsageResultDetail(
                            false,
                            $"ID: {usage.SparePartId}",
                            usage.VehicleNumber,
                            "Spare part not found in your housing inventory"
                        ));
                        failureCount++;
                        continue;
                    }

                    if (sparePart.Quantity < usage.QuantityUsed)
                    {
                        details.Add(new UsageResultDetail(
                            false,
                            sparePart.Name,
                            usage.VehicleNumber,
                            $"Insufficient quantity. Available: {sparePart.Quantity}, Requested: {usage.QuantityUsed}"
                        ));
                        failureCount++;
                        continue;
                    }

                    // Validate vehicle exists
                    var vehicle = await context.Vehicles
                        .FirstOrDefaultAsync(v => v.VehicleNumber == usage.VehicleNumber);

                    if (vehicle == null)
                    {
                        details.Add(new UsageResultDetail(
                            false,
                            sparePart.Name,
                            usage.VehicleNumber,
                            "Vehicle not found"
                        ));
                        failureCount++;
                        continue;
                    }

                    //// Validate vehicle belongs to housing
                    //if (!await IsVehicleInHousing(housing, usage.VehicleNumber))
                    //{
                    //    details.Add(new UsageResultDetail(
                    //        false,
                    //        sparePart.Name,
                    //        usage.VehicleNumber,
                    //        "Vehicle does not belong to your housing"
                    //    ));
                    //    failureCount++;
                    //    continue;
                    //}

                    // Record usage
                    var sparePartUsage = new SparePartUsage
                    {
                        SparePartId = usage.SparePartId,
                        VehicleNumber = usage.VehicleNumber,
                        QuantityUsed = usage.QuantityUsed,
                        UsedAt = Date,
                        Cost = sparePart.Price * usage.QuantityUsed,
                        Location = housing.Name   // ← save the manager's housing
                    };

                    await context.SparePartUsages.AddAsync(sparePartUsage);

                    // Update quantity
                    sparePart.Quantity -= usage.QuantityUsed;

                    details.Add(new UsageResultDetail(
                        true,
                        sparePart.Name,
                        usage.VehicleNumber,
                        $"Successfully recorded {usage.QuantityUsed} units"
                    ));
                    successCount++;
                }
                catch (Exception ex)
                {
                    details.Add(new UsageResultDetail(
                        false,
                        $"ID: {usage.SparePartId}",
                        usage.VehicleNumber,
                        $"Error: {ex.Message}"
                    ));
                    failureCount++;
                }
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            var response = new BatchUsageResponse(
                request.Usages.Count,
                successCount,
                failureCount,
                details
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<BatchUsageResponse>(
                new Error("BatchError", $"Batch operation failed: {ex.Message}", 500));
        }
    }
    public async Task<Result<IEnumerable<SparePartUsageResponse>>> GetSparePartUsageHistoryAsync(
        long managerIqamaNo,
        int sparePartId)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<IEnumerable<SparePartUsageResponse>>(housingResult.Error);

        var housing = housingResult.Value;

        var sparePart = await context.SpareParts
            .FirstOrDefaultAsync(sp => sp.Id == sparePartId);

        if (sparePart == null)
            return Result.Failure<IEnumerable<SparePartUsageResponse>>(
                new Error("NotFound", "Spare part not found in your housing inventory", 404));

        // ✅ Filter directly by location — no more employee/vehicle resolution
        var usages = await context.SparePartUsages
            .Include(u => u.SparePart)
            .Where(u => u.SparePartId == sparePartId
                && u.Location == housing.Name)
            .OrderByDescending(u => u.UsedAt)
            .AsNoTracking()
            .ToListAsync();

        var response = usages.Select(u => new SparePartUsageResponse(
            u.Id,
            u.SparePartId,
            u.SparePart.Name,
            u.VehicleNumber,
            u.QuantityUsed,
            u.UsedAt,
            u.Cost
        ));

        return Result.Success(response);
    }
    public async Task<Result<IEnumerable<SparePartUsageResponse>>> GetVehicleSparePartHistoryAsync(
        long managerIqamaNo,
        string vehicleNumber)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<IEnumerable<SparePartUsageResponse>>(housingResult.Error);

        var housing = housingResult.Value;

        // Validate vehicle belongs to housing
        if (!await IsVehicleInHousing(housing, vehicleNumber))
            return Result.Failure<IEnumerable<SparePartUsageResponse>>(
                new Error("Unauthorized", "Vehicle does not belong to your housing", 403));

        var usages = await context.SparePartUsages
            .Include(u => u.SparePart)
            .Where(u => u.VehicleNumber == vehicleNumber)
            .OrderByDescending(u => u.UsedAt)
            .AsNoTracking()
            .ToListAsync();

        var response = usages.Select(u => new SparePartUsageResponse(
            u.Id,
            u.SparePartId,
            u.SparePart.Name,
            u.VehicleNumber,
            u.QuantityUsed,
            u.UsedAt,
            u.Cost
        ));

        return Result.Success(response);
    }

    #endregion

    #region Rider Accessories Management

    public async Task<Result<IEnumerable<RiderAccessoryResponse>>> GetHousingAccessoriesAsync(
        long managerIqamaNo)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<IEnumerable<RiderAccessoryResponse>>(housingResult.Error);

        var housing = housingResult.Value;

        var accessories = await context.RiderAccessories
            .Include(a => a.RiderAccessoryUsages)
            .Where(a => a.Location == housing.Name)
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .ToListAsync();

        var response = accessories.Select(a => new RiderAccessoryResponse(
            a.Id,
            a.Name,
            a.Quantity,
            a.Quantity, // Available = Total for housing inventory
            a.Price,
            a.Location,
            a.CreatedAt
        ));

        return Result.Success(response);
    }

    public async Task<Result<RiderAccessoryResponse>> GetAccessoryByIdAsync(
        long managerIqamaNo,
        int id)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<RiderAccessoryResponse>(housingResult.Error);

        var housing = housingResult.Value;

        var accessory = await context.RiderAccessories
            .Include(a => a.RiderAccessoryUsages)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.Location == housing.Name);

        if (accessory == null)
            return Result.Failure<RiderAccessoryResponse>(
                new Error("NotFound", "Accessory not found in your housing inventory", 404));

        return Result.Success(new RiderAccessoryResponse(
            accessory.Id,
            accessory.Name,
            accessory.Quantity,
            accessory.Quantity,
            accessory.Price,
            accessory.Location,
            accessory.CreatedAt
        ));
    }

    public async Task<Result<IEnumerable<RiderAccessoryResponse>>> SearchAccessoriesAsync(
        long managerIqamaNo,
        string keyword)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<IEnumerable<RiderAccessoryResponse>>(housingResult.Error);

        var housing = housingResult.Value;
        keyword = keyword.ToLower();

        var accessories = await context.RiderAccessories
            .Include(a => a.RiderAccessoryUsages)
            .Where(a => a.Location == housing.Name
                && a.Name.ToLower().Contains(keyword))
            .AsNoTracking()
            .ToListAsync();

        var response = accessories.Select(a => new RiderAccessoryResponse(
            a.Id,
            a.Name,
            a.Quantity,
            a.Quantity,
            a.Price,
            a.Location,
            a.CreatedAt
        ));

        return Result.Success(response);
    }

    #endregion

    #region Rider Accessories Usage

    public async Task<Result<BatchUsageResponse>> RecordBatchAccessoryUsageAsync(
        DateTime Date,
        long managerIqamaNo,
        MemberBatchAccessoryUsageRequest request)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<BatchUsageResponse>(housingResult.Error);

        var housing = housingResult.Value;
        var details = new List<UsageResultDetail>();
        int successCount = 0;
        int failureCount = 0;

        using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            foreach (var usage in request.Usages)
            {
                try
                {
                    // Validate accessory exists and belongs to housing
                    var accessory = await context.RiderAccessories
                        .FirstOrDefaultAsync(a => a.Id == usage.AccessoryId
                            && a.Location == housing.Name);

                    if (accessory == null)
                    {
                        details.Add(new UsageResultDetail(
                            false,
                            $"ID: {usage.AccessoryId}",
                            $"Rider ID: {usage.RiderId}",
                            "Accessory not found in your housing inventory"
                        ));
                        failureCount++;
                        continue;
                    }

                    if (accessory.Quantity <= 0)
                    {
                        details.Add(new UsageResultDetail(
                            false,
                            accessory.Name,
                            $"Rider ID: {usage.RiderId}",
                            "Accessory is out of stock"
                        ));
                        failureCount++;
                        continue;
                    }

                    //// Validate rider belongs to housing
                    //if (!await IsRiderInHousing(housing, usage.RiderId))
                    //{
                    //    details.Add(new UsageResultDetail(
                    //        false,
                    //        accessory.Name,
                    //        $"Rider ID: {usage.RiderId}",
                    //        "Rider does not belong to your housing"
                    //    ));
                    //    failureCount++;
                    //    continue;
                    //}

                    var rider = await context.RiderDetails
                        .Include(r => r.Employee)
                        .FirstOrDefaultAsync(r => r.Id == usage.RiderId);

                    // Create usage record
                    var accessoryUsage = new Domain.Entities.Spare.RiderAccessoryUsage
                    {
                        RiderAccessoryId = usage.AccessoryId,
                        RiderId = usage.RiderId,
                        IssuedAt = Date,
                        Cost = accessory.Price,
                        Location = housing.Name   // ← save the manager's housing
                    };

                    await context.RiderAccessoryUsages.AddAsync(accessoryUsage);

                    // Update quantity
                    accessory.Quantity--;

                    details.Add(new UsageResultDetail(
                        true,
                        accessory.Name,
                        rider!.Employee.NameEN,
                        "Successfully issued accessory"
                    ));
                    successCount++;
                }
                catch (Exception ex)
                {
                    details.Add(new UsageResultDetail(
                        false,
                        $"ID: {usage.AccessoryId}",
                        $"Rider ID: {usage.RiderId}",
                        $"Error: {ex.Message}"
                    ));
                    failureCount++;
                }
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            var response = new BatchUsageResponse(
                request.Usages.Count,
                successCount,
                failureCount,
                details
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<BatchUsageResponse>(
                new Error("BatchError", $"Batch operation failed: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<RiderAccessoryUsageResponse>>> GetAccessoryUsageHistoryAsync(
    long managerIqamaNo,
    int accessoryId)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<IEnumerable<RiderAccessoryUsageResponse>>(housingResult.Error);

        var housing = housingResult.Value;

        var accessory = await context.RiderAccessories
            .FirstOrDefaultAsync(a => a.Id == accessoryId && a.Location == housing.Name);

        if (accessory == null)
            return Result.Failure<IEnumerable<RiderAccessoryUsageResponse>>(
                new Error("NotFound", "Accessory not found in your housing inventory", 404));

        // ✅ Filter directly by location — no more employee/rider ID resolution
        var usages = await context.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Include(u => u.Rider)
                .ThenInclude(r => r.Employee)
            .Where(u => u.RiderAccessoryId == accessoryId
                && u.Location == housing.Name)
            .OrderByDescending(u => u.IssuedAt)
            .AsNoTracking()
            .ToListAsync();

        var response = usages.Select(u => new RiderAccessoryUsageResponse(
            u.Id,
            u.RiderAccessoryId,
            u.RiderAccessory.Name,
            u.RiderId,
            u.Rider.Employee.NameEN,
            u.Rider.Employee.NameAR,
            u.IssuedAt,
            u.Cost
        ));

        return Result.Success(response);
    }
    public async Task<Result<IEnumerable<RiderAccessoryUsageResponse>>> GetRiderAccessoryHistoryAsync(
        long managerIqamaNo,
        int riderId)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<IEnumerable<RiderAccessoryUsageResponse>>(housingResult.Error);

        var housing = housingResult.Value;

        // Validate rider belongs to housing
        if (!await IsRiderInHousing(housing, riderId))
            return Result.Failure<IEnumerable<RiderAccessoryUsageResponse>>(
                new Error("Unauthorized", "Rider does not belong to your housing", 403));

        var usages = await context.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Include(u => u.Rider)
                .ThenInclude(r => r.Employee)
            .Where(u => u.RiderId == riderId)
            .OrderByDescending(u => u.IssuedAt)
            .AsNoTracking()
            .ToListAsync();

        var response = usages.Select(u => new RiderAccessoryUsageResponse(
            u.Id,
            u.RiderAccessoryId,
            u.RiderAccessory.Name,
            u.RiderId,
            u.Rider.Employee.NameEN,
            u.Rider.Employee.NameAR,
            u.IssuedAt,
            u.Cost
        ));

        return Result.Success(response);
    }

    #endregion

    #region Cost Tracking

    public async Task<Result<MemberVehicleCostResponse>> GetVehicleCostAsync(
        long managerIqamaNo,
        string vehicleNumber)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<MemberVehicleCostResponse>(housingResult.Error);

        var housing = housingResult.Value;

        // Validate vehicle belongs to housing
        if (!await IsVehicleInHousing(housing, vehicleNumber))
            return Result.Failure<MemberVehicleCostResponse>(
                new Error("Unauthorized", "Vehicle does not belong to your housing", 403));

        var vehicle = await context.Vehicles
            .FirstOrDefaultAsync(v => v.VehicleNumber == vehicleNumber);

        if (vehicle == null)
            return Result.Failure<MemberVehicleCostResponse>(
                new Error("NotFound", "Vehicle not found", 404));

        var sparePartUsages = await context.SparePartUsages
            .Include(u => u.SparePart)
            .Where(u => u.VehicleNumber == vehicleNumber)
            .OrderByDescending(u => u.UsedAt)
            .AsNoTracking()
            .ToListAsync();

        var sparePartDetails = sparePartUsages.Select(u => new CostItemDetail(
            u.SparePart.Name,
            u.QuantityUsed,
            u.SparePart.Price,
            u.QuantityUsed * u.SparePart.Price,
            u.UsedAt
        )).ToList();

        var totalSparePartsCost = sparePartDetails.Sum(d => d.TotalCost);

        var response = new MemberVehicleCostResponse(
            vehicleNumber,
            vehicle.PlateNumberA,
            totalSparePartsCost,
            totalSparePartsCost,
            sparePartDetails
        );

        return Result.Success(response);
    }

    public async Task<Result<MemberVehicleCostResponse>> GetVehicleCostByDateRangeAsync(
        long managerIqamaNo,
        string vehicleNumber,
        DateTime fromDate,
        DateTime toDate)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<MemberVehicleCostResponse>(housingResult.Error);

        var housing = housingResult.Value;

        // Validate vehicle belongs to housing
        if (!await IsVehicleInHousing(housing, vehicleNumber))
            return Result.Failure<MemberVehicleCostResponse>(
                new Error("Unauthorized", "Vehicle does not belong to your housing", 403));

        var vehicle = await context.Vehicles
            .FirstOrDefaultAsync(v => v.VehicleNumber == vehicleNumber);

        if (vehicle == null)
            return Result.Failure<MemberVehicleCostResponse>(
                new Error("NotFound", "Vehicle not found", 404));

        var sparePartUsages = await context.SparePartUsages
            .Include(u => u.SparePart)
            .Where(u => u.VehicleNumber == vehicleNumber
                && u.UsedAt >= fromDate
                && u.UsedAt <= toDate)
            .OrderByDescending(u => u.UsedAt)
            .AsNoTracking()
            .ToListAsync();

        var sparePartDetails = sparePartUsages.Select(u => new CostItemDetail(
            u.SparePart.Name,
            u.QuantityUsed,
            u.SparePart.Price,
            u.QuantityUsed * u.SparePart.Price,
            u.UsedAt
        )).ToList();

        var totalSparePartsCost = sparePartDetails.Sum(d => d.TotalCost);

        var response = new MemberVehicleCostResponse(
            vehicleNumber,
            vehicle.PlateNumberA,
            totalSparePartsCost,
            totalSparePartsCost,
            sparePartDetails
        );

        return Result.Success(response);
    }

    public async Task<Result<MemberRiderCostResponse>> GetRiderCostAsync(
        long managerIqamaNo,
        int riderId)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<MemberRiderCostResponse>(housingResult.Error);

        var housing = housingResult.Value;

        // Validate rider belongs to housing
        if (!await IsRiderInHousing(housing, riderId))
            return Result.Failure<MemberRiderCostResponse>(
                new Error("Unauthorized", "Rider does not belong to your housing", 403));

        var rider = await context.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.Id == riderId);

        if (rider == null)
            return Result.Failure<MemberRiderCostResponse>(
                new Error("NotFound", "Rider not found", 404));

        var accessoryUsages = await context.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Where(u => u.RiderId == riderId)
            .OrderByDescending(u => u.IssuedAt)
            .AsNoTracking()
            .ToListAsync();

        var accessoryDetails = accessoryUsages.Select(u => new CostItemDetail(
            u.RiderAccessory.Name,
            1,
            u.RiderAccessory.Price,
            u.RiderAccessory.Price,
            u.IssuedAt
        )).ToList();

        var totalAccessoriesCost = accessoryDetails.Sum(d => d.TotalCost);

        var response = new MemberRiderCostResponse(
            riderId,
            rider.EmployeeIqamaNo,
            rider.Employee.NameEN,
            rider.Employee.NameAR,
            rider.WorkingId ?? "N/A",
            totalAccessoriesCost,
            accessoryDetails
        );

        return Result.Success(response);
    }

    public async Task<Result<MemberRiderCostResponse>> GetRiderCostByDateRangeAsync(
        long managerIqamaNo,
        int riderId,
        DateTime fromDate,
        DateTime toDate)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<MemberRiderCostResponse>(housingResult.Error);

        var housing = housingResult.Value;

        // Validate rider belongs to housing
        if (!await IsRiderInHousing(housing, riderId))
            return Result.Failure<MemberRiderCostResponse>(
                new Error("Unauthorized", "Rider does not belong to your housing", 403));

        var rider = await context.RiderDetails
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.Id == riderId);

        if (rider == null)
            return Result.Failure<MemberRiderCostResponse>(
                new Error("NotFound", "Rider not found", 404));

        var accessoryUsages = await context.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Where(u => u.RiderId == riderId
                && u.IssuedAt >= fromDate
                && u.IssuedAt <= toDate)
            .OrderByDescending(u => u.IssuedAt)
            .AsNoTracking()
            .ToListAsync();

        var accessoryDetails = accessoryUsages.Select(u => new CostItemDetail(
            u.RiderAccessory.Name,
            1,
            u.RiderAccessory.Price,
            u.RiderAccessory.Price,
            u.IssuedAt
        )).ToList();

        var totalAccessoriesCost = accessoryDetails.Sum(d => d.TotalCost);

        var response = new MemberRiderCostResponse(
            riderId,
            rider.EmployeeIqamaNo,
            rider.Employee.NameEN,
            rider.Employee.NameAR,
            rider.WorkingId ?? "N/A",
            totalAccessoriesCost,
            accessoryDetails
        );

        return Result.Success(response);
    }

    public async Task<Result<MemberHousingCostSummaryResponse>> GetHousingCostSummaryAsync(
      long managerIqamaNo,
      DateTime fromDate,
      DateTime toDate)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<MemberHousingCostSummaryResponse>(housingResult.Error);

        var housing = housingResult.Value;

        // Get spare parts usage costs filtered directly by housing location
        var sparePartUsages = await context.SparePartUsages
            .Include(u => u.SparePart)
            .Where(u => u.Location == housing.Name
                && u.UsedAt >= fromDate
                && u.UsedAt <= toDate)
            .AsNoTracking()
            .ToListAsync();

        var totalSparePartsCost = sparePartUsages.Sum(u => u.QuantityUsed * u.SparePart.Price);

        // Get accessories usage costs filtered directly by housing location
        var accessoryUsages = await context.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Where(u => u.Location == housing.Name
                && u.IssuedAt >= fromDate
                && u.IssuedAt <= toDate)
            .AsNoTracking()
            .ToListAsync();

        var totalAccessoriesCost = accessoryUsages.Sum(u => u.RiderAccessory.Price);

        // Derive vehicle numbers and rider IDs from actual usages
        var housingVehicleNumbers = sparePartUsages.Select(u => u.VehicleNumber).Distinct().ToList();
        var housingRiderIds = accessoryUsages.Select(u => u.RiderId).Distinct().ToList();

        // Batch-load vehicle plates to avoid N+1
        var vehiclePlates = await context.Vehicles
            .Where(v => housingVehicleNumbers.Contains(v.VehicleNumber))
            .Select(v => new { v.VehicleNumber, v.PlateNumberA })
            .ToDictionaryAsync(v => v.VehicleNumber, v => v.PlateNumberA);

        // Batch-load rider details to avoid N+1
        var riderDetails = await context.RiderDetails
            .Include(r => r.Employee)
            .Where(r => housingRiderIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Employee.NameEN, r.WorkingId })
            .ToDictionaryAsync(r => r.Id);

        // Build vehicle cost summaries
        var vehicleCosts = sparePartUsages
            .GroupBy(u => u.VehicleNumber)
            .Select(g => new VehicleCostSummaryItem(
                g.Key,
                vehiclePlates.GetValueOrDefault(g.Key) ?? "N/A",
                g.Sum(u => u.QuantityUsed * u.SparePart.Price)
            ))
            .OrderByDescending(v => v.TotalCost)
            .ToList();

        // Build rider cost summaries
        var riderCosts = accessoryUsages
            .GroupBy(u => u.RiderId)
            .Select(g =>
            {
                var rider = riderDetails.GetValueOrDefault(g.Key);
                return new RiderCostSummaryItem(
                    g.Key,
                    rider?.NameEN ?? "Unknown",
                    rider?.WorkingId ?? "N/A",
                    g.Sum(u => u.RiderAccessory.Price)
                );
            })
            .OrderByDescending(r => r.TotalCost)
            .ToList();

        var response = new MemberHousingCostSummaryResponse(
            housing.Name,
            totalSparePartsCost,
            totalAccessoriesCost,
            totalSparePartsCost + totalAccessoriesCost,
            fromDate,
            toDate,
            housingVehicleNumbers.Count,
            housingRiderIds.Count,
            vehicleCosts,
            riderCosts
        );

        return Result.Success(response);
    }
    #endregion

    // Add these methods to the MemberService class

    #region Transfer Management

    private const string MAIN_LOCATION = "الشركة";

    public async Task<Result<TransferResponse>> TransferFromHousingAsync(
        long managerIqamaNo,
        MemberTransferRequest request)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<TransferResponse>(housingResult.Error);

        var housing = housingResult.Value;

        // Validate destination
        string toLocation;
        int? toHousingId;

        if (request.ToHousingId.HasValue)
        {
            var destinationHousing = await context.Housings
                .FirstOrDefaultAsync(h => h.Id == request.ToHousingId.Value);

            if (destinationHousing == null)
                return Result.Failure<TransferResponse>(
                    new Error("DestinationNotFound", "Destination housing not found", 404));

            toLocation = destinationHousing.Name;
            toHousingId = destinationHousing.Id;
        }
        else
        {
            // Transfer to main company
            toLocation = MAIN_LOCATION;
            toHousingId = null;
        }

        if (request.Items == null || !request.Items.Any())
            return Result.Failure<TransferResponse>(
                new Error("NoItems", "Transfer must contain at least one item", 400));

        using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            var transferItems = new List<TransferItem>();

            foreach (var item in request.Items)
            {
                var transferItem = await ProcessMemberTransferItem(
                    item,
                    housing.Name,
                    toLocation);

                if (transferItem == null)
                    return Result.Failure<TransferResponse>(
                        new Error("ItemNotFound",
                            $"Item with ID {item.ItemId} and type {item.ItemType} not found in your housing inventory or insufficient quantity", 404));

                transferItems.Add(transferItem);
            }

            // Get manager name
            var manager = await context.Employees
                .FirstOrDefaultAsync(e => e.IqamaNo == managerIqamaNo);

            if (manager == null)
                return Result.Failure<TransferResponse>(UserErrors.UserNotFound);

            // Create transfer record
            var transfer = new Domain.Entities.Spare.Transfer
            {
                FromLocation = housing.Name,
                ToLocation = toLocation,
                HousingId = toHousingId ?? 0,
                TransferredBy = manager.NameAR,
                TransferredAt = DateTime.UtcNow.AddHours(3),
                TransferItems = transferItems
            };

            await context.Transfers.AddAsync(transfer);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            // Reload for response
            transfer = await context.Transfers
                .Include(t => t.TransferItems)
                .FirstAsync(t => t.Id == transfer.Id);

            return Result.Success(MapTransferToResponse(transfer));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<TransferResponse>(
                new Error("TransferError", $"Failed to transfer items: {ex.Message}", 500));
        }
    }

    private async Task<TransferItem?> ProcessMemberTransferItem(
        MemberTransferItemRequest request,
        string fromLocation,
        string toLocation)
    {
        if (request.ItemType == TransferItemType.SparePart)
        {
            return await ProcessMemberSparePartTransfer(request, fromLocation, toLocation);
        }
        else if (request.ItemType == TransferItemType.Accessory)
        {
            return await ProcessMemberAccessoryTransfer(request, fromLocation, toLocation);
        }

        return null;
    }

    private async Task<TransferItem?> ProcessMemberSparePartTransfer(
        MemberTransferItemRequest request,
        string fromLocation,
        string toLocation)
    {
        // Get from housing location
        var fromSparePart = await context.SpareParts
            .FirstOrDefaultAsync(sp => sp.Id == request.ItemId &&
                                      sp.Location == fromLocation);

        if (fromSparePart == null || fromSparePart.Quantity < request.Quantity)
            return null;

        // Check if item exists in destination location
        var toSparePart = await context.SpareParts
            .FirstOrDefaultAsync(sp => sp.Name == fromSparePart.Name &&
                                      sp.Location == toLocation);

        if (toSparePart != null)
        {
            // Add to existing
            toSparePart.Quantity += request.Quantity;
        }
        else
        {
            // Create new in destination
            toSparePart = new Domain.Entities.Spare.SparePart
            {
                Name = fromSparePart.Name,
                Quantity = request.Quantity,
                Price = fromSparePart.Price,
                Location = toLocation,
                CreatedAt = DateTime.UtcNow.AddHours(3)
            };
            await context.SpareParts.AddAsync(toSparePart);
        }

        // Reduce from housing location
        fromSparePart.Quantity -= request.Quantity;

        return new TransferItem
        {
            ItemId = fromSparePart.Id,
            ItemName = fromSparePart.Name,
            ItemType = TransferItemType.SparePart,
            Quantity = request.Quantity
        };
    }

    private async Task<TransferItem?> ProcessMemberAccessoryTransfer(
        MemberTransferItemRequest request,
        string fromLocation,
        string toLocation)
    {
        // Get from housing location
        var fromAccessory = await context.RiderAccessories
            .FirstOrDefaultAsync(a => a.Id == request.ItemId &&
                                     a.Location == fromLocation);

        if (fromAccessory == null || fromAccessory.Quantity < request.Quantity)
            return null;

        // Check if item exists in destination location
        var toAccessory = await context.RiderAccessories
            .FirstOrDefaultAsync(a => a.Name == fromAccessory.Name &&
                                     a.Location == toLocation);

        if (toAccessory != null)
        {
            // Add to existing
            toAccessory.Quantity += request.Quantity;
        }
        else
        {
            // Create new in destination
            toAccessory = new Domain.Entities.Spare.RiderAccessory
            {
                Name = fromAccessory.Name,
                Quantity = request.Quantity,
                Price = fromAccessory.Price,
                Location = toLocation,
                CreatedAt = DateTime.UtcNow.AddHours(3)
            };
            await context.RiderAccessories.AddAsync(toAccessory);
        }

        // Reduce from housing location
        fromAccessory.Quantity -= request.Quantity;

        return new TransferItem
        {
            ItemId = fromAccessory.Id,
            ItemName = fromAccessory.Name,
            ItemType = TransferItemType.Accessory,
            Quantity = request.Quantity
        };
    }

    public async Task<Result<IEnumerable<TransferResponse>>> GetHousingTransfersAsync(
        long managerIqamaNo)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<IEnumerable<TransferResponse>>(housingResult.Error);

        var housing = housingResult.Value;

        var transfers = await context.Transfers
            .Include(t => t.TransferItems)
            .Where(t => t.FromLocation == housing.Name || t.ToLocation == housing.Name) // ← was: only FromLocation
            .OrderByDescending(t => t.TransferredAt)
            .AsNoTracking()
            .ToListAsync();

        var response = transfers.Select(MapTransferToResponse);
        return Result.Success<IEnumerable<TransferResponse>>(response);
    }

    public async Task<Result<HousingUsageHistoryResponse>> GetHousingUsageHistoryAsync(
    long managerIqamaNo,
    DateTime? fromDate = null,
    DateTime? toDate = null)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<HousingUsageHistoryResponse>(housingResult.Error);

        var housing = housingResult.Value;

        // ── Spare part usages ─────────────────────────────────────────────────
        var spQuery = context.SparePartUsages
            .Include(u => u.SparePart)
            .Where(u => u.Location == housing.Name)
            .AsQueryable();

        if (fromDate.HasValue)
            spQuery = spQuery.Where(u => u.UsedAt >= fromDate.Value);
        if (toDate.HasValue)
            spQuery = spQuery.Where(u => u.UsedAt <= toDate.Value);

        var spUsages = await spQuery
            .Include(c=>c.Vehicle)
            .OrderByDescending(u => u.UsedAt)
            .AsNoTracking()
            .ToListAsync();

        // ── Accessory usages ──────────────────────────────────────────────────
        var acQuery = context.RiderAccessoryUsages
            .Include(u => u.RiderAccessory)
            .Include(u => u.Rider)
                .ThenInclude(r => r.Employee)
            .Where(u => u.Location == housing.Name)
            .AsQueryable();

        if (fromDate.HasValue)
            acQuery = acQuery.Where(u => u.IssuedAt >= fromDate.Value);
        if (toDate.HasValue)
            acQuery = acQuery.Where(u => u.IssuedAt <= toDate.Value);

        var acUsages = await acQuery
            .OrderByDescending(u => u.IssuedAt)
            .AsNoTracking()
            .ToListAsync();

        // ── Map ───────────────────────────────────────────────────────────────
        var spResponse = spUsages.Select(u => new SparePartUsageResponse(
            u.Id,
            u.SparePartId,
            u.SparePart.Name,
            u.Vehicle.PlateNumberA,
            u.QuantityUsed,
            u.UsedAt,
            u.Cost ?? 0m
        )).ToList();

        var acResponse = acUsages.Select(u => new RiderAccessoryUsageResponse(
            u.Id,
            u.RiderAccessoryId,
            u.RiderAccessory.Name,
            u.RiderId,
            u.Rider?.Employee?.NameEN ?? "N/A",
            u.Rider?.Employee?.NameAR ?? "N/A",
            u.IssuedAt,
            u.Cost ?? 0m
        )).ToList();

        var totalSp = spResponse.Sum(u => u.Cost);
        var totalAc = acResponse.Sum(u => u.Cost);

        return Result.Success(new HousingUsageHistoryResponse(
            HousingName: housing.Name,
            TotalSparePartUsages: spResponse.Count,
            TotalAccessoryUsages: acResponse.Count,
            TotalSparePartsCost: totalSp ?? 0,
            TotalAccessoriesCost: totalAc ?? 0,
            GrandTotal: (totalSp ?? 0) + (totalAc ?? 0),
            SparePartUsages: spResponse,
            AccessoryUsages: acResponse
        ));
    }

    /// <summary>
    /// Housing-scoped audit trail: every manual spare-part / rider-accessory
    /// change (quantity, price, location, etc.) recorded at this housing —
    /// who did it, when, and the before/after values. Covers changes made
    /// while the item was AT this housing either before or after the edit,
    /// so a transfer in or out of the housing still shows up here.
    /// </summary>
    public async Task<Result<IEnumerable<InventoryAuditLogResponse>>> GetHousingInventoryAuditLogAsync(
        long managerIqamaNo,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<IEnumerable<InventoryAuditLogResponse>>(housingResult.Error);

        var housing = housingResult.Value;

        var query = context.InventoryAuditLogs
            .AsNoTracking()
            .Where(a => a.LocationBefore == housing.Name || a.LocationAfter == housing.Name)
            .AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(a => a.PerformedAt >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(a => a.PerformedAt <= toDate.Value);

        var logs = await query
            .OrderByDescending(a => a.PerformedAt)
            .ToListAsync();

        var response = logs.Select(a => new InventoryAuditLogResponse(
            a.Id,
            a.ItemType.ToString(),
            a.ItemId,
            a.ItemName,
            a.Action.ToString(),
            a.LocationBefore,
            a.LocationAfter,
            a.QuantityBefore,
            a.QuantityAfter,
            a.PriceBefore,
            a.PriceAfter,
            a.PerformedBy,
            a.PerformedAt,
            a.Notes
        ));

        return Result.Success(response);
    }

    private static TransferResponse MapTransferToResponse(Domain.Entities.Spare.Transfer transfer)
    {
        var items = transfer.TransferItems.Select(ti => new TransferItemResponse(
            ti.ItemId,
            ti.ItemName,
            ti.ItemType,
            ti.Quantity
        )).ToList();

        return new TransferResponse(
            transfer.Id,
            transfer.FromLocation,
            transfer.ToLocation,
            transfer.HousingId,
            transfer.TransferItems.Sum(ti => ti.Quantity),
            transfer.TransferredBy,
            transfer.TransferredAt,
            items
        );
    }

    #endregion
}




// Add to IMemberService.cs
public record MemberFixVehicleRequest(
    string VehiclePlate,
    string? FixDescription = null
);