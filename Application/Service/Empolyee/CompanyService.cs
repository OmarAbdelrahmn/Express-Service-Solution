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

public class CompanyService(ApplicationDbcontext dbcontext) : ICompanyService
{
    private readonly ApplicationDbcontext dbcontext = dbcontext;

    public async Task<Result<CompanyResponse>> CreateAsync(CompanyRequest Request)
    {
        var isExist =await dbcontext.Companies.AnyAsync(c => c.Name == Request.Name);

        if (isExist)
            return Result.Failure<CompanyResponse>(new Error("Company.AlreadyExists", $"Company with name {Request.Name} already exists.", 409));

        var company = Request.Adapt<Company>();

        dbcontext.Companies.Add(company);

        await dbcontext.SaveChangesAsync();
        
        var companyResponses = company.Adapt<CompanyResponse>();

        return Result.Success(companyResponses);
    }

    public async Task<Result> DeleteAsync(string CompanyName, CancellationToken cancellationToken = default)
    {
        var companies = await dbcontext.Companies.Where(c => c.Name == CompanyName).SingleOrDefaultAsync();

        if (companies == null)
            return Result.Failure(new Error("Company.NotFound", $"Company with name {CompanyName} was not found.", 404));

        dbcontext.Companies.Remove(companies);
        await dbcontext.SaveChangesAsync(cancellationToken);

        return Result.Success();

    }

    public async Task<Result<IEnumerable<CompanyResponse>>> Get(string CompanyName)
    {
        var companies = await dbcontext.Companies.Where(c => c.Name.StartsWith(CompanyName)).AsNoTracking().ToListAsync();

        if (companies == null)
            return Result.Failure<IEnumerable<CompanyResponse>>(new Error("Company.NotFound", $"Companies starts with name {CompanyName} was not found.", 404));

        var companyResponses = companies.Adapt<IEnumerable<CompanyResponse>>();

        return Result.Success(companyResponses); 
    }

    public async Task<Result<IEnumerable<CompanyResponse>>> GetAllEmployee()
    {
        var companies = await dbcontext.Companies.AsNoTracking().ToListAsync();

        if (companies == null)
            return Result.Failure<IEnumerable<CompanyResponse>>(new Error("CompanIes.NotFound", " no Company found.", 404));

        var companyResponses = companies.Adapt<IEnumerable<CompanyResponse>>();

        return Result.Success(companyResponses);
    }

    public async Task<Result<CompanyResponse>> UpdateAsync(string CompanyName, CompanyRequest Request)
    {
        var companies = await dbcontext.Companies.Where(c => c.Name == CompanyName).SingleOrDefaultAsync();

        if (companies == null)
            return Result.Failure<CompanyResponse>(new Error("Company.NotFound", $"Company with name {CompanyName} was not found.", 404));
        companies.Name = Request.Name;
        companies.Address = Request.Address;
        companies.Phone = Request.Phone;
        companies.Email = Request.Email;
        companies.Details = Request.Details;

        dbcontext.Companies.Update(companies);
        await dbcontext.SaveChangesAsync();

        var companyResponses = companies.Adapt<CompanyResponse>();
        return Result.Success(companyResponses);
    }
}
