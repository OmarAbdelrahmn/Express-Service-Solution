using Application.Contracts.RiderScorePerformance;
using Application.Service.RiderScorePerformance;
using Application.Service.Riders;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public class RiderScorePerformanceServiceTests
{
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
    public async Task Import_KeepsExcelRiderAndReturnsSubstituteNameAsNote()
    {
        await using var db = CreateDbContext();
        var original = await SeedRiderAsync(db, 103, "TEST-RIDER-103", 7100000003);
        var substitute = await SeedRiderAsync(db, 104, "TEST-RIDER-104", 7100000004);
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

        var list = await service.ListAsync(new RiderScorePerformanceFilter(
            original.Id, null, null, null, null, null));
        Assert.True(list.IsSuccess);
        Assert.Equal(
            substitute.Employee.NameAR,
            list.Value.Days.Single().Records.Single().SubstituteRiderName);
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
        long iqamaNo)
    {
        var company = await db.Companies.FirstOrDefaultAsync(x => x.Id == 901);
        if (company is null)
        {
            company = new Company { Id = 901, Name = "Test Company" };
            db.Companies.Add(company);
        }

        var employee = new Employees
        {
            IqamaNo = iqamaNo,
            IqamaEndM = new DateOnly(2032, 1, 1),
            IqamaEndH = new DateOnly(2032, 1, 1),
            NameEN = $"Synthetic Rider {riderId}",
            NameAR = $"Synthetic Rider {riderId}",
            DateOfBirth = new DateOnly(1992, 1, 1)
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
