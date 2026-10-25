using Application.Authentication;
using Application.Contracts.Notifications;
using Application.Service.Notifications;
using Application.Service.RiderMobile;
using Domain;
using Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Xunit;

namespace Accounting.Tests;

public class NotificationTests
{
    private static readonly IDataProtectionProvider Protection = new EphemeralDataProtectionProvider();
    private static ClaimsPrincipal Actor(string id = "master", bool master = true) => new(new ClaimsIdentity(
        master ? [new(ClaimTypes.NameIdentifier, id), new(ClaimTypes.Role, "Master")] : [new(ClaimTypes.NameIdentifier, id)], "test"));
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Sender : INotificationSender
    {
        public List<PushMessage> Messages { get; } = [];
        public PushResult Result { get; set; } = new(MessageId: "provider-accepted");
        public Action? OnSend { get; set; }
        public Task<bool> IsReadyAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<PushResult> SendAsync(PushMessage message, CancellationToken ct)
        { Messages.Add(message); OnSend?.Invoke(); return Task.FromResult(Result); }
    }
    private static RiderMobileService Mobile(ApplicationDbcontext db, Clock clock) => new(db,
        new RiderMobileTokenProvider(Options.Create(RiderMobileServiceTests.Jwt)), Options.Create(new RiderMobileOptions()), clock);
    private static NotificationJob Job(ApplicationDbcontext db, Clock clock, Sender sender, NotificationOptions? options = null) =>
        new(db, sender, Protection, Options.Create(options ?? new NotificationOptions { Enabled = true }), clock, NullLogger<NotificationJob>.Instance);
    private static NotificationService Service(ApplicationDbcontext db, Clock clock, NotificationOptions? options = null) =>
        new(db, Options.Create(options ?? new()), clock);
    private static async Task<(RiderDetails Rider, Guid Session)> Login(ApplicationDbcontext db, Clock clock, int id = 1)
    {
        var rider = await RiderMobileServiceTests.Seed(db, id);
        await Mobile(db, clock).LoginAsync(new((2100000000L + id).ToString(), "en"));
        return (rider, (await db.RiderMobileSessions.SingleAsync(x => x.RiderId == id && x.RevokedAtUtc == null)).Id);
    }
    private static Task<Application.Abstraction.Result> Register(ApplicationDbcontext db, Clock clock, int rider, Guid session,
        Guid installation, string fid = "test-fid") => new PushDeviceService(db, Protection, clock)
            .RegisterAsync(installation, rider, session, new(fid, "android", "authorized", "1.0.0"), default);

    [Fact]
    public async Task Registration_TransfersOwnership_DeduplicatesAndOldSessionCannotDelete()
    {
        await using var db = RiderMobileServiceTests.Database();
        var clock = new Clock();
        var first = await Login(db, clock);
        var second = await Login(db, clock, 2);
        var installation = Guid.NewGuid();
        Assert.True((await Register(db, clock, 1, first.Session, installation)).IsSuccess);
        var generation = (await db.RiderPushDevices.SingleAsync()).Generation;
        await Register(db, clock, 1, first.Session, installation);
        Assert.Equal(generation, (await db.RiderPushDevices.SingleAsync()).Generation);
        await Register(db, clock, 2, second.Session, installation);
        await new PushDeviceService(db, Protection, clock).UnregisterAsync(installation, 1, first.Session, default);
        Assert.Equal(2, (await db.RiderPushDevices.SingleAsync(x => x.IsActive)).RiderId);
        await Register(db, clock, 1, first.Session, Guid.NewGuid());
        Assert.Single(await db.RiderPushDevices.Where(x => x.IsActive).ToListAsync());
        Assert.Equal(1, (await db.RiderPushDevices.SingleAsync(x => x.IsActive)).RiderId);
        Assert.DoesNotContain("test-fid", (await db.RiderPushDevices.SingleAsync(x => x.IsActive)).ProtectedDestination);
    }

    [Fact]
    public async Task LanguageRotation_TransfersDeviceAndCommitsEvent_LogoutUnbinds()
    {
        await using var db = RiderMobileServiceTests.Database();
        var clock = new Clock();
        var login = await Login(db, clock);
        await Register(db, clock, 1, login.Session, Guid.NewGuid());
        await Mobile(db, clock).SetLanguageAsync(login.Session, 1, "bn");
        var replacement = await db.RiderMobileSessions.SingleAsync(x => x.RevokedAtUtc == null);
        Assert.Equal(replacement.Id, (await db.RiderPushDevices.SingleAsync()).SessionId);
        Assert.Single(db.NotificationRequests);
        var sender = new Sender();
        await Job(db, clock, sender).ProcessAsync();
        Assert.Equal("প্রোফাইল আপডেট", Assert.Single(sender.Messages).Title);
        await Mobile(db, clock).LogoutAsync(replacement.Id, 1);
        Assert.False((await db.RiderPushDevices.SingleAsync()).IsActive);
        Assert.Equal(401, (await Register(db, clock, 1, replacement.Id, Guid.NewGuid())).Error.StatuesCode);
    }

