using Application.Abstraction;
using Application.Contracts.Employees;
using Domain;
using Domain.Entities;
using Mapster;
using Microsoft.EntityFrameworkCore;
using static Application.Service.Empolyee.IVehicleService;
using static Application.Service.Member.IMemberService;

namespace Application.Service.Empolyee;

public class VehicleService(ApplicationDbcontext dbcontext) : IVehicleService
{
    private readonly ApplicationDbcontext dbcontext = dbcontext;

    #region Basic CRUD Operations

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

        return Result.Success(companyResponses);
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
        companies.Location = Request.Location;
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

    public async Task<Result<IEnumerable<VehicleResponse>>> Getplate(string PlateNumberA)
    {
        var companies = await dbcontext.Vehicles.Where(c => c.PlateNumberA.StartsWith(PlateNumberA)).AsNoTracking().ToListAsync();

        if (companies == null)
            return Result.Failure<IEnumerable<VehicleResponse>>(new Error("vehicle.NotFound", $"vehicle starts with name {PlateNumberA} was not found.", 404));

        var companyResponses = companies.Adapt<IEnumerable<VehicleResponse>>();

        return Result.Success(companyResponses);
    }

    public async Task<Result<IEnumerable<VehicleResponse>>> GetSerial(int Serial)
    {
        var companies = await dbcontext.Vehicles.Where(c => c.SerialNumber.ToString().StartsWith(Serial.ToString())).AsNoTracking().ToListAsync();

        if (companies == null)
            return Result.Failure<IEnumerable<VehicleResponse>>(new Error("vehicle.NotFound", $"vehicle starts with name {Serial} was not found.", 404));

        var companyResponses = companies.Adapt<IEnumerable<VehicleResponse>>();

        return Result.Success(companyResponses);
    }

    #endregion

    #region Direct Vehicle Operations (Admin with Permission)

    public async Task<Result> SwitchVehicleAsync(
    long IqamaNo,
    string newVehiclePlateNumber,
    string reason,
    string permission,
    DateTime permissionEndDate)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            // 1. Get rider details
            var rider = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(x => x.EmployeeIqamaNo == IqamaNo);

            if (rider == null)
                return Result.Failure(new Error("NoRider", "No rider found with this Iqama", 404));

            if (rider.Employee.Status != "enable")
                return Result.Failure(new Error("RiderDisabled", "Rider is disabled and cannot take a vehicle", 403));

            // 2. Check if rider has a current vehicle
            if (string.IsNullOrEmpty(rider.VehicleNumber))
                return Result.Failure(new Error("NoCurrentVehicle",
                    "Rider does not have a current vehicle to switch from. Use TakeVehicle instead.", 400));

            var currentVehicleNumber = rider.VehicleNumber;

            // 3. Get current vehicle's active status
            var currentActiveStatus = await dbcontext.RiderVehicleStatus
                .FirstOrDefaultAsync(s => s.VehicleNumber == currentVehicleNumber &&
                                         s.EmployeeIqamaNo == IqamaNo &&
                                         s.IsActive &&
                                         s.StatusType == VehicleStatusType.Taken);

            if (currentActiveStatus == null)
                return Result.Failure(new Error("NoActiveStatus",
                    "No active vehicle assignment found for current vehicle", 400));

