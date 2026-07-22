using Application.Abstraction;
using Application.Contracts.Common;
using Application.Contracts.Compensation;
using Domain.Entities.AccountingPlatform;

namespace Application.Service.Compensation;

public interface ICompensationService
{
    Task<Result<PagedResponse<CompensationPolicyResponse>>> GetPoliciesAsync(PaginationRequest pagination, int legalEntityId, int? platformAccountId, string? category, CompensationPolicyStatus? status, DateOnly? fromDate, DateOnly? toDate, string? search, string? sortBy, string? sortDirection, string actorId, CancellationToken cancellationToken = default);
    Task<Result<CompensationPolicyResponse>> CreatePolicyAsync(CreateCompensationPolicyRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<CompensationPolicyResponse>> CloneVersionAsync(Guid id, CloneCompensationPolicyVersionRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<CompensationPolicyResponse>> ActivatePolicyAsync(Guid id, ActivateCompensationPolicyRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<CompensationPolicyResponse>> RetirePolicyAsync(Guid id, RetireCompensationPolicyRequest request, string actorId, CancellationToken cancellationToken = default);
    Task<Result<CompensationPolicyResponse>> GetPolicyAsync(Guid id, string actorId, CancellationToken cancellationToken = default);
    Task<Result<CompensationSimulationResponse>> SimulateAsync(Guid id, SimulateCompensationPolicyRequest request, string actorId, CancellationToken cancellationToken = default);
}
