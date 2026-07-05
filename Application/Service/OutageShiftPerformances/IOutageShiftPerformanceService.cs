using Application.Abstraction;
using Application.Contracts.OutageShiftPerformances;

namespace Application.Service.OutageShiftPerformances;

public interface IOutageShiftPerformanceService
{
    Task<Result<OutageShiftPerformanceResponse>> CreateAsync(
        CreateOutageShiftPerformanceRequest request,
        DateOnly shiftDate,
        string createdBy,
        CancellationToken cancellationToken = default);

    Task<Result<OutageShiftPerformanceImportResponse>> ImportAsync(
        ImportOutageShiftPerformanceRequest request,
        string importedBy,
        CancellationToken cancellationToken = default);

    Task<Result<OutageShiftPerformanceResponse>> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<Result<List<OutageShiftPerformanceResponse>>> GetAsync(
        string? systemId,
        string? phoneNumber,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default);

    Task<Result<OutageShiftPerformanceResponse>> UpdateAsync(
        int id,
        UpdateOutageShiftPerformanceRequest request,
        DateOnly shiftDate,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default);
}
