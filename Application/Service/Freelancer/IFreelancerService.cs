using Application.Abstraction;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Freelancer;

public interface IFreelancerService
{
    Task<Result<KetaFreelancerImportResponse>> ImportKetaFreelancersFromExcelAsync(
        IFormFile file,
        string uploadedBy,
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<KetaFreelancerResponse>>> GetKetaFreelancersByMonthAsync(
        string month,
        CancellationToken cancellationToken = default);
}

// Response DTOs
public record KetaFreelancerImportResponse(
    int TotalRecords,
    int SuccessfulImports,
    int FailedRecords,
    List<KetaFreelancerImportRowResult> Results,
    List<string> Errors,
    DateTime ProcessedAt
);

public record KetaFreelancerImportRowResult(
    int RowNumber,
    bool Success,
    string WorkingId,
    string Month,
    int TotalOrders,
    bool Created,
    bool Updated,
    List<string> Warnings,
    string? ErrorMessage
);

public record KetaFreelancerResponse(
    int Id,
    string WorkingId,
    string Month,
    int TotalOrders,
    DateTime CreatedAt
);
