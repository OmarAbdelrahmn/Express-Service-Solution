using Application.Abstraction;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Service.Riders;

public interface IRiderWorkingIdHistoryService
{
    Task<Result> RecordWorkingIdChange(
        long riderIqamaNo,
        string newWorkingId,
        int newCompanyId,
        string? notes = null,
        CancellationToken cancellationToken = default);

    Task<Result<List<WorkingIdHistoryResponse>>> GetRiderWorkingIdHistory(
        long riderIqamaNo,
        CancellationToken cancellationToken = default);

    Task<Result<WorkingIdOwnershipInfo>> WhoHasWorkingId(
        string workingId,
        CancellationToken cancellationToken = default);

    Task<Result<List<string>>> GetAllWorkingIdsForRider(
        long riderIqamaNo,
        CancellationToken cancellationToken = default);

    Task<Result<RiderDetails?>> GetRiderByWorkingId(
        string workingId,
        CancellationToken cancellationToken = default);
}

public record WorkingIdHistoryResponse(
    string WorkingId,
    string CompanyName,
    DateTime StartDate,
    DateTime? EndDate,
    bool IsActive,
    string? Notes
);

public record WorkingIdOwnershipInfo(
    string WorkingId,
    long? CurrentRiderIqamaNo,
    string? CurrentRiderName,
    string? CurrentCompany,
    bool IsCurrentlyAssigned,
    List<PreviousOwner> PreviousOwners
);

public record PreviousOwner(
    long RiderIqamaNo,
    string RiderName,
    string CompanyName,
    DateTime StartDate,
    DateTime EndDate
);