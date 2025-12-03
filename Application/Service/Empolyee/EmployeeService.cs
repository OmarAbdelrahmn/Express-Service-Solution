using Application.Abstraction;
using Application.Abstraction.Errors;
using Application.Contracts.Employees;
using Application.Contracts.Roles;
using Azure.Core;
using Domain;
using Domain.Entities;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Application.Service.Empolyee;

public class EmployeeService(ApplicationDbcontext dbcontext) : IEmployeeService
{
    private readonly ApplicationDbcontext dbcontext = dbcontext;

    public async Task<Result<IEnumerable<EmpolyeeResponse>>> Get(int IqamaNo)
    {
        var isexist = await dbcontext
            .Employees
            .AsNoTracking()
            .Where(e => e.IqamaNo.ToString().StartsWith(IqamaNo.ToString()) && e.RiderDetails == null)
            .Include(e => e.Housing)
            .ToListAsync();

        if (isexist.Count == 0)
            return Result.Failure<IEnumerable<EmpolyeeResponse>>(
                new Error("No Employees Found", "no employees", 400)
            );

        var res = isexist.Select(emp => new EmpolyeeResponse(
       emp.IqamaNo,
       emp.IqamaEndM,
       emp.IqamaEndH,
       emp.PassportNo!,
       emp.PassportEnd ?? default,
       emp.SponsorNo,
       emp.Sponsor,
       emp.JobTitle,
       emp.NameAR,
       emp.NameEN,
       emp.Country,
       emp.Phone,
       emp.DateOfBirth,
       emp.Status,
       emp.IBAN!,
       emp.INKSA,
       emp.CreatedAt
            )).ToList();


        return Result.Success<IEnumerable<EmpolyeeResponse>>(res);
    }

    public async Task<Result<IEnumerable<EmpolyeeResponse>>> GetAllEmployee()
    {
        var isexist = await dbcontext
            .Employees
            .AsNoTracking()
            .Where(c => c.RiderDetails == null)
            .Include(e => e.Housing)
            .ToListAsync();

        if (isexist.Count == 0)
            return Result.Failure<IEnumerable<EmpolyeeResponse>>(
                new Error("No Employees Found", "no employees", 400)
            );

        var res = isexist.Select(emp => new EmpolyeeResponse(
       emp.IqamaNo,
       emp.IqamaEndM,
       emp.IqamaEndH,
       emp.PassportNo!,
       emp.PassportEnd ?? default,
       emp.SponsorNo,
       emp.Sponsor,
       emp.JobTitle,
       emp.NameAR,
       emp.NameEN,
       emp.Country,
       emp.Phone,
       emp.DateOfBirth,
       emp.Status,
       emp.IBAN!,
       emp.INKSA,
       emp.CreatedAt
            )).ToList();


        return Result.Success<IEnumerable<EmpolyeeResponse>>(res);
    }

    public async Task<Result<EmpolyeeResponse>> CreateAsync(EmpolyeeRequest Request)
    {
        var isexist = dbcontext.Employees.Any(x => x.IqamaNo == Request.IqamaNo);
        if (isexist)
            return Result.Failure<EmpolyeeResponse>(error: new Error("No Employee Found", "no employee found with this Iqama", 400));


        var Em = Request.Adapt<Employees>();

        await dbcontext.Employees.AddAsync(Em);
        await dbcontext.SaveChangesAsync();

        var response = Em.Adapt<EmpolyeeResponse>();
        return Result.Success(response);
    }

    public async Task<Result> DeleteAsync(int IqamaNo, CancellationToken cancellationToken = default)
    {
        var employee = await dbcontext.Employees.Where(c => c.IqamaNo == IqamaNo && c.RiderDetails == null).FirstOrDefaultAsync();

        if (employee is null)
            return Result.Failure<EmpolyeeResponse>(error: new Error("No Employee Found", "no employee found with this Iqama", 400));

        var Done = employee.Adapt<DeletedEmployees>();

        await dbcontext.DeletedEmployees.AddAsync(Done, cancellationToken);

        dbcontext.Employees.Remove(employee);

        dbcontext.SaveChanges();

        return Result.Success();
    }

