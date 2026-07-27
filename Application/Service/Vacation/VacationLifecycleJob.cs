namespace Application.Service.Vacation;

public class VacationLifecycleJob(IVacationService vacationService) : IVacationLifecycleJob
{
    public Task RunAsync(CancellationToken cancellationToken = default) => vacationService.ReconcileAsync(cancellationToken);
}