            // 4. Get the new vehicle
            var newVehicle = await dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.PlateNumberA == newVehiclePlateNumber);

            if (newVehicle == null)
                return Result.Failure(new Error("NoVehicle", "New vehicle not found", 404));

            // 5. Validate new vehicle availability
            var availabilityCheck = await ValidateVehicleAvailability(newVehicle.VehicleNumber, newVehiclePlateNumber);
            if (!availabilityCheck.IsSuccess)
                return availabilityCheck;

            // 6. Check if trying to switch to the same vehicle
            if (currentVehicleNumber == newVehicle.VehicleNumber)
                return Result.Failure(new Error("SameVehicle",
                    "Cannot switch to the same vehicle. Rider already has this vehicle.", 400));

            // === STEP 1: Return current vehicle ===

            // End permission for current vehicle
            await EndPermission(currentActiveStatus);

            // Create return status for current vehicle
            dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                EmployeeIqamaNo = IqamaNo,
                VehicleNumber = currentVehicleNumber,
                StatusType = VehicleStatusType.Returned,
                Reason = $"Vehicle switch: {reason}",
                IsActive = false,
                Permission = currentActiveStatus.Permission,
                PermissionStartDate = currentActiveStatus.PermissionStartDate,
                PermissionEndDate = DateTime.UtcNow.AddHours(3)
            });

            // === STEP 2: Take new vehicle ===

            // Update rider's vehicle assignment
            rider.VehicleNumber = newVehicle.VehicleNumber;

            // Create taken status for new vehicle
            dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                EmployeeIqamaNo = IqamaNo,
                VehicleNumber = newVehicle.VehicleNumber,
                StatusType = VehicleStatusType.Taken,
                Reason = $"Vehicle switch: {reason}",
                IsActive = true,
                Permission = permission,
                PermissionStartDate = DateTime.UtcNow.AddHours(3),
                PermissionEndDate = permissionEndDate
            });

            await dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(new Error("SwitchVehicleError",
                $"Failed to switch vehicle: {ex.Message}", 500));
        }
    }

    //public async Task<Result<IEnumerable<TempVehicleOperationResponse>>> GetPendingOperationsAsync()
    //{
    //    try
    //    {
    //        // Get pending operations that are NOT switch operations
    //        // Switch operations have both VehicleNumber and VehiclePlateNumber set
    //        var pendingOperations = await dbcontext.TempVehicleOperations
    //            .Where(t => !t.IsResolved
    //                && (string.IsNullOrEmpty(t.VehicleNumber)
    //                    || string.IsNullOrEmpty(t.VehiclePlateNumber)))  // Exclude switch operations
    //            .Include(t => t.Rider)
    //                .ThenInclude(r => r.Employee)
    //            .Include(t => t.Vehicle)
    //            .OrderBy(t => t.RequestedAt)
    //            .ToListAsync();

    //        var responses = new List<TempVehicleOperationResponse>();

    //        foreach (var operation in pendingOperations)
    //        {
    //            var validation = await ValidateOperation(operation);
    //            responses.Add(MapToResponse(operation, validation));
    //        }

    //        return Result.Success<IEnumerable<TempVehicleOperationResponse>>(responses);
    //    }
    //    catch (Exception ex)
    //    {
    //        return Result.Failure<IEnumerable<TempVehicleOperationResponse>>(
    //            new Error("GetPendingError", $"Failed to get pending operations: {ex.Message}", 500));
    //    }
    //}

    public async Task<Result<List<PendingSwitchVehicleAdminResponse>>> GetAllPendingSwitchOperationsAsync()
    {
        try
        {
            var pendingSwitches = await dbcontext.TempVehicleOperations
                .Include(t => t.Rider)
                    .ThenInclude(r => r.Employee)
                        .ThenInclude(e => e.Housing)
                .Where(t => !t.IsResolved
                    && !string.IsNullOrEmpty(t.VehicleNumber)
                    && !string.IsNullOrEmpty(t.VehiclePlateNumber)
                    && t.VehicleStatusType == VehicleStatusType.Taken)
                .OrderByDescending(t => t.RequestedAt)
                .ToListAsync();

            var responses = new List<PendingSwitchVehicleAdminResponse>();

            foreach (var operation in pendingSwitches)
            {
                var currentVehicle = await dbcontext.Vehicles
                    .FirstOrDefaultAsync(v => v.VehicleNumber == operation.VehicleNumber);

                var newVehicle = await dbcontext.Vehicles
                    .FirstOrDefaultAsync(v => v.PlateNumberA == operation.VehiclePlateNumber);

                if (currentVehicle == null || newVehicle == null)
                    continue;

                var validation = await ValidateSwitchOperation(
                    operation.RiderIqamaNo ?? 2536361732,
                    operation.VehicleNumber,
                    newVehicle.VehicleNumber);

                responses.Add(new PendingSwitchVehicleAdminResponse(
                    operation.Id,
                    operation.RiderIqamaNo ?? 2536361732,
                    operation.Rider?.Employee?.NameAR ?? "Unknown",
                    operation.Rider?.Employee?.NameEN ?? "Unknown",
                    operation.Rider?.Employee?.Housing?.Name ?? "Unknown",
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
        catch (Exception ex)
        {
            return Result.Failure<List<PendingSwitchVehicleAdminResponse>>(
                new Error("GetPendingSwitchesError",
                    $"Failed to get pending switch operations: {ex.Message}", 500));
        }
    }

    private async Task<VehicleSwitchValidation> ValidateSwitchOperation(
       long riderIqamaNo,
       string currentVehicleNumber,
       string newVehicleNumber)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Check if rider still has the current vehicle
        var rider = await dbcontext.RiderDetails
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
        var currentVehicleActive = await dbcontext.RiderVehicleStatus
            .AnyAsync(s => s.VehicleNumber == currentVehicleNumber
                && s.EmployeeIqamaNo == riderIqamaNo
                && s.IsActive
                && s.StatusType == VehicleStatusType.Taken);

        if (!currentVehicleActive)
        {
            warnings.Add("No active 'Taken' status found for current vehicle");
        }

        // Check if new vehicle is still available
        var newVehicleUnavailable = await dbcontext.RiderVehicleStatus
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

    public record PendingSwitchVehicleAdminResponse(
       int Id,
       long RiderIqamaNo,
       string RiderNameAR,
       string RiderNameEN,
       string HousingName,
       string CurrentVehicleNumber,
       string CurrentVehiclePlate,
       string NewVehicleNumber,
       string NewVehiclePlate,
       string Reason,
       DateTime RequestedAt,
       string RequestedBy,
       VehicleSwitchValidation Validation
   );

    public async Task<Result<VehicleLocationSyncResponse>> SyncAllVehicleLocationsAsync()
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            int assignedVehiclesUpdated = 0;
            int unassignedVehiclesUpdated = 0;
            int alreadyCorrect = 0;
            var errors = new List<string>();

            // Get all vehicles
            var allVehicles = await dbcontext.Vehicles.ToListAsync();

            foreach (var vehicle in allVehicles)
            {
                try
                {
                    // Check if vehicle is assigned to a rider
                    var rider = await dbcontext.RiderDetails
                        .Include(r => r.Employee)
                            .ThenInclude(e => e.Housing)
                        .FirstOrDefaultAsync(r => r.VehicleNumber == vehicle.VehicleNumber);

                    if (rider != null && rider.Employee.Housing != null)
                    {
                        // Vehicle is assigned to a rider with housing
                        var housingName = rider.Employee.Housing.Name;

                        if (vehicle.Location != housingName)
                        {
                            vehicle.Location = housingName;
                            assignedVehiclesUpdated++;
                        }
                        else
                        {
                            alreadyCorrect++;
                        }
                    }
                    else
                    {
                        // Vehicle is not assigned or rider has no housing - set to company
                        const string companyLocation = "الشركة";

                        if (vehicle.Location != companyLocation)
                        {
                            vehicle.Location = companyLocation;
                            unassignedVehiclesUpdated++;
                        }
                        else
                        {
                            alreadyCorrect++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Error updating vehicle {vehicle.VehicleNumber}: {ex.Message}");
                }
            }

            await dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            var response = new VehicleLocationSyncResponse
            {
                TotalVehicles = allVehicles.Count,
                AssignedVehiclesUpdated = assignedVehiclesUpdated,
                UnassignedVehiclesUpdated = unassignedVehiclesUpdated,
                AlreadyCorrect = alreadyCorrect,
                Errors = errors,
                Success = errors.Count == 0,
                Message = errors.Count == 0
                    ? "All vehicle locations synchronized successfully"
                    : $"Synchronized with {errors.Count} errors"
            };

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<VehicleLocationSyncResponse>(
                new Error("SyncLocationError",
                    $"Failed to sync vehicle locations: {ex.Message}", 500));
        }
    }

    public async Task<Result> TakeVehicleAsync(long IqamaNo, string PlateNumberA, string reason, string permission, DateTime permissionEndDate)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            var rider = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(x => x.EmployeeIqamaNo == IqamaNo);

            if (rider == null)
                return Result.Failure(new Error("NoRider", "No rider found with this Iqama", 400));

            if (rider.Employee.Status != "enable")
                return Result.Failure(new Error("RiderDisabled", "Rider is disabled and cannot take a vehicle", 403));

            if (!string.IsNullOrEmpty(rider.VehicleNumber))
                return Result.Failure(new Error("HasVehicle",
                    $"Rider already has vehicle {rider.VehicleNumber} assigned", 400));

            var vehicle = await dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.PlateNumberA == PlateNumberA);

            if (vehicle == null)
                return Result.Failure(new Error("NoVehicle", "Vehicle not found", 404));

            var availabilityCheck = await ValidateVehicleAvailability(vehicle.VehicleNumber, PlateNumberA);
            if (!availabilityCheck.IsSuccess)
                return availabilityCheck;

            rider.VehicleNumber = vehicle.VehicleNumber;

            dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                EmployeeIqamaNo = IqamaNo,
                VehicleNumber = vehicle.VehicleNumber,
                StatusType = VehicleStatusType.Taken,
                Reason = reason,
                IsActive = true,
                Permission = permission,
                PermissionStartDate = DateTime.UtcNow.AddHours(3),
                PermissionEndDate = permissionEndDate
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

    public async Task<Result> ReturnVehicleAsync(long IqamaNo, string PlateNumberA, string reason)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            var rider = await dbcontext.RiderDetails
                .FirstOrDefaultAsync(x => x.EmployeeIqamaNo == IqamaNo);

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
                                         s.EmployeeIqamaNo == IqamaNo &&
                                         s.IsActive &&
                                         s.StatusType == VehicleStatusType.Taken);

            if (activeStatus == null)
                return Result.Failure(new Error("NoActiveStatus",
                    "No active vehicle assignment found", 400));

            // End permission on return
            await EndPermission(activeStatus);

            rider.VehicleNumber = null;

            // Create return status
            dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                EmployeeIqamaNo = IqamaNo,
                VehicleNumber = VehicleNumber,
                StatusType = VehicleStatusType.Returned,
                Reason = reason,
                IsActive = false,
                Permission = activeStatus.Permission,
                PermissionStartDate = activeStatus.PermissionStartDate,
                PermissionEndDate = DateTime.UtcNow.AddHours(3)
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

    public async Task<Result> ReportProblemAsync(long? IqamaNo, string PlateNumberA, string reason)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            var vehicle = await dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.PlateNumberA == PlateNumberA);

            if (vehicle == null)
                return Result.Failure(new Error("NoVehicle", "Vehicle not found", 404));

            var VehicleNumber = vehicle.VehicleNumber;

            // Check if vehicle is already reported with an active problem
            var existingProblem = await dbcontext.RiderVehicleStatus
                .AnyAsync(s => s.VehicleNumber == VehicleNumber &&
                              s.IsActive &&
                              s.StatusType == VehicleStatusType.Problem);

            if (existingProblem)
                return Result.Failure(new Error("AlreadyReported",
                    "This vehicle already has an active problem reported", 400));

            // Check if vehicle is stolen or broken up
            var isStolen = await dbcontext.RiderVehicleStatus
                .AnyAsync(s => s.VehicleNumber == VehicleNumber &&
                              s.IsActive &&
                              s.StatusType == VehicleStatusType.Stolen);

            if (isStolen)
                return Result.Failure(new Error("VehicleStolen",
                    "Cannot report problem for a stolen vehicle", 400));

            var isBreakUp = await dbcontext.RiderVehicleStatus
                .AnyAsync(s => s.VehicleNumber == VehicleNumber &&
                              s.IsActive &&
                              s.StatusType == VehicleStatusType.BreakUp);

            if (isBreakUp)
                return Result.Failure(new Error("VehicleBreakUp",
                    "Cannot report problem for a broken up vehicle", 400));

            RiderDetails? rider = null;
            RiderVehicleStatus? activeTakenStatus = null;

            // If IqamaNo is provided, check if rider exists and has the vehicle
            if (IqamaNo.HasValue)
            {
                rider = await dbcontext.RiderDetails
                    .FirstOrDefaultAsync(x => x.EmployeeIqamaNo == IqamaNo.Value);

                if (rider == null)
                    return Result.Failure(new Error("NoRider", "No rider found with this Iqama", 404));

                // Check if rider has this vehicle
                activeTakenStatus = await dbcontext.RiderVehicleStatus
                    .FirstOrDefaultAsync(s => s.VehicleNumber == VehicleNumber &&
                                             s.EmployeeIqamaNo == IqamaNo.Value &&
                                             s.IsActive &&
                                             s.StatusType == VehicleStatusType.Taken);

                // If rider has the vehicle, end their permission
                if (activeTakenStatus != null)
                {
                    await EndPermission(activeTakenStatus);
                    rider.VehicleNumber = null;
                }
            }
            else
            {
                // Check if anyone has this vehicle
                activeTakenStatus = await dbcontext.RiderVehicleStatus
                    .FirstOrDefaultAsync(s => s.VehicleNumber == VehicleNumber &&
                                             s.IsActive &&
                                             s.StatusType == VehicleStatusType.Taken);

                if (activeTakenStatus != null)
                {
                    // End the active assignment
                    await EndPermission(activeTakenStatus);

                    // Clear rider assignment
                    var assignedRider = await dbcontext.RiderDetails
                        .FirstOrDefaultAsync(r => r.VehicleNumber == VehicleNumber);
                    if (assignedRider != null)
                    {
                        assignedRider.VehicleNumber = null;
                    }
                }
            }

            dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                EmployeeIqamaNo = IqamaNo,
                VehicleNumber = VehicleNumber,
                StatusType = VehicleStatusType.Problem,
                Reason = reason,
                IsActive = true,
                Permission = activeTakenStatus?.Permission,
                PermissionStartDate = activeTakenStatus?.PermissionStartDate,
                PermissionEndDate = DateTime.UtcNow.AddHours(3)
            });

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

    public async Task<Result> ReportVehicleStolenAsync(string PlateNumberA, long? reportedByIqamaNo, string? reason)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            var vehicle = await dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.PlateNumberA == PlateNumberA);

            if (vehicle == null)
                return Result.Failure(new Error("NoVehicle", "Vehicle not found", 404));

            var vehicleNumber = vehicle.VehicleNumber;

            var alreadyStolen = await dbcontext.RiderVehicleStatus
                .AnyAsync(s => s.VehicleNumber == vehicleNumber &&
                              s.IsActive &&
                              s.StatusType == VehicleStatusType.Stolen);

            if (alreadyStolen)
                return Result.Failure(new Error("AlreadyStolen",
                    "Vehicle is already reported as stolen", 400));

            await EndAllActivePermissionsForVehicle(vehicleNumber);

            dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                EmployeeIqamaNo = reportedByIqamaNo,
                VehicleNumber = vehicleNumber,
                StatusType = VehicleStatusType.Stolen,
                Reason = reason ?? "justStolen",
                IsActive = true,
                PermissionEndDate = DateTime.UtcNow.AddHours(3)
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

            var vehicleNumber = vehicle.VehicleNumber;

            var alreadyBreakUp = await dbcontext.RiderVehicleStatus
                .AnyAsync(s => s.VehicleNumber == vehicleNumber &&
                              s.IsActive &&
                              s.StatusType == VehicleStatusType.BreakUp);

            if (alreadyBreakUp)
                return Result.Failure(new Error("AlreadyBreakUp",
                    "Vehicle is already marked as broken up", 400));

            await EndAllActivePermissionsForVehicle(vehicleNumber);

            dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                EmployeeIqamaNo = null,
                VehicleNumber = vehicleNumber,
                StatusType = VehicleStatusType.BreakUp,
                Reason = reason,
                IsActive = true,
                PermissionEndDate = DateTime.UtcNow.AddHours(3) // End permission immediately
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

    #endregion

    #region Request-Based Operations (Member Requests)

    public async Task<Result> RequestTakeVehicleAsync(SVehicleResolutionRequest request, string UserId, string reason = "work")
    {
        try
        {
            var userName = await dbcontext.Users
                .Where(u => u.Id == UserId)
                .Select(u => u.UserName)
                .FirstOrDefaultAsync();


            var rider = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == request.RiderIqamaNo);

            if (rider is null)
                return Result.Failure(new Error("NoRider found", "Rider not found", 404));

            if (rider.Employee.Status != "enable")
                return Result.Failure(new Error("RiderDisabled", "Rider is disabled and cannot take a vehicle", 403));

            var vehicle = await dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.PlateNumberA == request.Plate);

            if (vehicle == null)
                return Result.Failure(new Error("NoVehicle found", "Vehicle not found", 404));

            var validation = await ValidateTakeOperation(request.RiderIqamaNo, vehicle.VehicleNumber);

            if (!validation.IsValid)
                return Result.Failure(new Error("ValidationFailed",
                    string.Join(", ", validation.Errors), 400));

            var existingRequest = await dbcontext.TempVehicleOperations
                .AnyAsync(t => t.RiderIqamaNo == request.RiderIqamaNo &&
                              !t.IsResolved &&
                              t.VehicleStatusType == VehicleStatusType.Taken);

            if (existingRequest)
                return Result.Failure(new Error("PendingRequest",
                    "There is already a pending take vehicle request for this rider", 400));

            var operation = new TempVehicleOperation
            {
                RiderIqamaNo = request.RiderIqamaNo,
                VehiclePlateNumber = request.Plate,
                VehicleNumber = vehicle.VehicleNumber,
                VehicleStatusType = VehicleStatusType.Taken,
                Reason = reason,
                RequestedAt = DateTime.UtcNow.AddHours(3),
                RequestedBy = userName!,
                IsResolved = false
            };

            await dbcontext.TempVehicleOperations.AddAsync(operation);
            await dbcontext.SaveChangesAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(
                new Error("RequestError", $"Failed to request take vehicle: {ex.Message}", 500));
        }
    }

    public async Task<Result> RequestReturnVehicleAsync(SVehicleResolutionRequest request, string UserId, string reason = "leave the work")
    {
        try
        {
            var userName = await dbcontext.Users
                .Where(u => u.Id == UserId)
                .Select(u => u.UserName)
                .FirstOrDefaultAsync();

            var rider = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == request.RiderIqamaNo);

            if (rider is null)
                return Result.Failure(new Error("NoRider", "Rider not found", 404));

            var vehicle = await dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.PlateNumberA == request.Plate);

            if (vehicle == null)
                return Result.Failure(new Error("NoVehicle", "Vehicle not found", 404));

            var validation = await ValidateReturnOperation(request.RiderIqamaNo, vehicle.VehicleNumber);
            if (!validation.IsValid)
                return Result.Failure(new Error("ValidationFailed",
                    string.Join(", ", validation.Errors), 400));

            var existingRequest = await dbcontext.TempVehicleOperations
                .AnyAsync(t => t.RiderIqamaNo == request.RiderIqamaNo &&
                              !t.IsResolved &&
                              t.VehicleStatusType == VehicleStatusType.Returned);

            if (existingRequest)
                return Result.Failure(new Error("PendingRequest",
                    "There is already a pending return vehicle request for this rider", 400));

            var operation = new TempVehicleOperation
            {
                RiderIqamaNo = request.RiderIqamaNo,
                VehiclePlateNumber = request.Plate,
                VehicleNumber = vehicle.VehicleNumber,
                VehicleStatusType = VehicleStatusType.Returned,
                Reason = reason,
                RequestedAt = DateTime.UtcNow.AddHours(3),
                RequestedBy = userName!,
                IsResolved = false
            };

            await dbcontext.TempVehicleOperations.AddAsync(operation);
            await dbcontext.SaveChangesAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(
                new Error("RequestError", $"Failed to request return vehicle: {ex.Message}", 500));
        }
    }

    public async Task<Result> RequestReportProblemAsync(SVehicleResolutionRequest request, string UserId, string reason = "problem at vehicle")
    {
        try
        {
            var userName = await dbcontext.Users
                .Where(u => u.Id == UserId)
                .Select(u => u.UserName)
                .FirstOrDefaultAsync();

            var rider = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == request.RiderIqamaNo);

            if (rider == null)
                return Result.Failure(new Error("NoRider", "Rider not found", 404));

            var vehicle = await dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.PlateNumberA == request.Plate);

            if (vehicle == null)
                return Result.Failure(new Error("NoVehicle", "Vehicle not found", 404));

            var validation = await ValidateReportProblemOperation(request.RiderIqamaNo, vehicle.VehicleNumber);
            if (!validation.IsValid)
                return Result.Failure(new Error("ValidationFailed",
                    string.Join(", ", validation.Errors), 400));

            var operation = new TempVehicleOperation
            {
                RiderIqamaNo = request.RiderIqamaNo,
                VehiclePlateNumber = request.Plate,
                VehicleNumber = vehicle.VehicleNumber,
                VehicleStatusType = VehicleStatusType.Problem,
                Reason = reason,
                RequestedAt = DateTime.UtcNow.AddHours(3),
                RequestedBy = userName!,
                IsResolved = false
            };

            await dbcontext.TempVehicleOperations.AddAsync(operation);
            await dbcontext.SaveChangesAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(
                new Error("RequestError", $"Failed to request report problem: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<TempVehicleOperationResponse>>> GetPendingOperationsAsync()
    {
        try
        {
            var pendingOperations = await dbcontext.TempVehicleOperations
                .Where(t => !t.IsResolved)
                .Include(t => t.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(t => t.Vehicle)
                .OrderBy(t => t.RequestedAt)
                .ToListAsync();

            var responses = new List<TempVehicleOperationResponse>();

            foreach (var operation in pendingOperations)
            {
                var validation = await ValidateOperation(operation);
                responses.Add(MapToResponse(operation, validation));
            }

            return Result.Success<IEnumerable<TempVehicleOperationResponse>>(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<TempVehicleOperationResponse>>(
                new Error("GetPendingError", $"Failed to get pending operations: {ex.Message}", 500));
        }
    }

    public async Task<Result> ResolveOperationAsync(VehicleResolutionRequest request)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            if (request.Resolution != "Approved" && request.Resolution != "Rejected")
                return Result.Failure(
                    new Error("InvalidResolution", "Resolution must be 'Approved' or 'Rejected'", 400));


            var vehicle = await dbcontext.Vehicles

                .FirstOrDefaultAsync(v => v.PlateNumberA == request.Plate)
                ;
            if (vehicle == null)
                return Result.Failure(
                    new Error("NoVehicle", "Vehicle not found", 404));

            var operation = await dbcontext.TempVehicleOperations
                .Where(t => t.VehicleNumber == vehicle.VehicleNumber && !t.IsResolved)
                .Include(t => t.Rider)
                    .ThenInclude(r => r.Employee)
                .Include(t => t.Vehicle)
                .SingleOrDefaultAsync();

            if (operation is null)
                return Result.Failure(
                    new Error("NoOperation", "No pending operation found for this rider", 404));

            try
            {
                if (request.Resolution == "Approved")
                {
                    if (operation.VehicleStatusType == VehicleStatusType.Taken)
                    {
                        if (string.IsNullOrWhiteSpace(request.Permission))
                        {
                            return Result.Failure(
                                new Error("PermissionRequired",
                                    "Permission and PermissionEndDate are required when approving Take requests", 400));
                        }

                        operation.Permission = request.Permission;
                        operation.PermissionEndDate = request.PermissionEndDate;
                    }

                    var executeResult = await ExecuteOperation(operation);

                    if (!executeResult.IsSuccess)
                    {
                        return Result.Failure(
                            new Error("ExecutionFailed", $"Failed to execute {operation.VehiclePlateNumber}: {executeResult.Error.Description}", 400));
                    }
                }

                operation.IsResolved = true;
                operation.Resolution = request.Resolution;
                operation.ResolvedBy = request.ResolvedBy;
                operation.ResolvedAt = DateTime.UtcNow.AddHours(3);
                operation.AdminNotes = request.Note;
            }
            catch (Exception ex)
            {
                return Result.Failure(
                    new Error("OperationError", $"Error processing operation: {ex.Message}", 500));
            }

            await dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(
                new Error("ResolveError", $"Failed to resolve operation: {ex.Message}", 500));
        }
    }
    public async Task<Result> ResolveSwitchOperationAsync(VehicleSwitchResolutionRequest request)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            if (request.Resolution != "Approved" && request.Resolution != "Rejected")
                return Result.Failure(
                    new Error("InvalidResolution", "Resolution must be 'Approved' or 'Rejected'", 400));

            // Find the switch operation
            var operation = await dbcontext.TempVehicleOperations
                .Include(t => t.Rider)
                    .ThenInclude(r => r.Employee)
                .FirstOrDefaultAsync(t => t.Id == request.OperationId && !t.IsResolved);

            if (operation == null)
                return Result.Failure(
                    new Error("NoOperation", "No pending operation found with this ID", 404));

            // Verify this is a switch operation (both VehicleNumber and VehiclePlateNumber are set)
            if (string.IsNullOrEmpty(operation.VehicleNumber) ||
                string.IsNullOrEmpty(operation.VehiclePlateNumber))
                return Result.Failure(
                    new Error("NotSwitchOperation", "This is not a switch operation", 400));

            var currentVehicleNumber = operation.VehicleNumber;
            var newVehiclePlate = operation.VehiclePlateNumber;

            // Get the new vehicle details
            var newVehicle = await dbcontext.Vehicles
                .FirstOrDefaultAsync(v => v.PlateNumberA == newVehiclePlate);

            if (newVehicle == null)
                return Result.Failure(
                    new Error("NewVehicleNotFound", "New vehicle not found", 404));

            if (request.Resolution == "Approved")
            {
                // Validate permission data
                if (string.IsNullOrWhiteSpace(request.Permission) || !request.PermissionEndDate.HasValue)
                {
                    return Result.Failure(
                        new Error("PermissionRequired",
                            "Permission and PermissionEndDate are required when approving switch requests", 400));
                }

                // Execute the switch
                var executeResult = await ExecuteSwitchOperation(
                    operation,
                    newVehicle.VehicleNumber,
                    request.Permission,
                    request.PermissionEndDate.Value);

                if (!executeResult.IsSuccess)
                {
                    return Result.Failure(
                        new Error("ExecutionFailed",
                            $"Failed to execute switch: {executeResult.Error.Description}", 400));
                }

                // Store permission data in the operation record
                operation.Permission = request.Permission;
                operation.PermissionEndDate = request.PermissionEndDate.Value;
            }

            // Mark as resolved
            operation.IsResolved = true;
            operation.Resolution = request.Resolution;
            operation.ResolvedBy = request.ResolvedBy;
            operation.ResolvedAt = DateTime.UtcNow.AddHours(3);
            operation.AdminNotes = request.Note;

            await dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(
                new Error("ResolveSwitchError", $"Failed to resolve switch operation: {ex.Message}", 500));
        }
    }

    #endregion

    #region Query Operations

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

    public async Task<Result<IEnumerable<Vehicle>>> GetStolenVehiclesAsync()
    {
        try
        {
            var unavailableVehicles = await dbcontext.RiderVehicleStatus
                .Where(s => s.IsActive && s.StatusType == VehicleStatusType.Stolen)
                .Select(s => s.VehicleNumber)
                .Distinct()
                .ToListAsync();

            var availableVehicles = await dbcontext.Vehicles
                .Where(v => unavailableVehicles.Contains(v.VehicleNumber))
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

    public async Task<Result<IEnumerable<Vehicle>>> GetBreackupVehiclesAsync()
    {
        try
        {
            var unavailableVehicles = await dbcontext.RiderVehicleStatus
                .Where(s => s.IsActive && s.StatusType == VehicleStatusType.BreakUp)
                .Select(s => s.VehicleNumber)
                .Distinct()
                .ToListAsync();

            var availableVehicles = await dbcontext.Vehicles
                .Where(v => unavailableVehicles.Contains(v.VehicleNumber))
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

    public async Task<Result<IEnumerable<Vehicle>>> GetProblemVehiclesAsync()
    {
        try
        {
            var unavailableVehicles = await dbcontext.RiderVehicleStatus
                .Where(s => s.IsActive && s.StatusType == VehicleStatusType.Problem)
                .Select(s => s.VehicleNumber)
                .Distinct()
                .ToListAsync();

            var availableVehicles = await dbcontext.Vehicles
                .Where(v => unavailableVehicles.Contains(v.VehicleNumber))
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


    #endregion

    #region Permission Helper Methods

    private async Task EndPermission(RiderVehicleStatus status)
    {
        status.IsActive = false;
        status.PermissionEndDate = DateTime.UtcNow.AddHours(3);
    }


    private async Task EndAllActivePermissionsForVehicle(string vehicleNumber)
    {
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
            status.PermissionEndDate = DateTime.UtcNow.AddHours(3);
        }

        // Clear rider assignment
        var rider = await dbcontext.RiderDetails
            .FirstOrDefaultAsync(r => r.VehicleNumber == vehicleNumber);
        if (rider != null)
        {
            rider.VehicleNumber = null;
        }
    }


    private async Task<Result> ValidateVehicleAvailability(string vehicleNumber, string plateNumber)
    {
        bool unavailable = await dbcontext.RiderVehicleStatus
            .AnyAsync(s => s.Vehicle.PlateNumberA == plateNumber &&
                          s.IsActive &&
                          s.StatusType == VehicleStatusType.Taken);

        if (unavailable)
            return Result.Failure(new Error("VehicleTaken", "Vehicle is already taken by another rider", 400));

        bool hasProblem = await dbcontext.RiderVehicleStatus
            .AnyAsync(s => s.Vehicle.PlateNumberA == plateNumber &&
                          s.IsActive &&
                          s.StatusType == VehicleStatusType.Problem);

        if (hasProblem)
            return Result.Failure(new Error("VehicleHasProblem", "Vehicle has active problems and cannot be taken", 400));

        bool stolen = await dbcontext.RiderVehicleStatus
            .AnyAsync(s => s.Vehicle.PlateNumberA == plateNumber &&
                          s.IsActive &&
                          s.StatusType == VehicleStatusType.Stolen);

        if (stolen)
            return Result.Failure(new Error("VehicleHasStolen", "Vehicle has active stolen report and cannot be taken", 400));

        bool breakup = await dbcontext.RiderVehicleStatus
            .AnyAsync(s => s.Vehicle.PlateNumberA == plateNumber &&
                          s.IsActive &&
                          s.StatusType == VehicleStatusType.BreakUp);

        if (breakup)
            return Result.Failure(new Error("Vehiclebreakup", "Vehicle has active breakup problems and cannot be taken", 400));

        return Result.Success();
    }

    #endregion

    #region Validation Helper Methods

    private async Task<VehicleOperationValidation> ValidateTakeOperation(long riderIqamaNo, string vehicleNumber)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var rider = await dbcontext.RiderDetails
            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == riderIqamaNo);

        if (!string.IsNullOrEmpty(rider?.VehicleNumber))
            errors.Add($"Rider already has vehicle {rider.VehicleNumber} assigned");

        var isUnavailable = await dbcontext.RiderVehicleStatus
            .AnyAsync(s => s.VehicleNumber == vehicleNumber &&
                          s.IsActive &&
                          s.StatusType == VehicleStatusType.Taken);

        if (isUnavailable)
            errors.Add("Vehicle is already taken by another rider");

        var hasProblem = await dbcontext.RiderVehicleStatus
            .AnyAsync(s => s.VehicleNumber == vehicleNumber &&
                          s.IsActive &&
                          s.StatusType == VehicleStatusType.Problem);

        if (hasProblem)
            errors.Add("Vehicle has active problems and cannot be taken");

        var isStolen = await dbcontext.RiderVehicleStatus
            .AnyAsync(s => s.VehicleNumber == vehicleNumber &&
                          s.IsActive &&
                          s.StatusType == VehicleStatusType.Stolen);

        if (isStolen)
            errors.Add("Vehicle is reported as stolen");

        return new VehicleOperationValidation(
            IsValid: errors.Count == 0,
            Errors: errors,
            Warnings: warnings
        );
    }

    private async Task<VehicleOperationValidation> ValidateReturnOperation(long riderIqamaNo, string vehicleNumber)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var rider = await dbcontext.RiderDetails
            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == riderIqamaNo);

        if (rider?.VehicleNumber != vehicleNumber)
            errors.Add("This vehicle is not assigned to this rider");

        var activeStatus = await dbcontext.RiderVehicleStatus
            .AnyAsync(s => s.VehicleNumber == vehicleNumber &&
                          s.EmployeeIqamaNo == riderIqamaNo &&
                          s.IsActive &&
                          s.StatusType == VehicleStatusType.Taken);

        if (!activeStatus)
            errors.Add("No active vehicle assignment found");

        return new VehicleOperationValidation(
            IsValid: errors.Count == 0,
            Errors: errors,
            Warnings: warnings
        );
    }

    private async Task<VehicleOperationValidation> ValidateReportProblemOperation(long riderIqamaNo, string vehicleNumber)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Rider is optional for problem reports
        RiderDetails? rider = null;
        if (riderIqamaNo != 0)
        {
            rider = await dbcontext.RiderDetails
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == riderIqamaNo);

            if (rider == null)
                warnings.Add("Rider not found, but problem report can proceed");
        }

        // Check if vehicle already has an active problem
        var existingProblem = await dbcontext.RiderVehicleStatus
            .AnyAsync(s => s.VehicleNumber == vehicleNumber &&
                          s.IsActive &&
                          s.StatusType == VehicleStatusType.Problem);

        if (existingProblem)
            errors.Add("Vehicle already has an active problem reported");

        // Check if vehicle is stolen or broken up
        var isStolen = await dbcontext.RiderVehicleStatus
            .AnyAsync(s => s.VehicleNumber == vehicleNumber &&
                          s.IsActive &&
                          s.StatusType == VehicleStatusType.Stolen);

        if (isStolen)
            errors.Add("Cannot report problem for a stolen vehicle");

        var isBreakUp = await dbcontext.RiderVehicleStatus
            .AnyAsync(s => s.VehicleNumber == vehicleNumber &&
                          s.IsActive &&
                          s.StatusType == VehicleStatusType.BreakUp);

        if (isBreakUp)
            errors.Add("Cannot report problem for a broken up vehicle");

        // Add warning if rider doesn't have the vehicle (this is now allowed)
        if (rider != null && rider.VehicleNumber != vehicleNumber)
        {
            var activeTakenStatus = await dbcontext.RiderVehicleStatus
                .AnyAsync(s => s.VehicleNumber == vehicleNumber &&
                              s.IsActive &&
                              s.StatusType == VehicleStatusType.Taken);

            if (activeTakenStatus)
                warnings.Add("Vehicle is currently assigned to another rider");
            else
                warnings.Add("Vehicle is not currently assigned to any rider");
        }

        return new VehicleOperationValidation(
            IsValid: errors.Count == 0,
            Errors: errors,
            Warnings: warnings
        );
    }

    private async Task<VehicleOperationValidation> ValidateOperation(TempVehicleOperation operation)
    {
        return operation.VehicleStatusType switch
        {
            VehicleStatusType.Taken => await ValidateTakeOperation(operation.RiderIqamaNo ?? 2536361732, operation.VehicleNumber),
            VehicleStatusType.Returned => await ValidateReturnOperation(operation.RiderIqamaNo ?? 2536361732, operation.VehicleNumber),
            VehicleStatusType.Problem => await ValidateReportProblemOperation(operation.RiderIqamaNo ?? 2536361732, operation.VehicleNumber),
            _ => new VehicleOperationValidation(false, new List<string> { "Unknown operation type" }, new List<string>())
        };
    }

    #endregion

    #region Operation Execution Methods

    private async Task<Result> ExecuteOperation(TempVehicleOperation operation)
    {
        try
        {
            switch (operation.VehicleStatusType)
            {
                case VehicleStatusType.Taken:
                    return await ExecuteTakeOperation(operation);
                case VehicleStatusType.Returned:
                    return await ExecuteReturnOperation(operation);
                case VehicleStatusType.Problem:
                    return await ExecuteReportProblemOperation(operation);
                default:
                    return Result.Failure(new Error("UnknownType", "Unknown operation type", 400));
            }
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("ExecuteError", $"Failed to execute operation: {ex.Message}", 500));
        }
    }

    private async Task<Result> ExecuteTakeOperation(TempVehicleOperation operation)
    {
        var rider = await dbcontext.RiderDetails
            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == operation.RiderIqamaNo);

        if (rider == null)
            return Result.Failure(new Error("NoRider", "Rider not found", 404));

        rider.VehicleNumber = operation.VehicleNumber;

        dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
        {
            EmployeeIqamaNo = operation.RiderIqamaNo,
            VehicleNumber = operation.VehicleNumber,
            StatusType = VehicleStatusType.Taken,
            Reason = operation.Reason,
            IsActive = true,
            Permission = operation.Permission, // NEW permission from admin's resolution
            PermissionStartDate = DateTime.UtcNow.AddHours(3), // Start NOW
            PermissionEndDate = null // End date from admin's resolution
        });

        await dbcontext.SaveChangesAsync();
        return Result.Success();
    }

    private async Task<Result> ExecuteReturnOperation(TempVehicleOperation operation)
    {
        // Special case: Vehicle fix (no rider involved)
        if (operation.RiderIqamaNo == 0 || !operation.RiderIqamaNo.HasValue)
        {
            return await ExecuteVehicleFixOperation(operation);
        }

        // Normal return operation with rider
        var rider = await dbcontext.RiderDetails
            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == operation.RiderIqamaNo);

        if (rider == null)
            return Result.Failure(new Error("NoRider", "Rider not found", 404));

        var activeStatus = await dbcontext.RiderVehicleStatus
            .FirstOrDefaultAsync(s => s.VehicleNumber == operation.VehicleNumber &&
                                     s.EmployeeIqamaNo == operation.RiderIqamaNo &&
                                     s.IsActive &&
                                     s.StatusType == VehicleStatusType.Taken);

        if (activeStatus == null)
            return Result.Failure(new Error("NoActiveStatus", "No active assignment found", 400));

        // End permission
        await EndPermission(activeStatus);

        rider.VehicleNumber = null;

        dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
        {
            EmployeeIqamaNo = operation.RiderIqamaNo,
            VehicleNumber = operation.VehicleNumber,
            StatusType = VehicleStatusType.Returned,
            Reason = operation.Reason,
            IsActive = false,
            Permission = activeStatus.Permission,
            PermissionStartDate = activeStatus.PermissionStartDate,
            PermissionEndDate = DateTime.UtcNow.AddHours(3)
        });

        await dbcontext.SaveChangesAsync();

        return Result.Success();
    }

    // NEW METHOD: Handle vehicle fix (no rider)
    private async Task<Result> ExecuteVehicleFixOperation(TempVehicleOperation operation)
    {
        // Find the active problem status
        var activeProblemStatus = await dbcontext.RiderVehicleStatus
            .FirstOrDefaultAsync(s => s.VehicleNumber == operation.VehicleNumber &&
                                     s.IsActive &&
                                     s.StatusType == VehicleStatusType.Problem);

        if (activeProblemStatus == null)
            return Result.Failure(new Error("NoProblem", "No active problem found for this vehicle", 404));

        // Deactivate the problem status
        activeProblemStatus.IsActive = false;

        // Add a new "Returned" status (vehicle is now available)
        dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
        {
            EmployeeIqamaNo = null, // No rider - vehicle is just being fixed
            VehicleNumber = operation.VehicleNumber,
            StatusType = VehicleStatusType.Returned,
            Reason = operation.Reason ?? "Vehicle problem fixed - now available",
            IsActive = false, // ✅ Vehicle is now available
            Timestamp = DateTime.UtcNow.AddHours(3)
        });

        await dbcontext.SaveChangesAsync();

        return Result.Success();
    }

    private async Task<Result> ExecuteReportProblemOperation(TempVehicleOperation operation)
    {
        RiderDetails? rider = null;

        // Rider is optional
        if (operation.RiderIqamaNo != 0)
        {
            rider = await dbcontext.RiderDetails
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == operation.RiderIqamaNo);

            if (rider == null)
                return Result.Failure(new Error("NoRider", "Rider not found", 404));
        }

        // Check if there's an active taken status
        var activeStatus = await dbcontext.RiderVehicleStatus
            .FirstOrDefaultAsync(s => s.VehicleNumber == operation.VehicleNumber &&
                                     s.IsActive &&
                                     s.StatusType == VehicleStatusType.Taken);

        // If vehicle is taken, end permission and clear assignment
        if (activeStatus != null)
        {
            await EndPermission(activeStatus);

            var assignedRider = await dbcontext.RiderDetails
                .FirstOrDefaultAsync(r => r.VehicleNumber == operation.VehicleNumber);

            if (assignedRider != null)
            {
                assignedRider.VehicleNumber = null;
            }
        }

        dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
        {
            EmployeeIqamaNo = operation.RiderIqamaNo,
            VehicleNumber = operation.VehicleNumber,
            StatusType = VehicleStatusType.Problem,
            Reason = operation.Reason,
            IsActive = true,
            Permission = activeStatus?.Permission,
            PermissionStartDate = activeStatus?.PermissionStartDate,
            PermissionEndDate = DateTime.UtcNow.AddHours(3)
        });

        await dbcontext.SaveChangesAsync();
        return Result.Success();
    }
    #endregion

    public async Task<Result<IEnumerable<VehicleHistoryDto>>> GetVehicleHistoryByIqamaAsync(long iqamaNo)
    {
        try
        {
            var riderExists = await dbcontext.RiderDetails
                .AnyAsync(r => r.EmployeeIqamaNo == iqamaNo);

            if (!riderExists)
                return Result.Failure<IEnumerable<VehicleHistoryDto>>(
                    new Error("NoRider", $"No rider found with IqamaNo {iqamaNo}", 404));

            var history = await dbcontext.RiderVehicleStatus
                .Where(s => s.EmployeeIqamaNo == iqamaNo)
                .OrderByDescending(s => s.Timestamp)
                .ToListAsync();

            if (!history.Any())
                return Result.Failure<IEnumerable<VehicleHistoryDto>>(
                    new Error("NoHistory", "No vehicle history found for this rider", 404));

            var rider = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == iqamaNo);

            var historyDtos = new List<VehicleHistoryDto>();

            foreach (var item in history)
            {
                var vehicle = await dbcontext.Vehicles
                    .FirstOrDefaultAsync(v => v.VehicleNumber == item.VehicleNumber);

                historyDtos.Add(new VehicleHistoryDto
                {
                    Id = item.Id,
                    VehicleNumber = item.VehicleNumber,
                    SerialNumber = vehicle?.SerialNumber ?? 0,
                    PlateNumberA = vehicle?.PlateNumberA ?? string.Empty,
                    PlateNumberE = vehicle?.PlateNumberE ?? string.Empty,
                    OwnerId = vehicle?.OwnerId ?? 0,
                    OwnerName = vehicle?.OwnerName ?? string.Empty,
                    ManufactureYear = vehicle?.ManufactureYear ?? 0,
                    Manufacturer = vehicle?.Manufacturer ?? string.Empty,
                    EmployeeIqamaNo = iqamaNo,
                    RiderName = rider?.Employee.NameAR ?? "N/A",
                    RiderNameE = rider?.Employee.NameEN ?? "N/A",
                    Location = vehicle?.Location ?? string.Empty,
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
                new Error("HistoryError", $"Failed to retrieve history: {ex.Message}", 500));
        }
    }
    public async Task<Result<IEnumerable<VehicleWithRiderDto>>> GetAllVehiclesRidersAsync()
    {
        try
        {
            // Get all vehicles
            var allVehicles = await dbcontext.Vehicles.ToListAsync();

            // Get vehicles with active Taken status
            var takenVehicles = await dbcontext.Vehicles
                .Select(v => new
                {
                    Vehicle = v,
                    TakenStatus = dbcontext.RiderVehicleStatus
                        .Where(s => s.VehicleNumber == v.VehicleNumber
                            && s.IsActive
                            && s.StatusType == VehicleStatusType.Taken)
                        .FirstOrDefault()
                })
                .Where(v => v.TakenStatus != null)
                .ToListAsync();

            var pVehicles = await dbcontext.Vehicles
                .Select(v => new
                {
                    Vehicle = v,
                    TakenStatus = dbcontext.RiderVehicleStatus
                        .Where(s => s.VehicleNumber == v.VehicleNumber
                            && s.IsActive
                            && s.StatusType == VehicleStatusType.Problem)
                        .FirstOrDefault()
                })
                .Where(v => v.TakenStatus != null)
                .ToListAsync();

            var sVehicles = await dbcontext.Vehicles
                .Select(v => new
                {
                    Vehicle = v,
                    TakenStatus = dbcontext.RiderVehicleStatus
                        .Where(s => s.VehicleNumber == v.VehicleNumber
                            && s.IsActive
                            && s.StatusType == VehicleStatusType.Stolen)
                        .FirstOrDefault()
                })
                .Where(v => v.TakenStatus != null)
                .ToListAsync();

            var bVehicles = await dbcontext.Vehicles
                .Select(v => new
                {
                    Vehicle = v,
                    TakenStatus = dbcontext.RiderVehicleStatus
                        .Where(s => s.VehicleNumber == v.VehicleNumber
                            && s.IsActive
                            && s.StatusType == VehicleStatusType.BreakUp)
                        .FirstOrDefault()
                })
                .Where(v => v.TakenStatus != null)
                .ToListAsync();

            var takenVehicleNumbers = takenVehicles.Select(v => v.Vehicle.VehicleNumber).ToList();
            var bVedshicles = bVehicles.Select(v => v.Vehicle.VehicleNumber).ToList();
            var bVdehicles = pVehicles.Select(v => v.Vehicle.VehicleNumber).ToList();
            var bdVehicles = sVehicles.Select(v => v.Vehicle.VehicleNumber).ToList();

            // Get all remaining vehicles (not in Taken status)
            var remainingVehicles = allVehicles
                .Where(v => !takenVehicleNumbers.Contains(v.VehicleNumber) && !bVedshicles.Contains(v.VehicleNumber) && !bVdehicles.Contains(v.VehicleNumber) && !bdVehicles.Contains(v.VehicleNumber))
                .Select(v => new
                {
                    Vehicle = v,
                    LatestStatus = dbcontext.RiderVehicleStatus
                        .Where(s => s.VehicleNumber == v.VehicleNumber && s.IsActive)
                        .OrderByDescending(s => s.Timestamp)
                        .FirstOrDefault()
                })
                .ToList();

            var result = new List<VehicleWithRiderDto>();

            // Process Taken vehicles
            foreach (var v in takenVehicles)
            {
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
                    Location = v.Vehicle.Location,
                    LicenseExpiryDate = v.Vehicle.LicenseExpiryDate,
                    CurrentStatus = "Taken",
                    StatusSince = v.TakenStatus.Timestamp,
                    IsAvailable = false,
                    HasActiveProblem = false,
                    IsStolen = false,
                    IsBreakUp = false,
                    ActiveProblemsCount = 0
                };

                // Populate rider info for Taken status
                var rider = await dbcontext.RiderDetails
                    .Include(r => r.Employee)
                    .Include(r=>r.Company)
                    .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == v.TakenStatus.EmployeeIqamaNo);

                if (rider != null)
                {
                    dto.CurrentRider = new RiderInfoDto
                    {
                        EmployeeIqamaNo = rider.EmployeeIqamaNo,
                        RiderName = rider.Employee.NameAR,
                        RiderNameE = rider.Employee.NameEN,
                        TakenDate = v.TakenStatus.Timestamp,
                        TakenReason = v.TakenStatus.Reason ?? "no reason",
                        CompanyName = rider.Company.Name
                    };
                }

                result.Add(dto);
            }

            // Process remaining vehicles (Returned, Available, etc.)
            foreach (var v in remainingVehicles)
            {
                var currentStatus = v.LatestStatus?.StatusType.ToString() ?? "Returned";
                var statusSince = v.LatestStatus?.Timestamp;

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
                    Location = v.Vehicle.Location,
                    LicenseExpiryDate = v.Vehicle.LicenseExpiryDate,
                    CurrentStatus = currentStatus,
                    StatusSince = statusSince,
                    IsAvailable = currentStatus == "Available",
                    HasActiveProblem = currentStatus == "Problem",
                    IsStolen = currentStatus == "Stolen",
                    IsBreakUp = currentStatus == "BreakUp",
                    ActiveProblemsCount = 0
                };

                // Populate rider info for Returned status
                if (v.LatestStatus != null && v.LatestStatus.StatusType == VehicleStatusType.Returned)
                {
                    var rider = await dbcontext.RiderDetails
                        .Include(r => r.Employee)
                        .Include(c=>c.Company)
                        .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == v.LatestStatus.EmployeeIqamaNo);

                    if (rider != null)
                    {
                        dto.CurrentRider = new RiderInfoDto
                        {
                            EmployeeIqamaNo = rider.EmployeeIqamaNo,
                            RiderName = rider.Employee.NameAR,
                            RiderNameE = rider.Employee.NameEN,
                            TakenDate = v.LatestStatus.Timestamp,
                            TakenReason = v.LatestStatus.Reason ?? "no reason",
                            CompanyName = rider.Company.Name

                        };
                    }
                }

                result.Add(dto);
            }

            return Result.Success<IEnumerable<VehicleWithRiderDto>>(result.OrderBy(v => v.VehicleNumber));
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<VehicleWithRiderDto>>(new Error($"{ex.Message}", "error", 404));
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
                    : "Returned";

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
                        .Include(c => c.Company)
                        .FirstOrDefault(r => r.EmployeeIqamaNo == takenStatus.EmployeeIqamaNo);

                    if (rider != null)
                    {
                        dto.CurrentRider = new RiderInfoDto
                        {
                            EmployeeIqamaNo = rider.EmployeeIqamaNo,
                            RiderName = rider.Employee.NameAR,
                            RiderNameE = rider.Employee.NameEN,
                            TakenDate = takenStatus.Timestamp,
                            TakenReason = takenStatus.Reason ?? "no reason",
                            CompanyName = rider.Company.Name,
                            PhoneNumber = rider.Employee.Phone
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
    public async Task<Result<UnavailableVehiclesResponse>> GetUnavailableVehiclesAsync(string statusFilter)
    {
        statusFilter = statusFilter ?? "all";

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
                        Since = item.LatestProblem?.Timestamp ?? DateTime.UtcNow.AddHours(3),
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
            { "Returned", new List<VehicleWithRiderDto>() },
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
                    CurrentStatus = vehicleStatus?.ToString() ?? "Returned",
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
                    _ => "Returned"
                };

                groups[groupKey].Add(dto);
            }

            var response = new GroupedVehicleStatusResponse
            {
                TotalVehicles = vehicles.Count,
                GeneratedAt = DateTime.UtcNow.AddHours(3),
                Groups = groups.Select(g => new VehicleStatusGroupDto
                {
                    Status = g.Key,
                    Count = g.Value.Count,
                    Vehicles = g.Value.OrderBy(v => v.VehicleNumber).ToList()
                }).ToList(),
                Summary = new VehicleStatusSummary
                {
                    AvailableCount = groups["Returned"].Count,
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

    #region Mapping and Response Helpers

    private TempVehicleOperationResponse MapToResponse(
        TempVehicleOperation operation,
        VehicleOperationValidation validation)
    {
        return new TempVehicleOperationResponse(
            Id: operation.Id,
            RiderIqamaNo: operation.RiderIqamaNo ?? 0,
            RiderNameAR: operation.Rider?.Employee?.NameAR ?? "N/A",
            RiderNameEN: operation.Rider?.Employee?.NameEN ?? "N/A",
            VehiclePlateNumber: operation.VehiclePlateNumber,
            VehicleNumber: operation.VehicleNumber,
            OperationType: operation.VehicleStatusType.ToString(),
            Reason: operation.Reason,
            RequestedAt: operation.RequestedAt,
            RequestedBy: operation.RequestedBy,
            IsResolved: operation.IsResolved,
            Resolution: operation.Resolution,
            ResolvedBy: operation.ResolvedBy,
            ResolvedAt: operation.ResolvedAt,
            Validation: validation,
            Permission: operation.Permission,
            PermissionEndDate: operation.PermissionEndDate
        );
    }

    #endregion


    // Add to VehicleService class


    private async Task<Result> ExecuteSwitchOperation(
        TempVehicleOperation operation,
        string newVehicleNumber,
        string permission,
        DateTime permissionEndDate)
    {
        try
        {
            var rider = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == operation.RiderIqamaNo);

            if (rider == null)
                return Result.Failure(new Error("NoRider", "Rider not found", 404));

            if (rider.Employee.Status != "enable")
                return Result.Failure(new Error("RiderDisabled",
                    "Rider is disabled and cannot switch vehicles", 403));

            var currentVehicleNumber = operation.VehicleNumber;

            // Verify rider still has the current vehicle
            if (rider.VehicleNumber != currentVehicleNumber)
                return Result.Failure(new Error("VehicleMismatch",
                    $"Rider no longer has the current vehicle. Expected: {currentVehicleNumber}, Actual: {rider.VehicleNumber}", 400));

            // Get current vehicle's active taken status
            var currentActiveStatus = await dbcontext.RiderVehicleStatus
                .FirstOrDefaultAsync(s => s.VehicleNumber == currentVehicleNumber
                    && s.EmployeeIqamaNo == operation.RiderIqamaNo
                    && s.IsActive
                    && s.StatusType == VehicleStatusType.Taken);

            if (currentActiveStatus == null)
                return Result.Failure(new Error("NoActiveStatus",
                    "No active vehicle assignment found for current vehicle", 400));

            // Verify new vehicle is still available
            var newVehicleUnavailable = await dbcontext.RiderVehicleStatus
                .AnyAsync(s => s.VehicleNumber == newVehicleNumber
                    && s.IsActive
                    && (s.StatusType == VehicleStatusType.Taken
                        || s.StatusType == VehicleStatusType.Problem
                        || s.StatusType == VehicleStatusType.Stolen
                        || s.StatusType == VehicleStatusType.BreakUp));

            if (newVehicleUnavailable)
                return Result.Failure(new Error("NewVehicleUnavailable",
                    "New vehicle is no longer available", 400));

            // Check if trying to switch to the same vehicle
            if (currentVehicleNumber == newVehicleNumber)
                return Result.Failure(new Error("SameVehicle",
                    "Cannot switch to the same vehicle", 400));

            // === STEP 1: Return current vehicle ===

            // End permission for current vehicle
            await EndPermission(currentActiveStatus);

            // Create return status for current vehicle
            dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                EmployeeIqamaNo = operation.RiderIqamaNo,
                VehicleNumber = currentVehicleNumber,
                StatusType = VehicleStatusType.Returned,
                Reason = operation.Reason ?? "Vehicle switch",
                IsActive = false,
                Permission = currentActiveStatus.Permission,
                PermissionStartDate = currentActiveStatus.PermissionStartDate,
                PermissionEndDate = DateTime.UtcNow.AddHours(3)
            });

            // === STEP 2: Take new vehicle ===

            // Update rider's vehicle assignment
            rider.VehicleNumber = newVehicleNumber;

            // Create taken status for new vehicle with NEW permission
            dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus
            {
                EmployeeIqamaNo = operation.RiderIqamaNo,
                VehicleNumber = newVehicleNumber,
                StatusType = VehicleStatusType.Taken,
                Reason = operation.Reason ?? "Vehicle switch",
                IsActive = true,
                Permission = permission,  // NEW permission from admin
                PermissionStartDate = DateTime.UtcNow.AddHours(3),
                PermissionEndDate = permissionEndDate  // NEW permission end date from admin
            });

            await dbcontext.SaveChangesAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("ExecuteSwitchError",
                $"Failed to execute vehicle switch: {ex.Message}", 500));
        }
    }





}
// Add these DTOs to Application/Service/IImportService.cs (at the end with other DTOs)

