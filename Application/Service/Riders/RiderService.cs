using Application.Abstraction;
using Application.Contracts.Employees;
using Application.Contracts.rider;
using Application.Service.Empolyee;
using Azure.Core;
using Domain;
using Domain.Entities;
using Mapster;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace Application.Service.Riders;

public class RiderService(ApplicationDbcontext dbcontext,IRiderWorkingIdHistoryService workingIdHistoryService) : IRiderService
{
    private readonly ApplicationDbcontext dbcontext = dbcontext;
    private readonly IRiderWorkingIdHistoryService _workingIdHistoryService = workingIdHistoryService;

    public async Task<Result<VehicleResponse>> GetRiderVehicle(long IqamaNo)
    {
        try
        {
            var employee = await dbcontext.Employees
                .Where(e => !e.IsDeleted && e.IqamaNo == IqamaNo)
                .Include(e => e.RiderDetails)
                    .ThenInclude(rd => rd.Vehicle)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (employee is null)
                return Result.Failure<VehicleResponse>(
                    new Error("NotFound", "Employee/Rider not found with this Iqama", 404));

            if (employee.RiderDetails is null)
                return Result.Failure<VehicleResponse>(
                    new Error("NotFound", "This employee is not a rider", 404));

            if (employee.RiderDetails.Vehicle is null)
                return Result.Failure<VehicleResponse>(
                    new Error("NotFound", "No vehicle assigned to this rider", 404));

            var vehicle = employee.RiderDetails.Vehicle;

            var response = new VehicleResponse(
                VehicleType: vehicle.VehicleType,
                VehicleNumber: vehicle.VehicleNumber,
                SerialNumber: vehicle.SerialNumber,
                PlateNumberA: vehicle.PlateNumberA,
                OwnerId: vehicle.OwnerId,
                OwnerName: vehicle.OwnerName,
                PlateNumberE: vehicle.PlateNumberE,
                ManufactureYear: vehicle.ManufactureYear,
                Manufacturer: vehicle.Manufacturer,
                LicenseExpiryDate: vehicle.LicenseExpiryDate,
                VehicleImagePath: vehicle.VehicleImagePath,
                LicenseImagePath: vehicle.LicenseImagePath,
                ExstraImage: vehicle.ExstraImage,
                ExstraImage1: vehicle.ExstraImage1,
                CreatedAt: vehicle.CreatedAt,
                Location: vehicle.Location
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<VehicleResponse>(
                new Error("ServerError", $"Error retrieving rider vehicle: {ex.Message}", 500));
        }
    }
    public async Task<Result<EmployeeStatisticsResponse>> GetEmployeeStatistics()
    {
        try
        {
            var totalEmployees = await dbcontext.Employees
                        .Where(e => !e.IsDeleted)
                .CountAsync();

            var totalRiders = await dbcontext.Employees
                        .Where(e => !e.IsDeleted)
                .Where(e => !e.IsEmployee)
                .CountAsync();

            // Calculate non-riders
            var totalNonRiders = totalEmployees - totalRiders;

            var response = new EmployeeStatisticsResponse(
                 totalEmployees,
                 totalRiders,
                 totalNonRiders
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<EmployeeStatisticsResponse>(
                new Error("ServerError", $"Error retrieving employee statistics: {ex.Message}", 500));
        }
    }
    public async Task<Result<IEnumerable<RiderResponse>>> Get(long IqamaNo)
    {

        var isexist = await dbcontext
           .Employees
                   .Where(e => !e.IsDeleted)
           .Where(r => r.IqamaNo.ToString().StartsWith(IqamaNo.ToString()))
           .Include(e => e.Housing)
           .Include(e => e.RiderDetails)
               .ThenInclude(rd => rd.Company)
           .AsNoTracking()
           .ToListAsync();

        if (isexist is null)
            return Result.Failure<IEnumerable<RiderResponse>>(error: new Error("No rider Found", "no rider found with this Iqama", 400));

        var response = isexist.Select(MapToResponse).ToList();



        return Result.Success<IEnumerable<RiderResponse>>(response);
    }
    public async Task<Result<IEnumerable<RiderResponse>>> GetAllEmployee()
    {
        var employees = await dbcontext.Employees
                    .Where(e => !e.IsDeleted)
            .AsNoTracking()
            .Include(e => e.Housing)
            .Include(e => e.RiderDetails)
                .ThenInclude(rd => rd.Company)
            .ToListAsync();

        if (!employees.Any())
            return Result.Failure<IEnumerable<RiderResponse>>(
                new Error("NotFound", "No employees or riders found", 404));

        var response = employees.Select(MapToResponse).ToList();
        return Result.Success<IEnumerable<RiderResponse>>(response);
    }
    public async Task<Result<IEnumerable<RiderResponse>>> GetAllEmployee2()
    {
        var employees = await dbcontext.Employees
                    .Where(e => !e.IsDeleted && !e.IsEmployee)
            .AsNoTracking()
            .Include(e => e.Housing)
            .Include(e => e.RiderDetails)
                .ThenInclude(rd => rd.Company)
            .ToListAsync();

        if (!employees.Any())
            return Result.Failure<IEnumerable<RiderResponse>>(
                new Error("NotFound", "No employees or riders found", 404));

        var response = employees.Select(MapToResponse).ToList();
        return Result.Success<IEnumerable<RiderResponse>>(response);
    }
    public async Task<Result> CreateAsync(RiderRequest Request)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();

        try
        {
            // Check if employee already exists
            var exists = await dbcontext.Employees.AnyAsync(x => x.IqamaNo == Request.IqamaNo);
            if (exists)
                return Result.Failure(new Error("AlreadyExists",
                    "Employee/Rider already exists with this Iqama", 400));

            var employee = new Employees
            {
                IqamaNo = Request.IqamaNo,
                IqamaEndM = Request.IqamaEndM,
                IqamaEndH = Request.IqamaEndH,
                PassportNo = Request.PassportNo,
                PassportEnd = Request.PassportEnd,
                Sponsor = Request.Sponsor,
                sponsorNo = Request.sponsorNo,
                JobTitle = Request.JobTitle,
                NameAR = Request.NameAR,
                NameEN = Request.NameEN,
                Country = Request.Country,
                Phone = Request.Phone,
                DateOfBirth = Request.DateOfBirth,
                Status = Request.Status,
                IBAN = Request.IBAN,
                INKSA = Request.INKSA,
                IsEmployee = Request.IsEmployee,
            };

            // Only create RiderDetails if WorkingId and CompanyName are provided
            if (!string.IsNullOrWhiteSpace(Request.WorkingId) &&
                !string.IsNullOrWhiteSpace(Request.CompanyName))
            {
                var company = await dbcontext.Companies
                    .FirstOrDefaultAsync(c => c.Name == Request.CompanyName);

                if (company is null)
                    return Result.Failure(new Error("NotFound",
                        $"Company '{Request.CompanyName}' not found", 404));

                // Check if WorkingId already exists
                var workingIdExists = await dbcontext.RiderDetails
                    .AnyAsync(rd => rd.WorkingId == Request.WorkingId);
                if (workingIdExists)
                    return Result.Failure(new Error("AlreadyExists",
                        $"WorkingId '{Request.WorkingId}' is already assigned", 400));

                employee.RiderDetails = new RiderDetails
                {
                    EmployeeIqamaNo = Request.IqamaNo,
                    WorkingId = Request.WorkingId,
                    TshirtSize = Request.TshirtSize,
                    LicenseNumber = Request.LicenseNumber,
                    CompanyId = company.Id
                };

                await dbcontext.Employees.AddAsync(employee);
                await dbcontext.SaveChangesAsync();

                // Record in history
                var historyResult = await _workingIdHistoryService.RecordWorkingIdChange(
                    Request.IqamaNo,
                    Request.WorkingId,
                    company.Id,
                    $"Initial assignment - Company: {company.Name}",
                    cancellationToken: default
                );

                if (historyResult.IsFailure)
                {
                    await transaction.RollbackAsync();
                    return Result.Failure(new Error("HistoryError",
                        $"Failed to record history: {historyResult.Error.Description}", 500));
                }
            }
            else
            {
                // Create employee without RiderDetails
                await dbcontext.Employees.AddAsync(employee);
                await dbcontext.SaveChangesAsync();
            }

            await transaction.CommitAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result> DeleteAsync(long IqamaNo,string Reason, CancellationToken cancellationToken = default)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var employee = await dbcontext.Employees
                .Include(c => c.RiderDetails)
                .FirstOrDefaultAsync(c => c.IqamaNo == IqamaNo, cancellationToken);

            if (employee is null)
                return Result.Failure(new Error("NotFound",
                    "Employee/Rider not found with this Iqama", 404));

            if (employee.IsDeleted)
                return Result.Failure(new Error("AlreadyDeleted",
                    "Employee/Rider is already deleted", 400));

            // ✅ Soft delete: Mark as deleted instead of removing
            employee.IsDeleted = true;
            employee.DeletedAt = DateTime.UtcNow.AddHours(3);
            employee.Status = Reason;

            // ✅ Deactivate history records (preserve audit trail)
            if (employee.RiderDetails != null)
            {
                var activeHistories = await dbcontext.RiderWorkingIdHistories
                    .Where(h => h.RiderIqamaNo == IqamaNo && h.IsActive)
                    .ToListAsync(cancellationToken);

                foreach (var history in activeHistories)
                {
                    history.IsActive = false;
                    history.EndDate = DateTime.UtcNow.AddHours(3);
                    history.Notes = $"{history.Notes} | Employee soft-deleted on {DateTime.UtcNow.AddHours(3):yyyy-MM-dd}";
                }
            }

            await dbcontext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure(new Error("ServerError", ex.Message, 500));
        }
    }
    public async Task<Result<RiderResponse>> UpdateAsync(long IqamaNo, URiderRequest request)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();

        try
        {
            // Fetch employee with all related data
            var employee = await dbcontext.Employees
                .Where(e => !e.IsDeleted)
                .Include(e => e.Housing)
                .Include(e => e.RiderDetails)
                    .ThenInclude(rd => rd.Company)
                .FirstOrDefaultAsync(e => e.IqamaNo == IqamaNo);

            if (employee is null)
            {
                return Result.Failure<RiderResponse>(
                    new Error("NotFound", "Employee/Rider not found with this Iqama", 404));
            }

            // Update basic employee fields
            UpdateEmployeeFields(employee, request);

            // Track changes for history
            bool needsHistoryRecord = false;
            string? finalWorkingId = null;
            int? finalCompanyId = null;
            string? companyName = null;

            // Handle RiderDetails updates
            if (employee.RiderDetails != null)
            {
                // Process Company change first
                if (!string.IsNullOrWhiteSpace(request.CompanyName))
                {
                    var company = await dbcontext.Companies
                        .FirstOrDefaultAsync(c => c.Name == request.CompanyName);

                    if (company is null)
                    {
                        return Result.Failure<RiderResponse>(
                            new Error("NotFound", $"Company '{request.CompanyName}' not found", 404));
                    }

                    // Check if company actually changed
                    if (company.Id != employee.RiderDetails.CompanyId)
                    {
                        employee.RiderDetails.CompanyId = company.Id;
                        employee.RiderDetails.Company = company;
                        needsHistoryRecord = true;
                    }

                    finalCompanyId = company.Id;
                    companyName = company.Name;
                }
                else
                {
                    // Keep existing company
                    finalCompanyId = employee.RiderDetails.CompanyId;
                    companyName = employee.RiderDetails.Company?.Name;
                }

                // Process WorkingId change
                if (!string.IsNullOrWhiteSpace(request.WorkingId))
                {
                    if (request.WorkingId != employee.RiderDetails.WorkingId)
                    {
                        // Validate new WorkingId is not in use by another rider
                        var workingIdExists = await dbcontext.RiderDetails
                            .AnyAsync(rd => rd.WorkingId == request.WorkingId &&
                                           rd.Id != employee.RiderDetails.Id);

                        if (workingIdExists)
                        {
                            return Result.Failure<RiderResponse>(
                                new Error("AlreadyExists",
                                    $"WorkingId '{request.WorkingId}' is already assigned", 400));
                        }

                        employee.RiderDetails.WorkingId = request.WorkingId;
                        needsHistoryRecord = true;
                    }

                    finalWorkingId = request.WorkingId;
                }
                else
                {
                    // Keep existing WorkingId
                    finalWorkingId = employee.RiderDetails.WorkingId;
                }

                // Update other RiderDetails fields
                if (!string.IsNullOrWhiteSpace(request.TshirtSize))
                    employee.RiderDetails.TshirtSize = request.TshirtSize;

                if (!string.IsNullOrWhiteSpace(request.LicenseNumber))
                    employee.RiderDetails.LicenseNumber = request.LicenseNumber;

                // Mark entity as modified to ensure EF tracks changes
                dbcontext.Entry(employee.RiderDetails).State = EntityState.Modified;
            }
            else if (!string.IsNullOrWhiteSpace(request.WorkingId) &&
                     !string.IsNullOrWhiteSpace(request.CompanyName))
            {
                // Create new RiderDetails
                var company = await dbcontext.Companies
                    .FirstOrDefaultAsync(c => c.Name == request.CompanyName);

                if (company is null)
                {
                    return Result.Failure<RiderResponse>(
                        new Error("NotFound", $"Company '{request.CompanyName}' not found", 404));
                }

                // Validate WorkingId is unique
                var workingIdExists = await dbcontext.RiderDetails
                    .AnyAsync(rd => rd.WorkingId == request.WorkingId);

                if (workingIdExists)
                {
                    return Result.Failure<RiderResponse>(
                        new Error("AlreadyExists",
                            $"WorkingId '{request.WorkingId}' is already assigned", 400));
                }

                // Create new RiderDetails
                employee.RiderDetails = new RiderDetails
                {
                    EmployeeIqamaNo = IqamaNo,
                    WorkingId = request.WorkingId,
                    TshirtSize = request.TshirtSize,
                    LicenseNumber = request.LicenseNumber,
                    CompanyId = company.Id
                };

                finalWorkingId = request.WorkingId;
                finalCompanyId = company.Id;
                companyName = company.Name;
                needsHistoryRecord = true;

                dbcontext.RiderDetails.Add(employee.RiderDetails);
            }

            // Mark employee as modified
            dbcontext.Entry(employee).State = EntityState.Modified;

            // Save all changes
            await dbcontext.SaveChangesAsync();

            // Record history if WorkingId or Company changed
            if (needsHistoryRecord && !string.IsNullOrWhiteSpace(finalWorkingId))
            {
                var historyResult = await _workingIdHistoryService.RecordWorkingIdChange(
                    IqamaNo,
                    finalWorkingId,
                    finalCompanyId!.Value,
                    $"Updated - Company: {companyName ?? "Unknown"}",
                    cancellationToken: default
                );

                if (historyResult.IsFailure)
                {
                    await transaction.RollbackAsync();
                    return Result.Failure<RiderResponse>(
                        new Error("HistoryError",
                            $"Failed to record history: {historyResult.Error.Description}", 500));
                }
            }

            // Commit transaction
            await transaction.CommitAsync();

            // Return response
            var response = MapToResponse(employee);
            return Result.Success(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<RiderResponse>(
                new Error("ServerError", ex.Message, 500));
        }
    }

    private static void UpdateEmployeeFields(Employees employee, URiderRequest request)
    {
        if (request.IqamaEndM.HasValue)
            employee.IqamaEndM = request.IqamaEndM.Value;

        if (request.IqamaEndH.HasValue)
            employee.IqamaEndH = request.IqamaEndH.Value;

        if (!string.IsNullOrWhiteSpace(request.PassportNo))
            employee.PassportNo = request.PassportNo;

        if (request.PassportEnd.HasValue)
            employee.PassportEnd = request.PassportEnd.Value;

        if (!string.IsNullOrWhiteSpace(request.Sponsor))
            employee.Sponsor = request.Sponsor;

        if (request.sponsorNo.HasValue)
            employee.sponsorNo = request.sponsorNo.Value;

        if (!string.IsNullOrWhiteSpace(request.JobTitle))
            employee.JobTitle = request.JobTitle;

        if (!string.IsNullOrWhiteSpace(request.NameAR))
            employee.NameAR = request.NameAR;

        if (!string.IsNullOrWhiteSpace(request.NameEN))
            employee.NameEN = request.NameEN;

        if (!string.IsNullOrWhiteSpace(request.Country))
            employee.Country = request.Country;

        if (!string.IsNullOrWhiteSpace(request.Phone))
            employee.Phone = request.Phone;

        if (request.DateOfBirth.HasValue)
            employee.DateOfBirth = request.DateOfBirth.Value;

        if (!string.IsNullOrWhiteSpace(request.Status))
            employee.Status = request.Status;

        if (!string.IsNullOrWhiteSpace(request.IBAN))
            employee.IBAN = request.IBAN;

        if (request.INKSA.HasValue)
            employee.INKSA = request.INKSA.Value;
    }

    public async Task<List<RiderResponse>> SmartSearch(string keyword)
    {
        keyword = keyword.ToLower();

        var query = await dbcontext.Employees.Where(e => !e.IsDeleted)
            .Include(e => e.Housing)
            .Include(e => e.RiderDetails)
                .ThenInclude(rd => rd.Company)
            .Where(e =>
                e.NameAR.ToLower().Contains(keyword) ||
                e.NameEN.ToLower().Contains(keyword) ||
                e.Country.ToLower().Contains(keyword) ||
                e.Sponsor.ToLower().Contains(keyword) ||
                e.JobTitle.ToLower().Contains(keyword) ||
                (e.IBAN != null && e.IBAN.ToLower().Contains(keyword)) ||
                e.IqamaNo.ToString().StartsWith(keyword) ||
                e.sponsorNo.ToString().StartsWith(keyword) ||
                (e.RiderDetails != null && e.RiderDetails.WorkingId != null &&
                 e.RiderDetails.WorkingId.ToLower().Contains(keyword))
            )
            .ToListAsync();

        return query.Select(MapToResponse).ToList();
    }
    public async Task<Result<IEnumerable<RiderResponse>>> Filter(EmployeeFilterr filter)
    {
        var query = dbcontext.Employees.Where(e => !e.IsDeleted)
            .Include(e => e.RiderDetails)
                .ThenInclude(rd => rd.Company)
            .Include(e => e.Housing)
            .AsQueryable();

        if (filter.IqamaEndH.HasValue)
            query = query.Where(e => e.IqamaEndH == filter.IqamaEndH);

        if (filter.IqamaEndM.HasValue)
            query = query.Where(e => e.IqamaEndM == filter.IqamaEndM);

        if (!string.IsNullOrWhiteSpace(filter.Sponsor))
            query = query.Where(e => e.Sponsor.Contains(filter.Sponsor));

        if (filter.sponsorNo.HasValue)
            query = query.Where(e => e.sponsorNo == filter.sponsorNo.Value);

        if (filter.PassportEnd.HasValue)
            query = query.Where(e => e.PassportEnd == filter.PassportEnd);

        if (!string.IsNullOrWhiteSpace(filter.JobTitle))
            query = query.Where(e => e.JobTitle.Contains(filter.JobTitle));

        if (!string.IsNullOrWhiteSpace(filter.NameAR))
            query = query.Where(e => e.NameAR.Contains(filter.NameAR));

        if (!string.IsNullOrWhiteSpace(filter.NameEN))
            query = query.Where(e => e.NameEN.Contains(filter.NameEN));

        if (!string.IsNullOrWhiteSpace(filter.Country))
            query = query.Where(e => e.Country.Contains(filter.Country));

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(e => e.Status.Contains(filter.Status));

        if (!string.IsNullOrWhiteSpace(filter.WorkingId))
            query = query.Where(e => e.RiderDetails != null &&
                                     e.RiderDetails.WorkingId!.Contains(filter.WorkingId));

        if (!string.IsNullOrWhiteSpace(filter.CompanyName))
            query = query.Where(e => e.RiderDetails != null &&
                                     e.RiderDetails.Company.Name.Contains(filter.CompanyName));

        if (filter.INKSA.HasValue)
            query = query.Where(e => e.INKSA == filter.INKSA);

        if (!string.IsNullOrWhiteSpace(filter.HousingName))
            query = query.Where(e => e.Housing != null &&
                                     e.Housing.Name.Contains(filter.HousingName));

        var results = await query.ToListAsync();
        var response = results.Select(MapToResponse).ToList();

        return Result.Success<IEnumerable<RiderResponse>>(response);
    }


    public async Task<Result> ChangeWorkinId(string OldWorkinId, string NewWorkingId)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();

        try
        {
            var rider = await dbcontext.RiderDetails
                .Include(r => r.Employee)
                .Include(r => r.Company)
                .FirstOrDefaultAsync(r => r.WorkingId == OldWorkinId);

            if (rider is null)
                return Result.Failure(
                    new Error("NotFound", "No rider found with the specified old working ID", 404));

            var newIdExists = await dbcontext.RiderDetails
                .AnyAsync(r => r.WorkingId == NewWorkingId && r.Id != rider.Id);

            if (newIdExists)
                return Result.Failure(
                    new Error("AlreadyExists", $"WorkingId {NewWorkingId} is already assigned to another rider", 400));

            var historyCheck = await _workingIdHistoryService.WhoHasWorkingId(
                NewWorkingId,
                default);

            if (historyCheck.IsSuccess && historyCheck.Value.IsCurrentlyAssigned)
            {
                return Result.Failure(
                    new Error("AlreadyExists",
                        $"WorkingId {NewWorkingId} is currently assigned to {historyCheck.Value.CurrentRiderName}",
                        400));
            }

            // Update the WorkingId
            rider.WorkingId = NewWorkingId;
            await dbcontext.SaveChangesAsync();

            // ✅ Record the change in history AFTER saving
            var historyResult = await _workingIdHistoryService.RecordWorkingIdChange(
                rider.EmployeeIqamaNo,
                NewWorkingId,
                rider.CompanyId,
                $"WorkingId changed from {OldWorkinId} to {NewWorkingId}",
                cancellationToken: default  // ✅ Use named parameter
            );

            // ✅ Check if history recording failed
            if (historyResult.IsFailure)
            {
                await transaction.RollbackAsync();
                return Result.Failure(new Error("HistoryError",
                    $"Failed to record history: {historyResult.Error.Description}", 500));
            }

            await transaction.CommitAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(new Error("ServerError", ex.Message, 500));
        }
    }
    public async Task<Result> AddETOR(long IqamaNo, EMTOR request)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();