    [Fact]
    public async Task StaffCommands_AreScopedIdempotentAndRejectChangedPayload()
    {
        await using var db = RiderMobileServiceTests.Database();
        var clock = new Clock();
        await Login(db, clock);
        var key = Guid.NewGuid();
        var service = Service(db, clock);
        var first = await service.QueueAsync(Actor(), 1, key, NotificationTemplates.DailyReminder, default);
        var replay = await service.QueueAsync(Actor(), 1, key, NotificationTemplates.DailyReminder, default);
        Assert.Equal(first.Value.RequestId, replay.Value.RequestId);
        Assert.Equal(409, (await service.QueueAsync(Actor(), 1, key, NotificationTemplates.ProfileChanged, default)).Error.StatuesCode);
        Assert.Equal(403, (await service.QueueAsync(Actor("admin", false), 1, Guid.NewGuid(), NotificationTemplates.DailyReminder, default)).Error.StatuesCode);
        var scoped = Service(db, clock, new() { StaffGrants = [new() { UserId = "scoped", CompanyId = 999, Send = true }] });
        Assert.Equal(404, (await scoped.QueueAsync(Actor("scoped", false), 1, Guid.NewGuid(), NotificationTemplates.DailyReminder, default)).Error.StatuesCode);
        Assert.Equal(404, (await scoped.StatusAsync(Actor("scoped", false), first.Value.RequestId, default)).Error.StatuesCode);
    }

    [Fact]
    public async Task Broadcast_CheckpointsAudience_FallsBackToEnglish_AndCountsNoDevices()
    {
        await using var db = RiderMobileServiceTests.Database();
        var clock = new Clock();
        var one = await Login(db, clock);
        await Login(db, clock, 2);
        await Register(db, clock, 1, one.Session, Guid.NewGuid());
        one.Rider.PreferredLanguage = "bn";
        await db.SaveChangesAsync();
        var payload = new SendNotificationRequest(new() { ["en"] = new("English", "Fallback") }, new() { ["type"] = "home" });
        var queued = await Service(db, clock).QueueAsync(Actor(), null, Guid.NewGuid(), payload, default);
        await Login(db, clock, 3); // Created after the request upper bound.
        var sender = new Sender();
        await Job(db, clock, sender).ProcessAsync();
        await Job(db, clock, sender).ProcessAsync();
        Assert.Equal("English", Assert.Single(sender.Messages).Title);
        var status = (await Service(db, clock).StatusAsync(Actor(), queued.Value.RequestId, default)).Value;
        Assert.Equal("completed", status.Status);
        Assert.Equal(2, status.AudienceRiders);
        Assert.Equal(1, status.SkippedNoDeviceRiders);
        Assert.Equal(1, status.AcceptedDeliveries);
    }

    [Theory]
    [InlineData("logout")]
    [InlineData("reassign")]
    [InlineData("disabled")]
    [InlineData("expiry")]
    public async Task Retry_RechecksSessionOwnershipEligibilityAndExpiry(string change)
    {
        await using var db = RiderMobileServiceTests.Database();
        var clock = new Clock();
        var login = await Login(db, clock);
        var installation = Guid.NewGuid();
        await Register(db, clock, 1, login.Session, installation);
        var sender = new Sender { Result = new(ErrorCode: "Unavailable", Retryable: true) };
        var queued = await Service(db, clock).QueueAsync(Actor(), 1, Guid.NewGuid(), NotificationTemplates.DailyReminder, default);
        await Job(db, clock, sender).ProcessAsync();
        Assert.Single(sender.Messages);
        if (change == "logout") await Mobile(db, clock).LogoutAsync(login.Session, 1);
        if (change == "reassign")
        {
            var second = await Login(db, clock, 2);
            await Register(db, clock, 2, second.Session, installation);
        }
        if (change == "disabled")
        {
            var rider = await db.RiderDetails.Include(x => x.Employee).SingleAsync(x => x.Id == 1);
            rider.Employee.Status = "disable";
            await db.SaveChangesAsync();
        }
        clock.Now += change == "expiry" ? TimeSpan.FromDays(2) : TimeSpan.FromMinutes(3);
        sender.Result = new(MessageId: "accepted");
        await Job(db, clock, sender).ProcessAsync();
        Assert.Single(sender.Messages);
        var delivery = await db.NotificationDeliveries.SingleAsync();
        Assert.Equal(change == "expiry" ? "expired" : "skipped", delivery.Status);
    }

