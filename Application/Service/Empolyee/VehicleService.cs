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
        var companies = await dbcontext.Vehicles.Where(c => c.VehicleNumber.StartsWith(VehicleNumber)).AsNoTracking().ToListAsync();

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

    public async Task<Result<VehicleResponse>> UpdateAsync(string VehicleNumber, UVehicleRequest Request)
    {
        var companies = await dbcontext.Vehicles.Where(c => c.VehicleNumber == VehicleNumber).SingleOrDefaultAsync();

        if (companies == null)
            return Result.Failure<VehicleResponse>(new Error("Vehicle.NotFound", $"Vehicle with name {VehicleNumber} was not found.", 404));

        companies.VehicleType = Request.VehicleType;
        companies.LicenseNumber = Request.LicenseNumber;
        companies.LicenseExpiryDate = Request.LicenseExpiryDate;
        companies.VehicleImagePath = Request.VehicleImagePath;
        companies.LicenseImagePath = Request.LicenseImagePath;
        companies.ExstraImage = Request.ExstraImage;
        companies.ExstraImage1 = Request.ExstraImage1;

        await dbcontext.SaveChangesAsync();

        var companyResponses = companies.Adapt<VehicleResponse>();
        return Result.Success(companyResponses);
    }
}
