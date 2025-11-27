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
            .Where(e => e.IqamaNo.ToString().StartsWith(IqamaNo.ToString()))
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
       emp.Sponsor,
       emp.SponsorNo,
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
       emp.Housing?.Name   // safe
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
       emp.Sponsor,
       emp.SponsorNo,
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
       emp.Housing?.Name   // safe
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
        var isexist = await dbcontext.Employees.FindAsync(IqamaNo, cancellationToken);

        if (isexist is null)
            return Result.Failure<EmpolyeeResponse>(error: new Error("No Employee Found", "no employee found with this Iqama", 400));

        var Done = isexist.Adapt<DeletedEmployees>();

        await dbcontext.DeletedEmployees.AddAsync(Done, cancellationToken);

        dbcontext.Employees.Remove(isexist);

        dbcontext.SaveChanges();

        return Result.Success();
    }

    public async Task<Result<EmpolyeeResponse>> UpdateAsync(int IqamaNo, UEmpolyeeRequest request)
    {
        var employee = await dbcontext.Employees.Where(c => c.IqamaNo == IqamaNo).Include(c => c.Housing).FirstOrDefaultAsync();

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

        if (request.SponserNo.HasValue)
            employee.SponsorNo = request.SponserNo.Value;

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
           emp.Sponsor,
           emp.SponsorNo,
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
           emp.Housing.Name ?? null  // safe
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
            HousingAddress: employee.Housing?.Name,
            SponserNo: employee.SponsorNo
        );
    }

    public async Task<Result<PagedList<EmpolyeeResponse>>> Filter2(EmployeeFilter2 filter)
    {

        var query = dbcontext.Employees
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
                   emp.Sponsor,
                   emp.SponsorNo,
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
                   emp.Housing.Name ?? null  // safe
                        )).ToListAsync();

        return Result.Success(
            new PagedList<EmpolyeeResponse>(data, totalCount, filter.Page, filter.PageSize)
        );
    }

    public async Task<List<EmpolyeeResponse>> SmartSearch(string keyword)
    {

        keyword = keyword.ToLower();

        var query = dbcontext.Employees
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
           emp.Sponsor,
           emp.SponsorNo,
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
           emp.Housing.Name ?? null  // safe
                )).ToListAsync();
    }

}