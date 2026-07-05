using Application.Abstraction;
using Application.Contracts.SystemIdPhoneStatuses;

namespace Application.Service.SystemIdPhoneStatuses;

public interface ISystemIdPhoneStatusService
{
    Task<Result<SystemIdPhoneStatusResponse>> CreateAsync(
        CreateSystemIdPhoneStatusRequest request,
        string createdBy,
        CancellationToken cancellationToken = default);

    Task<Result<SystemIdPhoneStatusImportResponse>> ImportAsync(
        ImportSystemIdPhoneStatusRequest request,
        string importedBy,
        CancellationToken cancellationToken = default);

    Task<Result<SystemIdPhoneStatusResponse>> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<Result<List<SystemIdPhoneStatusResponse>>> GetAsync(
        string? systemId,
        string? phoneNumber,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default);

    Task<Result<SystemIdPhoneStatusResponse>> UpdateAsync(
        int id,
        UpdateSystemIdPhoneStatusRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default);
}
