using Application.Abstraction;
using Application.Contracts.Employees;
using Domain;
using Domain.Entities;
using Mapster;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Empolyee;

public class HousingService(ApplicationDbcontext dbcontext) : IHousingService
{
    private readonly ApplicationDbcontext dbcontext = dbcontext;


    public async Task<Result<HousingResponse>> CreateAsync(HousingRequest Request)
    {
        var isExist = await dbcontext.Housings.AnyAsync(c => c.Name == Request.Name);

        if (isExist)
            return Result.Failure<HousingResponse>(new Error("Housing.AlreadyExists", $"House with name {Request.Name} already exists.", 409));

        var company = Request.Adapt<Housing>();

        dbcontext.Housings.Add(company);

        await dbcontext.SaveChangesAsync();

        var companyResponses = company.Adapt<HousingResponse>();

        return Result.Success(companyResponses);
    }

    public async Task<Result> DeleteAsync(string Name, CancellationToken cancellationToken = default)
    {
        var companies = await dbcontext.Housings.Where(c => c.Name == Name).SingleOrDefaultAsync();

        if (companies == null)
            return Result.Failure(new Error("housing.NotFound", $"house with name {Name} was not found.", 404));

        dbcontext.Housings.Remove(companies);
        await dbcontext.SaveChangesAsync(cancellationToken);

        return Result.Success();

    }

    public async Task<Result<IEnumerable<HousingResponse>>> Get(string Name)
    {
        var companies = await dbcontext.Housings.Where(c => c.Name.StartsWith(Name)).Include(c=>c.Employees).AsNoTracking().ToListAsync();

        if (companies == null)
            return Result.Failure<IEnumerable<HousingResponse>>(new Error("Housing.NotFound", $"Housing starts with name {Name} was not found.", 404));

        var companyResponses = companies.Adapt<IEnumerable<HousingResponse>>();

        return Result.Success(companyResponses); 
    }

    public async Task<Result<IEnumerable<HousingResponse>>> GetAllEmployee()
    {
        var companies = await dbcontext.Housings.Include(c => c.Employees).AsNoTracking().ToListAsync();

        if (companies == null)
            return Result.Failure<IEnumerable<HousingResponse>>(new Error("housing.NotFound", " no Housing found.", 404));

        var companyResponses = companies.Adapt<IEnumerable<HousingResponse>>();

        return Result.Success(companyResponses);
    }

    public async Task<Result<IEnumerable<HousingResponse>>> GetWithManagerIqama(int ManagerIqamaNo)
    {
        var companies = await dbcontext.Housings.Where(c => c.ManagerId == ManagerIqamaNo).Include(c => c.Employees).AsNoTracking().ToListAsync();

        if (companies == null)
            return Result.Failure<IEnumerable<HousingResponse>>(new Error("housing.NotFound", $"Housing with ManagerIqamaNo {ManagerIqamaNo} was not found.", 404));

        var companyResponses = companies.Adapt<IEnumerable<HousingResponse>>();

        return Result.Success(companyResponses);
    }

    public async Task<Result<UHousingResponse>> UpdateAsync(string Name, HousingRequest Request)
    {
        var companies = await dbcontext.Housings.Where(c => c.Name == Name).FirstOrDefaultAsync();

        if (companies == null)
            return Result.Failure<UHousingResponse>(new Error("house.NotFound", $"Housing with name {Name} was not found.", 404));

       
        companies.Name = Request.Name;
        companies.Address = Request.Address;
        companies.Capacity = Request.Capacity;
        companies.ManagerId = Request.ManagerIqamaNo;

        await dbcontext.SaveChangesAsync();

        var companyResponses = companies.Adapt<UHousingResponse>();
        return Result.Success(companyResponses);
    }


}

