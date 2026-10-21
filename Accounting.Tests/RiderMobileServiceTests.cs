using Application.Authentication;
using Application.Contracts.RiderMobile;
using Application.Service.RiderMobile;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Xunit;

namespace Accounting.Tests;

public class RiderMobileServiceTests
{
    internal static readonly JwtOptions Jwt = new()
    {
        Issuer = "rider-tests", Audience = "staff-tests", ExpiryIn = 60,
        Key = "test-only-secret-key-with-more-than-thirty-two-bytes"
    };

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    [InlineData("bn")]
    public async Task Login_PersistsLanguageAndExpiringScopedSession(string language)
    {
        await using var db = Database();
        var rider = await Seed(db);
        var clock = new TestClock();
        var service = Service(db, clock);
        var result = await service.LoginAsync(new("2100000001", language));

        Assert.True(result.IsSuccess);
        Assert.Equal(language, result.Value.Language);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddDays(30), result.Value.ExpiresAtUtc);
        Assert.Equal(language, rider.PreferredLanguage);
        var session = await db.RiderMobileSessions.SingleAsync();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Value.AccessToken);
        Assert.Equal(language, jwt.Claims.Single(x => x.Type == "lang").Value);
        Assert.Equal(rider.Id.ToString(), jwt.Subject);
        Assert.DoesNotContain(jwt.Claims, x => x.Type is "roles" or "security_stamp");
        Assert.True(await service.IsSessionValidAsync(session.Id, rider.Id, language));
        Assert.False(await service.IsSessionValidAsync(session.Id, 999, language));
        clock.Now = clock.Now.AddDays(30);
        Assert.False(await service.IsSessionValidAsync(session.Id, rider.Id, language));
    }

    [Fact]
    public async Task LanguageChange_ReplacesSessionWithoutExtendingExpiry_AndLogoutRevokesIt()
    {
        await using var db = Database();
        var rider = await Seed(db);
        var clock = new TestClock();
        var service = Service(db, clock);
        var first = await service.LoginAsync(new("2100000001", "en"));
        var original = await db.RiderMobileSessions.SingleAsync();
        clock.Now = clock.Now.AddDays(2);

        var changed = await service.SetLanguageAsync(original.Id, rider.Id, "bn");
        Assert.True(changed.IsSuccess);
        Assert.Equal(first.Value.ExpiresAtUtc, changed.Value.ExpiresAtUtc);
        Assert.False(await service.IsSessionValidAsync(original.Id, rider.Id, "en"));
        var replacement = await db.RiderMobileSessions.SingleAsync(x => x.RevokedAtUtc == null);
        Assert.True(await service.IsSessionValidAsync(replacement.Id, rider.Id, "bn"));
        Assert.False(await service.IsSessionValidAsync(replacement.Id, rider.Id, "en"));
        Assert.True((await service.LogoutAsync(replacement.Id, rider.Id)).IsSuccess);
        Assert.False(await service.IsSessionValidAsync(replacement.Id, rider.Id, "bn"));
        var nextLogin = await service.LoginAsync(new("2100000001"));
        Assert.Equal("bn", nextLogin.Value.Language);
    }

    [Theory]
    [InlineData("disable", false, true)]
    [InlineData("enable", true, true)]
    [InlineData("enable", false, false)]
    public async Task IneligibleRider_CannotLoginOrKeepExistingSession(string status, bool deleted, bool inKsa)
    {
        await using var db = Database();
        var rider = await Seed(db);
        var service = Service(db);
        await service.LoginAsync(new("2100000001", "ar"));
        var session = await db.RiderMobileSessions.SingleAsync();
        rider.Employee.Status = status;
        rider.Employee.IsDeleted = deleted;
        rider.Employee.INKSA = inKsa;
        await db.SaveChangesAsync();

        Assert.True((await service.LoginAsync(new("2100000001", "ar"))).IsFailure);
        Assert.False(await service.IsSessionValidAsync(session.Id, rider.Id, "ar"));
    }

    [Theory]
    [InlineData("2100000002", "ar", 401)]
    [InlineData("invalid", "en", 401)]
    [InlineData("2100000001", "fr", 400)]
    public async Task Login_RejectsUnknownOrMalformedCredentials(string iqama, string language, int status)
    {
        await using var db = Database();
        await Seed(db);
        var result = await Service(db).LoginAsync(new(iqama, language));
        Assert.True(result.IsFailure);
        Assert.Equal(status, result.Error.StatuesCode);
        Assert.Empty(db.RiderMobileSessions);
    }

    [Fact]
    public void Tokens_ValidateInMobileSchemeAndFailStaffValidation()
    {
        var session = new RiderMobileSession
        {
            RiderId = 1, Language = "ar", CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30)
        };
        var token = new RiderMobileTokenProvider(Options.Create(Jwt)).Generate(session);
        var handler = new JwtSecurityTokenHandler();
        handler.ValidateToken(token, RiderMobileTokenProvider.ValidationParameters(Jwt), out _);
        var staff = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
            ValidateIssuerSigningKey = true, ValidIssuer = Jwt.Issuer, ValidAudience = Jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Jwt.Key))
        };
        Assert.ThrowsAny<SecurityTokenException>(() => handler.ValidateToken(token, staff, out _));
    }

    [Theory]
    [InlineData("ar", "التوصيل")]
    [InlineData("en", "On-time")]
    [InlineData("bn", "ডেলিভারি")]
    public async Task Dashboard_UsesDailyTotalsLatestRankAndLocalizedTargetedAdvice(string language, string expected)
    {
        await using var db = Database();
        var rider = await Seed(db);
        var other = await Seed(db, 2);
        AddScore(db, rider, new(2026, 10, 2), 1, 1, 1, "A", .99m);
        AddScore(db, rider, new(2026, 10, 3), 99, 79, 0, "C", .8m);
        AddScore(db, other, new(2026, 10, 3), 1000, 1000, 1000, "A", 1m);
        AddScore(db, rider, new(2026, 9, 30), 50, 50, 50, "A", 1m);
        AddScore(db, rider, new(2026, 10, 25), 1000, 1000, 1000, "A", 1m);
        await db.SaveChangesAsync();

        var result = await Service(db).GetDashboardAsync(rider.Id, language);
        var current = result.Value.CurrentMonth;
        Assert.Equal("C", current.Performance.Rank);
        Assert.Equal(100, current.Performance.CompletedOrders);
        Assert.Equal(80m, current.Performance.OnTimeDeliveryPercentage);
        Assert.Equal(80m, current.Performance.DeliveryQualityPercentage);
        Assert.Equal(95m, current.CoachingTargets.OnTimeDeliveryPercentage);
        var note = Assert.Single(current.Notes.Where(x => x.Code == "OnTime"));
        Assert.Contains(expected, note.Message);
        Assert.False(string.IsNullOrWhiteSpace(note.Advice));
        Assert.Equal(15m, note.Gap);
        Assert.Equal("A", result.Value.PreviousMonth.Performance.Rank);
        Assert.DoesNotContain(result.Value.PreviousMonth.Notes, x => x.Code == "OnTime");
    }

    [Fact]
    public async Task Attendance_SumsWorkingIdsWithoutDoubleCounting_DistinguishesMissingAndFutureDays()
    {
        await using var db = Database();
        var rider = await Seed(db);
        AddShift(db, rider, new(2026, 10, 4), 4, 5, "old");
        AddShift(db, rider, new(2026, 10, 4), 4, 7, "new");
        AddShift(db, rider, new(2026, 10, 5), 7, 4);
        AddShift(db, rider, new(2026, 10, 8), 0, 0); // Thursday
        AddShift(db, rider, new(2026, 10, 20), 2, 3); // Today is still in progress
        AddShift(db, rider, new(2026, 10, 25), 10, 50); // Future import is excluded
        await db.SaveChangesAsync();

        var report = (await Service(db).GetMonthAsync(rider.Id, "en", 2026, 10)).Value;
        var attendance = report.Attendance;
        Assert.Equal(31, attendance.Days.Count);
        Assert.Equal(1, attendance.QualifiedWorkingDays);
        Assert.Equal(17m, attendance.TotalHours);
        Assert.Equal(19, attendance.AcceptedOrders);
        Assert.Equal(1, attendance.ShortDays);
        Assert.Equal(1, attendance.ProhibitedRecordedDaysOff);
        var merged = attendance.Days.Single(x => x.Date.Day == 4);
        Assert.Equal(8, merged.WorkingHours);
        Assert.Equal(12, merged.AcceptedOrders);
        var missing = attendance.Days.Single(x => x.Date.Day == 6);
        Assert.False(missing.HasShiftData);
        Assert.False(missing.IsRecordedDayOff);
        Assert.Empty(missing.Notes);
        var today = attendance.Days.Single(x => x.Date.Day == 20);
        Assert.Equal("InProgress", today.Status);
        Assert.Empty(today.Notes);
        var future = attendance.Days.Single(x => x.Date.Day == 25);
        Assert.Equal("Future", future.Status);
        Assert.False(future.HasShiftData);
        Assert.Null(attendance.IsCompliant);
    }

    [Theory]
    [InlineData(2026, 2, 28, 2)]
    [InlineData(2024, 2, 29, 3)]
    [InlineData(2026, 9, 30, 3)]
    [InlineData(2026, 8, 31, 3)]
    public async Task Calendar_EnforcesWeekdaysMonthEdgesAndVariableMonthLengths(int year, int month, int count, int maxBreaks)
    {
        await using var db = Database();
        var rider = await Seed(db);
        var report = (await Service(db).GetMonthAsync(rider.Id, "bn", year, month)).Value;
        Assert.Equal(count, report.Attendance.Days.Count);
        Assert.Equal(maxBreaks, report.Attendance.MaximumBreakDays);
        Assert.Null(report.Attendance.IsCompliant); // No data does not establish absence.
        Assert.Null(report.Performance.OnTimeDeliveryPercentage);
        Assert.Null(report.Performance.Rank);
        foreach (var day in report.Attendance.Days)
        {
            var allowed = day.Date.Day > 3 && day.Date.Day <= count - 3 &&
                day.Date.DayOfWeek is not (DayOfWeek.Thursday or DayOfWeek.Friday or DayOfWeek.Saturday);
            Assert.Equal(allowed, day.IsBreakDateAllowed);
            Assert.Equal(allowed, day.BreakRestrictionCodes.Count == 0);
            Assert.Equal(day.BreakRestrictionCodes.Count, day.BreakRestrictionMessages.Count);
            Assert.All(day.BreakRestrictionMessages, text => Assert.Contains("ছুটি", text));
        }
    }

    [Fact]
    public async Task ClosedMonth_With26FullDaysAndTwoAllowedBreaks_IsCompliant()
    {
        await using var db = Database();
        var rider = await Seed(db);
        for (var day = 1; day <= 28; day++)
            AddShift(db, rider, new(2026, 2, day), day is 8 or 9 ? 0 : 8, day is 8 or 9 ? 0 : 10);
        await db.SaveChangesAsync();
        var result = (await Service(db).GetMonthAsync(rider.Id, "en", 2026, 2)).Value;
        Assert.True(result.Attendance.IsCompliant);
        Assert.Equal(26, result.Attendance.QualifiedWorkingDays);
        Assert.Equal(208, result.Attendance.TotalHours);
        Assert.DoesNotContain(result.Notes, x => x.Code is "WorkingDays" or "DailyHours" or "ProhibitedBreak");
    }

    [Fact]
    public async Task ExtraHours_DoNotReplaceQualifiedDays_AndConfiguredCoachingTargetsAreUsed()
    {
        await using var db = Database();
        var rider = await Seed(db);
        AddShift(db, rider, new(2026, 9, 1), 16, 10);
        AddScore(db, rider, new(2026, 9, 1), 100, 90, 100, "B", .9m);
        await db.SaveChangesAsync();
        var service = Service(db, settings: new() { OnTimeDeliveryTargetPercentage = 85,
            DeliveryQualityTargetPercentage = 85, VerificationTargetPercentage = 85 });
        var result = (await service.GetMonthAsync(rider.Id, "en", 2026, 9)).Value;
        Assert.Equal(1, result.Attendance.QualifiedWorkingDays);
        Assert.Equal(25, result.Attendance.RemainingQualifiedDays);
        Assert.DoesNotContain(result.Notes, x => x.Code is "OnTime" or "Quality" or "Verification");
        Assert.Contains(result.Notes, x => x.Code == "RankBelowA");
    }

    [Fact]
    public async Task HoursJustBelowEight_DoNotQualifyAfterDisplayRounding()
    {
        await using var db = Database();
        var rider = await Seed(db);
        AddShift(db, rider, new(2026, 9, 1), 7.999f, 10);
        await db.SaveChangesAsync();
        var report = (await Service(db).GetMonthAsync(rider.Id, "en", 2026, 9)).Value;
        Assert.Equal(0, report.Attendance.QualifiedWorkingDays);
        Assert.Equal(1, report.Attendance.ShortDays);
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(2026, 11)]
    [InlineData(1999, 1)]
    public async Task MonthQuery_RejectsInvalidAndFuturePeriods(int year, int month)
    {
        await using var db = Database();
        await Seed(db);
        var result = await Service(db).GetMonthAsync(1, "ar", year, month);
        Assert.True(result.IsFailure);
        Assert.Equal(400, result.Error.StatuesCode);
    }

    [Fact]
    public async Task Dashboard_UsesSaudiDateAndPreviousYearAcrossJanuaryBoundary()
    {
        await using var db = Database();
        await Seed(db);
        var clock = new TestClock { Now = new DateTimeOffset(2025, 12, 31, 22, 0, 0, TimeSpan.Zero) };
        var report = (await Service(db, clock).GetDashboardAsync(1, "ar")).Value;
        Assert.Equal(new DateOnly(2026, 1, 1), report.Today);
        Assert.Contains("يناير", report.CurrentMonth.MonthName);
        Assert.Equal(2025, report.PreviousMonth.Year);
        Assert.Equal(12, report.PreviousMonth.Month);
    }

    internal static ApplicationDbcontext Database() => new(new DbContextOptionsBuilder<ApplicationDbcontext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static RiderMobileService Service(ApplicationDbcontext db, TestClock? clock = null, RiderMobileOptions? settings = null) =>
        new(db, new RiderMobileTokenProvider(Options.Create(Jwt)), Options.Create(settings ?? new()), clock ?? new());

    internal static async Task<RiderDetails> Seed(ApplicationDbcontext db, int id = 1)
    {
        var company = await db.Companies.FindAsync(900) ?? new Company { Id = 900, Name = "Test" };
        var rider = new RiderDetails
        {
            Id = id, WorkingId = $"rider-{id}", Company = company, CompanyId = 900,
            EmployeeIqamaNo = 2100000000 + id,
            Employee = new Employees
            {
                IqamaNo = 2100000000 + id, NameAR = $"مندوب {id}", NameEN = $"Rider {id}",
                DateOfBirth = new(1990, 1, 1), IqamaEndH = new(2030, 1, 1), IqamaEndM = new(2030, 1, 1)
            }
        };
        db.RiderDetails.Add(rider);
        await db.SaveChangesAsync();
        return rider;
    }

    private static void AddShift(ApplicationDbcontext db, RiderDetails rider, DateOnly date, float hours,
        int orders, string? workingId = null) => db.RiderShifts.Add(new RiderShift
        {
            RiderId = rider.Id, WorkingId = workingId ?? rider.WorkingId!, ShiftDate = date,
            WorkingHours = hours, AcceptedDailyOrders = orders, CompanyId = rider.CompanyId
        });

    private static void AddScore(ApplicationDbcontext db, RiderDetails rider, DateOnly date,
        int completed, int onTime, int verified, string rank, decimal quality) => db.RiderScorePerformances.Add(new()
        {
            RiderId = rider.Id, WorkingId = rider.WorkingId!, SourceRiderId = rider.WorkingId!,
            PerformanceDate = date, GrossOrders = completed, CompletedOrders = completed,
            CompletedOrdersInTime = onTime, TotalVerificationRequests = completed,
            SuccessfulVerificationRequests = verified, FinalDeliveryQualityScore = quality,
            OnTimeDeliveryScore = quality, Segment = rank
        });

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
