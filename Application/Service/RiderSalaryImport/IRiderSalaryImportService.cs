using Application.Abstraction;
using Application.Contracts.RiderSalaryImport;
using Microsoft.AspNetCore.Http;

namespace Application.Service.RiderSalaryImport;

public interface IRiderSalaryImportService
{
    Task<Result<RiderSalaryImportResponse>> ImportAsync(
        IFormFile file,
        CancellationToken cancellationToken = default);
}
