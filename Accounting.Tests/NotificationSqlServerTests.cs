using Application.Authentication;
using Application.Contracts.Notifications;
using Application.Service.Notifications;
using Application.Service.RiderMobile;
using Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Xunit;

namespace Accounting.Tests;

public class NotificationSqlFactAttribute : FactAttribute
{
    public NotificationSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOTIFICATION_SQL_TEST_CONNECTION")))
            Skip = "Set NOTIFICATION_SQL_TEST_CONNECTION to a local SQL Server; tests create and delete their own isolated database.";
    }
}

public class NotificationSqlServerTests
{
    private sealed class Database : IAsyncDisposable
    {
        private readonly string name = "ExpressNotificationTests_" + Guid.NewGuid().ToString("N");
        private bool created;
        public string Connection { get; }
        public Database()
        {
            var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NOTIFICATION_SQL_TEST_CONNECTION"))
            { InitialCatalog = name, TrustServerCertificate = true, ConnectTimeout = 5 };
            Connection = builder.ConnectionString;
        }
        public ApplicationDbcontext Open() => new(new DbContextOptionsBuilder<ApplicationDbcontext>().UseSqlServer(Connection).Options);
        public async Task Initialize()
        {
            await using var db = Open();
            await db.Database.EnsureCreatedAsync();
            created = true;
            db.RiderDetails.Add(new Domain.Entities.RiderDetails
            {
                WorkingId = "test-rider", Company = new() { Name = "Notification SQL test" },
                Employee = new() { IqamaNo = 2100000001, NameAR = "مندوب", NameEN = "Rider", Status = "enable", INKSA = true }
            });
            await db.SaveChangesAsync();
            var now = DateTime.UtcNow;
            db.RiderMobileSessions.Add(new() { RiderId = 1, Language = "en", CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(30) });
            await db.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync()
        {
            // The generated database is owned exclusively by this test fixture.
            if (!created) return;
            if (!name.StartsWith("ExpressNotificationTests_", StringComparison.Ordinal)) throw new InvalidOperationException();
            await using var db = Open();
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static readonly IDataProtectionProvider Protection = new EphemeralDataProtectionProvider();
    private static ClaimsPrincipal Actor => new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "master"), new(ClaimTypes.Role, "Master")], "test"));
    private static NotificationService Service(ApplicationDbcontext db) => new(db, Options.Create(new NotificationOptions()), TimeProvider.System);
    private sealed class Sender : INotificationSender
    {
        public int Count;
        public Task<bool> IsReadyAsync(CancellationToken ct) => Task.FromResult(true);
        public async Task<PushResult> SendAsync(PushMessage message, CancellationToken ct)
        { Interlocked.Increment(ref Count); await Task.Delay(50, ct); return new(MessageId: "accepted"); }
    }
    private static NotificationJob Job(ApplicationDbcontext db, Sender sender) => new(db, sender, Protection,
        Options.Create(new NotificationOptions { Enabled = true }), TimeProvider.System, NullLogger<NotificationJob>.Instance);

    [NotificationSqlFact]
    public async Task SqlServer_ConcurrentRequestsAndWorkers_AreIdempotent_AndCleanupRespectsForeignKeys()
    {
        await using var database = new Database();
        await database.Initialize();
        var key = Guid.NewGuid();
        await using var db1 = database.Open();
        await using var db2 = database.Open();
        var queued = await Task.WhenAll(Service(db1).QueueAsync(Actor, 1, key, NotificationTemplates.DailyReminder, default),
            Service(db2).QueueAsync(Actor, 1, key, NotificationTemplates.DailyReminder, default));
        Assert.All(queued, result => Assert.True(result.IsSuccess));
        Assert.Equal(queued[0].Value.RequestId, queued[1].Value.RequestId);
        var session = await db1.RiderMobileSessions.SingleAsync();
        var installation = Guid.NewGuid();
        var registrations = await Task.WhenAll(
            new PushDeviceService(db1, Protection, TimeProvider.System).RegisterAsync(installation, 1, session.Id, new("fid", "android", "authorized", null), default),
            new PushDeviceService(db2, Protection, TimeProvider.System).RegisterAsync(installation, 1, session.Id, new("fid", "android", "authorized", null), default));
        Assert.All(registrations, result => Assert.True(result.IsSuccess));
        var sender = new Sender();
        await Task.WhenAll(Job(db1, sender).ProcessAsync(), Job(db2, sender).ProcessAsync());
        Assert.Equal(1, sender.Count);
        db1.ChangeTracker.Clear();
        Assert.Equal(1, await db1.NotificationRecipients.CountAsync());
        Assert.Equal("accepted", (await db1.NotificationDeliveries.SingleAsync()).Status);
        var request = await db1.NotificationRequests.SingleAsync();
        request.ExpiresAtUtc = DateTime.UtcNow.AddDays(-100);
        await db1.SaveChangesAsync();
        await Job(db1, sender).CleanupAsync();
        Assert.Empty(await db1.NotificationRequests.ToListAsync());
        Assert.Empty(await db1.NotificationDeliveries.ToListAsync());
    }

    [NotificationSqlFact]
    public async Task SqlServer_ConcurrentCoachingJobs_QueueEachEventOnlyOnce()
    {
        await using var database = new Database();
        await database.Initialize();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).DateTime);
        await using (var seed = database.Open())
        {
            var rider = await seed.RiderDetails.SingleAsync();
            seed.RiderShifts.Add(new()
            {
                RiderId = rider.Id, WorkingId = rider.WorkingId!, CompanyId = rider.CompanyId,
                ShiftDate = today.AddDays(-1), RealRejectedDailyOrders = 3
            });
            seed.RiderScorePerformances.AddRange(new Domain.Entities.RiderScorePerformance
            {
                RiderId = rider.Id, WorkingId = rider.WorkingId!, SourceRiderId = rider.WorkingId!,
                PerformanceDate = today.AddDays(-2), Segment = "C"
            }, new Domain.Entities.RiderScorePerformance
            {
                RiderId = rider.Id, WorkingId = rider.WorkingId!, SourceRiderId = rider.WorkingId!,
                PerformanceDate = today.AddDays(-1), Segment = "B"
            });
            await seed.SaveChangesAsync();
        }
        await using var first = database.Open();
        await using var second = database.Open();
        var settings = Options.Create(new NotificationOptions { Enabled = true });
        var one = new RiderMobileCoachingJob(first, settings, TimeProvider.System);
        var two = new RiderMobileCoachingJob(second, settings, TimeProvider.System);

        await Task.WhenAll(one.NotifyRejectionsAsync(), two.NotifyRejectionsAsync());
        await Task.WhenAll(one.NotifyRankChangesAsync(), two.NotifyRankChangesAsync());
        await Task.WhenAll(one.NotifyOnTimeDeliveryAsync(), two.NotifyOnTimeDeliveryAsync());

        Assert.Equal(3, await first.NotificationRequests.CountAsync());
        Assert.Equal(3, await first.NotificationRequests.Select(x => x.EventKey).Distinct().CountAsync());
    }

    [NotificationSqlFact]
    public async Task SqlServer_StaleRowversionCannotCompleteClaim_AndLogoutIsAtomic()
    {
        await using var database = new Database();
        await database.Initialize();
        await using var db1 = database.Open();
        await using var db2 = database.Open();
        var session = await db1.RiderMobileSessions.SingleAsync();
        await new PushDeviceService(db1, Protection, TimeProvider.System).RegisterAsync(Guid.NewGuid(), 1, session.Id,
            new("fid", "android", "authorized", null), default);
        var queued = await Service(db1).QueueAsync(Actor, 1, Guid.NewGuid(), NotificationTemplates.DailyReminder, default);
        var first = await db1.NotificationRequests.SingleAsync();
        var stale = await db2.NotificationRequests.SingleAsync();
        first.LeaseOwner = Guid.NewGuid();
        first.LeaseUntilUtc = DateTime.UtcNow.AddMinutes(2);
        await db1.SaveChangesAsync();
        stale.ExpansionComplete = true;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
        var mobile = new RiderMobileService(db1, new RiderMobileTokenProvider(Options.Create(RiderMobileServiceTests.Jwt)),
            Options.Create(new RiderMobileOptions()), TimeProvider.System);
        Assert.True((await mobile.LogoutAsync(session.Id, 1)).IsSuccess);
        db1.ChangeTracker.Clear();
        Assert.NotNull((await db1.RiderMobileSessions.SingleAsync()).RevokedAtUtc);
        Assert.False((await db1.RiderPushDevices.SingleAsync()).IsActive);
    }
}