        try
        {
            var employee = await dbcontext.Employees
                        .Where(e => !e.IsDeleted)
                .Include(e => e.RiderDetails)
                .FirstOrDefaultAsync(e => e.IqamaNo == IqamaNo);

            if (employee is null)
                return Result.Failure(
                    new Error("NotFound", "Employee not found with the specified Iqama No", 404));

            if (employee.RiderDetails != null)
                return Result.Failure(
                    new Error("AlreadyExists", "Rider details already exist for this employee", 400));

            var company = await dbcontext.Companies
                .FirstOrDefaultAsync(c => c.Name == request.CompanyName);

            if (company is null)
                return Result.Failure(
                    new Error("NotFound", $"Company '{request.CompanyName}' not found", 404));

            var workingIdExists = await dbcontext.RiderDetails
                .AnyAsync(rd => rd.WorkingId == request.WorkingId);
            if (workingIdExists)
                return Result.Failure(
                    new Error("AlreadyExists",
                        $"WorkingId '{request.WorkingId}' is already assigned", 400));

            var riderDetails = new RiderDetails
            {
                EmployeeIqamaNo = IqamaNo,
                WorkingId = request.WorkingId,
                TshirtSize = request.TshirtSize,
                LicenseNumber = request.LicenseNumber,
                CompanyId = company.Id
            };

            await dbcontext.RiderDetails.AddAsync(riderDetails);
            await dbcontext.SaveChangesAsync();

            var historyResult = await _workingIdHistoryService.RecordWorkingIdChange(
                IqamaNo,
                request.WorkingId,
                company.Id,
                $"Rider details added - Company: {company.Name}",
                cancellationToken: default
            );

            if (historyResult.IsFailure)
            {
                await transaction.RollbackAsync();
                return Result.Failure(new Error("HistoryError",
                    $"Failed to record history: {historyResult.Error.Description}", 500));
            }

            await transaction.CommitAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(new Error("ServerError", ex.Message, 500));
        }
    }

    public async Task<Result<RiderResponse>> Getbyid(long Id)
    {
        var employee = await dbcontext.Employees
                    .Where(e => !e.IsDeleted)
            .Include(e => e.Housing)
            .Include(e => e.RiderDetails)
                .ThenInclude(rd => rd.Company)
            .FirstOrDefaultAsync(e => e.IqamaNo == Id);

        if (employee is null)
            return Result.Failure<RiderResponse>(
                new Error("NotFound", "No employee or rider found with this ID", 404));

        return Result.Success(MapToResponse(employee));
    }

    public async Task<Result<IEnumerable<RiderResponse>>> GetAllEmployeeNO()
    {
        var employees = await dbcontext.Employees
                    .Where(e => !e.IsDeleted)
            .AsNoTracking()
            .Where(r => !r.IsEmployee && r.RiderDetails != null &&
                        r.RiderDetails.VehicleNumber == null && r.Status == "disable")
            .Include(e => e.Housing)
            .Include(e => e.RiderDetails)
                .ThenInclude(rd => rd.Company)
            .ToListAsync();

        if (!employees.Any())
            return Result.Failure<IEnumerable<RiderResponse>>(
                new Error("NotFound", "No riders found matching criteria", 404));

        var response = employees.Select(MapToResponse).ToList();
        return Result.Success<IEnumerable<RiderResponse>>(response);
    }

    private static RiderResponse MapToResponse(Employees employee)
    {
        return new RiderResponse(
            IqamaNo: employee.IqamaNo,
            IsEmployee: employee.IsEmployee,
            IqamaEndM: employee.IqamaEndM,
            IqamaEndH: employee.IqamaEndH,
            PassportNo: employee.PassportNo ?? string.Empty,
            PassportEnd: employee.PassportEnd ?? default,
            Sponsor: employee.Sponsor,
            sponsorNo: employee.sponsorNo,
            JobTitle: employee.JobTitle,
            NameAR: employee.NameAR,
            NameEN: employee.NameEN,
            Country: employee.Country,
            Phone: employee.Phone,
            DateOfBirth: employee.DateOfBirth,
            Status: employee.Status,
            IBAN: employee.IBAN ?? string.Empty,
            INKSA: employee.INKSA,
            CreatedAt: employee.CreatedAt,
            HousingAddress: employee.Housing?.Name ?? "None",
            WorkingId: employee.RiderDetails?.WorkingId ?? "N/A",
            EmployeeIqamaNo: employee.IqamaNo,
            TshirtSize: employee.RiderDetails?.TshirtSize ?? "N/A",
            LicenseNumber: employee.RiderDetails?.LicenseNumber ?? "N/A",
            CompanyName: employee.RiderDetails?.Company?.Name ?? "N/A",
            RiderId : employee.RiderDetails?.Id
        );
    }
}


