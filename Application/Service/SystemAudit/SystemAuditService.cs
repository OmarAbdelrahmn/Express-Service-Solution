using Application.Abstraction;
using Application.Contracts.SystemAudit;
using Domain;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Application.Service.SystemAudit;

public class SystemAuditService(ApplicationDbcontext dbcontext) : ISystemAuditService
{
    public async Task<Result<SystemAuditPageResponse>> GetAllAsync(
        SystemAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.FromUtc.HasValue && query.ToUtc.HasValue && query.FromUtc > query.ToUtc)
        {
            return Result.Failure<SystemAuditPageResponse>(new Error(
                "SystemAudit.InvalidDateRange",
                "FromUtc must be before or equal to ToUtc.",
                StatusCodes.Status400BadRequest));
        }

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var events = dbcontext.SystemAuditEvents.AsNoTracking().AsQueryable();

        if (query.FromUtc.HasValue) events = events.Where(x => x.OccurredAtUtc >= query.FromUtc.Value);
        if (query.ToUtc.HasValue) events = events.Where(x => x.OccurredAtUtc <= query.ToUtc.Value);
        if (!string.IsNullOrWhiteSpace(query.EntityType)) events = events.Where(x => x.EntityType == query.EntityType);
        if (!string.IsNullOrWhiteSpace(query.EntityKey)) events = events.Where(x => x.EntityKey == query.EntityKey);
        if (!string.IsNullOrWhiteSpace(query.ActorUserId)) events = events.Where(x => x.ActorUserId == query.ActorUserId);
        if (query.OperationId.HasValue) events = events.Where(x => x.OperationId == query.OperationId.Value);
        if (!string.IsNullOrWhiteSpace(query.Source)) events = events.Where(x => x.Source == query.Source);
        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            if (!Enum.TryParse<SystemAuditAction>(query.Action, true, out var action))
            {
                return Result.Failure<SystemAuditPageResponse>(new Error(
                    "SystemAudit.InvalidAction",
                    "Action must be Create, Update, or Delete.",
                    StatusCodes.Status400BadRequest));
            }

            events = events.Where(x => x.Action == action);
        }

        var totalCount = await events.CountAsync(cancellationToken);
        var items = await events
            .OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(new SystemAuditPageResponse(totalCount, page, pageSize, items.Select(ToSummary)));
    }

    public async Task<Result<SystemAuditDetailResponse>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var auditEvent = await dbcontext.SystemAuditEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (auditEvent is null)
        {
            return Result.Failure<SystemAuditDetailResponse>(new Error(
                "SystemAudit.NotFound",
                "Audit event was not found.",
                StatusCodes.Status404NotFound));
        }

        return Result.Success(ToDetail(auditEvent));
    }

    private static SystemAuditSummaryResponse ToSummary(SystemAuditEvent auditEvent) => new(
        auditEvent.Id,
        auditEvent.OperationId,
        auditEvent.OccurredAtUtc,
        auditEvent.ActorType.ToString(),
        auditEvent.ActorUserId,
        auditEvent.ActorName,
        auditEvent.Source,
        auditEvent.EntityType,
        auditEvent.EntityKey,
        auditEvent.EntityDisplayName,
        auditEvent.Action.ToString(),
        ReadChangedFields(auditEvent.ChangedFieldsJson));

    private static SystemAuditDetailResponse ToDetail(SystemAuditEvent auditEvent) => new(
        auditEvent.Id,
        auditEvent.OperationId,
        auditEvent.OccurredAtUtc,
        auditEvent.ActorType.ToString(),
        auditEvent.ActorUserId,
        auditEvent.ActorName,
        auditEvent.Source,
        auditEvent.OperationName,
        auditEvent.CorrelationId,
        auditEvent.HttpMethod,
        auditEvent.RequestPath,
        auditEvent.IpAddress,
        auditEvent.EntityType,
        auditEvent.EntityKey,
        auditEvent.EntityDisplayName,
        auditEvent.Action.ToString(),
        ReadChangedFields(auditEvent.ChangedFieldsJson),
        ParseJson(auditEvent.OldValuesJson),
        ParseJson(auditEvent.NewValuesJson),
        auditEvent.ScopeType,
        auditEvent.ScopeBefore,
        auditEvent.ScopeAfter);

    private static IReadOnlyList<string> ReadChangedFields(string json) =>
        JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
