namespace Application.EmailWarmup;

public interface IEmailWarmupJob
{
    Task RunAsync(CancellationToken cancellationToken);
}