using Application.Abstraction;
using Application.Contracts.Employees;
using Application.Contracts.Roles;
using Application.Contracts.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Empolyee;

public interface IEmployeeService
{
    Task<Result<IEnumerable<EmpolyeeResponse>>>GetAllEmployee();
    Task<Result<EmpolyeeResponse>>Get(int IqamaNo);
    Task<Result<EmpolyeeResponse>> CreateAsync(EmpolyeeRequest Request);
    Task<Result<EmpolyeeResponse>> UpdateAsync(int IqamaNo, UEmpolyeeRequest Request);
    Task<Result> DeleteAsync(int IqamaNo, CancellationToken cancellationToken = default);
    Task<Result> AddEmployeeToHousing(int IqamaNo , string HousingName);
    Task<Result> ChangeEmployeeToHousing(int IqamaNo , string oldHousingName , string NewHousingName);

}
