using Application.Abstraction;
using Application.Contracts.Employees;
using Domain;
using Domain.Entities;
using Mapster;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Text;

namespace Application.Service.Empolyee;

public class VehicleService(ApplicationDbcontext dbcontext) : IVehicleService
{
    private readonly ApplicationDbcontext dbcontext = dbcontext;

    public async Task<Result<VehicleResponse>> CreateAsync(VehicleRequest Request)
    {
        var isExist = await dbcontext.Vehicles.AnyAsync(c => c.VehicleNumber == Request.VehicleNumber);

        if (isExist)
            return Result.Failure<VehicleResponse>(new Error("vehicle.AlreadyExists", $"Company with name {Request.VehicleNumber} already exists.", 409));

        var company = Request.Adapt<Vehicle>();

        dbcontext.Vehicles.Add(company);

        await dbcontext.SaveChangesAsync();

        var companyResponses = company.Adapt<VehicleResponse>();

        return Result.Success(companyResponses);
    }
    public async Task<Result> DeleteAsync(string VehicleNumber, CancellationToken cancellationToken = default)
    {
        var companies = await dbcontext.Vehicles.Where(c => c.VehicleNumber == VehicleNumber).SingleOrDefaultAsync();

        if (companies == null)
            return Result.Failure(new Error("vehicle.NotFound", $"vehicle with name {VehicleNumber} was not found.", 404));

        dbcontext.Vehicles.Remove(companies);
        await dbcontext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
    public async Task<Result<IEnumerable<VehicleResponse>>> Get(string VehicleNumber)
    {
        var companies = await dbcontext.Vehicles.Where(c => c.VehicleNumber.Contains(VehicleNumber)).AsNoTracking().ToListAsync();

        if (companies == null)
            return Result.Failure<IEnumerable<VehicleResponse>>(new Error("vehicle.NotFound", $"vehicle starts with name {VehicleNumber} was not found.", 404));

        var companyResponses = companies.Adapt<IEnumerable<VehicleResponse>>();

        return Result.Success(companyResponses); ;
    }
    public async Task<Result<IEnumerable<VehicleResponse>>> GetAllEmployee()
    {
        var companies = await dbcontext.Vehicles.AsNoTracking().ToListAsync();

        if (companies == null)
            return Result.Failure<IEnumerable<VehicleResponse>>(new Error("Vehicle.NotFound", " no Vehicle found.", 404));

        var companyResponses = companies.Adapt<IEnumerable<VehicleResponse>>();

        return Result.Success(companyResponses);
    }
    public async Task<Result<VehicleResponse>> UpdateAsync(string PlateNumberA, UVehicleRequest Request)
    {
        var companies = await dbcontext.Vehicles.Where(c => c.PlateNumberA == PlateNumberA).SingleOrDefaultAsync();

        if (companies == null)
            return Result.Failure<VehicleResponse>(new Error("Vehicle.NotFound", $"Vehicle with name {PlateNumberA} was not found.", 404));

        companies.VehicleType = Request.VehicleType;
        companies.SerialNumber = Request.SerialNumber;
        companies.PlateNumberA = Request.PlateNumberA;
        companies.OwnerId = Request.OwnerId;
        companies.OwnerName = Request.OwnerName;
        companies.PlateNumberE = Request.PlateNumberE;
        companies.ManufactureYear = Request.ManufactureYear;
        companies.Manufacturer = Request.Manufacturer;
        companies.LicenseExpiryDate = Request.LicenseExpiryDate;
        companies.VehicleImagePath = Request.VehicleImagePath;
        companies.LicenseImagePath = Request.LicenseImagePath;
        companies.ExstraImage = Request.ExstraImage;
        companies.ExstraImage1 = Request.ExstraImage1;
        companies.Location = Request.Location; // ADDED
        await dbcontext.SaveChangesAsync();

        var companyResponses = companies.Adapt<VehicleResponse>();
        return Result.Success(companyResponses);
    }
    public async Task<Result> ChangeLocation(string PlatNo, string NewLocation)
    {
        var companies = await dbcontext.Vehicles.Where(c => c.PlateNumberA == PlatNo).AsNoTracking().SingleOrDefaultAsync();

        if (companies == null)
            return Result.Failure(new Error("vehicle.NotFound", $"vehicle with Plate Number {PlatNo} was not found.", 404));

        companies.Location = NewLocation;

        dbcontext.Vehicles.Update(companies);
        await dbcontext.SaveChangesAsync();
        return Result.Success();
    }


    public async Task<Result> TakeVehicleAsync(int iqamaNo, string PlateNumberA, string reason)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {

            var rider = await dbcontext.RiderDetails
            .FirstOrDefaultAsync(x => x.EmployeeIqamaNo == iqamaNo);

            if (rider == null)
                return Result.Failure(new Error("NoRider", "No rider found with this Iqama", 400));

            if (!string.IsNullOrEmpty(rider.VehicleNumber))
                return Result.Failure(new Error("HasVehicle",
                    $"Rider already has vehicle {rider.VehicleNumber} assigned", 400));

            var vehicle = await dbcontext.Vehicles
               .FirstOrDefaultAsync(v => v.PlateNumberA == PlateNumberA);


            if (vehicle == null)
                return Result.Failure(new Error("NoVehicle", "Vehicle not found", 404));

            bool unavailable = await dbcontext.RiderVehicleStatus
              .AnyAsync(s => s.Vehicle.PlateNumberA == PlateNumberA &&
                            s.IsActive &&
                            s.StatusType == VehicleStatusType.Taken);

            if (unavailable)
                return Result.Failure(new Error("VehicleTaken", "Vehicle is already taken by another rider", 400));


            bool hasProblem = await dbcontext.RiderVehicleStatus
              .AnyAsync(s => s.Vehicle.PlateNumberA == PlateNumberA &&
                            s.IsActive &&
                            s.StatusType == VehicleStatusType.Problem);

            if (hasProblem)
                return Result.Failure(new Error("VehicleHasProblem", "Vehicle has active problems and cannot be taken", 400));


            rider.VehicleNumber = vehicle.VehicleNumber;





            dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                EmployeeIqamaNo = iqamaNo,
                VehicleNumber = vehicle.VehicleNumber,
                StatusType = VehicleStatusType.Taken,
                Reason = reason,
                IsActive = true
            });