public record VehicleRelocationImportResponse(
    int TotalRecords,
    int SuccessfulRelocations,
    int LocationUpdated,
    int StatusUpdated,
    int FailedRecords,
    int VehicleNotFound,
    int HousingNotFound,
    List<VehicleRelocationRowResult> Results,
    List<string> Errors,
    DateTime ProcessedAt
);

public record VehicleRelocationRowResult(
    int RowNumber,
    bool Success,
    string PlateNumber,
    string VehicleNumber,
    string VehicleType,
    bool LocationUpdated,
    bool StatusUpdated,
    string? OldLocation,
    string? NewLocation,
    string? OldStatus,
    string? NewStatus,
    string? Reason,
    List<string> Warnings,
    string? ErrorMessage
);
public record TempVehicleOperationResponse(
    int Id,
    long RiderIqamaNo,
    string RiderNameAR,
    string RiderNameEN,
    string VehiclePlateNumber,
    string VehicleNumber,
    string OperationType,
    string? Reason,
    DateTime RequestedAt,
    string RequestedBy,
    bool IsResolved,
    string? Resolution,
    string? ResolvedBy,
    DateTime? ResolvedAt,
    VehicleOperationValidation Validation,
    string? Permission,
    DateTime? PermissionEndDate
);

public record VehicleOperationValidation(
    bool IsValid,
    List<string> Errors,
    List<string> Warnings
);

public record VehicleResolutionRequest(
    long RiderIqamaNo,
    string Resolution, // "Approved" or "Rejected"
    string ResolvedBy,
    string Plate,
    string? Note,
    string? Permission, // Required for "Take" operations when approved
    DateTime? PermissionEndDate
);

public record SVehicleResolutionRequest
{
    public long RiderIqamaNo { get; init; }
    public string ResolvedBy { get; init; }
    public string Plate { get; init; }
}