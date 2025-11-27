using Application.Abstraction;
using Application.Contracts.Employees;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Empolyee;

public interface IHousingService
{
    Task<Result<IEnumerable<HousingResponse>>> GetAllEmployee();
    Task<Result<IEnumerable<HousingResponse>>> Get(string Name);
    Task<Result<IEnumerable<HousingResponse>>> GetWithManagerIqama(int ManagerIqamaNo);
    Task<Result<HousingResponse>> CreateAsync(HousingRequest Request);
    Task<Result<UHousingResponse>> UpdateAsync(HousingRequest  Request);
    Task<Result> DeleteAsync(string Name, CancellationToken cancellationToken = default);
}