            await dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(new Error("TakeVehicleError", $"Failed to take vehicle: {ex.Message}", 500));
        }
    }
    public async Task<Result> ReturnVehicleAsync(int iqamaNo, string PlateNumberA, string reason)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            var rider = await dbcontext.RiderDetails
                .FirstOrDefaultAsync(x => x.EmployeeIqamaNo == iqamaNo);

            if (rider == null)
                return Result.Failure(new Error("NoRider", "No rider found with this Iqama", 404));

            var VehicleNumber = await dbcontext.Vehicles
                .Where(v => v.PlateNumberA == PlateNumberA)
                .Select(v => v.VehicleNumber)
                .FirstOrDefaultAsync();

            if (rider.VehicleNumber != VehicleNumber)
                return Result.Failure(new Error("NotAssigned",
                    "This vehicle is not assigned to this rider", 400));

            var activeStatus = await dbcontext.RiderVehicleStatus
                .FirstOrDefaultAsync(s => s.Vehicle.PlateNumberA == PlateNumberA &&
                                         s.EmployeeIqamaNo == iqamaNo &&
                                         s.IsActive &&
                                         s.StatusType == VehicleStatusType.Taken);

            if (activeStatus == null)
                return Result.Failure(new Error("NoActiveStatus",
                    "No active vehicle assignment found", 400));

            activeStatus.IsActive = false;

            rider.VehicleNumber = null;

            dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                EmployeeIqamaNo = iqamaNo,
                VehicleNumber = VehicleNumber,
                StatusType = VehicleStatusType.Returned,
                Reason = reason,
                IsActive = false
            });

            await dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(new Error("ReturnVehicleError", $"Failed to return vehicle: {ex.Message}", 500));
        }
    }
    public async Task<Result> ReportProblemAsync(int iqamaNo, string PlateNumberA, string reason)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            var rider = await dbcontext.RiderDetails
                .FirstOrDefaultAsync(x => x.EmployeeIqamaNo == iqamaNo);

            if (rider == null)
                return Result.Failure(new Error("NoRider", "No rider found with this Iqama", 404));

            var VehicleNumber = await dbcontext.Vehicles
                .Where(v => v.PlateNumberA == PlateNumberA)
                .Select(v => v.VehicleNumber)
                .FirstOrDefaultAsync();

            if (rider.VehicleNumber != VehicleNumber)
                return Result.Failure(new Error("NotAssigned",
                    "Cannot report problem for a vehicle not assigned to you", 403));


            var vehicle = await dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.VehicleNumber == VehicleNumber);

            if (vehicle == null)
                return Result.Failure(new Error("NoVehicle", "Vehicle not found", 404));


            var activeTakenStatus = await dbcontext.RiderVehicleStatus
                .AnyAsync(s => s.VehicleNumber == VehicleNumber &&
                              s.EmployeeIqamaNo == iqamaNo &&
                              s.IsActive &&
                              s.StatusType == VehicleStatusType.Taken);

            if (!activeTakenStatus)
                return Result.Failure(new Error("NoActiveAssignment",
                    "No active vehicle assignment found", 400));

            var activeStatus = await dbcontext.RiderVehicleStatus
            .FirstOrDefaultAsync(s => s.VehicleNumber == vehicle.VehicleNumber &&
                                      s.EmployeeIqamaNo == iqamaNo &&
                                      s.IsActive &&
                                      s.StatusType == VehicleStatusType.Taken);

            if (activeStatus == null)
                return Result.Failure(new Error("NoActiveAssignment",
                    "No active vehicle assignment found", 400));

            activeStatus.IsActive = false;

            dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                EmployeeIqamaNo = iqamaNo,
                VehicleNumber = VehicleNumber,
                StatusType = VehicleStatusType.Problem,
                Reason = reason,
                IsActive = true
            });

            rider.VehicleNumber = null;

            await dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(new Error("ReportProblemError", $"Failed to report problem: {ex.Message}", 500));
        }
    }
    public async Task<Result> IsVehicleAvailableAsync(string PlateNumberA)
    {

        var vehicleNumber = await dbcontext.Vehicles
            .Where(v => v.PlateNumberA == PlateNumberA)
            .Select(v => v.VehicleNumber)
            .FirstOrDefaultAsync();

        var vehicleExists = await dbcontext.Vehicles
            .AnyAsync(v => v.VehicleNumber == vehicleNumber);

        if (!vehicleExists)
            return Result.Failure(new Error("NoVehicle", "Vehicle not found", 404));

        var isUnavailable = await dbcontext.RiderVehicleStatus
            .AnyAsync(s => s.VehicleNumber == vehicleNumber &&
                          s.IsActive &&
                          (s.StatusType == VehicleStatusType.Taken ||
                           s.StatusType == VehicleStatusType.Problem ||
                           s.StatusType == VehicleStatusType.Stolen ||
                           s.StatusType == VehicleStatusType.BreakUp));

        if (isUnavailable)
        {
            var currentStatus = await dbcontext.RiderVehicleStatus
                .Where(s => s.VehicleNumber == vehicleNumber && s.IsActive)
                .Select(s => s.StatusType)
                .FirstOrDefaultAsync();

            return Result.Failure(new Error("VehicleUnavailable",
                $"Vehicle is not available (Status: {currentStatus})", 400));
        }

        return Result.Success();
    }
    public async Task<Result<IEnumerable<RiderVehicleStatus>>> GetVehicleHistoryAsync(string PlateNumberA)
    {
        try
        {
            var vehicle = await dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.PlateNumberA == PlateNumberA);

            if (vehicle == null)
                return Result.Failure<IEnumerable<RiderVehicleStatus>>(new Error("not available", "Not availabe vehicle", 400));


            var history = await dbcontext.RiderVehicleStatus
                .Where(s => s.VehicleNumber == vehicle.VehicleNumber)
                .OrderByDescending(s => s.Timestamp)
                .ToListAsync();

            return Result.Success<IEnumerable<RiderVehicleStatus>>(history);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<RiderVehicleStatus>>(new Error("nohistory", "No history found for this vehicle", 400));
        }
    }
    public async Task<Result<IEnumerable<Vehicle>>> GetAvailableVehiclesAsync()
    {
        try
        {
            var unavailableVehicles = await dbcontext.RiderVehicleStatus
                .Where(s => s.IsActive &&
                           (s.StatusType == VehicleStatusType.Taken ||
                            s.StatusType == VehicleStatusType.Problem ||
                            s.StatusType == VehicleStatusType.Stolen ||
                            s.StatusType == VehicleStatusType.BreakUp))
                .Select(s => s.VehicleNumber)
                .Distinct()
                .ToListAsync();

            var availableVehicles = await dbcontext.Vehicles
                .Where(v => !unavailableVehicles.Contains(v.VehicleNumber))
                .OrderBy(v => v.VehicleNumber)
                .ToListAsync();

            return Result.Success<IEnumerable<Vehicle>>(availableVehicles);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<Vehicle>>(
                new Error("GetAvailableError",
                    $"Failed to retrieve available vehicles: {ex.Message}", 500));
        }
    }
    public async Task<Result> FixVehicleProblemAsync(string PlateNumberA, string reason)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            var vehicleNumber = await dbcontext.Vehicles
                .Where(v => v.PlateNumberA == PlateNumberA)
                .Select(v => v.VehicleNumber)
                .FirstOrDefaultAsync();

            var activeProblems = await dbcontext.RiderVehicleStatus
                .Where(s => s.VehicleNumber == vehicleNumber &&
                           s.IsActive &&
                           s.StatusType == VehicleStatusType.Problem)
                .ToListAsync();

            if (!activeProblems.Any())
                return Result.Failure(new Error("NoActiveProblem",
                    "No active problems found for this vehicle", 400));

            foreach (var problem in activeProblems)
            {
                problem.IsActive = false;
            }

            dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                EmployeeIqamaNo = null,
                VehicleNumber = vehicleNumber,
                StatusType = VehicleStatusType.Returned,
                Reason = $"Fixed: {reason} | Resolved {activeProblems.Count} problem(s)",
                IsActive = false
            });

            await dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(new Error("FixProblemError", $"Failed to fix vehicle problem: {ex.Message}", 500));
        }
    }


    public async Task<Result<IEnumerable<VehicleHistoryDto>>> GetVehicleHistoryAsync1(string PlateNumberA)
    {
        try
        {
            var vehicle = await dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.PlateNumberA == PlateNumberA);

            if (vehicle == null)
                return Result.Failure<IEnumerable<VehicleHistoryDto>>(
                    new Error("not available", "Not available vehicle", 400));

            var history = await dbcontext.RiderVehicleStatus
                .Where(s => s.VehicleNumber == vehicle.VehicleNumber)
                .OrderByDescending(s => s.Timestamp)
                .ToListAsync();

            var historyDtos = new List<VehicleHistoryDto>();

            foreach (var item in history)
            {
                var rider = item.EmployeeIqamaNo.HasValue
                    ? await dbcontext.RiderDetails
                        .Include(r => r.Employee)
                        .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == item.EmployeeIqamaNo.Value)
                    : null;

                historyDtos.Add(new VehicleHistoryDto
                {
                    Id = item.Id,
                    VehicleNumber = item.VehicleNumber,
                    SerialNumber = vehicle.SerialNumber,
                    PlateNumberA = vehicle.PlateNumberA ?? string.Empty,
                    PlateNumberE = vehicle.PlateNumberE ?? string.Empty,
                    OwnerId = vehicle.OwnerId,
                    OwnerName = vehicle.OwnerName ?? string.Empty,
                    ManufactureYear = vehicle.ManufactureYear,
                    Manufacturer = vehicle.Manufacturer ?? string.Empty,
                    EmployeeIqamaNo = item.EmployeeIqamaNo,
                    RiderName = rider?.Employee.NameAR ?? "N/A",
                    RiderNameE = rider?.Employee.NameEN ?? "N/A",
                    Location = vehicle.Location ?? string.Empty, // ADDED
                    StatusType = item.StatusType,
                    StatusTypeDisplay = item.StatusType.ToString(),
                    Reason = item.Reason ?? "no reason",
                    Timestamp = item.Timestamp,
                    IsActive = item.IsActive
                });
            }

            return Result.Success<IEnumerable<VehicleHistoryDto>>(historyDtos);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<VehicleHistoryDto>>(
                new Error("nohistory", "No history found for this vehicle", 400));
        }
    }
    public async Task<Result<IEnumerable<VehicleWithRiderDto>>> GetAllVehiclesWithRidersAsync()
    {
        try
        {
            var vehicles = await dbcontext.Vehicles
                .Select(v => new
                {
                    Vehicle = v,
                    ActiveStatuses = dbcontext.RiderVehicleStatus
                        .Where(s => s.VehicleNumber == v.VehicleNumber && s.IsActive)
                        .ToList()
                })
                .ToListAsync();

            var result = vehicles.Select(v =>
            {
                var takenStatus = v.ActiveStatuses
                    .FirstOrDefault(s => s.StatusType == VehicleStatusType.Taken);

                var currentStatus = v.ActiveStatuses.Any()
                    ? v.ActiveStatuses.First().StatusType.ToString()
                    : "Available";

                var statusSince = v.ActiveStatuses.Any()
                    ? v.ActiveStatuses.OrderBy(s => s.Timestamp).First().Timestamp
                    : (DateTime?)null;

                var dto = new VehicleWithRiderDto
                {
                    VehicleNumber = v.Vehicle.VehicleNumber,
                    VehicleType = v.Vehicle.VehicleType,
                    PlateNumberA = v.Vehicle.PlateNumberA,
                    PlateNumberE = v.Vehicle.PlateNumberE,
                    SerialNumber = v.Vehicle.SerialNumber,
                    Manufacturer = v.Vehicle.Manufacturer,
                    ManufactureYear = v.Vehicle.ManufactureYear,
                    OwnerName = v.Vehicle.OwnerName,
                    OwnerId = v.Vehicle.OwnerId,
                    Location = v.Vehicle.Location, // ADDED
                    LicenseExpiryDate = v.Vehicle.LicenseExpiryDate,
                    CurrentStatus = currentStatus,
                    StatusSince = statusSince,

                    IsAvailable = !v.ActiveStatuses.Any(),

                    HasActiveProblem = v.ActiveStatuses
                        .Any(s => s.StatusType == VehicleStatusType.Problem),

                    IsStolen = v.ActiveStatuses
                        .Any(s => s.StatusType == VehicleStatusType.Stolen),

                    IsBreakUp = v.ActiveStatuses
                        .Any(s => s.StatusType == VehicleStatusType.BreakUp),

                    ActiveProblemsCount = v.ActiveStatuses
                        .Count(s => s.StatusType == VehicleStatusType.Problem)
                };

                if (takenStatus != null)
                {
                    var rider = dbcontext.RiderDetails
                        .Include(r => r.Employee)
                        .FirstOrDefault(r => r.EmployeeIqamaNo == takenStatus.EmployeeIqamaNo);

                    if (rider != null)
                    {
                        dto.CurrentRider = new RiderInfoDto
                        {
                            EmployeeIqamaNo = rider.EmployeeIqamaNo,
                            RiderName = rider.Employee.NameAR,
                            RiderNameE = rider.Employee.NameEN,
                            TakenDate = takenStatus.Timestamp,
                            TakenReason = takenStatus.Reason ?? "no reason"
                        };
                    }
                }

                return dto;
            })
            .OrderBy(v => v.VehicleNumber)
            .ToList();

            return Result.Success<IEnumerable<VehicleWithRiderDto>>(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<VehicleWithRiderDto>>(
                new Error("GetAllVehiclesError",
                    $"Failed to retrieve vehicles: {ex.Message}", 500));
        }
    }
    public async Task<Result<VehicleWithRiderDto>> GetVehicleWithRiderByVehicleNumberAsync(string PlateNumberA)
    {
        try
        {
            var vehicle = await dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.PlateNumberA == PlateNumberA);

            if (vehicle == null)
                return Result.Failure<VehicleWithRiderDto>(
                    new Error("NoVehicle", "Vehicle not found", 404));

            var vehicleNumber = await dbcontext.Vehicles
                .Where(v => v.PlateNumberA == PlateNumberA)
                .Select(v => v.VehicleNumber)
                .FirstOrDefaultAsync();

            var currentRider = await dbcontext.RiderVehicleStatus
                .Where(s => s.VehicleNumber == vehicleNumber &&
                           s.IsActive &&
                           s.StatusType == VehicleStatusType.Taken)
                .Join(
                    dbcontext.RiderDetails.Include(r => r.Employee),
                    status => status.EmployeeIqamaNo,
                    rider => rider.EmployeeIqamaNo,
                    (status, rider) => new RiderInfoDto
                    {
                        EmployeeIqamaNo = rider.EmployeeIqamaNo,
                        RiderName = rider.Employee.NameAR,
                        RiderNameE = rider.Employee.NameEN,
                        TakenDate = status.Timestamp,
                        TakenReason = status.Reason ?? "no reason"
                    }
                )
                .FirstOrDefaultAsync();

            var activeStatuses = await dbcontext.RiderVehicleStatus
                .Where(s => s.VehicleNumber == vehicleNumber && s.IsActive)
                .ToListAsync();

            var isAvailable = !activeStatuses.Any();

            var hasActiveProblem = activeStatuses
                .Any(s => s.StatusType == VehicleStatusType.Problem);

            var isStolen = activeStatuses
                .Any(s => s.StatusType == VehicleStatusType.Stolen);

            var isBreakUp = activeStatuses
                .Any(s => s.StatusType == VehicleStatusType.BreakUp);

            var activeProblemsCount = activeStatuses
                .Count(s => s.StatusType == VehicleStatusType.Problem);

            var currentStatus = activeStatuses.Any()
                ? activeStatuses.First().StatusType.ToString()
                : "Available";

            var statusSince = activeStatuses.Any()
                ? activeStatuses.OrderBy(s => s.Timestamp).First().Timestamp
                : (DateTime?)null;

            var vehicleDto = new VehicleWithRiderDto
            {
                VehicleNumber = vehicle.VehicleNumber,
                VehicleType = vehicle.VehicleType,
                PlateNumberA = vehicle.PlateNumberA,
                PlateNumberE = vehicle.PlateNumberE,
                SerialNumber = vehicle.SerialNumber,
                Manufacturer = vehicle.Manufacturer,
                ManufactureYear = vehicle.ManufactureYear,
                OwnerName = vehicle.OwnerName,
                OwnerId = vehicle.OwnerId,
                LicenseExpiryDate = vehicle.LicenseExpiryDate,
                CurrentRider = currentRider,
                IsAvailable = isAvailable,
                HasActiveProblem = hasActiveProblem,
                Location = vehicle.Location, // ADDED
                IsStolen = isStolen,
                IsBreakUp = isBreakUp,
                ActiveProblemsCount = activeProblemsCount,
                CurrentStatus = currentStatus,
                StatusSince = statusSince
            };

            return Result.Success(vehicleDto);
        }
        catch (Exception ex)
        {
            return Result.Failure<VehicleWithRiderDto>(
                new Error("GetVehicleError", $"Failed to retrieve vehicle: {ex.Message}", 500));
        }
    }
    public async Task<Result<UnavailableVehiclesResponse>> GetUnavailableVehiclesAsync(string statusFilter = "all")
    {
        try
        {
            var validFilters = new[] { "all", "unavailable", "problem", "stolen", "breakup" };
            if (!validFilters.Contains(statusFilter.ToLower()))
                return Result.Failure<UnavailableVehiclesResponse>(
                    new Error("InvalidFilter",
                        "Filter must be 'all', 'unavailable', 'problem', 'stolen', or 'breakup'", 400));

            statusFilter = statusFilter.ToLower();

            var takenVehicles = new List<UnavailableVehicleDto>();
            if (statusFilter == "all" || statusFilter == "unavailable")
            {
                takenVehicles = await dbcontext.RiderVehicleStatus
                    .Where(s => s.IsActive && s.StatusType == VehicleStatusType.Taken)
                    .Join(
                        dbcontext.Vehicles,
                        status => status.VehicleNumber,
                        vehicle => vehicle.VehicleNumber,
                        (status, vehicle) => new { status, vehicle }
                    )
                    .Join(
                        dbcontext.RiderDetails.Include(r => r.Employee),
                        sv => sv.status.EmployeeIqamaNo,
                        rider => rider.EmployeeIqamaNo,
                        (sv, rider) => new UnavailableVehicleDto
                        {
                            VehicleNumber = sv.vehicle.VehicleNumber,
                            VehicleType = sv.vehicle.VehicleType,
                            SerialNumber = sv.vehicle.SerialNumber,
                            PlateNumberA = sv.vehicle.PlateNumberA ?? string.Empty,
                            PlateNumberE = sv.vehicle.PlateNumberE ?? string.Empty,
                            OwnerId = sv.vehicle.OwnerId,
                            OwnerName = sv.vehicle.OwnerName,
                            Manufacturer = sv.vehicle.Manufacturer,
                            ManufactureYear = sv.vehicle.ManufactureYear,
                            LicenseExpiryDate = sv.vehicle.LicenseExpiryDate,
                            Location = sv.vehicle.Location, // ADDED
                            StatusType = "Taken",
                            RiderIqamaNo = rider.EmployeeIqamaNo,
                            RiderName = rider.Employee.NameAR,
                            RiderNameE = rider.Employee.NameEN,
                            Reason = sv.status.Reason,
                            Since = sv.status.Timestamp,
                            ProblemsCount = 0
                        }
                    )
                    .ToListAsync();
            }

            var problemVehicles = new List<UnavailableVehicleDto>();
            if (statusFilter == "all" || statusFilter == "problem")
            {
                var vehiclesWithProblems = await dbcontext.RiderVehicleStatus
                    .Where(s => s.IsActive && s.StatusType == VehicleStatusType.Problem)
                    .GroupBy(s => s.VehicleNumber)
                    .Select(g => new
                    {
                        VehicleNumber = g.Key,
                        ProblemsCount = g.Count(),
                        LatestProblem = g.OrderByDescending(s => s.Timestamp).FirstOrDefault()
                    })
                    .ToListAsync();

                foreach (var item in vehiclesWithProblems)
                {
                    var vehicle = await dbcontext.Vehicles
                        .FirstOrDefaultAsync(v => v.VehicleNumber == item.VehicleNumber);

                    var rider = item.LatestProblem.EmployeeIqamaNo.HasValue
                        ? await dbcontext.RiderDetails
                            .Include(r => r.Employee)
                            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == item.LatestProblem.EmployeeIqamaNo.Value)
                        : null;

                    problemVehicles.Add(new UnavailableVehicleDto
                    {
                        VehicleNumber = vehicle?.VehicleNumber ?? item.VehicleNumber,
                        VehicleType = vehicle?.VehicleType ?? "Unknown",
                        SerialNumber = vehicle?.SerialNumber ?? 0,
                        PlateNumberA = vehicle?.PlateNumberA ?? string.Empty,
                        PlateNumberE = vehicle?.PlateNumberE ?? string.Empty,
                        OwnerId = vehicle?.OwnerId ?? 0,
                        OwnerName = vehicle?.OwnerName,
                        Location = vehicle.Location ?? "unkown", // ADDED
                        LicenseExpiryDate = vehicle?.LicenseExpiryDate ?? default,
                        StatusType = "Problem",
                        RiderIqamaNo = rider?.EmployeeIqamaNo,
                        RiderName = rider?.Employee.NameAR ?? "N/A",
                        RiderNameE = rider?.Employee.NameEN ?? "N/A",
                        Reason = item.LatestProblem?.Reason ?? "Unknown",
                        Since = item.LatestProblem?.Timestamp ?? DateTime.Now,
                        ProblemsCount = item.ProblemsCount,
                        Manufacturer = vehicle?.Manufacturer,
                        ManufactureYear = vehicle?.ManufactureYear ?? 0
                    });
                }
            }

            var stolenVehicles = new List<UnavailableVehicleDto>();
            if (statusFilter == "all" || statusFilter == "stolen")
            {
                var stolenStatuses = await dbcontext.RiderVehicleStatus
                    .Where(s => s.IsActive && s.StatusType == VehicleStatusType.Stolen)
                    .ToListAsync();

                foreach (var status in stolenStatuses)
                {
                    var vehicle = await dbcontext.Vehicles
                        .FirstOrDefaultAsync(v => v.VehicleNumber == status.VehicleNumber);

                    var rider = status.EmployeeIqamaNo.HasValue
                        ? await dbcontext.RiderDetails
                            .Include(r => r.Employee)
                            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == status.EmployeeIqamaNo.Value)
                        : null;

                    stolenVehicles.Add(new UnavailableVehicleDto
                    {
                        VehicleNumber = vehicle?.VehicleNumber ?? status.VehicleNumber,
                        VehicleType = vehicle?.VehicleType ?? "Unknown",
                        SerialNumber = vehicle?.SerialNumber ?? 0,
                        PlateNumberA = vehicle?.PlateNumberA ?? string.Empty,
                        PlateNumberE = vehicle?.PlateNumberE ?? string.Empty,
                        OwnerId = vehicle?.OwnerId ?? 0,
                        OwnerName = vehicle?.OwnerName ?? string.Empty,
                        LicenseExpiryDate = vehicle?.LicenseExpiryDate ?? default,
                        StatusType = "Stolen",
                        RiderIqamaNo = rider?.EmployeeIqamaNo,
                        RiderName = rider?.Employee.NameAR ?? "Unknown",
                        RiderNameE = rider?.Employee.NameEN ?? "Unknown",
                        Reason = status.Reason,
                        Since = status.Timestamp,
                        Location = vehicle.Location ?? "unknown", // ADDED
                        ProblemsCount = 0,
                        Manufacturer = vehicle?.Manufacturer,
                        ManufactureYear = vehicle?.ManufactureYear ?? 0

                    });
                }
            }

            var breakupVehicles = new List<UnavailableVehicleDto>();
            if (statusFilter == "all" || statusFilter == "breakup")
            {
                var breakupStatuses = await dbcontext.RiderVehicleStatus
                    .Where(s => s.IsActive && s.StatusType == VehicleStatusType.BreakUp)
                    .ToListAsync();

                foreach (var status in breakupStatuses)
                {
                    var vehicle = await dbcontext.Vehicles
                        .FirstOrDefaultAsync(v => v.VehicleNumber == status.VehicleNumber);

                    breakupVehicles.Add(new UnavailableVehicleDto
                    {
                        VehicleNumber = vehicle?.VehicleNumber ?? status.VehicleNumber,
                        VehicleType = vehicle?.VehicleType ?? "Unknown",
                        SerialNumber = vehicle?.SerialNumber ?? 0,
                        PlateNumberA = vehicle?.PlateNumberA ?? string.Empty,
                        PlateNumberE = vehicle?.PlateNumberE ?? string.Empty,
                        OwnerId = vehicle?.OwnerId ?? 0,
                        OwnerName = vehicle?.OwnerName ?? string.Empty,
                        LicenseExpiryDate = vehicle?.LicenseExpiryDate ?? default,
                        StatusType = "BreakUp",
                        RiderIqamaNo = null,
                        RiderName = "N/A",
                        RiderNameE = "N/A",
                        Reason = status.Reason,
                        Since = status.Timestamp,
                        Location = vehicle.Location ?? "unknown", // ADDED
                        ProblemsCount = 0,
                        Manufacturer = vehicle?.Manufacturer,
                        ManufactureYear = vehicle?.ManufactureYear ?? 0
                    });
                }
            }

            var allUnavailable = takenVehicles
                .Concat(problemVehicles)
                .Concat(stolenVehicles)
                .Concat(breakupVehicles)
                .OrderBy(v => v.VehicleNumber)
                .ToList();

            var totalVehicles = await dbcontext.Vehicles.CountAsync();
            var availableCount = totalVehicles - allUnavailable.Count;

            var response = new UnavailableVehiclesResponse
            {
                TotalCount = allUnavailable.Count,
                AvailableCount = availableCount,
                TakenCount = takenVehicles.Count,
                ProblemCount = problemVehicles.Count,
                StolenCount = stolenVehicles.Count,
                BreakUpCount = breakupVehicles.Count,
                Filter = statusFilter,
                Vehicles = allUnavailable
            };

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<UnavailableVehiclesResponse>(
                new Error("GetUnavailableError",
                    $"Failed to retrieve unavailable vehicles: {ex.Message}", 500));
        }
    }
    public async Task<Result> ReportVehicleStolenAsync(string PlateNumberA, int? reportedByIqamaNo, string? reason)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            var vehicle = await dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.PlateNumberA == PlateNumberA);

            if (vehicle == null)
                return Result.Failure(new Error("NoVehicle", "Vehicle not found", 404));


            var vehicleNumber = await dbcontext.Vehicles
                .Where(v => v.PlateNumberA == PlateNumberA)
                .Select(v => v.VehicleNumber)
                .FirstOrDefaultAsync();

            var alreadyStolen = await dbcontext.RiderVehicleStatus
                .AnyAsync(s => s.VehicleNumber == vehicleNumber &&
                              s.IsActive &&
                              s.StatusType == VehicleStatusType.Stolen);

            if (alreadyStolen)
                return Result.Failure(new Error("AlreadyStolen",
                    "Vehicle is already reported as stolen", 400));

            var activeTakenStatus = await dbcontext.RiderVehicleStatus
                .FirstOrDefaultAsync(s => s.VehicleNumber == vehicleNumber &&
                                         s.IsActive &&
                                         s.StatusType == VehicleStatusType.Taken);

            if (activeTakenStatus != null)
            {
                activeTakenStatus.IsActive = false;

                var rider = await dbcontext.RiderDetails
                    .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == activeTakenStatus.EmployeeIqamaNo);
                if (rider != null)
                {
                    rider.VehicleNumber = null;
                }
            }

            var activeProblems = await dbcontext.RiderVehicleStatus
                .Where(s => s.VehicleNumber == vehicleNumber &&
                           s.IsActive &&
                           s.StatusType == VehicleStatusType.Problem)
                .ToListAsync();

            foreach (var problem in activeProblems)
            {
                problem.IsActive = false;
            }

            dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                EmployeeIqamaNo = reportedByIqamaNo ?? null,
                VehicleNumber = vehicleNumber,
                StatusType = VehicleStatusType.Stolen,
                Reason = reason ?? "justStolen",
                IsActive = true
            });

            await dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(new Error("ReportStolenError",
                $"Failed to report vehicle as stolen: {ex.Message}", 500));
        }
    }
    public async Task<Result> MarkVehicleAsBreakUpAsync(string PlateNumberA, string reason)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            var vehicle = await dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.PlateNumberA == PlateNumberA);

            if (vehicle == null)
                return Result.Failure(new Error("NoVehicle", "Vehicle not found", 404));

            var vehicleNumber = await dbcontext.Vehicles
                .Where(v => v.PlateNumberA == PlateNumberA)
                .Select(v => v.VehicleNumber)
                .FirstOrDefaultAsync();

            var alreadyBreakUp = await dbcontext.RiderVehicleStatus
                .AnyAsync(s => s.VehicleNumber == vehicleNumber &&
                              s.IsActive &&
                              s.StatusType == VehicleStatusType.BreakUp);

            if (alreadyBreakUp)
                return Result.Failure(new Error("AlreadyBreakUp",
                    "Vehicle is already marked as broken up", 400));

            var activeStatuses = await dbcontext.RiderVehicleStatus
                .Where(s => s.VehicleNumber == vehicleNumber &&
                           s.IsActive &&
                           (s.StatusType == VehicleStatusType.Taken ||
                            s.StatusType == VehicleStatusType.Problem ||
                            s.StatusType == VehicleStatusType.Stolen))
                .ToListAsync();

            foreach (var status in activeStatuses)
            {
                status.IsActive = false;
            }

            var rider = await dbcontext.RiderDetails
                .FirstOrDefaultAsync(r => r.VehicleNumber == vehicleNumber);
            if (rider != null)
            {
                rider.VehicleNumber = null;
            }

            dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                EmployeeIqamaNo = null, // Admin action
                VehicleNumber = vehicleNumber,
                StatusType = VehicleStatusType.BreakUp,
                Reason = reason,
                IsActive = true
            });

            await dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(new Error("MarkBreakUpError",
                $"Failed to mark vehicle as break up: {ex.Message}", 500));
        }
    }
    public async Task<Result> RecoverStolenVehicleAsync(string PlateNumberA, string recoveryDetails)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            var vehicle = await dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.PlateNumberA == PlateNumberA);

            if (vehicle == null)
                return Result.Failure(new Error("NoVehicle found", "Vehicle not found", 404));

            var vehicleNumber = vehicle.VehicleNumber;

            var stolenStatus = await dbcontext.RiderVehicleStatus
                .FirstOrDefaultAsync(s => s.VehicleNumber == vehicleNumber &&
                                         s.IsActive &&
                                         s.StatusType == VehicleStatusType.Stolen);

            if (stolenStatus == null)
                return Result.Failure(new Error("NotStolen",
                    "Vehicle is not currently marked as stolen", 400));

            stolenStatus.IsActive = false;

            dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                EmployeeIqamaNo = null,
                VehicleNumber = vehicleNumber,
                StatusType = VehicleStatusType.Returned,
                Reason = $"Recovered from stolen: {recoveryDetails}",
                IsActive = false
            });

            await dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(new Error("RecoverStolenError",
                $"Failed to recover stolen vehicle: {ex.Message}", 500));
        }
    }
    public async Task<Result<GroupedVehicleStatusResponse>> GetVehiclesGroupedByStatusAsync()
    {

        try
        {
            var vehicles = await dbcontext.Vehicles
                .ToListAsync();

            var activeStatuses = await dbcontext.RiderVehicleStatus
                .Where(s => s.IsActive)
                .ToListAsync();

            var riders = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                .ToListAsync();

            var groups = new Dictionary<string, List<VehicleWithRiderDto>>
        {
            { "Available", new List<VehicleWithRiderDto>() },
            { "Taken", new List<VehicleWithRiderDto>() },
            { "Problem", new List<VehicleWithRiderDto>() },
            { "Stolen", new List<VehicleWithRiderDto>() },
            { "BreakUp", new List<VehicleWithRiderDto>() }
        };

            foreach (var vehicle in vehicles)
            {
                var status = activeStatuses
                    .Where(s => s.VehicleNumber == vehicle.VehicleNumber)
                    .ToList();

                var vehicleStatus = status.FirstOrDefault()?.StatusType;
                var statusSince = status.Any()
                    ? status.OrderBy(s => s.Timestamp).First().Timestamp
                    : (DateTime?)null;

                var dto = new VehicleWithRiderDto
                {
                    VehicleNumber = vehicle.VehicleNumber,
                    VehicleType = vehicle.VehicleType,
                    PlateNumberA = vehicle.PlateNumberA,
                    PlateNumberE = vehicle.PlateNumberE,
                    SerialNumber = vehicle.SerialNumber,
                    Manufacturer = vehicle.Manufacturer,
                    ManufactureYear = vehicle.ManufactureYear,
                    OwnerName = vehicle.OwnerName,
                    Location = vehicle.Location, // ADDED
                    OwnerId = vehicle.OwnerId,
                    LicenseExpiryDate = vehicle.LicenseExpiryDate,
                    CurrentStatus = vehicleStatus?.ToString() ?? "Available",
                    StatusSince = statusSince,
                    ActiveProblemsCount = status.Count(s => s.StatusType == VehicleStatusType.Problem),
                    HasActiveProblem = status.Any(s => s.StatusType == VehicleStatusType.Problem),
                    IsBreakUp = status.Any(s => s.StatusType == VehicleStatusType.BreakUp),
                    IsStolen = status.Any(s => s.StatusType == VehicleStatusType.Stolen),
                    IsAvailable = !status.Any()
                };

                var taken = status.FirstOrDefault(s => s.StatusType == VehicleStatusType.Taken);
                if (taken != null)
                {
                    var rider = riders.FirstOrDefault(r => r.EmployeeIqamaNo == taken.EmployeeIqamaNo);
                    if (rider != null)
                    {
                        dto.CurrentRider = new RiderInfoDto
                        {
                            EmployeeIqamaNo = rider.EmployeeIqamaNo,
                            RiderName = rider.Employee.NameAR,
                            RiderNameE = rider.Employee.NameEN,
                            TakenDate = taken.Timestamp,
                            TakenReason = taken.Reason ?? "no reason"
                        };
                    }
                }

                string groupKey = vehicleStatus switch
                {
                    VehicleStatusType.Taken => "Taken",
                    VehicleStatusType.Problem => "Problem",
                    VehicleStatusType.Stolen => "Stolen",
                    VehicleStatusType.BreakUp => "BreakUp",
                    _ => "Available"
                };

                groups[groupKey].Add(dto);
            }

            var response = new GroupedVehicleStatusResponse
            {
                TotalVehicles = vehicles.Count,
                GeneratedAt = DateTime.Now,
                Groups = groups.Select(g => new VehicleStatusGroupDto
                {
                    Status = g.Key,
                    Count = g.Value.Count,
                    Vehicles = g.Value.OrderBy(v => v.VehicleNumber).ToList()
                }).ToList(),
                Summary = new VehicleStatusSummary
                {
                    AvailableCount = groups["Available"].Count,
                    TakenCount = groups["Taken"].Count,
                    ProblemCount = groups["Problem"].Count,
                    StolenCount = groups["Stolen"].Count,
                    BreakUpCount = groups["BreakUp"].Count
                }
            };

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<GroupedVehicleStatusResponse>(
                new Error("GroupedVehicleStatusError",
                $"Failed to group vehicles by status: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<VehicleResponse>>> Getplate(string PlateNumberA)
    {
        var companies = await dbcontext.Vehicles.Where(c => c.PlateNumberA.StartsWith(PlateNumberA)).AsNoTracking().ToListAsync();

        if (companies == null)
            return Result.Failure<IEnumerable<VehicleResponse>>(new Error("vehicle.NotFound", $"vehicle starts with name {PlateNumberA} was not found.", 404));

        var companyResponses = companies.Adapt<IEnumerable<VehicleResponse>>();

        return Result.Success(companyResponses); ;
    }

    public async Task<Result<IEnumerable<VehicleResponse>>> GetSerial(int Serial)
    {
        var companies = await dbcontext.Vehicles.Where(c => c.SerialNumber.ToString().StartsWith(Serial.ToString())).AsNoTracking().ToListAsync();

        if (companies == null)
            return Result.Failure<IEnumerable<VehicleResponse>>(new Error("vehicle.NotFound", $"vehicle starts with name {Serial} was not found.", 404));

        var companyResponses = companies.Adapt<IEnumerable<VehicleResponse>>();

        return Result.Success(companyResponses); ;
    }
}

