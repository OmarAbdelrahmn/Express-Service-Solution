using Application.Abstraction;
using Application.Contracts.Employees;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Empolyee;

public interface ICompanyService
{
    Task<Result<IEnumerable<CompanyResponse>>> GetAllEmployee();
    Task<Result<IEnumerable<CompanyResponse>>> Get(string CompanyName);
    Task<Result<CompanyResponse>> CreateAsync(CompanyRequest Request);
    Task<Result<CompanyResponse>> UpdateAsync(string CompanyName, CompanyRequest Request);
    Task<Result> DeleteAsync(string CompanyName, CancellationToken cancellationToken = default);

}
