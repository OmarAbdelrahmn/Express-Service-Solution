using Application.Abstraction;
using Application.Contracts.OutRiderInfos;

namespace Application.Service.OutRiderInfos;

public interface IOutRiderInfoService
{
    Task<Result<OutRiderInfoResponse>> CreateAsync(
        CreateOutRiderInfoRequest request,
        string createdBy,
        CancellationToken cancellationToken = default);

    Task<Result<OutRiderInfoResponse>> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<Result<List<OutRiderInfoResponse>>> GetAsync(
        string? riderId,
        string? name,
        string? phoneNumber,
        CancellationToken cancellationToken = default);

    Task<Result<OutRiderInfoResponse>> UpdateAsync(
        int id,
        UpdateOutRiderInfoRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default);
}
