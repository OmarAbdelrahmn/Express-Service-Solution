using Application.Abstraction;
using Application.Contracts.Notifications;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Application.Service.Notifications;

public interface INotificationService
{
    Task<Result<NotificationQueuedResponse>> QueueAsync(ClaimsPrincipal actor, int? riderId, Guid key, SendNotificationRequest request, CancellationToken ct);
    Task<Result<NotificationStatusResponse>> StatusAsync(ClaimsPrincipal actor, Guid requestId, CancellationToken ct);
}

public class NotificationService(ApplicationDbcontext db, IOptions<NotificationOptions> options, TimeProvider clock) : INotificationService
{
    public async Task<Result<NotificationQueuedResponse>> QueueAsync(ClaimsPrincipal actor, int? riderId, Guid key,
        SendNotificationRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var error = NotificationRules.Validate(request, now);
        if (error is not null) return Result.Failure<NotificationQueuedResponse>(error);
        if (key == Guid.Empty) return Result.Failure<NotificationQueuedResponse>(NotificationRules.Invalid("A UUID Idempotency-Key header is required."));
        var userId = actor.FindFirstValue(ClaimTypes.NameIdentifier) ?? actor.FindFirstValue("sub");
        var grant = Grant(actor, riderId is null);
        if (userId is null || !grant.Allowed) return Result.Failure<NotificationQueuedResponse>(NotificationRules.Forbidden);
        var scope = NotificationRules.Hash($"{userId}|{grant.CompanyId}|{riderId?.ToString() ?? "broadcast"}|{key:D}");
        var hash = NotificationRules.Canonical(request).Hash;
        var existing = await db.NotificationRequests.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyScope == scope, ct);
        if (existing is not null)
            return existing.PayloadHash == hash ? Result.Success(new NotificationQueuedResponse(existing.Id))
                : Result.Failure<NotificationQueuedResponse>(NotificationRules.Conflict);
        var audience = NotificationRules.EligibleRiders(db);
        if (grant.CompanyId is int companyId) audience = audience.Where(x => x.CompanyId == companyId);
        if (riderId is int target && !await audience.AnyAsync(x => x.Id == target, ct))
            return Result.Failure<NotificationQueuedResponse>(NotificationRules.NotFound);
        var upper = await audience.Select(x => (int?)x.Id).MaxAsync(ct) ?? 0;
        var entity = NotificationRules.Create(request, now, upper, riderId, grant.CompanyId, userId, scope);
        db.NotificationRequests.Add(entity);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await db.NotificationRequests.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyScope == scope, ct);
            if (winner is null) throw;
            return winner.PayloadHash == hash ? Result.Success(new NotificationQueuedResponse(winner.Id))
                : Result.Failure<NotificationQueuedResponse>(NotificationRules.Conflict);
        }
        return Result.Success(new NotificationQueuedResponse(entity.Id));
    }

    public async Task<Result<NotificationStatusResponse>> StatusAsync(ClaimsPrincipal actor, Guid requestId, CancellationToken ct)
    {
        var request = await db.NotificationRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestId, ct);
        var grant = Grant(actor, false);
        var broadcastGrant = Grant(actor, true);
        if (request is null || (!grant.Allowed && !broadcastGrant.Allowed) ||
            (!actor.IsInRole("Master") && !options.Value.StaffGrants.Any(x => x.UserId ==
                (actor.FindFirstValue(ClaimTypes.NameIdentifier) ?? actor.FindFirstValue("sub")) &&
                (x.Send || x.Broadcast) && (x.CompanyId == null || x.CompanyId == request.CompanyId))))
            return Result.Failure<NotificationStatusResponse>(NotificationRules.NotFound);
        var recipients = db.NotificationRecipients.AsNoTracking().Where(x => x.RequestId == requestId);
        var counts = await db.NotificationDeliveries.AsNoTracking().Where(x => x.Recipient.RequestId == requestId)
            .GroupBy(x => x.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(ct);
        int Count(string status) => counts.Where(x => x.Status == status).Sum(x => x.Count);
        var pending = Count("pending");
        var failed = Count("failed");
        var expired = Count("expired");
        var state = !request.ExpansionComplete ? (request.LastRiderId == 0 ? "queued" : "expanding") :
            pending > 0 ? "processing" : failed + expired > 0 ? "completed_with_errors" : "completed";
        return Result.Success(new NotificationStatusResponse(requestId, state, await recipients.CountAsync(ct),
            await recipients.CountAsync(x => x.NoDevice, ct), counts.Sum(x => x.Count), pending, Count("accepted"), failed,
            Count("skipped"), expired, request.CreatedAtUtc, request.ExpiresAtUtc));
    }

    private (bool Allowed, int? CompanyId) Grant(ClaimsPrincipal actor, bool broadcast)
    {
        if (actor.Identity?.IsAuthenticated != true) return (false, null);
        if (actor.IsInRole("Master")) return (true, null);
        var userId = actor.FindFirstValue(ClaimTypes.NameIdentifier) ?? actor.FindFirstValue("sub");
        var grants = options.Value.StaffGrants.Where(x => x.UserId == userId && (broadcast ? x.Broadcast : x.Send)).ToList();
        // One grant per user avoids silently widening a multi-company request.
        return grants.Count == 1 ? (true, grants[0].CompanyId) : (false, null);
    }
}
