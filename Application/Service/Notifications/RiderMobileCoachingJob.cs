using Domain;
using Domain.Entities.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Application.Service.Notifications;

public interface IRiderMobileCoachingJob
{
    Task NotifyRejectionsAsync(CancellationToken ct = default);
    Task NotifyRankChangesAsync(CancellationToken ct = default);
    Task NotifyOnTimeDeliveryAsync(CancellationToken ct = default);
}

public class RiderMobileCoachingJob(ApplicationDbcontext db, IOptions<NotificationOptions> options,
    TimeProvider clock) : IRiderMobileCoachingJob
{
    private const int PageSize = 100;

    [DisableConcurrentExecution(300)]
    [AutomaticRetry(Attempts = 3)]
    public async Task NotifyRejectionsAsync(CancellationToken ct = default)
    {
        if (!options.Value.Enabled || !options.Value.RejectionAlertsEnabled) return;
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(3)).DateTime);
        var yesterday = today.AddDays(-1);
        // The word "yesterday" must not be delivered on a later Saudi calendar day.
        var expires = new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(3)).UtcDateTime;
        var eligible = NotificationRules.EligibleRiders(db).Select(x => x.Id);
        var afterRiderId = 0;

        while (true)
        {
            var page = await db.RiderShifts.AsNoTracking()
                .Where(x => x.ShiftDate == yesterday && x.RiderId > afterRiderId && eligible.Contains(x.RiderId))
                .GroupBy(x => x.RiderId)
                .Select(x => new { RiderId = x.Key, Rejections = x.Sum(s => (long)s.RealRejectedDailyOrders) })
                .Where(x => x.Rejections > 0).OrderBy(x => x.RiderId).Take(PageSize).ToListAsync(ct);
            if (page.Count == 0) break;

            var requests = page.Select(x =>
            {
                var key = FormattableString.Invariant($"mobile-rejections:{x.RiderId}:{yesterday:yyyy-MM-dd}");
                var request = NotificationRules.Create(NotificationTemplates.RejectedOrders(yesterday, x.Rejections),
                    now.UtcDateTime, x.RiderId, riderId: x.RiderId, eventKey: key);
                request.ExpiresAtUtc = expires;
                return request;
            }).ToList();
            await QueuePageAsync(requests, ct);
            afterRiderId = page[^1].RiderId;
        }
    }

    [DisableConcurrentExecution(300)]
    [AutomaticRetry(Attempts = 3)]
    public async Task NotifyRankChangesAsync(CancellationToken ct = default)
    {
        if (!options.Value.Enabled || !options.Value.RankChangeAlertsEnabled) return;
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(3)).DateTime);
        var yesterday = today.AddDays(-1);
        var eligible = NotificationRules.EligibleRiders(db).Select(x => x.Id);
        var afterRiderId = 0;

        while (true)
        {
            // Only the latest current snapshot can generate an alert. Historical backfills
            // and future imports must not send stale rank changes.
            var page = await db.RiderScorePerformances.AsNoTracking()
                .Where(x => x.RiderId > afterRiderId && eligible.Contains(x.RiderId) &&
                    x.PerformanceDate >= yesterday && x.PerformanceDate <= today &&
                    !db.RiderScorePerformances.Any(later => later.RiderId == x.RiderId &&
                        later.PerformanceDate > x.PerformanceDate && later.PerformanceDate <= today))
                .OrderBy(x => x.RiderId)
                .Select(x => new
                {
                    x.RiderId, x.PerformanceDate, x.Segment,
                    PreviousRank = db.RiderScorePerformances.Where(previous => previous.RiderId == x.RiderId &&
                        previous.PerformanceDate < x.PerformanceDate)
                        .OrderByDescending(previous => previous.PerformanceDate)
                        .Select(previous => previous.Segment).FirstOrDefault()
                }).Take(PageSize).ToListAsync(ct);
            if (page.Count == 0) break;

            var requests = new List<NotificationRequest>();
            foreach (var item in page)
            {
                var previous = NormalizeRank(item.PreviousRank);
                var current = NormalizeRank(item.Segment);
                if (previous is null || current is null || previous == current) continue;
                var key = FormattableString.Invariant(
                    $"mobile-rank:{item.RiderId}:{item.PerformanceDate:yyyy-MM-dd}:{previous}:{current}");
                requests.Add(NotificationRules.Create(
                    NotificationTemplates.RankChanged(item.PerformanceDate, previous, current), now.UtcDateTime,
                    item.RiderId, riderId: item.RiderId, eventKey: key));
            }
            await QueuePageAsync(requests, ct);
            afterRiderId = page[^1].RiderId;
        }
    }

    [DisableConcurrentExecution(300)]
    [AutomaticRetry(Attempts = 3)]
    public async Task NotifyOnTimeDeliveryAsync(CancellationToken ct = default)
    {
        if (!options.Value.Enabled || !options.Value.OnTimeDeliveryAlertsEnabled) return;
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(3)).DateTime);
        var yesterday = today.AddDays(-1);
        var eligible = NotificationRules.EligibleRiders(db).Select(x => x.Id);
        var afterRiderId = 0;

        while (true)
        {
            var page = await db.RiderScorePerformances.AsNoTracking()
                .Where(x => x.RiderId > afterRiderId && eligible.Contains(x.RiderId) &&
                    x.PerformanceDate >= yesterday && x.PerformanceDate <= today &&
                    !db.RiderScorePerformances.Any(later => later.RiderId == x.RiderId &&
                        later.PerformanceDate > x.PerformanceDate && later.PerformanceDate <= today))
                .OrderBy(x => x.RiderId)
                .Select(x => new
                {
                    x.RiderId, x.PerformanceDate,
                    // Imports store daily counter deltas. Match the mobile report's
                    // month-to-date ratio, rather than averaging daily percentages
                    // or using the separate platform OnTimeDeliveryScore snapshot.
                    Completed = db.RiderScorePerformances.Where(s => s.RiderId == x.RiderId &&
                        s.PerformanceDate.Year == x.PerformanceDate.Year &&
                        s.PerformanceDate.Month == x.PerformanceDate.Month && s.PerformanceDate <= x.PerformanceDate)
                        .Sum(s => (long)s.CompletedOrders),
                    OnTime = db.RiderScorePerformances.Where(s => s.RiderId == x.RiderId &&
                        s.PerformanceDate.Year == x.PerformanceDate.Year &&
                        s.PerformanceDate.Month == x.PerformanceDate.Month && s.PerformanceDate <= x.PerformanceDate)
                        .Sum(s => (long)s.CompletedOrdersInTime)
                }).Take(PageSize).ToListAsync(ct);
            if (page.Count == 0) break;

            var requests = new List<NotificationRequest>();
            foreach (var item in page)
            {
                if (item.Completed < 0 || item.OnTime < 0 || item.OnTime > item.Completed) continue;
                // Compare exact counts so 98.999% is not congratulated as 99% after rounding.
                var targetReached = item.Completed > 0 && item.OnTime * 100m >= item.Completed * 99m;
                var key = FormattableString.Invariant($"mobile-on-time:{item.RiderId}:{item.PerformanceDate:yyyy-MM-dd}");
                requests.Add(NotificationRules.Create(
                    NotificationTemplates.OnTimeDeliveryGoal(item.PerformanceDate, targetReached), now.UtcDateTime,
                    item.RiderId, riderId: item.RiderId, eventKey: key));
            }
            await QueuePageAsync(requests, ct);
            afterRiderId = page[^1].RiderId;
        }
    }

    private static string? NormalizeRank(string? value)
    {
        var rank = value?.Trim().ToUpperInvariant();
        return rank is "A" or "B" or "C" or "D" or "E" or "F" ? rank : null;
    }

    private async Task QueuePageAsync(List<NotificationRequest> pending, CancellationToken ct)
    {
        while (pending.Count > 0)
        {
            var keys = pending.Select(x => x.EventKey!).ToArray();
            var existing = await db.NotificationRequests.AsNoTracking().Where(x => keys.Contains(x.EventKey!))
                .Select(x => x.EventKey).ToListAsync(ct);
            pending.RemoveAll(x => existing.Contains(x.EventKey));
            if (pending.Count == 0) return;
            db.NotificationRequests.AddRange(pending);
            try
            {
                await db.SaveChangesAsync(ct);
                foreach (var request in pending) db.Entry(request).State = EntityState.Detached;
                return;
            }
            catch (DbUpdateException)
            {
                foreach (var request in pending) db.Entry(request).State = EntityState.Detached;
                // A competing worker may have queued a subset of the same page.
                // Only ignore actual event-key duplicates; retry the remaining rows.
                var attemptedKeys = pending.Select(x => x.EventKey!).ToArray();
                if (!await db.NotificationRequests.AsNoTracking().AnyAsync(x => attemptedKeys.Contains(x.EventKey!), ct)) throw;
            }
        }
    }
}
