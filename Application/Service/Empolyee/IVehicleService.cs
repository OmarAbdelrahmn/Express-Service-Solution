using Application.Abstraction;
using Application.Contracts.Employees;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Empolyee;

public interface IVehicleService
{
    Task<Result<IEnumerable<VehicleResponse>>> GetAllEmployee();
    Task<Result<IEnumerable<VehicleResponse>>> Get(string VehicleNumber);
    Task<Result<VehicleResponse>> CreateAsync(VehicleRequest Request);
    Task<Result<VehicleResponse>> UpdateAsync(string VehicleNumber, UVehicleRequest Request);
    Task<Result> DeleteAsync(string VehicleNumber, CancellationToken cancellationToken = default);
}
