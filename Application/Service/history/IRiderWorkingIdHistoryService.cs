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

    // <summary>
    /// Get complete working ID history for a rider by IqamaNo
    /// </summary>
    Task<Result<RiderWorkingIdHistoryReport>> GetRiderHistoryReport(
        long riderIqamaNo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Suggest previous working ID for a rider when changing to a specific company
    /// </summary>
    Task<Result<SuggestedWorkingIdResponse>> SuggestWorkingIdForCompany(
        long riderIqamaNo,
        int companyId,
        CancellationToken cancellationToken = default);
}

// Add these records to IRiderWorkingIdHistoryService.cs or a separate DTOs file

public record RiderWorkingIdHistoryReport(
    long RiderIqamaNo,
    string RiderNameEN,
    string RiderNameAR,
    string CurrentWorkingId,
    string CurrentCompanyName,
    int TotalCompanyChanges,
    int TotalWorkingIdChanges,
    List<WorkingIdHistoryEntry> History
);

public record WorkingIdHistoryEntry(
    int Id,
    string WorkingId,
    string CompanyName,
    int CompanyId,
    DateTime StartDate,
    DateTime? EndDate,
    bool IsActive,
    int DurationDays,
    string Notes
);

public record SuggestedWorkingIdResponse(
    long RiderIqamaNo,
    string RiderNameEN,
    string RiderNameAR,
    int CompanyId,
    string CompanyName,
    bool HasPreviousHistory,
    string? SuggestedWorkingId,
    DateTime? LastUsedDate,
    int? DaysUsed,
    string Message,
    List<PreviousWorkingIdOption> AllPreviousIds
);

public record PreviousWorkingIdOption(
    string WorkingId,
    DateTime StartDate,
    DateTime? EndDate,
    int DaysUsed,
    bool IsCurrentlyInUse,
    string? CurrentlyUsedBy
);
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