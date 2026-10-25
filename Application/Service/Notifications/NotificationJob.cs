using Application.Contracts.Notifications;
using Domain;
using Domain.Entities.Notifications;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

namespace Application.Service.Notifications;

public interface INotificationJob
{
    Task ProcessAsync(CancellationToken ct = default);
    Task ReminderAsync(CancellationToken ct = default);
    Task CleanupAsync(CancellationToken ct = default);
}

public class NotificationJob(ApplicationDbcontext db, INotificationSender sender, IDataProtectionProvider protection,
    IOptions<NotificationOptions> options, TimeProvider clock, ILogger<NotificationJob> logger) : INotificationJob
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private readonly IDataProtector protector = protection.CreateProtector("ExpressService.PushDestinations.v1");

    [AutomaticRetry(Attempts = 0)]
    public async Task ProcessAsync(CancellationToken ct = default)
    {
        // Database rows, rather than Hangfire invocations, own work and retries.
        for (var i = 0; i < 10 && !ct.IsCancellationRequested; i++)
        {
            db.ChangeTracker.Clear();
            if (!await ExpandAsync(ct)) break;
        }
        db.ChangeTracker.Clear();
        var expiredAt = Now;
        var expired = await db.NotificationDeliveries.Where(x => x.Status == "pending" &&
            x.Recipient.Request.ExpiresAtUtc <= expiredAt && (x.LeaseUntilUtc == null || x.LeaseUntilUtc < expiredAt))
            .OrderBy(x => x.NextAttemptAtUtc).Take(500).ToListAsync(ct);
        foreach (var delivery in expired) { delivery.Status = "expired"; delivery.LeaseOwner = null; delivery.LeaseUntilUtc = null; }
        if (expired.Count > 0) await SaveClaimAsync(ct);
        if (!options.Value.Enabled) return;
        for (var i = 0; i < 50 && !ct.IsCancellationRequested; i++)
        {
            db.ChangeTracker.Clear();
            if (!await DeliverAsync(ct)) break;
        }
    }

    private async Task<bool> ExpandAsync(CancellationToken ct)
    {
        var now = Now;
        var request = await db.NotificationRequests.Where(x => !x.ExpansionComplete &&
            (x.LeaseUntilUtc == null || x.LeaseUntilUtc < now)).OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (request is null) return false;
        var owner = Guid.NewGuid();
        request.LeaseOwner = owner;
        request.LeaseUntilUtc = now.AddMinutes(2);
        if (!await SaveClaimAsync(ct)) return true;
        if (request.ExpiresAtUtc <= now)
        {
            request.ExpansionComplete = true;
        }
        else
        {
            var query = NotificationRules.EligibleRiders(db).AsNoTracking().Where(x => x.Id > request.LastRiderId && x.Id <= request.UpperRiderId);
            if (request.TargetRiderId is int riderId) query = query.Where(x => x.Id == riderId);
            if (request.CompanyId is int company) query = query.Where(x => x.CompanyId == company);
            var riders = await query.OrderBy(x => x.Id).Select(x => x.Id).Take(100).ToListAsync(ct);
            var devices = await db.RiderPushDevices.AsNoTracking().Where(x => riders.Contains(x.RiderId) &&
                x.IsActive && x.Permission == "authorized" && x.Session.RevokedAtUtc == null &&
                x.Session.ExpiresAtUtc > now && x.Session.RiderId == x.RiderId).ToListAsync(ct);
            foreach (var id in riders)
            {
                var riderDevices = devices.Where(x => x.RiderId == id).ToList();
                var recipient = new NotificationRecipient { RequestId = request.Id, RiderId = id, NoDevice = riderDevices.Count == 0 };
                db.NotificationRecipients.Add(recipient);
                foreach (var device in riderDevices)
                    db.NotificationDeliveries.Add(new NotificationDelivery
                    {
                        Recipient = recipient, DeviceId = device.Id, DeviceGeneration = device.Generation, NextAttemptAtUtc = now
                    });
            }
            if (riders.Count > 0) request.LastRiderId = riders[^1];
            request.ExpansionComplete = riders.Count < 100 || request.TargetRiderId != null;
        }
        request.LeaseOwner = null;
        request.LeaseUntilUtc = null;
        // Rowversion prevents a worker whose lease was recovered from committing
        // its page/checkpoint. EF saves the page and checkpoint atomically.
        await SaveClaimAsync(ct);
        return true;
    }

    private async Task<bool> DeliverAsync(CancellationToken ct)
    {
        var now = Now;
        var delivery = await db.NotificationDeliveries.Where(x => x.Status == "pending" && x.NextAttemptAtUtc <= now &&
            (x.LeaseUntilUtc == null || x.LeaseUntilUtc < now)).OrderBy(x => x.NextAttemptAtUtc).FirstOrDefaultAsync(ct);
        if (delivery is null) return false;
        delivery.LeaseOwner = Guid.NewGuid();
        delivery.LeaseUntilUtc = now.AddMinutes(2);
        delivery.Attempts++;
        if (!await SaveClaimAsync(ct)) return true;
        var request = await db.NotificationRecipients.AsNoTracking().Where(x => x.Id == delivery.RecipientId).Select(x => x.Request).SingleAsync(ct);
        var recipientRiderId = await db.NotificationRecipients.Where(x => x.Id == delivery.RecipientId).Select(x => x.RiderId).SingleAsync(ct);
        // Fresh reads after the claim; never use cached ownership across retries.
        var device = await db.RiderPushDevices.AsNoTracking().SingleAsync(x => x.Id == delivery.DeviceId, ct);
        var rider = await NotificationRules.EligibleRiders(db).AsNoTracking().Where(x => x.Id == recipientRiderId &&
            (request.CompanyId == null || x.CompanyId == request.CompanyId)).Select(x => new { x.PreferredLanguage }).SingleOrDefaultAsync(ct);
        var sessionValid = await db.RiderMobileSessions.AsNoTracking().AnyAsync(x => x.Id == device.SessionId &&
            x.RiderId == recipientRiderId && x.RevokedAtUtc == null && x.ExpiresAtUtc > now, ct);
        PushResult? result = null;
        if (request.ExpiresAtUtc <= now) delivery.Status = "expired";
        else if (delivery.Attempts > 6) { delivery.Status = "failed"; delivery.LastErrorCode = "attempts_exhausted"; }
        else if (!device.IsActive || device.Permission != "authorized" || device.Generation != delivery.DeviceGeneration ||
            device.RiderId != recipientRiderId || !sessionValid || rider is null) delivery.Status = "skipped";
        else
        {
            try
            {
                var content = JsonSerializer.Deserialize<Dictionary<string, NotificationContent>>(request.ContentJson)!;
                var selected = content.GetValueOrDefault(rider.PreferredLanguage) ?? content["en"];
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(request.DataJson)!;
                data["notificationId"] = request.Id.ToString("D");
                var fid = protector.Unprotect(device.ProtectedDestination);
                // SQL datetime2 does not preserve Kind; this column stores UTC instants.
                // Reuse the saved time so delayed delivery and retries retain the original event time.
                var eventTimeUtc = DateTime.SpecifyKind(request.CreatedAtUtc, DateTimeKind.Utc);
                result = await sender.SendAsync(new(fid, selected.Title, selected.Body, data,
                    request.ExpiresAtUtc - Now, eventTimeUtc), ct);
            }
            catch (CryptographicException)
            {
                result = new(ErrorCode: "destination_key_unavailable", Retryable: true, ConfigurationError: true);
            }
            if (result.MessageId is not null)
            {
                delivery.Status = "accepted";
                delivery.ProviderMessageId = result.MessageId;
                delivery.AcceptedAtUtc = Now;
            }
            else
            {
                delivery.LastErrorCode = result.ErrorCode;
                if (result.ConfigurationError)
                    logger.LogError("Notification provider configuration failure for request {RequestId}: {Category}", request.Id, result.ErrorCode);
                if (result.Unregistered && db.Database.IsRelational())
                    await db.RiderPushDevices.Where(x => x.Id == device.Id && x.Generation == delivery.DeviceGeneration &&
                        x.DestinationHash == device.DestinationHash).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)
                            .SetProperty(x => x.Generation, x => x.Generation + 1).SetProperty(x => x.UpdatedAtUtc, Now), ct);
                else if (result.Unregistered)
                {
                    var current = await db.RiderPushDevices.FindAsync([device.Id], ct);
                    if (current?.Generation == delivery.DeviceGeneration && current.DestinationHash == device.DestinationHash)
                    { current.IsActive = false; current.Generation++; }
                }
                delivery.Status = result.Retryable && delivery.Attempts < 6 ? "pending" : "failed";
                var seconds = Math.Min(3600, 60 * Math.Pow(2, delivery.Attempts - 1)) + Random.Shared.Next(0, 30);
                var delay = TimeSpan.FromSeconds(seconds);
                if (result.ConfigurationError) delay = TimeSpan.FromHours(1);
                if (result.RetryAfter > delay) delay = result.RetryAfter.Value;
                delivery.NextAttemptAtUtc = Now.Add(delay);
                if (delivery.Status == "pending" && delivery.NextAttemptAtUtc >= request.ExpiresAtUtc) delivery.Status = "expired";
            }
        }
        delivery.LeaseOwner = null;
        delivery.LeaseUntilUtc = null;
        await SaveClaimAsync(ct);
        // A configuration error affects every destination: stop this run.
        return result?.ConfigurationError != true;
    }

    private async Task<bool> SaveClaimAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return false; }
    }

    [DisableConcurrentExecution(60)]
    public async Task ReminderAsync(CancellationToken ct = default)
    {
        if (!options.Value.DailyReminderEnabled) return;
        var now = Now;
        var key = $"daily-reminder:{now.AddHours(3):yyyy-MM-dd}";
        if (await db.NotificationRequests.AnyAsync(x => x.EventKey == key, ct)) return;
        var upper = await NotificationRules.EligibleRiders(db).Select(x => (int?)x.Id).MaxAsync(ct) ?? 0;
        db.NotificationRequests.Add(NotificationRules.Create(NotificationTemplates.DailyReminder, now, upper, eventKey: key));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (!await db.NotificationRequests.AnyAsync(x => x.EventKey == key, ct)) throw;
        }
    }

    [DisableConcurrentExecution(60)]
    public async Task CleanupAsync(CancellationToken ct = default)
    {
        var cutoff = Now.AddDays(-options.Value.HistoryRetentionDays);
        // This repository forces restrictive foreign keys, so remove terminal
        // children explicitly in the same transaction as their parent request.
        var ids = await db.NotificationRequests.Where(x => x.ExpiresAtUtc < cutoff && x.ExpansionComplete &&
            (x.LeaseUntilUtc == null || x.LeaseUntilUtc < Now) && !db.NotificationDeliveries.Any(d =>
                d.Recipient.RequestId == x.Id && (d.Status == "pending" || d.LeaseUntilUtc > Now))).OrderBy(x => x.ExpiresAtUtc)
            .Select(x => x.Id).Take(100).ToListAsync(ct);
        if (db.Database.IsRelational())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.NotificationDeliveries.Where(x => ids.Contains(x.Recipient.RequestId)).ExecuteDeleteAsync(ct);
            await db.NotificationRecipients.Where(x => ids.Contains(x.RequestId)).ExecuteDeleteAsync(ct);
            await db.NotificationRequests.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);
        }
        var now = Now;
        var stale = await db.RiderPushDevices.Where(x => x.IsActive && (x.Session.RevokedAtUtc != null ||
            x.Session.ExpiresAtUtc <= now || x.Session.Rider.Employee.IsDeleted ||
            x.Session.Rider.Employee.Status != "enable" || !x.Session.Rider.Employee.INKSA))
            .Take(500).ToListAsync(ct);
        foreach (var device in stale) { device.IsActive = false; device.Generation++; device.UpdatedAtUtc = now; }
        await SaveClaimAsync(ct);
    }
}
