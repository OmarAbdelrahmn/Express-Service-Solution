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

    public async Task<Result<EmployeeStatisticsResponse>> GetEmployeeStatistics()
    {
        try
        {
            var totalEmployees = await dbcontext.Employees
                .CountAsync();

            var totalRiders = await dbcontext.Employees
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
           .Where(r => r.IqamaNo.ToString().StartsWith(IqamaNo.ToString()))
           .Include(e => e.Housing)
           .Include(e => e.RiderDetails)
               .ThenInclude(rd => rd.Company)
           .AsNoTracking()
           .ToListAsync();

        if (isexist is null)
            return Result.Failure<IEnumerable<RiderResponse>>(error: new Error("No rider Found", "no rider found with this Iqama", 400));


        var res = isexist.Select(emp => new RiderResponse(
       emp.IqamaNo,
              emp.IsEmployee,
       emp.IqamaEndM,
       emp.IqamaEndH,
       emp.PassportNo!,
       emp.PassportEnd ?? default,
       emp.Sponsor,
       emp.sponsorNo,
       emp.JobTitle,
       emp.NameAR,
       emp.NameEN,
       emp.Country,
       emp.Phone,
       emp.DateOfBirth,
       emp.Status,
       emp.IBAN!,
       emp.INKSA,
       emp.CreatedAt,
       emp.Housing?.Name ?? "none",
       emp.RiderDetails?.WorkingId!,
         emp.IqamaNo,
            emp.RiderDetails?.TshirtSize,
            emp.RiderDetails?.LicenseNumber,
            emp.RiderDetails?.Company.Name
            )).ToList();


        return Result.Success<IEnumerable<RiderResponse>>(res);
    }
    public async Task<Result<IEnumerable<RiderResponse>>> GetAllEmployee()
    {
        var isexist = await dbcontext
            .Employees
            .AsNoTracking()
            .Include(e => e.Housing)
            .Include(e => e.RiderDetails)
                .ThenInclude(rd => rd.Company)
            .ToListAsync();

        if (isexist.Count == 0)
            return Result.Failure<IEnumerable<RiderResponse>>(
                new Error("No rider Found", "no rider", 400)
            );

        var res = isexist.Select(emp => new RiderResponse(
       emp.IqamaNo,
       emp.IsEmployee,
       emp.IqamaEndM,
       emp.IqamaEndH,
       emp.PassportNo!,
       emp.PassportEnd ?? default,
       emp.Sponsor,
       emp.sponsorNo,
       emp.JobTitle,
       emp.NameAR,
       emp.NameEN,
       emp.Country,
       emp.Phone,
       emp.DateOfBirth,
       emp.Status,
       emp.IBAN!,
       emp.INKSA,
       emp.CreatedAt,
       emp.Housing?.Name,
       emp.RiderDetails?.WorkingId!,
         emp.IqamaNo,
            emp.RiderDetails?.TshirtSize,
            emp.RiderDetails?.LicenseNumber,
            emp.RiderDetails?.Company.Name
            )).ToList();


        return Result.Success<IEnumerable<RiderResponse>>(res);
    }
    public async Task<Result> CreateAsync(RiderRequest Request)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();

        try
        {
            var isexist = dbcontext.RiderDetails.Any(x => x.EmployeeIqamaNo == Request.IqamaNo);
            if (isexist)
                return Result.Failure(error: new Error("rider exists", "rider already exists with this Iqama", 400));


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

            if (Request.IsEmployee && Request.WorkingId == null && Request.CompanyName == null)
            {

                await dbcontext.Employees.AddAsync(employee);
                await dbcontext.SaveChangesAsync();
                return Result.Success();


            }


            else
            {
                var Company = await dbcontext.Companies.FirstOrDefaultAsync(c => c.Name == Request.CompanyName);

                if (Company is null)
                    return Result.Failure(error: new Error("no company found", $"no company found with this name ", 400));


                employee.RiderDetails = new RiderDetails
                {
                    EmployeeIqamaNo = Request.IqamaNo,
                    WorkingId = Request.WorkingId,
                    TshirtSize = Request.TshirtSize,
                    LicenseNumber = Request.LicenseNumber,
                    CompanyId = Company.Id
                };

                await dbcontext.Employees.AddAsync(employee);
                await dbcontext.SaveChangesAsync();

                var historyResult = await _workingIdHistoryService.RecordWorkingIdChange(
                Request.IqamaNo,
                Request.WorkingId,
                Company.Id,
                $"Initial assignment - Company: {Company.Name}",
                cancellationToken: default  // ✅ Use named parameter
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
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(new Error("Server Error", ex.Message, 500));
        }
    }
    public async Task<Result> DeleteAsync(long IqamaNo, CancellationToken cancellationToken = default)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var isexist = await dbcontext.Employees.Include(c=>c.RiderDetails).FirstOrDefaultAsync(c=>c.IqamaNo == IqamaNo);

            if (isexist is null)
                return Result.Failure<EmpolyeeResponse>(error: new Error("No rider Found", "no rider found with this Iqama", 400));



            var Done = new DeletedEmployees
            {
                IqamaNo = isexist.IqamaNo,
                IqamaEndM = isexist.IqamaEndM,
                IqamaEndH = isexist.IqamaEndH,
                PassportNo = isexist.PassportNo,
                PassportEnd = isexist.PassportEnd,
                Sponsor = isexist.Sponsor,
                JobTitle = isexist.JobTitle,
                NameAR = isexist.NameAR,
                NameEN = isexist.NameEN,
                Country = isexist.Country,
                Phone = isexist.Phone,
                DateOfBirth = isexist.DateOfBirth.ToDateTime(TimeOnly.MaxValue),
                Status = isexist.Status,
                AcountStatus = isexist.Status,
                IBAN = isexist.IBAN,
                CreatedAt = isexist.CreatedAt,
                INKSA = isexist.INKSA,
                HousingId = isexist.HousingId,
                WorkingId = isexist.RiderDetails?.WorkingId,
                TshirtSize = isexist.RiderDetails?.TshirtSize,
                LicenseNumber = isexist.RiderDetails?.LicenseNumber,
                CompanyId = isexist.RiderDetails?.CompanyId,
                VehicleId = 0
            };

            //var histories = await dbcontext.RiderWorkingIdHistories
            //.Where(h => h.RiderIqamaNo == IqamaNo)
            //.ToListAsync(cancellationToken);

            //        dbcontext.RiderWorkingIdHistories.RemoveRange(histories);


            await dbcontext.DeletedEmployees.AddAsync(Done, cancellationToken);

            dbcontext.RiderDetails.Remove(isexist.RiderDetails);

          
            dbcontext.Employees.Remove(isexist);

            await dbcontext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure(new Error("error", ex.Message, 500));
        }
    }
    public async Task<Result<RiderResponse>> UpdateAsync(long IqamaNo, URiderRequest request)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();

        try
        {

            var employee = await dbcontext.Employees
        .Include(e => e.Housing)
        .Include(e => e.RiderDetails)
            .ThenInclude(rd => rd.Company)
        .FirstOrDefaultAsync(e => e.IqamaNo == IqamaNo);

            if (employee is null)
                return Result.Failure<RiderResponse>(error: new Error("No rider Found", "no rider found with this Iqama", 400));

            var riderDetails = employee.RiderDetails;


            bool workingIdChanged = false;
            int? newCompanyId = null;
            string? newWorkingId = null;

            if (!string.IsNullOrWhiteSpace(request.WorkingId) &&
                request.WorkingId != riderDetails.WorkingId)
            {
                newWorkingId = request.WorkingId;
                workingIdChanged = true;
            }

            if (request.CompanyName is not null)
            {
                var company = await dbcontext.Companies
                    .FirstOrDefaultAsync(c => c.Name == request.CompanyName);

                if (company is null)
                    return Result.Failure<RiderResponse>(
                        new Error("no company found", $"no company found with name {request.CompanyName}", 400));

                if (company.Id != riderDetails.CompanyId)
                {
                    newCompanyId = company.Id;
                    workingIdChanged = true;
                }
            }

            if (request.IqamaEndM.HasValue)
                employee.IqamaEndM = request.IqamaEndM.Value;
            if (request.IqamaEndH.HasValue)
                employee.IqamaEndH = request.IqamaEndH.Value;

            if (request.PassportNo is not null)
                employee.PassportNo = request.PassportNo;

            if (request.PassportEnd.HasValue)
                employee.PassportEnd = request.PassportEnd;

            if (request.Sponsor is not null)
                employee.Sponsor = request.Sponsor;
            
            if (request.sponsorNo is not null)
                employee.sponsorNo = request.sponsorNo ?? 0;

            if (request.JobTitle is not null)
                employee.JobTitle = request.JobTitle;

            if (request.NameAR is not null)
                employee.NameAR = request.NameAR;

            if (request.NameEN is not null)
                employee.NameEN = request.NameEN;

            if (request.Country is not null)
                employee.Country = request.Country;

            if (request.Phone is not null)
                employee.Phone = request.Phone;

            if (request.DateOfBirth.HasValue)
                employee.DateOfBirth = request.DateOfBirth.Value;

            if (request.Status is not null)
                employee.Status = request.Status;

            if (request.IBAN is not null)
                employee.IBAN = request.IBAN;

            if (request.INKSA is not null)
                employee.INKSA = request.INKSA.Value;

            if (!string.IsNullOrWhiteSpace(request.WorkingId) || int.TryParse(request.WorkingId, out var id) || id > 0)
                riderDetails.WorkingId = request.WorkingId;

            if (request.TshirtSize is not null)
                riderDetails.TshirtSize = request.TshirtSize;

            if (request.LicenseNumber is not null)
                riderDetails.LicenseNumber = request.LicenseNumber;

            if (request.CompanyName is not null)
            {
                var company = await dbcontext.Companies.FirstOrDefaultAsync(c => c.Name == request.CompanyName);
                if (company is null)
                    return Result.Failure<RiderResponse>(error: new Error("no company found", $"no company found with this name {request.CompanyName}", 400));
                riderDetails.CompanyId = company.Id;
            }

            await dbcontext.SaveChangesAsync();

            if (workingIdChanged)
            {
                var finalWorkingId = newWorkingId ?? riderDetails.WorkingId!;
                var finalCompanyId = newCompanyId ?? riderDetails.CompanyId;

                var company = await dbcontext.Companies
                    .AsNoTracking()  
                    .FirstOrDefaultAsync(c => c.Id == finalCompanyId);

                var historyResult = await _workingIdHistoryService.RecordWorkingIdChange(
                    IqamaNo,
                    finalWorkingId,
                    finalCompanyId,
                    $"Updated - Company: {company?.Name ?? "Unknown"}",
                    cancellationToken: default 
                );

                if (historyResult.IsFailure)
                {
                    await transaction.RollbackAsync();
                    return Result.Failure<RiderResponse>(new Error("HistoryError",
                        $"Failed to record history: {historyResult.Error.Description}", 500));
                }
            }

            var response = MapToResponse(employee, riderDetails);
            await transaction.CommitAsync();

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<RiderResponse>(new Error("Server Error", ex.Message, 500));
        }
    }
    public async Task<List<RiderResponse>> SmartSearch(string keyword)
    {
        keyword = keyword.ToLower();

        var query = dbcontext.Employees
            .Include(e => e.Housing)
            .Include(e => e.RiderDetails)
                .ThenInclude(rd => rd.Company)
            .Where(e =>
                e.NameAR.ToLower().Contains(keyword) ||
                e.NameEN.ToLower().Contains(keyword) ||
                e.Country.ToLower().Contains(keyword) ||
                e.Sponsor.ToLower().Contains(keyword) ||
                e.JobTitle.ToLower().Contains(keyword) ||
                e.IBAN.ToLower().Contains(keyword) ||
                e.IqamaNo .ToString().StartsWith(keyword) ||
                e.sponsorNo.ToString().ToLower().StartsWith(keyword)
            )
            .Select(emp => new RiderResponse(
                emp.IqamaNo,
                emp.IsEmployee,
                emp.IqamaEndM,
                emp.IqamaEndH,
                emp.PassportNo,
                emp.PassportEnd ?? default,
                emp.Sponsor,
                emp.sponsorNo,
                emp.JobTitle,
                emp.NameAR,
                emp.NameEN,
                emp.Country,
                emp.Phone,
                emp.DateOfBirth,
                emp.Status,
                emp.IBAN,
                emp.INKSA,
                emp.CreatedAt,

                emp.Housing.Name ?? "null",

                emp.RiderDetails.WorkingId,
                emp.IqamaNo,
                emp.RiderDetails.TshirtSize,
                emp.RiderDetails.LicenseNumber,
                emp.RiderDetails.Company.Name
            ));

        return await query.ToListAsync();
    }
    public async Task<Result<IEnumerable<RiderResponse>>> Filter(EmployeeFilterr filter)
    {
        var query = dbcontext.Employees
            .Include(e => e.RiderDetails)
                .ThenInclude(rd => rd.Company)
            .Include(e => e.Housing)
            .AsQueryable();

        if (filter.IqamaEndH is not null)
            query = query.Where(e => e.IqamaEndH == filter.IqamaEndH);

        if (filter.IqamaEndM is not null)
            query = query.Where(e => e.IqamaEndM == filter.IqamaEndM);

        if (!string.IsNullOrWhiteSpace(filter.Sponsor))
            query = query.Where(e => e.Sponsor.Contains(filter.Sponsor));

        if (filter.sponsorNo.HasValue)
            query = query.Where(e => e.sponsorNo == filter.sponsorNo.Value);

        if (filter.PassportEnd is not null)
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
            query = query.Where(e => e.RiderDetails.WorkingId.Contains(filter.WorkingId));
       
        if (!string.IsNullOrWhiteSpace(filter.CompanyName))
            query = query.Where(e => e.RiderDetails.Company.Name.Contains(filter.CompanyName));

        if (filter.INKSA is not null)
            query = query.Where(e => e.INKSA == filter.INKSA);

        if (!string.IsNullOrWhiteSpace(filter.HousingName))
            query = query.Where(e => e.Housing != null &&
                                     e.Housing.Name.Contains(filter.HousingName));

        var res = query.Select(emp => new RiderResponse(
       emp.IqamaNo,
         emp.IsEmployee,
       emp.IqamaEndM,
       emp.IqamaEndH,
       emp.PassportNo!,
       emp.PassportEnd ?? default,
       emp.Sponsor,
       emp.sponsorNo,
       emp.JobTitle,
       emp.NameAR,
       emp.NameEN,
       emp.Country,
       emp.Phone,
       emp.DateOfBirth,
       emp.Status,
       emp.IBAN!,
       emp.INKSA,
       emp.CreatedAt,
       emp.Housing.Name ?? "none",
       emp.RiderDetails.WorkingId!,
         emp.IqamaNo,
            emp.RiderDetails.TshirtSize!,
            emp.RiderDetails.LicenseNumber!,
            emp.RiderDetails.Company.Name
            )).ToList();


        return Result.Success<IEnumerable<RiderResponse>>(res);
    }


    private static RiderResponse MapToResponse(Employees employee, RiderDetails rider)
    {
        return new RiderResponse(
            IqamaNo: employee.IqamaNo,
             employee.IsEmployee,
            IqamaEndM: employee.IqamaEndM,
            IqamaEndH: employee.IqamaEndH,
            PassportNo: employee.PassportNo,
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
            IBAN: employee.IBAN,
            CreatedAt: employee.CreatedAt,
            INKSA: employee.INKSA,
            HousingAddress: employee.Housing?.Name,
            WorkingId: rider.WorkingId ?? "0",
            EmployeeIqamaNo: rider.EmployeeIqamaNo,
            TshirtSize: rider.TshirtSize!,
            LicenseNumber: rider.LicenseNumber!,
            CompanyName: rider.Company.Name
        );
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
            var employee = await dbcontext.Employees.AnyAsync(e => e.IqamaNo == IqamaNo);

            if (!employee)
                return Result.Failure(new Error("Not Found", "No employee found with the specified Iqama No", 404));

            var isRider = await dbcontext.RiderDetails.AnyAsync(r => r.EmployeeIqamaNo == IqamaNo);

            if (isRider)
                return Result.Failure(new Error("Found", "rider details found for this employee", 404));


            var Company = await dbcontext.Companies.FirstOrDefaultAsync(c => c.Name == request.CompanyName);

            if (Company is null)
                return Result.Failure(error: new Error("no company found", $"no company found with this name {Company.Name}", 400));

            var emtor = new RiderDetails
            {
                EmployeeIqamaNo = IqamaNo,
                WorkingId = request.WorkingId,
                TshirtSize = request.TshirtSize,
                LicenseNumber = request.LicenseNumber,
                CompanyId = Company.Id
            };
            await dbcontext.RiderDetails.AddAsync(emtor);
            await dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(new Error("Server Error", ex.Message, 500));

        }
    }

    public async Task<Result<RiderResponse>> Getbyid(int Id)
    {
        var rider = await
            dbcontext.Employees
            .Include(e => e.Housing)
            .Include(e => e.RiderDetails)
                .ThenInclude(rd => rd.Company)
            .FirstOrDefaultAsync(e => e.IqamaNo == Id && !e.IsEmployee);

        if (rider is null)
            return Result.Failure<RiderResponse>(error: new Error("No rider Found", "no rider found with this Iqama", 400));

        var response = new RiderResponse(
            rider.IqamaNo,
            rider.IsEmployee,
            rider.IqamaEndM,
            rider.IqamaEndH,
            rider.PassportNo!,
            rider.PassportEnd ?? default,
            rider.Sponsor,
            rider.sponsorNo,
            rider.JobTitle,
            rider.NameAR,
            rider.NameEN,
            rider.Country,
            rider.Phone,
            rider.DateOfBirth,
            rider.Status,
            rider.IBAN!,
            rider.INKSA,
            rider.CreatedAt,
            rider.Housing?.Name,
            rider.RiderDetails!.WorkingId!,
            rider.IqamaNo,
            rider.RiderDetails.TshirtSize!,
            rider.RiderDetails.LicenseNumber!,
            rider.RiderDetails.Company.Name
            );
        return Result.Success(response);
    }

    public async Task<Result<IEnumerable<RiderResponse>>> GetAllEmployeeNO()
    {
        var isexist = await dbcontext
            .Employees
            .AsNoTracking()
            .Where(r => r.IsEmployee == false && r.RiderDetails.VehicleNumber == null && r.Status == "disable")
            .Include(e => e.Housing)
            .Include(e => e.RiderDetails)
                .ThenInclude(rd => rd.Company)
            .ToListAsync();

        if (isexist.Count == 0)
            return Result.Failure<IEnumerable<RiderResponse>>(
                new Error("No rider Found", "no rider", 400)
            );

        var res = isexist.Select(emp => new RiderResponse(
       emp.IqamaNo,
         emp.IsEmployee,
       emp.IqamaEndM,
       emp.IqamaEndH,
       emp.PassportNo!,
       emp.PassportEnd ?? default,
       emp.Sponsor,
       emp.sponsorNo,
       emp.JobTitle,
       emp.NameAR,
       emp.NameEN,
       emp.Country,
       emp.Phone,
       emp.DateOfBirth,
       emp.Status,
       emp.IBAN!,
       emp.INKSA,
       emp.CreatedAt,
       emp.Housing?.Name,
       emp.RiderDetails.WorkingId!,
         emp.IqamaNo,
            emp.RiderDetails.TshirtSize!,
            emp.RiderDetails.LicenseNumber!,
            emp.RiderDetails.Company.Name
            )).ToList();


        return Result.Success<IEnumerable<RiderResponse>>(res);
    }
}

