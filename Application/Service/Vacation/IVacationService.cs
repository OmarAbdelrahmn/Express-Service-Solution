using Application.Abstraction;
using Application.Contracts.Vacation;

namespace Application.Service.Vacation;

public interface IVacationService
{
    Task<Result<VacationRequestResponse>> CreateForMemberAsync(string actorUserId, long managerIqamaNo, CreateVacationRequest request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<VacationRequestResponse>>> GetMemberRequestsAsync(long managerIqamaNo, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<VacationRequestResponse>>> GetMemberVacationRidersAsync(long managerIqamaNo, DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken = default);
    Task<Result<VacationDateChangeResponse>> RequestDateChangeAsync(string actorUserId, long managerIqamaNo, Guid vacationRequestId, CreateVacationDateChangeRequest request, CancellationToken cancellationToken = default);
    Task<Result<VacationCancellationResponse>> RequestCancellationAsync(string actorUserId, long managerIqamaNo, Guid vacationRequestId, CreateVacationCancellationRequest request, CancellationToken cancellationToken = default);
    Task<Result<VacationPagedResponse>> GetAllAsync(VacationRequestQuery query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<VacationRequestResponse>>> GetInboxAsync(string actorUserId, CancellationToken cancellationToken = default);
    Task<Result<VacationRequestResponse>> GetDetailAsync(string actorUserId, bool isOversightUser, Guid id, CancellationToken cancellationToken = default);
    Task<Result<VacationRequestResponse>> DecideAsync(string actorUserId, Guid id, VacationDecisionRequest request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<VacationDateChangeResponse>>> GetDateChangesAsync(CancellationToken cancellationToken = default);
    Task<Result<VacationDateChangeResponse>> ResolveDateChangeAsync(string actorUserId, Guid id, ResolveVacationAmendmentRequest request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<VacationCancellationResponse>>> GetCancellationsAsync(CancellationToken cancellationToken = default);
    Task<Result<VacationCancellationResponse>> ResolveCancellationAsync(string actorUserId, Guid id, ResolveVacationAmendmentRequest request, CancellationToken cancellationToken = default);
    Task<Result<VacationRequestResponse>> DirectCancelAsync(string actorUserId, Guid id, DirectVacationCancellationRequest request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<VacationRoleAssignmentResponse>>> GetRoleAssignmentsAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<VacationRoleAssignmentResponse>>> SetRolesAsync(string grantedByUserId, string userId, SetVacationRolesRequest request, CancellationToken cancellationToken = default);
    Task ReconcileAsync(CancellationToken cancellationToken = default);
}
