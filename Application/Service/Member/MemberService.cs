using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Authentication;
using Domain;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Member;

public class MemberService(UserManager<ApplicationUser> userManager , SignInManager<ApplicationUser> signInManager , IJwtProvider jwtProvider , ApplicationDbcontext context) : IMemberService
{
    private readonly UserManager<ApplicationUser> userManager = userManager;
    private readonly SignInManager<ApplicationUser> signInManager = signInManager;
    private readonly IJwtProvider jwtProvider = jwtProvider;
    private readonly ApplicationDbcontext context = context;

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

        var riders = await context.RiderDetails
            .Include(r => r.Employee)
            .Include(r => r.Company)
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .ToListAsync();

        var activeRiders = riders.Count(r => r.Employee.Status.ToLower() == "enable");
        var inactiveRiders = riders.Count - activeRiders;

        var vehicles = await context.Vehicles
            .Where(v => riders.Select(r => r.VehicleNumber).Contains(v.VehicleNumber))
            .ToListAsync();

        var vehiclesInUse = await context.RiderVehicleStatus
            .Where(rvs => vehicles.Select(v => v.VehicleNumber).Contains(rvs.VehicleNumber)
                && rvs.IsActive
                && rvs.StatusType == VehicleStatusType.Taken)
            .CountAsync();

        var pendingVehicleOps = await context.TempVehicleOperations
            .Where(t => !t.IsResolved && riders.Select(r => r.EmployeeIqamaNo).Contains(t.RiderIqamaNo))
            .CountAsync();

        var pendingEmpUpdates = await context.TempEmployeeUpdates
            .Where(t => !t.IsResolved && employeeIqamas.Contains(t.IqamaNo))
            .CountAsync();

        var pendingStatusChanges = await context.TempEmployeeStatusChanges
            .Where(t => !t.IsResolved && employeeIqamas.Contains(t.EmployeeIqamaNo))
            .CountAsync();

        var activeDisabilities = await context.Set<HungerDisability>()
            .Where(h => riders.Select(r => r.Id).Contains(h.ActualRiderId)
                && h.ShiftDate >= today)
            .CountAsync();

        var todayShifts = await context.RiderShifts
            .Where(rs => riders.Select(r => r.Id).Contains(rs.RiderId)
                && rs.ShiftDate == today)
            .CountAsync();

        // Recent activities
        var recentActivities = new List<RecentActivityItem>();

        var recentVehicleOps = await context.TempVehicleOperations
            .Where(t => riders.Select(r => r.EmployeeIqamaNo).Contains(t.RiderIqamaNo))
            .OrderByDescending(t => t.RequestedAt)
            .Take(5)
            .Select(t => new RecentActivityItem(
                "VehicleOperation",
                $"Vehicle operation request: {t.VehicleStatusType}",
                t.RequestedAt
            ))
            .ToListAsync();

        recentActivities.AddRange(recentVehicleOps);

        var stats = new Statistics(
            housing.Employees.Count,
            activeRiders,
            inactiveRiders,
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

        var response = new HousingDashboardResponse(
            housingInfo,
            stats,
            recentActivities.OrderByDescending(a => a.Timestamp).Take(10).ToList()
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
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .ToDictionaryAsync(r => r.EmployeeIqamaNo, r => r);

        var employees = housing.Employees.Select(e => new EmployeeSummary(
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
            manager?.NameEN,
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

    public async Task<Result<List<HousingRiderResponse>>> GetHousingRiders(long managerIqamaNo)
    {
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<List<HousingRiderResponse>>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var riders = await context.RiderDetails
            .Include(r => r.Employee)
            .Include(r => r.Company)
            .Include(r => r.Vehicle)
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .ToListAsync();

        var response = riders.Select(r => new HousingRiderResponse(
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
            r.CreatedAt
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
            rs.Rider.Employee.NameEN,
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
            rider.Employee.NameEN,
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
            s.Rider.Employee.NameEN,
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
        var housingResult = await GetManagedHousing(managerIqamaNo);
        if (housingResult.IsFailure)
            return Result.Failure<List<HousingVehicleResponse>>(housingResult.Error);

        var housing = housingResult.Value;
        var employeeIqamas = housing.Employees.Select(e => e.IqamaNo).ToList();

        var riders = await context.RiderDetails
            .Include(r => r.Employee)
            .Where(r => employeeIqamas.Contains(r.EmployeeIqamaNo))
            .ToListAsync();

        var vehicleNumbers = riders
            .Where(r => !string.IsNullOrEmpty(r.VehicleNumber))
            .Select(r => r.VehicleNumber!)
            .ToList();

        var vehicles = await context.Vehicles
            .Where(v => vehicleNumbers.Contains(v.VehicleNumber))
            .ToListAsync();

        var latestStatuses = await context.RiderVehicleStatus
            .Where(rvs => vehicleNumbers.Contains(rvs.VehicleNumber) && rvs.IsActive)
            .GroupBy(rvs => rvs.VehicleNumber)
            .Select(g => g.OrderByDescending(rvs => rvs.Timestamp).FirstOrDefault())
            .ToListAsync();

        var statusDict = latestStatuses
            .Where(s => s != null)
            .ToDictionary(s => s!.VehicleNumber, s => s);

        var riderDict = riders.ToDictionary(r => r.VehicleNumber ?? string.Empty, r => r);

        var response = vehicles.Select(v =>
        {
            var status = statusDict.ContainsKey(v.VehicleNumber) ? statusDict[v.VehicleNumber] : null;
            var rider = riderDict.ContainsKey(v.VehicleNumber) ? riderDict[v.VehicleNumber] : null;

            return new HousingVehicleResponse(
                v.VehicleNumber,
                v.VehicleType,
                v.PlateNumberA,
                v.PlateNumberE,
                v.ManufactureYear,
                v.Manufacturer,
                v.LicenseExpiryDate,
                v.Location,
                status?.StatusType.ToString(),
                rider?.EmployeeIqamaNo,
                rider?.Employee.NameEN,
                status?.Timestamp
            );
        }).ToList();

        return Result.Success(response);
    }

    // Continuation of HousingMemberService class - Final Methods

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
}