    [Fact]
    public async Task TransientRetry_HonorsProviderDelay_AndReducesTtl()
    {
        await using var db = RiderMobileServiceTests.Database();
        var clock = new Clock();
        var login = await Login(db, clock);
        await Register(db, clock, 1, login.Session, Guid.NewGuid());
        await Service(db, clock).QueueAsync(Actor(), 1, Guid.NewGuid(), NotificationTemplates.DailyReminder, default);
        var eventTimeUtc = clock.Now.UtcDateTime;
        // Match SQL Server's datetime2 materialization, which loses DateTime.Kind.
        var request = await db.NotificationRequests.SingleAsync();
        request.CreatedAtUtc = DateTime.SpecifyKind(request.CreatedAtUtc, DateTimeKind.Unspecified);
        await db.SaveChangesAsync();
        clock.Now += TimeSpan.FromMinutes(2);
        var sender = new Sender { Result = new(ErrorCode: "QuotaExceeded", Retryable: true, RetryAfter: TimeSpan.FromMinutes(10)) };
        await Job(db, clock, sender).ProcessAsync();
        Assert.Equal(eventTimeUtc, Assert.Single(sender.Messages).EventTimestampUtc);
        Assert.Equal(DateTimeKind.Utc, sender.Messages[0].EventTimestampUtc.Kind);
        Assert.Equal(clock.Now.UtcDateTime.AddMinutes(10), (await db.NotificationDeliveries.SingleAsync()).NextAttemptAtUtc);
        clock.Now += TimeSpan.FromMinutes(11);
        sender.Result = new(MessageId: "accepted");
        await Job(db, clock, sender).ProcessAsync();
        Assert.Equal(2, sender.Messages.Count);
        Assert.Equal(eventTimeUtc, sender.Messages[1].EventTimestampUtc);
        Assert.Equal(DateTimeKind.Utc, sender.Messages[1].EventTimestampUtc.Kind);
        Assert.True(sender.Messages[1].RemainingLifetime < sender.Messages[0].RemainingLifetime);
        Assert.Equal("accepted", (await db.NotificationDeliveries.SingleAsync()).Status);
    }

    [Fact]
    public async Task UnregisteredResponse_CannotDeactivateNewDeviceGeneration()
    {
        await using var db = RiderMobileServiceTests.Database();
        var clock = new Clock();
        var login = await Login(db, clock);
        await Register(db, clock, 1, login.Session, Guid.NewGuid());
        await Service(db, clock).QueueAsync(Actor(), 1, Guid.NewGuid(), NotificationTemplates.DailyReminder, default);
        var sender = new Sender
        {
            Result = new(ErrorCode: "Unregistered", Unregistered: true),
            OnSend = () =>
            {
                var device = db.RiderPushDevices.Single();
                device.Generation++;
                device.ProtectedDestination = Protection.CreateProtector("ExpressService.PushDestinations.v1").Protect("new-fid");
                db.SaveChanges();
            }
        };
        await Job(db, clock, sender).ProcessAsync();
        Assert.True((await db.RiderPushDevices.SingleAsync()).IsActive);
        Assert.Equal("failed", (await db.NotificationDeliveries.SingleAsync()).Status);
    }

    [Fact]
    public async Task DailyReminder_IsIdempotentBySaudiDate()
    {
        await using var db = RiderMobileServiceTests.Database();
        var clock = new Clock { Now = new(2026, 10, 22, 22, 0, 0, TimeSpan.Zero) };
        await Login(db, clock);
        var job = Job(db, clock, new(), new() { DailyReminderEnabled = true });
        await job.ReminderAsync();
        await job.ReminderAsync();
        Assert.Equal("daily-reminder:2026-10-23", (await db.NotificationRequests.SingleAsync()).EventKey);
    }

    [Theory]
    [InlineData("url", "2026", "10")]
    [InlineData("performance_report", "2026", "11")]
    [InlineData("performance_report", "1999", "1")]
    [InlineData("performance_report", "2026", "13")]
    public void Validation_RejectsUnsupportedActionsAndReportPeriods(string type, string year, string month)
    {
        var request = NotificationTemplates.DailyReminder with { Data = new() { ["type"] = type, ["year"] = year, ["month"] = month } };
        Assert.NotNull(NotificationRules.Validate(request, new(2026, 10, 22)));
    }
}