    public async Task<Result<EmpolyeeResponse>> UpdateAsync(int IqamaNo, UEmpolyeeRequest request)
    {
        var employee = await dbcontext.Employees.Where(c => c.IqamaNo == IqamaNo && c.RiderDetails == null).Include(c => c.Housing).FirstOrDefaultAsync();

        if (employee is null)
            return Result.Failure<EmpolyeeResponse>(error: new Error("No Employee Found", "no employee found with this Iqama", 400));


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

        if (request.SponsorNo.HasValue)
            employee.SponsorNo = request.SponsorNo.Value;

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


        await dbcontext.SaveChangesAsync();

        var response = MapToResponse(employee);

        return Result.Success(response);
    }

    public async Task<Result> AddEmployeeToHousing(int IqamaNo, string HousingName)
    {
        var isexist = await dbcontext
            .Employees
            .Where(e => e.IqamaNo == IqamaNo)
            .AsNoTracking()
            .SingleOrDefaultAsync();


        if (isexist is null)
            return Result.Failure<EmpolyeeResponse>(error: new Error("No Employee Found", "no employee found with this Iqama", 400));

        if (isexist.HousingId is not null)
            return Result.Failure<EmpolyeeResponse>(error: new Error("AlreadyIn", "This Employee is already In housing if you want to change the housing go to change housing page", 400));


        var housingId = await dbcontext
            .Housings
            .Where(c => c.Name == HousingName)
            .Select(c => c.Id)
            .SingleOrDefaultAsync();

        if (housingId == 0)
            return Result.Failure<EmpolyeeResponse>(error: new Error("No housing Found", "no housing found with this name", 400));


        isexist.HousingId = housingId;

        dbcontext.Update(isexist);
        await dbcontext.SaveChangesAsync();

        var response = MapToResponse(isexist);

        return Result.Success();

    }

    public async Task<Result> ChangeEmployeeToHousing(int IqamaNo, string oldHousingName, string NewHousingName)
    {
        var isexist = await dbcontext
           .Employees
           .Where(e => e.IqamaNo == IqamaNo)
           .AsNoTracking()
           .SingleOrDefaultAsync();


        if (isexist is null)
            return Result.Failure<EmpolyeeResponse>(error: new Error("No Employee Found", "no employee found with this Iqama", 400));

        if (isexist.HousingId is null)
            return Result.Failure<EmpolyeeResponse>(error: new Error("he is not in housing", "This Employee is not In housing if you want to add him go to add housing page", 400));


        var housingId = await dbcontext
            .Housings
            .Where(c => c.Name == oldHousingName)
            .Select(c => c.Id)
            .SingleOrDefaultAsync();

        var newhousingId = await dbcontext
            .Housings
            .Where(c => c.Name == NewHousingName)
            .Select(c => c.Id)
            .SingleOrDefaultAsync();

        if (housingId == 0 || newhousingId == 0)
            return Result.Failure<EmpolyeeResponse>(error: new Error("No housing Found", "no housing found with this name", 400));

        isexist.HousingId = newhousingId;

        dbcontext.Update(isexist);

        await dbcontext.SaveChangesAsync();

        return Result.Success();

    }
    public async Task<Result<IEnumerable<EmpolyeeResponse>>> Filter(EmployeeFilter filter)
    {
        var query = dbcontext.Employees
            .Where(e => e.RiderDetails == null)
            .Include(e => e.Housing)
            .AsQueryable();

        if (filter.IqamaEndH is not null)
            query = query.Where(e => e.IqamaEndH == filter.IqamaEndH);

        if (filter.IqamaEndM is not null)
            query = query.Where(e => e.IqamaEndM == filter.IqamaEndM);

        if (!string.IsNullOrWhiteSpace(filter.Sponsor))
            query = query.Where(e => e.Sponsor.Contains(filter.Sponsor));

        if (filter.SponsorNo.HasValue)
            query = query.Where(e => e.SponsorNo == filter.SponsorNo.Value);

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

        if (filter.INKSA is not null)
            query = query.Where(e => e.INKSA == filter.INKSA);

        if (!string.IsNullOrWhiteSpace(filter.HousingName))
            query = query.Where(e => e.Housing != null &&
                                     e.Housing.Name.Contains(filter.HousingName));

        var list = await query
            .Select(emp => new EmpolyeeResponse(
           emp.IqamaNo,
           emp.IqamaEndM,
           emp.IqamaEndH,
           emp.PassportNo!,
           emp.PassportEnd ?? default,
           emp.SponsorNo,
           emp.Sponsor,
           emp.JobTitle,
           emp.NameAR,
           emp.NameEN,
           emp.Country,
           emp.Phone,
           emp.DateOfBirth,
           emp.Status,
           emp.IBAN!,
           emp.INKSA,
           emp.CreatedAt
                )).ToListAsync();


        return Result.Success<IEnumerable<EmpolyeeResponse>>(list);
    }

