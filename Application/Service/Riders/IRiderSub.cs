using Application.Abstraction;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Riders;

public interface IRiderSub 
{
    Task<Result<RiderSubstitutionResponse>> StartSubstitution(StartSubstitutionRequest request, CancellationToken cancellationToken = default);
    Task<Result<RiderSubstitutionResponse>> StopSubstitutionByWorkingId(string WorkingId, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<RiderSubstitutionResponse>>> GetActiveSubstitutions(CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<RiderSubstitutionResponse>>> GetSubstitutionHistory(string riderId, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<RiderSubstitutionResponse>>> GetAllSubstitutions(CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<RiderSubstitutionResponse>>> GetInactiveSubstitutions(CancellationToken cancellationToken = default);

}


public record StartSubstitutionRequest(
    string ActualRiderWorkingId,
    string SubstituteWorkingId,
    string Reason,
    string? CreatedBy
);

public record RiderSubstitutionResponse(
    int Id,
    string ActualRiderName,
    string ActualRiderWorkingId,
    string SubstituteRiderName,  // ✅ Add this
    string SubstituteWorkingId,
    DateTime StartDate,
    DateTime? EndDate,
    string Reason,
    bool IsActive
);