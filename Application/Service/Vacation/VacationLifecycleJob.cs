using Domain.Auditing;

namespace Application.Service.Vacation;

public class VacationLifecycleJob(IVacationService vacationService, IAuditContextAccessor auditContextAccessor) : IVacationLifecycleJob
{
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        auditContextAccessor.Set(new AuditContext(
            Guid.NewGuid(), AuditActorType.System, null, "Hangfire:VacationLifecycle", "Hangfire", "VacationLifecycle"));
        return vacationService.ReconcileAsync(cancellationToken);
    }
}