    private static EmpolyeeResponse MapToResponse(Employees employee)
    {
        return new EmpolyeeResponse(
            IqamaNo: employee.IqamaNo,
            IqamaEndM: employee.IqamaEndM,
            IqamaEndH: employee.IqamaEndH,
            PassportNo: employee.PassportNo,
            PassportEnd: employee.PassportEnd ?? default,
            Sponsor: employee.Sponsor,
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
            SponsorNo: employee.SponsorNo
        );
    }

    public async Task<Result<PagedList<EmpolyeeResponse>>> Filter2(EmployeeFilter2 filter)
    {

        var query = dbcontext.Employees
            .Where(e => e.RiderDetails == null)
            .Include(e => e.Housing)
            .AsQueryable();

        if (filter.Sponsor?.Any() == true)
            query = query.Where(e => filter.Sponsor!.Contains(e.Sponsor));

        if (filter.SponsorNo?.ToString().Any() == true)
            query = query.Where(e => filter.SponsorNo!.ToString().Contains(e.SponsorNo.ToString()));

        if (filter.JobTitle?.Any() == true)
            query = query.Where(e => filter.JobTitle!.Contains(e.JobTitle));

        if (filter.NameAR?.Any() == true)
            query = query.Where(e => filter.NameAR!.Any(v => e.NameAR.Contains(v)));

        if (filter.NameEN?.Any() == true)
            query = query.Where(e => filter.NameEN!.Any(v => e.NameEN.Contains(v)));

        if (filter.Country?.Any() == true)
            query = query.Where(e => filter.Country!.Contains(e.Country));

        if (filter.Status?.Any() == true)
            query = query.Where(e => filter.Status!.Contains(e.Status));

        if (filter.INKSA is not null)
            query = query.Where(e => e.INKSA == filter.INKSA);

        if (filter.HousingName?.Any() == true)
            query = query.Where(e => e.Housing != null &&
                                     filter.HousingName!.Contains(e.Housing.Name));


        if (!string.IsNullOrWhiteSpace(filter.SortBy))
        {
            bool descending = filter.SortDirection?.ToUpper() == "DESC";

            query = filter.SortBy switch
            {
                "IqamaEndH" => descending ? query.OrderByDescending(e => e.IqamaEndH)
                                             : query.OrderBy(e => e.IqamaEndH),

                "IqamaEndM" => descending ? query.OrderByDescending(e => e.IqamaEndM)
                                             : query.OrderBy(e => e.IqamaEndM),

                "Sponsor" => descending ? query.OrderByDescending(e => e.Sponsor)
                                             : query.OrderBy(e => e.Sponsor),

                "SponsorNo" => descending ? query.OrderByDescending(e => e.SponsorNo)
                                             : query.OrderBy(e => e.SponsorNo),

                "JobTitle" => descending ? query.OrderByDescending(e => e.JobTitle)
                                             : query.OrderBy(e => e.JobTitle),

                _ => query
            };
        }


        int skip = (filter.Page - 1) * filter.PageSize;

        var totalCount = await query.CountAsync();

        var data = await query
            .Skip(skip)
            .Take(filter.PageSize)
            .Select(emp => new EmpolyeeResponse(
                   emp.IqamaNo,
                   emp.IqamaEndM,
                   emp.IqamaEndH,
                   emp.PassportNo!,
                   emp.PassportEnd ?? default,
                     emp.SponsorNo,
                   emp.Sponsor,
                   emp.JobTitle,
                   emp.NameAR,
                   emp.NameEN,
                   emp.Country,
                   emp.Phone,
                   emp.DateOfBirth,
                   emp.Status,
                   emp.IBAN!,
                   emp.INKSA,
                   emp.CreatedAt
                        )).ToListAsync();

        return Result.Success(
            new PagedList<EmpolyeeResponse>(data, totalCount, filter.Page, filter.PageSize)
        );
    }

