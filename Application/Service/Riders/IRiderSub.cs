using Application.Abstraction;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Riders;

public interface IRiderSub 
{
    Task<Result<RiderSubstitutionResponse>> StartSubstitution(StartSubstitutionRequest request, CancellationToken cancellationToken = default);
    Task<Result<RiderSubstitutionResponse>> StopSubstitutionByWorkingId(int workingId, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<RiderSubstitutionResponse>>> GetActiveSubstitutions(CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<RiderSubstitutionResponse>>> GetSubstitutionHistory(int riderId, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<RiderSubstitutionResponse>>> GetAllSubstitutions(CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<RiderSubstitutionResponse>>> GetInactiveSubstitutions(CancellationToken cancellationToken = default);

}


public record StartSubstitutionRequest(
    int ActualRiderWorkingId,
    int SubstituteWorkingId,
    string Reason,
    string? CreatedBy
);

public record RiderSubstitutionResponse(
    int Id,
    string ActualRiderName,
    int ActualRiderWorkingId,
    string SubstituteRiderName,  // ✅ Add this
    int SubstituteWorkingId,
    DateTime StartDate,
    DateTime? EndDate,
    string Reason,
    bool IsActive
);