using Application.Contracts.Employees;
using Application.Service.Empolyee;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public sealed class VehicleDocumentServiceTests
{
    // UTC is still the previous date; Saudi time is already October 18.
    private static readonly DateOnly Today = new(2026, 10, 18);
    private static readonly TimeProvider Clock = new FixedClock(new DateTimeOffset(2026, 10, 17, 21, 30, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(VehicleDocumentType.Registration)]
    [InlineData(VehicleDocumentType.Insurance)]
    [InlineData(VehicleDocumentType.Inspection)]
    public async Task Expiring_FiltersEachDocumentAndIncludesBothWindowBoundaries(VehicleDocumentType type)
    {
        await using var db = CreateDb();
        foreach (var (number, offset) in new[] { ("expired", -1), ("today", 0), ("near", 15), ("boundary", 30), ("later", 31) })
            db.Vehicles.Add(VehicleWithDate(number, type, Today.AddDays(offset)));
        db.Vehicles.Add(VehicleWithDate("missing", type, null));
        await db.SaveChangesAsync();

        var result = await new VehicleDocumentService(db, Clock).GetExpiringAsync(new() { DocumentType = type });

        Assert.True(result.IsSuccess);
        Assert.Equal(Today, result.Value.CheckDate);
        Assert.Equal(new[] { "today", "near", "boundary" }, result.Value.Items.Select(i => i.VehicleNumber));
        Assert.All(result.Value.Items, i => Assert.Equal(type, i.DocumentType));
        Assert.Equal(1, result.Value.DueTodayCount);
        Assert.Equal(2, result.Value.UpcomingCount);
        Assert.Equal(0, result.Value.ExpiredCount);
    }

    [Theory]
    [InlineData(VehicleDocumentType.Registration)]
    [InlineData(VehicleDocumentType.Insurance)]
    [InlineData(VehicleDocumentType.Inspection)]
    public async Task Missing_IncludesNullAndLegacyEmptyDateButExcludesExpired(VehicleDocumentType type)
    {
        await using var db = CreateDb();
        db.Vehicles.AddRange(VehicleWithDate("null", type, null),
            VehicleWithDate("legacy", type, DateOnly.MinValue),
            VehicleWithDate("expired", type, Today.AddDays(-1)));
        await db.SaveChangesAsync();

        var report = (await new VehicleDocumentService(db, Clock)
            .GetMissingAsync(new() { DocumentType = type })).Value;

        Assert.Equal(2, report.TotalDocuments);
        Assert.Equal(2, report.MissingCount);
        Assert.All(report.Items, i =>
        {
            Assert.Equal(VehicleDocumentStatus.Missing, i.Status);
            Assert.Null(i.ExpiryDate);
            Assert.Null(i.DaysUntilExpiry);
        });
    }

    [Fact]
    public async Task Reminders_CombineAllThreeDocumentsWithCountsBeforePaging()
    {
        await using var db = CreateDb();
        db.Vehicles.AddRange(
            new Vehicle { VehicleNumber = "A", LicenseExpiryDate = Today.AddDays(-2), InsuranceExpiryDate = Today, InspectionExpiryDate = Today.AddDays(30) },
            new Vehicle { VehicleNumber = "B", LicenseExpiryDate = Today.AddDays(31), InsuranceExpiryDate = Today.AddDays(40) });
        await db.SaveChangesAsync();
        var service = new VehicleDocumentService(db, Clock);

        var report = (await service.GetRemindersAsync(new() { PageSize = 2 })).Value;
        var next = (await service.GetRemindersAsync(new() { PageSize = 2, Page = 2 })).Value;

        Assert.Equal(4, report.TotalDocuments);
        Assert.Equal(1, report.MissingCount);
        Assert.Equal(1, report.ExpiredCount);
        Assert.Equal(1, report.DueTodayCount);
        Assert.Equal(1, report.UpcomingCount);
        Assert.Equal(new[] { VehicleDocumentStatus.Missing, VehicleDocumentStatus.Expired }, report.Items.Select(i => i.Status));
        Assert.Equal(new[] { VehicleDocumentStatus.DueToday, VehicleDocumentStatus.Upcoming }, next.Items.Select(i => i.Status));
        Assert.Equal(new[] { 0, 30 }, next.Items.Select(i => i.DaysUntilExpiry!.Value));
        Assert.All(report.Items.Concat(next.Items), i => Assert.False(string.IsNullOrWhiteSpace(i.Message)));
    }

    [Fact]
    public async Task Update_PersistsDatesClearsMissingAndImmediatelyRemovesRenewedReminder()
    {
        await using var db = CreateDb();
        db.Vehicles.Add(new Vehicle { VehicleNumber = "A", LicenseExpiryDate = Today });
        await db.SaveChangesAsync();
        var service = new VehicleDocumentService(db, Clock);

        var updated = await service.UpdateAsync("A", new(Today.AddYears(1), Today.AddDays(3), null));
        Assert.True(updated.IsSuccess);
        Assert.Equal(Today.AddDays(3), updated.Value.InsuranceExpiryDate);
        Assert.Null(updated.Value.InspectionExpiryDate);
        var report = (await service.GetRemindersAsync(new())).Value;
        Assert.DoesNotContain(report.Items, i => i.DocumentType == VehicleDocumentType.Registration);
        Assert.Contains(report.Items, i => i.DocumentType == VehicleDocumentType.Insurance && i.Status == VehicleDocumentStatus.Upcoming);
        Assert.Contains(report.Items, i => i.DocumentType == VehicleDocumentType.Inspection && i.Status == VehicleDocumentStatus.Missing);

        await service.UpdateAsync("A", new(null, DateOnly.MinValue, Today));
        db.ChangeTracker.Clear();
        var saved = await db.Vehicles.SingleAsync();
        Assert.Null(saved.LicenseExpiryDate);
        Assert.Null(saved.InsuranceExpiryDate);
        Assert.Equal(Today, saved.InspectionExpiryDate);
        Assert.Equal(404, (await service.UpdateAsync("unknown", new(null, null, null))).Error.StatuesCode);
    }

    [Fact]
    public async Task EmptyAndInvalidQueries_ReturnEmptyReportOrValidationFailure()
    {
        await using var db = CreateDb();
        var service = new VehicleDocumentService(db, Clock);
        Assert.Empty((await service.GetRemindersAsync(new())).Value.Items);
        foreach (var query in new VehicleDocumentQuery[]
        {
            new() { DaysBeforeExpiry = -1 }, new() { DaysBeforeExpiry = 3651 },
            new() { Page = 0 }, new() { PageSize = 201 }, new() { PageSize = 0 },
            new() { Page = int.MaxValue }, new() { DocumentType = (VehicleDocumentType)99 }
        })
            Assert.True((await service.GetRemindersAsync(query)).IsFailure);
    }

    [Fact]
    public async Task ZeroDayWindow_OnlyIncludesDocumentsDueToday()
    {
        await using var db = CreateDb();
        db.Vehicles.Add(new Vehicle { VehicleNumber = "A", LicenseExpiryDate = Today, InsuranceExpiryDate = Today.AddDays(1) });
        await db.SaveChangesAsync();
        var report = (await new VehicleDocumentService(db, Clock).GetExpiringAsync(new() { DaysBeforeExpiry = 0 })).Value;
        Assert.Equal(VehicleDocumentType.Registration, Assert.Single(report.Items).DocumentType);
    }

    [Fact]
    public void SqlServer_TranslatesCombinedFilteringPagingAndSummaryWithoutConnecting()
    {
        using var db = new ApplicationDbcontext(new DbContextOptionsBuilder<ApplicationDbcontext>()
            .UseSqlServer("Server=localhost;Database=VehicleDocumentSqlTranslation;Integrated Security=True;TrustServerCertificate=True").Options);
        var query = new VehicleDocumentService(db, Clock).BuildDocumentsQuery(null)
            .Where(d => d.ExpiryDate == null || d.ExpiryDate <= Today.AddDays(30));
        var pageSql = query.OrderBy(d => d.ExpiryDate).ThenBy(d => d.VehicleNumber).ThenBy(d => d.DocumentType)
            .Skip(2).Take(2).ToQueryString();
        var summarySql = query.GroupBy(d => 1).Select(g => new
        {
            Total = g.Count(), Missing = g.Count(d => d.ExpiryDate == null),
            Expired = g.Count(d => d.ExpiryDate < Today), DueToday = g.Count(d => d.ExpiryDate == Today),
            Upcoming = g.Count(d => d.ExpiryDate > Today)
        }).ToQueryString();
        Assert.Contains("UNION ALL", pageSql);
        Assert.Contains("OFFSET", pageSql);
        Assert.Contains("InsuranceExpiryDate", summarySql);
        Assert.Contains("InspectionExpiryDate", summarySql);
    }

    private static ApplicationDbcontext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbcontext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static Vehicle VehicleWithDate(string number, VehicleDocumentType type, DateOnly? date)
    {
        var vehicle = new Vehicle
        {
            VehicleNumber = number, PlateNumberA = number,
            LicenseExpiryDate = Today.AddYears(1), InsuranceExpiryDate = Today.AddYears(1), InspectionExpiryDate = Today.AddYears(1)
        };
        switch (type)
        {
            case VehicleDocumentType.Registration: vehicle.LicenseExpiryDate = date; break;
            case VehicleDocumentType.Insurance: vehicle.InsuranceExpiryDate = date; break;
            case VehicleDocumentType.Inspection: vehicle.InspectionExpiryDate = date; break;
        }
        return vehicle;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