    public async Task<List<EmpolyeeResponse>> SmartSearch(string keyword)
    {

        keyword = keyword.ToLower();

        var query = dbcontext.Employees.Where(e => e.RiderDetails == null)
            .Include(e => e.Housing)
            .Where(e =>
                e.NameAR.ToLower().Contains(keyword) ||
                e.NameEN.ToLower().Contains(keyword) ||
                e.Country.ToLower().Contains(keyword) ||
                e.Sponsor.ToLower().Contains(keyword) ||
                e.SponsorNo.ToString().Contains(keyword) ||
                e.JobTitle.ToLower().Contains(keyword)
            );

        return await query
            .Select(emp => new EmpolyeeResponse(
           emp.IqamaNo,
           emp.IqamaEndM,
           emp.IqamaEndH,
           emp.PassportNo!,
           emp.PassportEnd ?? default,
                  emp.SponsorNo,
           emp.Sponsor,
           emp.JobTitle,
           emp.NameAR,
           emp.NameEN,
           emp.Country,
           emp.Phone,
           emp.DateOfBirth,
           emp.Status,
           emp.IBAN!,
           emp.INKSA,
           emp.CreatedAt
                )).ToListAsync();
    }

    public async Task<Result> RequestEnableEmployeeAsync(int iqamaNo, string reason, string requestedBy)
    {
        try
        {
            var employee = await dbcontext.Employees
                .Include(e => e.RiderDetails)
                .FirstOrDefaultAsync(e => e.IqamaNo == iqamaNo);

            if (employee == null)
                return Result.Failure(
                    new Error("NotFound", "Employee not found", 404));

            // Check if there's already a pending request for this employee
            var existingRequest = await dbcontext.TempEmployeeStatusChanges
                .AnyAsync(t => t.EmployeeIqamaNo == iqamaNo && !t.IsResolved);

            if (existingRequest)
                return Result.Failure(
                    new Error("PendingRequest",
                        "There is already a pending status change request for this employee", 400));

            var statusChange = new TempEmployeeStatusChange
            {
                EmployeeIqamaNo = iqamaNo,
                Action = "enable",
                Reason = reason,
                RequestedBy = requestedBy,
                RequestedAt = DateTime.Now,
                IsResolved = false
            };

            await dbcontext.TempEmployeeStatusChanges.AddAsync(statusChange);
            await dbcontext.SaveChangesAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(
                new Error("RequestError", $"Failed to request enable employee: {ex.Message}", 500));
        }
    }

