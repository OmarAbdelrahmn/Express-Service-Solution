using Application.Authentication;
using Application.Contracts.Notifications;
using Application.Service.Notifications;
using Application.Service.RiderMobile;
using Domain;
using Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Data.Common;
using System.Text.Json;
using Xunit;

namespace Accounting.Tests;

public class RiderMobileCoachingJobTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 25, 7, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public DateOnly Today => DateOnly.FromDateTime(Now.ToOffset(TimeSpan.FromHours(3)).DateTime);
    }

    private static RiderMobileCoachingJob Job(ApplicationDbcontext db, Clock? clock = null, NotificationOptions? settings = null) =>
        new(db, Options.Create(settings ?? new() { Enabled = true }), clock ?? new());

    private static void Shift(ApplicationDbcontext db, RiderDetails rider, DateOnly date, int real, string? workingId = null) =>
        db.RiderShifts.Add(new RiderShift
        {
            RiderId = rider.Id, WorkingId = workingId ?? rider.WorkingId!, ShiftDate = date,
            RealRejectedDailyOrders = real, RejectedDailyOrders = real + 2, CompanyId = rider.CompanyId
        });

    private static void Rank(ApplicationDbcontext db, RiderDetails rider, DateOnly date, string rank,
        int completed = 0, int onTime = 0, decimal score = 0) =>
        db.RiderScorePerformances.Add(new RiderScorePerformance
        {
            RiderId = rider.Id, WorkingId = rider.WorkingId!, SourceRiderId = rider.WorkingId!,
            PerformanceDate = date, Segment = rank, CompletedOrders = completed,
            CompletedOrdersInTime = onTime, OnTimeDeliveryScore = score
        });

    private static Dictionary<string, NotificationContent> Content(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, NotificationContent>>(json)!;

    [Theory]
    [InlineData(100, 98, false)]
    [InlineData(100, 99, true)]
    [InlineData(100, 100, true)]
    [InlineData(100000, 98999, false)]
    [InlineData(0, 0, false)]
    public async Task OnTimeDelivery_SelectsGoalOrMaintenanceUsingExactPercentage(int completed, int onTime, bool reached)
    {
        await using var db = RiderMobileServiceTests.Database();
        var rider = await RiderMobileServiceTests.Seed(db);
        var clock = new Clock();
        // The independent platform score must not determine the completed-order percentage.
        Rank(db, rider, clock.Today.AddDays(-1), "A", completed, onTime, reached ? 0m : 1m);
        await db.SaveChangesAsync();

        await Job(db, clock).NotifyOnTimeDeliveryAsync();

        var request = await db.NotificationRequests.SingleAsync();
        var content = Content(request.ContentJson);
        Assert.Equal(reached ? "Keep up your on-time deliveries!" : "Let's reach 99% on-time delivery!", content["en"].Title);
        Assert.Contains("99%", content["en"].Body);
        Assert.Equal(rider.Id, request.TargetRiderId);
        Assert.Equal("mobile-on-time:1:2026-10-24", request.EventKey);
        Assert.Null(NotificationRules.Validate(new(content,
            JsonSerializer.Deserialize<Dictionary<string, string>>(request.DataJson)!), clock.Now.UtcDateTime));
    }

    [Fact]
    public async Task OnTimeDelivery_UsesWeightedMonthToDateTotals_AndResetsAcrossYearBoundary()
    {
        await using var db = RiderMobileServiceTests.Database();
        var low = await RiderMobileServiceTests.Seed(db);
        var high = await RiderMobileServiceTests.Seed(db, 2);
        var clock = new Clock { Now = new(2026, 1, 2, 7, 0, 0, TimeSpan.Zero) };
        Rank(db, low, new(2026, 1, 1), "A", 1000, 900);
        Rank(db, low, new(2026, 1, 2), "A", 10, 10); // Today's 100% does not erase earlier late orders.
        Rank(db, high, new(2025, 12, 31), "A", 1000, 0); // Prior month is excluded.
        Rank(db, high, new(2026, 1, 1), "A", 1000, 990);
        Rank(db, high, new(2026, 1, 2), "A", 0, 0); // A zero-order day does not erase the month's 99%.
        await db.SaveChangesAsync();

        await Job(db, clock).NotifyOnTimeDeliveryAsync();

        var requests = await db.NotificationRequests.ToListAsync();
        Assert.Equal(2, requests.Count);
        Assert.Equal("Let's reach 99% on-time delivery!", Content(requests.Single(x => x.TargetRiderId == low.Id).ContentJson)["en"].Title);
        var highRequest = requests.Single(x => x.TargetRiderId == high.Id);
        Assert.Equal("Keep up your on-time deliveries!", Content(highRequest.ContentJson)["en"].Title);
        var action = JsonSerializer.Deserialize<Dictionary<string, string>>(highRequest.DataJson)!;
        Assert.Equal("2026", action["year"]);
        Assert.Equal("1", action["month"]);
    }

    [Fact]
    public async Task OnTimeDelivery_DeduplicatesCorrectionsAndRepeatedRuns_ButNotNextDailyUpdate()
    {
        await using var db = RiderMobileServiceTests.Database();
        var rider = await RiderMobileServiceTests.Seed(db);
        var clock = new Clock();
        Rank(db, rider, clock.Today.AddDays(-1), "A", 1000, 980);
        await db.SaveChangesAsync();
        await Job(db, clock).NotifyOnTimeDeliveryAsync();
        var performance = await db.RiderScorePerformances.SingleAsync();
        performance.CompletedOrdersInTime = 999;
        await db.SaveChangesAsync();
        await Job(db, clock).NotifyOnTimeDeliveryAsync();
        Assert.Single(await db.NotificationRequests.ToListAsync());

        Rank(db, rider, clock.Today, "A", 100, 100);
        await db.SaveChangesAsync();
        await Job(db, clock).NotifyOnTimeDeliveryAsync();
        clock.Now = clock.Now.AddDays(1); // No new daily upload: no extra reminder.
        await Job(db, clock).NotifyOnTimeDeliveryAsync();

        var requests = await db.NotificationRequests.OrderBy(x => x.EventKey).ToListAsync();
        Assert.Equal(2, requests.Count);
        Assert.Equal("Let's reach 99% on-time delivery!", Content(requests[0].ContentJson)["en"].Title);
        Assert.Equal("Keep up your on-time deliveries!", Content(requests[1].ContentJson)["en"].Title);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task OnTimeDelivery_RespectsProviderAndFeatureSwitches(bool enabled, bool alerts)
    {
        await using var db = RiderMobileServiceTests.Database();
        var rider = await RiderMobileServiceTests.Seed(db);
        var clock = new Clock();
        Rank(db, rider, clock.Today.AddDays(-1), "A", 100, 99);
        await db.SaveChangesAsync();

        await Job(db, clock, new() { Enabled = enabled, OnTimeDeliveryAlertsEnabled = alerts }).NotifyOnTimeDeliveryAsync();

        Assert.Empty(await db.NotificationRequests.ToListAsync());
    }

    [Fact]
    public async Task OnTimeDelivery_ExcludesMissingStaleFutureInactiveAndInvalidData()
    {
        await using var db = RiderMobileServiceTests.Database();
        var clock = new Clock();
        for (var id = 1; id <= 6; id++)
        {
            var rider = await RiderMobileServiceTests.Seed(db, id);
            if (id == 1) continue; // No performance record.
            if (id == 2) Rank(db, rider, clock.Today.AddDays(-2), "A", 100, 99);
            if (id == 3) Rank(db, rider, clock.Today.AddDays(1), "A", 100, 99);
            if (id == 4)
            {
                rider.Employee.IsDeleted = true;
                Rank(db, rider, clock.Today.AddDays(-1), "A", 100, 99);
            }
            if (id == 5) Rank(db, rider, clock.Today.AddDays(-1), "A", 100, 101);
            if (id == 6)
            {
                Rank(db, rider, clock.Today.AddDays(-1), "A", 100, 99);
                Rank(db, rider, clock.Today.AddDays(1), "A", 100, 0); // Must not hide the latest valid date.
            }
        }
        await db.SaveChangesAsync();

        await Job(db, clock).NotifyOnTimeDeliveryAsync();

        Assert.Equal(6, (await db.NotificationRequests.SingleAsync()).TargetRiderId);
    }

    [Fact]
    public async Task Rejections_SumsOnlyYesterdayRealRejections_AndDeduplicatesRepeatedUploads()
    {
        await using var db = RiderMobileServiceTests.Database();
        var rider = await RiderMobileServiceTests.Seed(db);
        var zero = await RiderMobileServiceTests.Seed(db, 2);
        var inactive = await RiderMobileServiceTests.Seed(db, 3);
        inactive.Employee.Status = "disable";
        var clock = new Clock();
        var yesterday = clock.Today.AddDays(-1);
        Shift(db, rider, yesterday, 2, "old-id");
        Shift(db, rider, yesterday, 3, "new-id");
        Shift(db, rider, yesterday.AddDays(-1), 100);
        Shift(db, rider, clock.Today, 200);
        Shift(db, rider, clock.Today.AddDays(1), 300);
        Shift(db, zero, yesterday, 0); // Raw rejected orders alone must not trigger an alert.
        Shift(db, inactive, yesterday, 10);
        await db.SaveChangesAsync();

        await Job(db, clock).NotifyRejectionsAsync();
        await Job(db, clock).NotifyRejectionsAsync();

        var request = await db.NotificationRequests.SingleAsync();
        Assert.Equal(rider.Id, request.TargetRiderId);
        Assert.Contains("5 real rejected orders yesterday (2026-10-24)", Content(request.ContentJson)["en"].Body);
        Assert.Equal("mobile-rejections:1:2026-10-24", request.EventKey);
        Assert.Equal(new DateTime(2026, 10, 25, 21, 0, 0, DateTimeKind.Utc), request.ExpiresAtUtc);
        var action = JsonSerializer.Deserialize<Dictionary<string, string>>(request.DataJson)!;
        Assert.Equal("performance_report", action["type"]);
        Assert.Equal("10", action["month"]);
    }

    [Theory]
    [InlineData("F", "E", true)]
    [InlineData("E", "D", true)]
    [InlineData("D", "C", true)]
    [InlineData("C", "B", true)]
    [InlineData("B", "A", true)]
    [InlineData("A", "F", false)]
    public async Task RankChange_UsesAAsHighestAndSelectsSupportiveMessage(string previous, string current, bool improved)
    {
        await using var db = RiderMobileServiceTests.Database();
        var rider = await RiderMobileServiceTests.Seed(db);
        var clock = new Clock();
        Rank(db, rider, clock.Today.AddDays(-2), previous);
        Rank(db, rider, clock.Today.AddDays(-1), current);
        await db.SaveChangesAsync();

        await Job(db, clock).NotifyRankChangesAsync();
        await Job(db, clock).NotifyRankChangesAsync();

        var request = await db.NotificationRequests.SingleAsync();
        var content = Content(request.ContentJson);
        Assert.Equal(improved ? "Your rank improved!" : "You can bounce back!", content["en"].Title);
        Assert.Contains($"from {previous} to {current}", content["en"].Body);
        Assert.Equal(3, content.Count);
        Assert.Equal(rider.Id, request.TargetRiderId);
    }

    [Theory]
    [InlineData(null, "A")]
    [InlineData("A", "A")]
    [InlineData(" b ", "B")]
    [InlineData("Unknown", "B")]
    [InlineData("B", "Unknown")]
    [InlineData("B", "")]
    public async Task RankChange_SkipsFirstUnchangedOrInvalidRanks(string? previous, string current)
    {
        await using var db = RiderMobileServiceTests.Database();
        var rider = await RiderMobileServiceTests.Seed(db);
        var clock = new Clock();
        if (previous is not null) Rank(db, rider, clock.Today.AddDays(-2), previous);
        Rank(db, rider, clock.Today.AddDays(-1), current);
        await db.SaveChangesAsync();

        await Job(db, clock).NotifyRankChangesAsync();

        Assert.Empty(await db.NotificationRequests.ToListAsync());
    }

    [Fact]
    public async Task RankChange_UsesLatestDatedSnapshot_IgnoresFutureAndStaleDataAndInactiveRiders()
    {
        await using var db = RiderMobileServiceTests.Database();
        var rider = await RiderMobileServiceTests.Seed(db);
        var stale = await RiderMobileServiceTests.Seed(db, 2);
        var inactive = await RiderMobileServiceTests.Seed(db, 3);
        inactive.Employee.INKSA = false;
        var clock = new Clock();
        Rank(db, rider, clock.Today, " a ");
        Rank(db, rider, clock.Today.AddDays(-2), "C"); // Insert order must not select the baseline.
        Rank(db, rider, clock.Today.AddDays(-1), "B");
        Rank(db, rider, clock.Today.AddDays(1), "F");
        Rank(db, stale, clock.Today.AddDays(-4), "A");
        Rank(db, stale, clock.Today.AddDays(-3), "F");
        Rank(db, inactive, clock.Today.AddDays(-2), "A");
        Rank(db, inactive, clock.Today.AddDays(-1), "F");
        await db.SaveChangesAsync();

        await Job(db, clock).NotifyRankChangesAsync();

        var request = await db.NotificationRequests.SingleAsync();
        Assert.Equal(rider.Id, request.TargetRiderId);
        Assert.Contains("from B to A", Content(request.ContentJson)["en"].Body);
        Assert.Contains("2026-10-25:B:A", request.EventKey);
    }

    [Fact]
    public async Task Jobs_UseSaudiCalendarAndCrossYearBaseline()
    {
        await using var db = RiderMobileServiceTests.Database();
        var rider = await RiderMobileServiceTests.Seed(db);
        var clock = new Clock { Now = new(2025, 12, 31, 22, 0, 0, TimeSpan.Zero) };
        Shift(db, rider, new(2025, 12, 31), 3);
        Rank(db, rider, new(2025, 12, 31), "B");
        Rank(db, rider, new(2026, 1, 1), "A");
        await db.SaveChangesAsync();

        await Job(db, clock).NotifyRejectionsAsync();
        await Job(db, clock).NotifyRankChangesAsync();

        var requests = await db.NotificationRequests.ToListAsync();
        Assert.Equal(2, requests.Count);
        var rejection = requests.Single(x => x.EventKey!.StartsWith("mobile-rejections:"));
        Assert.Contains("2025-12-31", rejection.EventKey);
        Assert.Equal(new DateTime(2026, 1, 1, 21, 0, 0, DateTimeKind.Utc), rejection.ExpiresAtUtc);
        Assert.Equal("12", JsonSerializer.Deserialize<Dictionary<string, string>>(rejection.DataJson)!["month"]);
        Assert.Contains(requests, x => x.EventKey!.Contains("2026-01-01:B:A"));
    }

    [Theory]
    [InlineData(false, true, true, 0)]
    [InlineData(true, false, true, 1)]
    [InlineData(true, true, false, 1)]
    public async Task Jobs_RespectProviderAndIndividualFeatureSwitches(bool enabled, bool rejections, bool ranks, int count)
    {
        await using var db = RiderMobileServiceTests.Database();
        var rider = await RiderMobileServiceTests.Seed(db);
        var clock = new Clock();
        Shift(db, rider, clock.Today.AddDays(-1), 1);
        Rank(db, rider, clock.Today.AddDays(-2), "B");
        Rank(db, rider, clock.Today.AddDays(-1), "A");
        await db.SaveChangesAsync();
        var job = Job(db, clock, new() { Enabled = enabled, RejectionAlertsEnabled = rejections, RankChangeAlertsEnabled = ranks });

        await job.NotifyRejectionsAsync();
        await job.NotifyRankChangesAsync();

        Assert.Equal(count, await db.NotificationRequests.CountAsync());
    }

    [Fact]
    public async Task Jobs_ProcessMoreThanOnePageWithoutDuplicates()
    {
        await using var db = RiderMobileServiceTests.Database();
        var clock = new Clock();
        for (var id = 1; id <= 101; id++)
        {
            var rider = await RiderMobileServiceTests.Seed(db, id);
            Shift(db, rider, clock.Today.AddDays(-1), 1);
            Rank(db, rider, clock.Today.AddDays(-2), "B");
            Rank(db, rider, clock.Today.AddDays(-1), "A");
        }
        await db.SaveChangesAsync();

        await Job(db, clock).NotifyRejectionsAsync();
        await Job(db, clock).NotifyRankChangesAsync();
        await Job(db, clock).NotifyRejectionsAsync();
        await Job(db, clock).NotifyRankChangesAsync();
        await Job(db, clock).NotifyOnTimeDeliveryAsync();
        await Job(db, clock).NotifyOnTimeDeliveryAsync();

        Assert.Equal(303, await db.NotificationRequests.CountAsync());
        Assert.Equal(303, await db.NotificationRequests.Select(x => x.EventKey).Distinct().CountAsync());
    }

    [Theory]
    [InlineData("ar", "الطلبات المرفوضة أمس", "تصنيفك في تحسن!", "معًا نصل إلى 99% في التوصيل!")]
    [InlineData("en", "Yesterday's rejected orders", "Your rank improved!", "Let's reach 99% on-time delivery!")]
    [InlineData("bn", "গতকালের প্রত্যাখ্যাত অর্ডার", "আপনার র‍্যাঙ্ক উন্নত হয়েছে!", "সময়মতো ডেলিভারিতে 99% অর্জন করি!")]
    public async Task Delivery_UsesLatestPreferredLanguageAndReportAction(string language, string rejectionTitle, string rankTitle, string onTimeTitle)
    {
        await using var db = RiderMobileServiceTests.Database();
        var rider = await RiderMobileServiceTests.Seed(db);
        var clock = new Clock();
        var protection = new EphemeralDataProtectionProvider();
        var mobile = new RiderMobileService(db, new RiderMobileTokenProvider(Options.Create(RiderMobileServiceTests.Jwt)),
            Options.Create(new RiderMobileOptions()), clock);
        await mobile.LoginAsync(new("2100000001", "en"));
        var session = await db.RiderMobileSessions.SingleAsync();
        await new PushDeviceService(db, protection, clock).RegisterAsync(Guid.NewGuid(), rider.Id, session.Id,
            new("coaching-fid", "android", "authorized", "1.0.0"), default);
        Shift(db, rider, clock.Today.AddDays(-1), 3);
        Rank(db, rider, clock.Today.AddDays(-2), "B");
        Rank(db, rider, clock.Today.AddDays(-1), "A");
        await db.SaveChangesAsync();
        await Job(db, clock).NotifyRejectionsAsync();
        await Job(db, clock).NotifyRankChangesAsync();
        await Job(db, clock).NotifyOnTimeDeliveryAsync();
        rider.PreferredLanguage = language; // Preference at delivery, rather than when queued.
        await db.SaveChangesAsync();
        var sender = new Sender();

        await new NotificationJob(db, sender, protection, Options.Create(new NotificationOptions { Enabled = true }),
            clock, NullLogger<NotificationJob>.Instance).ProcessAsync();

        Assert.Equal(3, sender.Messages.Count);
        Assert.Contains(sender.Messages, x => x.Title == rejectionTitle);
        Assert.Contains(sender.Messages, x => x.Title == rankTitle);
        Assert.Contains(sender.Messages, x => x.Title == onTimeTitle);
        Assert.All(sender.Messages, x => Assert.Equal("performance_report", x.Data["type"]));
    }

    private sealed class Sender : INotificationSender
    {
        public List<PushMessage> Messages { get; } = [];
        public Task<bool> IsReadyAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<PushResult> SendAsync(PushMessage message, CancellationToken ct)
        {
            Messages.Add(message);
            return Task.FromResult(new PushResult(MessageId: Guid.NewGuid().ToString()));
        }
    }

    [Fact]
    public async Task FailedQueue_DetachesPendingRequests_AndNextRunCanRecover()
    {
        var interceptor = new FailNotificationSaveInterceptor();
        await using var db = new ApplicationDbcontext(new DbContextOptionsBuilder<ApplicationDbcontext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(interceptor).Options);
        var rider = await RiderMobileServiceTests.Seed(db);
        var clock = new Clock();
        Shift(db, rider, clock.Today.AddDays(-1), 2);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateException>(() => Job(db, clock).NotifyRejectionsAsync());

        Assert.Empty(await db.NotificationRequests.ToListAsync());
        Assert.DoesNotContain(db.ChangeTracker.Entries(), x =>
            x.Entity is Domain.Entities.Notifications.NotificationRequest && x.State == EntityState.Added);
        await Job(db, clock).NotifyRejectionsAsync();
        Assert.Single(await db.NotificationRequests.ToListAsync());
    }

    private sealed class FailNotificationSaveInterceptor : SaveChangesInterceptor
    {
        private bool fail = true;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (fail && eventData.Context!.ChangeTracker.Entries<Domain.Entities.Notifications.NotificationRequest>()
                .Any(x => x.State == EntityState.Added))
            {
                fail = false;
                throw new DbUpdateException("Simulated notification storage failure.");
            }
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData("rejections")]
    [InlineData("rank")]
    [InlineData("on-time")]
    public async Task SqlServerQueries_TranslateBeforeOpeningConnection(string job)
    {
        await using var db = new ApplicationDbcontext(new DbContextOptionsBuilder<ApplicationDbcontext>()
            .UseSqlServer("Server=localhost;Database=QueryTranslationOnly;Integrated Security=True")
            .AddInterceptors(new RejectConnectionInterceptor()).Options);

        await Assert.ThrowsAsync<ConnectionReachedException>(() => job switch
        {
            "rejections" => Job(db).NotifyRejectionsAsync(),
            "rank" => Job(db).NotifyRankChangesAsync(),
            _ => Job(db).NotifyOnTimeDeliveryAsync()
        });
    }

    private sealed class ConnectionReachedException : Exception;
    private sealed class RejectConnectionInterceptor : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw new ConnectionReachedException();
    }
}
