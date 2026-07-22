using System.Data;
using Domain;
using Domain.Entities.AccountingCore;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Service.AccountingOutbox;

public class LoggingAccountingOutboxDispatcher(ILogger<LoggingAccountingOutboxDispatcher> logger) : IAccountingOutboxDispatcher
{
    public Task DispatchAsync(string type, string payloadJson, string correlationId, CancellationToken cancellationToken = default)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
        logger.LogInformation("Accounting outbox event {EventType}: {Payload}", type, payloadJson);
        return Task.CompletedTask;
    }
}

public class AccountingOutboxJob(ApplicationDbcontext dbcontext, IAccountingOutboxDispatcher dispatcher, ILogger<AccountingOutboxJob> logger) : IAccountingOutboxJob
{
    private const int MaximumAttempts = 5;

    [DisableConcurrentExecution(55)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        var workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";
        var messages = await ClaimAsync(workerId, cancellationToken);
        foreach (var message in messages)
        {
            try
            {
                await dispatcher.DispatchAsync(message.Type, message.PayloadJson, message.CorrelationId, cancellationToken);
                message.ProcessedAt = DateTime.UtcNow;
                message.LockedBy = null;
                message.LockedUntil = null;
                message.LastError = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.RetryCount++;
                message.LastError = exception.Message[..Math.Min(exception.Message.Length, 2000)];
                message.LockedBy = null;
                message.LockedUntil = null;
                if (message.RetryCount >= MaximumAttempts) message.DeadLetteredAt = DateTime.UtcNow;
                else message.NextAttemptAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, message.RetryCount));
                logger.LogError(exception, "Accounting outbox message {OutboxMessageId} failed on attempt {Attempt}.", message.Id, message.RetryCount);
            }
            await dbcontext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<IReadOnlyCollection<AccountingOutboxMessage>> ClaimAsync(string workerId, CancellationToken ct)
    {
        if (!dbcontext.Database.IsSqlServer())
        {
            var pending = await dbcontext.AccountingOutboxMessages.Where(x => x.ProcessedAt == null && x.DeadLetteredAt == null && (x.NextAttemptAt == null || x.NextAttemptAt <= DateTime.UtcNow) && (x.LockedUntil == null || x.LockedUntil < DateTime.UtcNow)).OrderBy(x => x.OccurredAt).Take(100).ToListAsync(ct);
            foreach (var message in pending) { message.LockedBy = workerId; message.LockedUntil = DateTime.UtcNow.AddMinutes(2); }
            await dbcontext.SaveChangesAsync(ct);
            return pending;
        }

        await using var transaction = await dbcontext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var messages = await dbcontext.AccountingOutboxMessages.FromSqlRaw("""
            SELECT TOP (100) * FROM [AccountingOutboxMessages] WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE [ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL
              AND ([NextAttemptAt] IS NULL OR [NextAttemptAt] <= SYSUTCDATETIME())
              AND ([LockedUntil] IS NULL OR [LockedUntil] < SYSUTCDATETIME())
            ORDER BY [OccurredAt]
            """).ToListAsync(ct);
        foreach (var message in messages) { message.LockedBy = workerId; message.LockedUntil = DateTime.UtcNow.AddMinutes(2); }
        await dbcontext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return messages;
    }
}
