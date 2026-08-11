namespace Domain.Auditing;

public enum AuditActorType
{
    User = 1,
    BackgroundJob = 2,
    System = 3
}

public sealed record AuditContext(
    Guid OperationId,
    AuditActorType ActorType,
    string? ActorUserId,
    string ActorName,
    string Source,
    string? OperationName = null,
    string? CorrelationId = null,
    string? HttpMethod = null,
    string? RequestPath = null,
    string? IpAddress = null)
{
    public static AuditContext System(string operationName = "System") => new(
        Guid.NewGuid(),
        AuditActorType.System,
        null,
        "System",
        "System",
        operationName);
}

public interface IAuditContextAccessor
{
    AuditContext Current { get; }

    void Set(AuditContext context);
}

public sealed class AuditContextAccessor : IAuditContextAccessor
{
    private AuditContext? current;

    public AuditContext Current => current ??= AuditContext.System();

    public void Set(AuditContext context)
    {
        current = context;
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class AuditIgnoreAttribute : Attribute
{
}
