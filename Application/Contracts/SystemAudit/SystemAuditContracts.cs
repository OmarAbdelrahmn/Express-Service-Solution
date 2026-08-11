using System.Text.Json;

namespace Application.Contracts.SystemAudit;

public record SystemAuditQuery(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? EntityType = null,
    string? EntityKey = null,
    string? Action = null,
    string? ActorUserId = null,
    Guid? OperationId = null,
    string? Source = null,
    int Page = 1,
    int PageSize = 50);

public record SystemAuditSummaryResponse(
    long Id,
    Guid OperationId,
    DateTimeOffset OccurredAtUtc,
    string ActorType,
    string? ActorUserId,
    string ActorName,
    string Source,
    string EntityType,
    string EntityKey,
    string? EntityDisplayName,
    string Action,
    IReadOnlyList<string> ChangedFields);

public record SystemAuditDetailResponse(
    long Id,
    Guid OperationId,
    DateTimeOffset OccurredAtUtc,
    string ActorType,
    string? ActorUserId,
    string ActorName,
    string Source,
    string? OperationName,
    string? CorrelationId,
    string? HttpMethod,
    string? RequestPath,
    string? IpAddress,
    string EntityType,
    string EntityKey,
    string? EntityDisplayName,
    string Action,
    IReadOnlyList<string> ChangedFields,
    JsonElement? OldValues,
    JsonElement? NewValues,
    string? ScopeType,
    string? ScopeBefore,
    string? ScopeAfter);

public record SystemAuditPageResponse(
    int TotalCount,
    int Page,
    int PageSize,
    IEnumerable<SystemAuditSummaryResponse> Items);
