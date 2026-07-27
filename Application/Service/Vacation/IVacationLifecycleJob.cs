namespace Application.Service.Vacation;

public interface IVacationLifecycleJob
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
