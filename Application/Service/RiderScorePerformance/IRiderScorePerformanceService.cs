using Application.Abstraction;
using Application.Contracts.RiderScorePerformance;

namespace Application.Service.RiderScorePerformance;

public interface IRiderScorePerformanceService
{
    Task<Result<RiderScorePerformanceResponse>> CreateAsync(
        CreateRiderScorePerformanceRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<RiderScorePerformanceResponse>> GetAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<Result<RiderScorePerformanceListResponse>> ListAsync(
        RiderScorePerformanceFilter filter,
        CancellationToken cancellationToken = default);

    Task<Result<RiderScorePerformanceResponse>> UpdateAsync(
        int id,
        UpdateRiderScorePerformanceRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<Result<RiderScorePerformanceImportResult>> ImportAsync(
        Stream excelStream,
        DateOnly performanceDate,
        CancellationToken cancellationToken = default);
}
