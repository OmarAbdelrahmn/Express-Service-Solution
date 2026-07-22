namespace Application.Service.AccountingOutbox;

public interface IAccountingOutboxJob
{
    Task ProcessAsync(CancellationToken cancellationToken = default);
}

public interface IAccountingOutboxDispatcher
{
    Task DispatchAsync(string type, string payloadJson, string correlationId, CancellationToken cancellationToken = default);
}