    public async Task<Result> RequestDisableEmployeeAsync(int iqamaNo, string reason, string requestedBy)
    {
        try
        {
            var employee = await dbcontext.Employees
                .Include(e => e.RiderDetails)
                .FirstOrDefaultAsync(e => e.IqamaNo == iqamaNo);

            if (employee == null)
                return Result.Failure(
                    new Error("NotFound", "Employee not found", 404));

            var existingRequest = await dbcontext.TempEmployeeStatusChanges
                .AnyAsync(t => t.EmployeeIqamaNo == iqamaNo && !t.IsResolved);

            if (existingRequest)
                return Result.Failure(
                    new Error("PendingRequest",
                        "There is already a pending status change request for this employee", 400));

            var statusChange = new TempEmployeeStatusChange
            {
                EmployeeIqamaNo = iqamaNo,
                Action = "Disable",
                Reason = reason,
                RequestedBy = requestedBy,
                RequestedAt = DateTime.Now,
                IsResolved = false
            };

            await dbcontext.TempEmployeeStatusChanges.AddAsync(statusChange);
            await dbcontext.SaveChangesAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(
                new Error("RequestError", $"Failed to request disable employee: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<TempEmployeeStatusChangeResponse>>> GetPendingStatusChangesAsync()
    {
        try
        {
            var pendingChanges = await dbcontext.TempEmployeeStatusChanges
                .Where(t => !t.IsResolved)
                .Include(t => t.Employee)
                .OrderBy(t => t.RequestedAt)
                .ToListAsync();

            var responses = pendingChanges.Select(MapToResponse).ToList();

            return Result.Success<IEnumerable<TempEmployeeStatusChangeResponse>>(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<TempEmployeeStatusChangeResponse>>(
                new Error("GetPendingError", $"Failed to get pending status changes: {ex.Message}", 500));
        }
    }

    public async Task<Result> ResolveStatusChangesAsync(EBulkResolutionRequest request)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            if (request.Resolution != "Approved" && request.Resolution != "Rejected")
                return Result.Failure<BulkResolutionResponse>(
                    new Error("InvalidResolution", "Resolution must be 'Approved' or 'Rejected'", 400));

            var statusChanges = await dbcontext.TempEmployeeStatusChanges
                .Where(t => request.IqamaNo == t.EmployeeIqamaNo && !t.IsResolved)
                .Include(t => t.Employee)
                .SingleOrDefaultAsync();

            if (statusChanges is null)
                return Result.Failure<BulkResolutionResponse>(
                    new Error("NoChanges", "No pending status changes found with IqamaNo", 404));

            try
            {
                if (request.Resolution == "Approved")
                {
                    var employee = await dbcontext.Employees
                        .FirstOrDefaultAsync(u => u.IqamaNo == statusChanges.EmployeeIqamaNo);

                    if (employee == null)
                    {
                        return Result.Failure(new Error(
                            "not_found",
                            $"Warning: No Employee found for {statusChanges.EmployeeIqamaNo}",
                            404
                        ));
                    }

                    employee.Status = statusChanges.Action == "enable" ? "enable" : "disable";
                    dbcontext.Employees.Update(employee);

                    statusChanges.IsResolved = true;
                    statusChanges.Resolution = request.Resolution;   // "Approved" or "Rejected"
                    statusChanges.ResolvedBy = request.ResolvedBy;
                    statusChanges.ResolvedAt = DateTime.Now;

                    await dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Result.Success();
                }
                else
                {
                    statusChanges.AdminNotes = request.AdminNot ?? "Request was rejected";
                    // Always update statusChanges
                    statusChanges.IsResolved = true;
                    statusChanges.Resolution = request.Resolution;   // "Approved" or "Rejected"
                    statusChanges.ResolvedBy = request.ResolvedBy;
                    statusChanges.ResolvedAt = DateTime.Now;

                    await dbcontext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Result.Success();
                }

                
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Result.Failure(new Error(
                    "error",
                    $"Something went wrong: {ex.Message}",
                    500
                ));
            }
        }

        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure<BulkResolutionResponse>(
                new Error("ResolveError", $"Failed to resolve status changes: {ex.Message}", 500));
        }
        }
        
    private TempEmployeeStatusChangeResponse MapToResponse(TempEmployeeStatusChange statusChange)
    {
        return new TempEmployeeStatusChangeResponse(
            Id: statusChange.Id,
            EmployeeIqamaNo: statusChange.EmployeeIqamaNo,
            EmployeeNameAR: statusChange.Employee?.NameAR ?? "N/A",
            EmployeeNameEN: statusChange.Employee?.NameEN ?? "N/A",
            Action: statusChange.Action,
            Reason: statusChange.Reason,
            RequestedBy: statusChange.RequestedBy,
            RequestedAt: statusChange.RequestedAt,
            IsResolved: statusChange.IsResolved,
            Resolution: statusChange.Resolution,
            ResolvedBy: statusChange.ResolvedBy,
            ResolvedAt: statusChange.ResolvedAt
        );
    }

    public async Task<Result<IEnumerable<DeletedEmployees>>> GetAlldeletedEmployee()
    {
        var employees = await dbcontext.DeletedEmployees.AsNoTracking().ToListAsync();

        if (employees.Count == 0)
            return Result.Failure<IEnumerable<DeletedEmployees>>(
                new Error("No Deleted Employees Found", "no deleted employees", 400)
            );

        return Result.Success<IEnumerable<DeletedEmployees>>(employees);
    }
}



public record TempEmployeeStatusChangeResponse(
    int Id,
    int EmployeeIqamaNo,
    string EmployeeNameAR,
    string EmployeeNameEN,
    string Action,
    string? Reason,
    string RequestedBy,
    DateTime RequestedAt,
    bool IsResolved,
    string? Resolution,
    string? ResolvedBy,
    DateTime? ResolvedAt
);