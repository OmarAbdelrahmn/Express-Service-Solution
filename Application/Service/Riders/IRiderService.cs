using Application.Abstraction;
using Application.Contracts.Employees;
using Application.Contracts.rider;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Riders;

public interface IRiderService
{
    Task<Result<IEnumerable<RiderResponse>>> GetAllEmployee();
    Task<Result<IEnumerable<RiderResponse>>> GetAllEmployeeNO();
    Task<Result<IEnumerable<RiderResponse>>> Get(int IqamaNo);
    Task<Result<RiderResponse>> Getbyid(int Id);
    Task<Result> CreateAsync(RiderRequest Request);
    Task<Result<RiderResponse>> UpdateAsync(int IqamaNo, URiderRequest Request);
    Task<Result> DeleteAsync(int IqamaNo, CancellationToken cancellationToken = default);
    Task<List<RiderResponse>> SmartSearch(string keyword);
    Task<Result> ChangeWorkinId(int OldWorkinId, int NewWorkingId);
    Task<Result> AddETOR(int IqamaNo, EMTOR request );

}
