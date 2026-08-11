using Domain.Auditing;

namespace Domain.Entities;

public enum SystemAuditAction
{
    Create = 1,
    Update = 2,
    Delete = 3
}

/// <summary>
/// Immutable history of non-accounting business changes. Values are persisted
/// as JSON so one row can describe any EF entity without coupling audit schema
/// changes to every operational model.
/// </summary>
public class SystemAuditEvent
{
    public long Id { get; set; }
    public Guid OperationId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }

    public AuditActorType ActorType { get; set; }
    public string? ActorUserId { get; set; }
    public string ActorName { get; set; } = "System";
    public string Source { get; set; } = "System";
    public string? OperationName { get; set; }
    public string? CorrelationId { get; set; }
    public string? HttpMethod { get; set; }
    public string? RequestPath { get; set; }
    public string? IpAddress { get; set; }

    public string EntityType { get; set; } = string.Empty;
    public string EntityKey { get; set; } = string.Empty;
    public string? EntityDisplayName { get; set; }
    public SystemAuditAction Action { get; set; }
    public string ChangedFieldsJson { get; set; } = "[]";
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }

    public string? ScopeType { get; set; }
    public string? ScopeBefore { get; set; }
    public string? ScopeAfter { get; set; }
}
