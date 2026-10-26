using System.Data.Common;
using Application.Contracts.RiderScorePerformance;
using Application.Service.RiderScorePerformance;
using Application.Service.Riders;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Accounting.Tests;

public class RiderScorePerformanceServiceTests
{
    [Fact]
    public async Task SqlServerQueries_TranslateBeforeOpeningConnection()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbcontext>()
            .UseSqlServer("Server=localhost;Database=QueryTranslationOnly;Integrated Security=True")
            .AddInterceptors(new RejectConnectionInterceptor())
            .Options;
        await using var db = new ApplicationDbcontext(options);
        var service = CreateService(db);

        await Assert.ThrowsAsync<ConnectionReachedException>(() =>
            service.ListAsync(new RiderScorePerformanceFilter(null, null, null, null, null, null)));
        await Assert.ThrowsAsync<ConnectionReachedException>(() =>
            service.ListForHousingAsync(1,
                new RiderScorePerformanceFilter(null, null, null, null, null, null)));
        await Assert.ThrowsAsync<ConnectionReachedException>(() => service.GetAsync(1));
    }

    private sealed class ConnectionReachedException : Exception;

    private sealed class RejectConnectionInterceptor : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) => throw new ConnectionReachedException();
    }

    [Fact]
    public async Task Import_MapsExactHeadersByName_AndUsesTheSuppliedDate()
    {
        await using var db = CreateDbContext();
        var rider = await SeedRiderAsync(db, 101, "TEST-RIDER-101", 7100000001);
        db.ChangeTracker.Clear();
        var service = CreateService(db);

        var headers = new[]
        {
            "segment",
            "completed_orders_in_time",
            "rider_id",
            "gross_orders",
            "successful_verification_requests",
            "verification_success_rate",
            "total_verification_requests",
            "completed_orders",
            "failed_orders_by_rider",
            "final_delivery_quality_score",
            "on_time_delivery_score"
        };
        var values = new Dictionary<string, object>
        {
            ["rider_id"] = rider.WorkingId!,
            ["total_verification_requests"] = 8,
            ["successful_verification_requests"] = 7,
            ["verification_success_rate"] = 0.875m,
            ["gross_orders"] = 24,
            ["completed_orders"] = 22,
            ["completed_orders_in_time"] = 20,
            ["failed_orders_by_rider"] = 1,
            ["on_time_delivery_score"] = 0.91m,
            ["final_delivery_quality_score"] = 0.89m,
            ["segment"] = "B"
        };
        await using var stream = CreateWorkbook(headers, values);
        var performanceDate = new DateOnly(2026, 8, 15);

        var result = await service.ImportAsync(stream, performanceDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.SuccessCount);
        Assert.Equal(0, result.Value.ErrorCount);
        var saved = await db.RiderScorePerformances.SingleAsync();
        Assert.Equal(rider.Id, saved.RiderId);
        Assert.Equal(rider.WorkingId, saved.SourceRiderId);
        Assert.Equal(performanceDate, saved.PerformanceDate);
        Assert.Equal(0.89m, saved.FinalDeliveryQualityScore);
    }

    [Fact]
    public async Task Import_AcceptsHeaderAlternatives_AndRejectsDuplicateRiderRows()
    {
        await using var db = CreateDbContext();
        var rider = await SeedRiderAsync(db, 102, "TEST-RIDER-102", 7100000002);
        db.ChangeTracker.Clear();
        var service = CreateService(db);

        var headers = new[]
        {
            "Rider Id",
            "Total Verification Requests",
            "SuccessfulVerificationRequests",
            "Verification Success Rate",
            "GrossOrders",
            "Completed Orders",
            "CompletedOrdersInTime",
            "Failed Orders by Rider",
            "OnTimeDeliveryScore",
            "Final Delivery Quality Score",
            "Rider Segment"
        };
        var firstRow = new object[] { rider.WorkingId!, 2, 2, 1m, 10, 10, 9, 0, 0.9m, 0.95m, "A" };
        var secondRow = new object[] { rider.WorkingId!, 3, 3, 1m, 11, 11, 10, 0, 0.91m, 0.96m, "A" };
        await using var stream = CreateWorkbook(headers, firstRow, secondRow);

        var result = await service.ImportAsync(stream, new DateOnly(2026, 8, 16));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.SuccessCount);
        Assert.Equal(1, result.Value.ErrorCount);
        Assert.Contains("Duplicate", result.Value.Errors.Single().Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.RiderScorePerformances.CountAsync());
    }

    [Fact]
    public async Task Import_ConvertsMonthlyCumulativeCountersToDailyCounters()
    {
        await using var db = CreateDbContext();
        var rider = await SeedRiderAsync(db, 106, "TEST-RIDER-106", 7100000006);
        db.ChangeTracker.Clear();
        var service = CreateService(db);

        using var firstDay = CreateCanonicalWorkbook(
            rider.WorkingId!, 10, 8, 15, 14, 13, 1, 0.8m, 0.93m, 0.91m);
        var firstResult = await service.ImportAsync(firstDay, new DateOnly(2026, 8, 1));

        using var secondDay = CreateCanonicalWorkbook(
            rider.WorkingId!, 20, 18, 30, 28, 26, 3, 0.9m, 0.95m, 0.94m);
        var secondResult = await service.ImportAsync(secondDay, new DateOnly(2026, 8, 2));

        Assert.True(firstResult.IsSuccess);
        Assert.True(secondResult.IsSuccess);
        var saved = await db.RiderScorePerformances
            .OrderBy(x => x.PerformanceDate)
            .ToListAsync();
        Assert.Equal(2, saved.Count);
        Assert.Equal(15, saved[0].GrossOrders);
        Assert.Equal(15, saved[1].GrossOrders);
        Assert.Equal(10, saved[1].TotalVerificationRequests);
        Assert.Equal(10, saved[1].SuccessfulVerificationRequests);
        Assert.Equal(14, saved[1].CompletedOrders);
        Assert.Equal(13, saved[1].CompletedOrdersInTime);
        Assert.Equal(2, saved[1].FailedOrdersByRider);
        Assert.Equal(0.94m, saved[1].FinalDeliveryQualityScore);
    }

    [Fact]
    public async Task Import_SkipsCumulativeCountersThatDecreaseWithinTheMonth()
    {
        await using var db = CreateDbContext();
        var rider = await SeedRiderAsync(db, 107, "TEST-RIDER-107", 7100000007);
        db.ChangeTracker.Clear();
        var service = CreateService(db);

        using var firstDay = CreateCanonicalWorkbook(
            rider.WorkingId!, 10, 8, 15, 14, 13, 1, 0.8m, 0.93m, 0.91m);
        await service.ImportAsync(firstDay, new DateOnly(2026, 8, 1));
        using var lowerSnapshot = CreateCanonicalWorkbook(
            rider.WorkingId!, 9, 8, 14, 14, 13, 1, 0.8m, 0.93m, 0.91m);

        var result = await service.ImportAsync(lowerSnapshot, new DateOnly(2026, 8, 2));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.SuccessCount);
        Assert.Equal(1, result.Value.ErrorCount);
        Assert.Contains("lower", result.Value.Errors.Single().Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.RiderScorePerformances.CountAsync());
    }

    [Fact]
    public async Task Import_AcceptsCumulativeSnapshotAfterTwentyDayGap()
    {
        await using var db = CreateDbContext();
        var rider = await SeedRiderAsync(db, 120, "TEST-RIDER-120", 7100000020);
        var service = CreateService(db);
        using var oldSnapshot = CreateCanonicalWorkbook(
            rider.WorkingId!, 10, 8, 100, 90, 80, 1, 0.8m, 0.9m, 0.9m);
        Assert.Equal(1, (await service.ImportAsync(oldSnapshot, new DateOnly(2026, 10, 6))).Value.SuccessCount);
        using var newSnapshot = CreateCanonicalWorkbook(
            rider.WorkingId!, 30, 28, 700, 690, 680, 1, 0.9m, 0.95m, 0.94m);

        var result = await service.ImportAsync(newSnapshot, new DateOnly(2026, 10, 26));

        Assert.Equal(1, result.Value.SuccessCount);
        Assert.Empty(result.Value.Errors);
        var saved = await db.RiderScorePerformances.SingleAsync(x => x.PerformanceDate.Day == 26);
        Assert.Equal(600, saved.GrossOrders);
        Assert.Equal(0, saved.FailedOrdersByRider);
    }

    [Fact]
    public async Task Import_BackdatedSnapshotPreservesLaterTotals_AndDoesNotRejectNextUpload()
    {
        await using var db = CreateDbContext();
        var rider = await SeedRiderAsync(db, 121, "TEST-RIDER-121", 7100000021);
        var service = CreateService(db);
        // Reproduce the affected rider's sequence: October 3 was entered after October 6.
        var snapshots = new[]
        {
            (Day: 1, Verified: 3, Gross: 18, Completed: 17, OnTime: 17, Failed: 0),
            (Day: 2, Verified: 7, Gross: 66, Completed: 66, OnTime: 64, Failed: 0),
            (Day: 4, Verified: 16, Gross: 119, Completed: 117, OnTime: 114, Failed: 1),
            (Day: 5, Verified: 24, Gross: 152, Completed: 150, OnTime: 147, Failed: 1),
            (Day: 6, Verified: 27, Gross: 182, Completed: 180, OnTime: 177, Failed: 1),
            (Day: 3, Verified: 10, Gross: 92, Completed: 91, OnTime: 88, Failed: 1)
        };
        foreach (var snapshot in snapshots)
        {
            using var workbook = CreateCanonicalWorkbook(rider.WorkingId!, snapshot.Verified,
                snapshot.Verified, snapshot.Gross, snapshot.Completed, snapshot.OnTime, snapshot.Failed,
                1m, 0.93m, 0.91m);
            var imported = await service.ImportAsync(workbook, new DateOnly(2026, 10, snapshot.Day));
            Assert.Equal(1, imported.Value.SuccessCount);
        }

        db.ChangeTracker.Clear();
        var records = await db.RiderScorePerformances.OrderBy(x => x.PerformanceDate).ToListAsync();
        Assert.Equal(27, records.Sum(x => x.TotalVerificationRequests));
        Assert.Equal(27, records.Sum(x => x.SuccessfulVerificationRequests));
        Assert.Equal(182, records.Sum(x => x.GrossOrders));
        Assert.Equal(180, records.Sum(x => x.CompletedOrders));
        Assert.Equal(177, records.Sum(x => x.CompletedOrdersInTime));
        Assert.Equal(1, records.Sum(x => x.FailedOrdersByRider));
        var next = records.Single(x => x.PerformanceDate.Day == 4);
        Assert.Equal(6, next.TotalVerificationRequests);
        Assert.Equal(27, next.GrossOrders);
        Assert.Equal(26, next.CompletedOrders);
        Assert.Equal(26, next.CompletedOrdersInTime);
        Assert.Equal(0, next.FailedOrdersByRider);
        Assert.Equal(0.91m, next.FinalDeliveryQualityScore);
        Assert.Equal(30, records.Single(x => x.PerformanceDate.Day == 6).GrossOrders);

        // Values from the affected rider's day 24 workbook. Failed orders remain at one.
        using var latest = CreateCanonicalWorkbook(rider.WorkingId!, 68, 68, 707, 699, 688, 1,
            1m, 0.9842632331902719m, 0.9914244030166353m);
        var result = await service.ImportAsync(latest, new DateOnly(2026, 10, 24));
        Assert.Equal(1, result.Value.SuccessCount);
        Assert.Empty(result.Value.Errors);
        Assert.Equal(707, await db.RiderScorePerformances.SumAsync(x => x.GrossOrders));
        var day24 = await db.RiderScorePerformances.SingleAsync(x => x.PerformanceDate.Day == 24);
        Assert.Equal(41, day24.TotalVerificationRequests);
        Assert.Equal(525, day24.GrossOrders);
        Assert.Equal(519, day24.CompletedOrders);
        Assert.Equal(511, day24.CompletedOrdersInTime);
        Assert.Equal(0, day24.FailedOrdersByRider);
    }

    [Fact]
    public async Task Import_Day24WorkbookValuesAcceptFailedOrderReduction()
    {
        await using var db = CreateDbContext();
        var rider = await SeedRiderAsync(db, 125, "TEST-RIDER-125", 7100000025);
        var service = CreateService(db);
        // Existing totals for the affected rider after the legacy backfill bug.
        using var inflated = CreateCanonicalWorkbook(rider.WorkingId!, 30, 30, 208, 205, 201, 2,
            1m, 0.95m, 0.94m);
        await service.ImportAsync(inflated, new DateOnly(2026, 10, 6));
        using var day24 = CreateCanonicalWorkbook(rider.WorkingId!, 68, 68, 707, 699, 688, 1,
            1m, 0.9842632331902719m, 0.9914244030166353m);

        var result = await service.ImportAsync(day24, new DateOnly(2026, 10, 24));

        Assert.Equal(1, result.Value.SuccessCount);
        Assert.Empty(result.Value.Errors);
        db.ChangeTracker.Clear();
        var saved = await db.RiderScorePerformances.SingleAsync(x => x.PerformanceDate.Day == 24);
        Assert.Equal(-1, saved.FailedOrdersByRider);
        Assert.Equal(499, saved.GrossOrders);
        Assert.Equal(1, await db.RiderScorePerformances.SumAsync(x => x.FailedOrdersByRider));

        var list = await service.ListAsync(new RiderScorePerformanceFilter(
            rider.Id, null, null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 24), null));
        Assert.Equal(1, list.Value.Totals.FailedOrdersByRider);
        var update = await service.UpdateAsync(saved.Id, new UpdateRiderScorePerformanceRequest(
            null, null, null, null, null, null, null, null, null, null, null, "A"));
        Assert.True(update.IsSuccess);
        Assert.Equal(-1, update.Value.FailedOrdersByRider);
    }

    [Fact]
    public async Task Import_BackdatedFailedOrdersCanExceedLaterTotalAfterAppeals()
    {
        await using var db = CreateDbContext();
        var rider = await SeedRiderAsync(db, 126, "TEST-RIDER-126", 7100000026);
        var service = CreateService(db);
        using var later = CreateCanonicalWorkbook(rider.WorkingId!, 10, 10, 100, 90, 80, 2,
            1m, 0.9m, 0.9m);
        await service.ImportAsync(later, new DateOnly(2026, 10, 24));
        using var earlier = CreateCanonicalWorkbook(rider.WorkingId!, 5, 5, 50, 45, 40, 3,
            1m, 0.9m, 0.9m);

        var result = await service.ImportAsync(earlier, new DateOnly(2026, 10, 3));

        Assert.Equal(1, result.Value.SuccessCount);
        Assert.Empty(result.Value.Errors);
        db.ChangeTracker.Clear();
        var records = await db.RiderScorePerformances.OrderBy(x => x.PerformanceDate).ToListAsync();
        Assert.Equal(3, records[0].FailedOrdersByRider);
        Assert.Equal(-1, records[1].FailedOrdersByRider);
        Assert.Equal(2, records.Sum(x => x.FailedOrdersByRider));
        Assert.Equal(100, records.Sum(x => x.GrossOrders));
    }

    [Theory]
    [InlineData(3, 2, -1)]
    [InlineData(3, 0, -3)]
    [InlineData(2, 2, 0)]
    [InlineData(2, 3, 1)]
    public async Task Import_FailedOrderAdjustmentsMatchLatestCumulativeTotal(int oldTotal, int newTotal, int adjustment)
    {
        await using var db = CreateDbContext();
        var rider = await SeedRiderAsync(db, 127, "TEST-RIDER-127", 7100000027);
        var service = CreateService(db);
        using var older = CreateCanonicalWorkbook(rider.WorkingId!, 10, 10, 100, 90, 80, oldTotal,
            1m, 0.9m, 0.9m);
        await service.ImportAsync(older, new DateOnly(2026, 10, 1));
        using var latest = CreateCanonicalWorkbook(rider.WorkingId!, 20, 20, 200, 190, 180, newTotal,
            1m, 0.95m, 0.94m);
        var result = await service.ImportAsync(latest, new DateOnly(2026, 10, 24));

        Assert.Equal(1, result.Value.SuccessCount);
        Assert.Empty(result.Value.Errors);
        db.ChangeTracker.Clear();
        var saved = await db.RiderScorePerformances.SingleAsync(x => x.PerformanceDate.Day == 24);
        Assert.Equal(adjustment, saved.FailedOrdersByRider);
        Assert.Equal(newTotal, await db.RiderScorePerformances.SumAsync(x => x.FailedOrdersByRider));

        using var duplicate = CreateCanonicalWorkbook(rider.WorkingId!, 20, 20, 200, 190, 180, newTotal,
            1m, 0.95m, 0.94m);
        Assert.Equal(0, (await service.ImportAsync(duplicate, new DateOnly(2026, 10, 24))).Value.SuccessCount);
        Assert.Equal(newTotal, await db.RiderScorePerformances.SumAsync(x => x.FailedOrdersByRider));
    }

    [Fact]
    public async Task Import_StillRejectsNegativeCumulativeFailedOrderValueInWorkbook()
    {
        await using var db = CreateDbContext();
        var rider = await SeedRiderAsync(db, 128, "TEST-RIDER-128", 7100000028);
        var service = CreateService(db);
        using var workbook = CreateCanonicalWorkbook(rider.WorkingId!, 10, 10, 100, 90, 80, -1,
            1m, 0.9m, 0.9m);

        var result = await service.ImportAsync(workbook, new DateOnly(2026, 10, 24));

        Assert.Equal(0, result.Value.SuccessCount);
        Assert.Contains("failed_orders_by_rider", Assert.Single(result.Value.Errors).Message);
        Assert.Empty(await db.RiderScorePerformances.ToListAsync());
    }

    [Fact]
    public async Task Import_RejectsBackdatedSnapshotAboveNextSnapshot_WithoutChangingSavedRecords()
    {
        await using var db = CreateDbContext();
        var rider = await SeedRiderAsync(db, 122, "TEST-RIDER-122", 7100000022);
        var service = CreateService(db);
        using var later = CreateCanonicalWorkbook(rider.WorkingId!, 10, 8, 100, 90, 80, 1,
            0.8m, 0.9m, 0.9m);
        await service.ImportAsync(later, new DateOnly(2026, 10, 6));
        using var earlier = CreateCanonicalWorkbook(rider.WorkingId!, 5, 4, 101, 45, 40, 2,
            0.8m, 0.9m, 0.9m);

        var result = await service.ImportAsync(earlier, new DateOnly(2026, 10, 3));

        Assert.Equal(0, result.Value.SuccessCount);
        var error = Assert.Single(result.Value.Errors);
        Assert.Contains("2026-10-06", error.Message);
        Assert.Contains("gross_orders", error.Message);
        // A later save in the same unit of work must not persist a rejected row's adjustments.
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var saved = await db.RiderScorePerformances.SingleAsync();
        Assert.Equal(100, saved.GrossOrders);
        Assert.Equal(1, saved.FailedOrdersByRider);
    }

    [Fact]
    public async Task Import_DoesNotSubtractPreviousMonth_OrAdjustNextMonth()
    {
        await using var db = CreateDbContext();
        var rider = await SeedRiderAsync(db, 123, "TEST-RIDER-123", 7100000023);
        var service = CreateService(db);
        foreach (var date in new[] { new DateOnly(2026, 9, 30), new DateOnly(2026, 11, 1) })
        {
            using var workbook = CreateCanonicalWorkbook(rider.WorkingId!);
            await service.ImportAsync(workbook, date);
        }
        using var october = CreateCanonicalWorkbook(rider.WorkingId!);

        var result = await service.ImportAsync(october, new DateOnly(2026, 10, 26));

        Assert.Equal(1, result.Value.SuccessCount);
        Assert.Equal(3, await db.RiderScorePerformances.CountAsync());
        Assert.All(await db.RiderScorePerformances.ToListAsync(), x => Assert.Equal(16, x.GrossOrders));
    }

    [Theory]
    [InlineData(0, "total_verification_requests")]
    [InlineData(1, "successful_verification_requests")]
    [InlineData(2, "gross_orders")]
    [InlineData(3, "completed_orders")]
    [InlineData(4, "completed_orders_in_time")]
    public async Task Import_LowerCounterErrorIdentifiesMetricValuesAndDate(int index, string metric)
    {
        await using var db = CreateDbContext();
        var rider = await SeedRiderAsync(db, 124, "TEST-RIDER-124", 7100000024);
        var service = CreateService(db);
        using var first = CreateCanonicalWorkbook(rider.WorkingId!, 10, 10, 10, 10, 10, 10,
            1m, 1m, 1m);
        await service.ImportAsync(first, new DateOnly(2026, 10, 6));
        var counts = Enumerable.Repeat(20, 6).ToArray();
        counts[index] = 9;
        counts[5] = 9; // An appeal is allowed, but must not hide a decrease in another counter.
        using var lower = CreateCanonicalWorkbook(rider.WorkingId!, counts[0], counts[1], counts[2],
            counts[3], counts[4], counts[5], 1m, 1m, 1m);

        var result = await service.ImportAsync(lower, new DateOnly(2026, 10, 26));

        Assert.Equal(0, result.Value.SuccessCount);
        var error = Assert.Single(result.Value.Errors);
        Assert.Contains(metric, error.Message);
        Assert.Contains("uploaded=9", error.Message);
        Assert.Contains("saved=10", error.Message);
        Assert.Contains("2026-10-26", error.Message);
        Assert.DoesNotContain("failed_orders_by_rider", error.Message);
        Assert.Single(await db.RiderScorePerformances.ToListAsync());
    }

    [Fact]
    public async Task Import_KeepsExcelRiderAndReturnsSubstituteNameAsNote()
    {
        await using var db = CreateDbContext();
        var original = await SeedRiderAsync(db, 103, "TEST-RIDER-103", 7100000003, housingId: 31);
        var substitute = await SeedRiderAsync(db, 104, "TEST-RIDER-104", 7100000004, housingId: 32);
        db.RiderShiftSubstitutions.Add(new RiderShiftSubstitution
        {
            ActualRiderId = original.Id,
            ActualRiderWorkingId = original.WorkingId!,
            SubstituteRiderId = substitute.Id,
            SubstituteWorkingId = substitute.WorkingId!,
            SubstituteRider = substitute,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(1),
            Reason = "Synthetic test substitution",
            IsActive = true,
            CreatedBy = "test"
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = CreateService(db);
        await using var stream = CreateCanonicalWorkbook(original.WorkingId!);

        var result = await service.ImportAsync(stream, new DateOnly(2026, 8, 17));

        Assert.True(result.IsSuccess);
        var saved = await db.RiderScorePerformances.SingleAsync();
        Assert.Equal(original.Id, saved.RiderId);
        Assert.Equal(original.WorkingId, saved.SourceRiderId);
        Assert.Equal(original.WorkingId, saved.WorkingId);
        Assert.Equal(substitute.Employee.NameAR, saved.SubstituteRiderName);

        var response = await service.GetAsync(saved.Id);
        Assert.True(response.IsSuccess);
        Assert.Equal(original.Id, response.Value.RiderId);
        Assert.Equal(original.WorkingId, response.Value.WorkingId);
        Assert.Equal(substitute.Employee.NameAR, response.Value.SubstituteRiderName);
        Assert.Equal(substitute.Employee.Housing!.Name, response.Value.HousingName);

        var list = await service.ListAsync(new RiderScorePerformanceFilter(
            original.Id, null, null, null, null, null));
        Assert.True(list.IsSuccess);
        Assert.Equal(
            substitute.Employee.NameAR,
            list.Value.Days.Single().Records.Single().SubstituteRiderName);
        Assert.Equal(
            substitute.Employee.Housing!.Name,
            list.Value.Days.Single().Records.Single().HousingName);

        var substituteHousingList = await service.ListForHousingAsync(
            substitute.Employee.HousingId!.Value,
            new RiderScorePerformanceFilter(null, null, null, null, null, null));
        Assert.True(substituteHousingList.IsSuccess);
        Assert.Single(substituteHousingList.Value.Days.Single().Records);

        var mainHousingList = await service.ListForHousingAsync(
            original.Employee.HousingId!.Value,
            new RiderScorePerformanceFilter(null, null, null, null, null, null));
        Assert.True(mainHousingList.IsSuccess);
        Assert.Empty(mainHousingList.Value.Days);
    }

    [Fact]
    public async Task Crud_EnforcesOneRecordPerRiderAndDate_AndSupportsFilters()
    {
        await using var db = CreateDbContext();
        var rider = await SeedRiderAsync(db, 105, "TEST-RIDER-105", 7100000005);
        db.ChangeTracker.Clear();
        var service = CreateService(db);
        var date = new DateOnly(2026, 8, 18);
        var create = CreateRequest(rider.WorkingId!, date, "B");

        var created = await service.CreateAsync(create);
        var duplicate = await service.CreateAsync(create);
        var listed = await service.ListAsync(new RiderScorePerformanceFilter(
            rider.Id, null, date, null, null, "B"));
        var updated = await service.UpdateAsync(
            created.Value.Id,
            new UpdateRiderScorePerformanceRequest(
                null, null, null, null, null, null, null, null, null, null, 0.97m, "A"));
        var deleted = await service.DeleteAsync(created.Value.Id);
        var missing = await service.GetAsync(created.Value.Id);

        Assert.True(created.IsSuccess);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(409, duplicate.Error.StatuesCode);
        Assert.True(listed.IsSuccess);
        Assert.Single(listed.Value.Days);
        Assert.Single(listed.Value.Days.Single().Records);
        Assert.True(updated.IsSuccess);
        Assert.Equal("A", updated.Value.Segment);
        Assert.Equal(0.97m, updated.Value.FinalDeliveryQualityScore);
        Assert.True(deleted.IsSuccess);
        Assert.False(missing.IsSuccess);
    }

    [Fact]
    public async Task List_ReturnsDailyRecordsAndTotalsForTheRequestedDateRange()
    {
        await using var db = CreateDbContext();
        var firstRider = await SeedRiderAsync(db, 108, "TEST-RIDER-108", 7100000008);
        var secondRider = await SeedRiderAsync(db, 109, "TEST-RIDER-109", 7100000009);
        db.ChangeTracker.Clear();
        var service = CreateService(db);
        var firstDate = new DateOnly(2026, 8, 20);
        var secondDate = firstDate.AddDays(1);
        var thirdDate = secondDate.AddDays(1);

        await service.CreateAsync(CreateRequest(firstRider.WorkingId!, firstDate, "A") with
        {
            GrossOrders = 10,
            CompletedOrders = 9,
            CompletedOrdersInTime = 8
        });
        await service.CreateAsync(CreateRequest(secondRider.WorkingId!, firstDate, "A") with
        {
            GrossOrders = 20,
            CompletedOrders = 19,
            CompletedOrdersInTime = 18
        });
        await service.CreateAsync(CreateRequest(firstRider.WorkingId!, secondDate, "B") with
        {
            GrossOrders = 30,
            CompletedOrders = 29,
            CompletedOrdersInTime = 28
        });

        var result = await service.ListAsync(new RiderScorePerformanceFilter(
            null, null, null, firstDate, thirdDate, null));

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Days.Count);
        Assert.Equal(firstDate, result.Value.Days[0].PerformanceDate);
        Assert.Equal(30, result.Value.Days[0].Totals.GrossOrders);
        Assert.Equal(30, result.Value.Days[1].Totals.GrossOrders);
        Assert.Equal(thirdDate, result.Value.Days[2].PerformanceDate);
        Assert.Empty(result.Value.Days[2].Records);
        Assert.Equal(0, result.Value.Days[2].Totals.GrossOrders);
        Assert.Equal(60, result.Value.Totals.GrossOrders);
        Assert.Equal(3, result.Value.Totals.RecordCount);
        Assert.Equal(2, result.Value.Totals.RiderCount);
    }

    [Fact]
    public async Task ListForHousing_ReturnsOnlyScoresForRidersCurrentlyInThatHousing()
    {
        await using var db = CreateDbContext();
        var housingRider = await SeedRiderAsync(
            db,
            110,
            "TEST-RIDER-110",
            7100000010,
            housingId: 21);
        var otherHousingRider = await SeedRiderAsync(
            db,
            111,
            "TEST-RIDER-111",
            7100000011,
            housingId: 22);
        db.ChangeTracker.Clear();
        var service = CreateService(db);
        var date = new DateOnly(2026, 8, 23);

        await service.CreateAsync(CreateRequest(housingRider.WorkingId!, date, "A"));
        await service.CreateAsync(CreateRequest(otherHousingRider.WorkingId!, date, "B"));

        var result = await service.ListForHousingAsync(
            21,
            new RiderScorePerformanceFilter(null, null, null, null, null, null));

        Assert.True(result.IsSuccess);
        var record = Assert.Single(Assert.Single(result.Value.Days).Records);
        Assert.Equal(housingRider.Id, record.RiderId);
        Assert.Equal(housingRider.Employee.Housing!.Name, record.HousingName);
        Assert.Equal(1, result.Value.Totals.RecordCount);
        Assert.Equal(1, result.Value.Totals.RiderCount);
    }

    [Fact]
    public async Task Import_RejectsWorkbookWithMissingRequiredHeader()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db);
        await using var stream = CreateWorkbook(
            ["rider_id", "segment"],
            ["TEST-RIDER-999", "A"]);

        var result = await service.ImportAsync(stream, new DateOnly(2026, 8, 19));

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error.StatuesCode);
        Assert.Contains("total_verification_requests", result.Error.Description);
        Assert.Empty(db.RiderScorePerformances);
    }

    private static RiderScorePerformanceService CreateService(ApplicationDbcontext db) =>
        new(db, new RiderWorkingIdHistoryService(db));

    private static ApplicationDbcontext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbcontext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<RiderDetails> SeedRiderAsync(
        ApplicationDbcontext db,
        int riderId,
        string workingId,
        long iqamaNo,
        int? housingId = null)
    {
        var company = await db.Companies.FirstOrDefaultAsync(x => x.Id == 901);
        if (company is null)
        {
            company = new Company { Id = 901, Name = "Test Company" };
            db.Companies.Add(company);
        }

        Housing? housing = null;
        if (housingId.HasValue)
        {
            housing = await db.Housings.FirstOrDefaultAsync(x => x.Id == housingId.Value);
            if (housing is null)
            {
                housing = new Housing
                {
                    Id = housingId.Value,
                    Name = $"Test Housing {housingId.Value}",
                    Address = $"Test Address {housingId.Value}",
                    Capacity = 100
                };
                db.Housings.Add(housing);
            }
        }

        var employee = new Employees
        {
            IqamaNo = iqamaNo,
            IqamaEndM = new DateOnly(2032, 1, 1),
            IqamaEndH = new DateOnly(2032, 1, 1),
            NameEN = $"Synthetic Rider {riderId}",
            NameAR = $"Synthetic Rider {riderId}",
            DateOfBirth = new DateOnly(1992, 1, 1),
            HousingId = housingId,
            Housing = housing
        };
        var rider = new RiderDetails
        {
            Id = riderId,
            WorkingId = workingId,
            EmployeeIqamaNo = iqamaNo,
            CompanyId = company.Id,
            Employee = employee,
            Company = company
        };

        db.AddRange(employee, rider);
        await db.SaveChangesAsync();
        return rider;
    }

    private static CreateRiderScorePerformanceRequest CreateRequest(
        string workingId,
        DateOnly date,
        string segment) => new(
            workingId,
            date,
            5,
            4,
            0.8m,
            20,
            19,
            18,
            1,
            0.94m,
            0.91m,
            segment);

    private static MemoryStream CreateCanonicalWorkbook(string workingId) =>
        CreateCanonicalWorkbook(workingId, 4, 3, 16, 15, 14, 1, 0.75m, 0.93m, 0.9m);

    private static MemoryStream CreateCanonicalWorkbook(
        string workingId,
        int totalVerificationRequests,
        int successfulVerificationRequests,
        int grossOrders,
        int completedOrders,
        int completedOrdersInTime,
        int failedOrdersByRider,
        decimal verificationSuccessRate,
        decimal onTimeDeliveryScore,
        decimal finalDeliveryQualityScore) =>
        CreateWorkbook(
            [
                "rider_id",
                "total_verification_requests",
                "successful_verification_requests",
                "verification_success_rate",
                "gross_orders",
                "completed_orders",
                "completed_orders_in_time",
                "failed_orders_by_rider",
                "on_time_delivery_score",
                "final_delivery_quality_score",
                "segment"
            ],
            [
                workingId,
                totalVerificationRequests,
                successfulVerificationRequests,
                verificationSuccessRate,
                grossOrders,
                completedOrders,
                completedOrdersInTime,
                failedOrdersByRider,
                onTimeDeliveryScore,
                finalDeliveryQualityScore,
                "B"
            ]);

    private static MemoryStream CreateWorkbook(
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, object> values)
    {
        var row = headers.Select(header => values[header]).ToArray();
        return CreateWorkbook(headers, row);
    }

    private static MemoryStream CreateWorkbook(
        IReadOnlyList<string> headers,
        params IReadOnlyList<object>[] rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Performance");

        for (var column = 0; column < headers.Count; column++)
            worksheet.Cell(1, column + 1).Value = headers[column];

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            for (var column = 0; column < rows[rowIndex].Count; column++)
                worksheet.Cell(rowIndex + 2, column + 1).Value = XLCellValue.FromObject(rows[rowIndex][column]);
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
