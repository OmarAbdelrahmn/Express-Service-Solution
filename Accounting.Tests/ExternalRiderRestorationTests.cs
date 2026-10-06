using Application.Contracts.OutRiderInfos;
using Application.Contracts.OutageShiftPerformances;
using Application.Service.OutRiderInfos;
using Application.Service.OutageShiftPerformances;
using Application.Service.Riders;
using ClosedXML.Excel;
using Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public class ExternalRiderRestorationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DailyUpload_SkipsRegisteredExternalIdsAndReportsUnknownIds(bool compare)
    {
        await using var db = CreateDbContext();
        var infoService = new OutRiderInfoService(db);
        var created = await infoService.CreateAsync(
            new CreateOutRiderInfoRequest(" EXT-001 ", "External Rider", "0500000000"), "Test");
        Assert.True(created.IsSuccess);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Daily performance");
        string[] headers = ["Rider Id", "Completed Deliveries", "Declined Deliveries",
            "Stacked Deliveries", "Actual Working Hours"];
        for (var column = 0; column < headers.Length; column++)
            sheet.Cell(1, column + 1).Value = headers[column];
        string[] workingIds = ["ext-001", "UNKNOWN-001"];
        for (var row = 0; row < workingIds.Length; row++)
        {
            sheet.Cell(row + 2, 1).Value = workingIds[row];
            sheet.Cell(row + 2, 2).Value = 20;
            sheet.Cell(row + 2, 3).Value = 0;
            sheet.Cell(row + 2, 4).Value = 0;
            sheet.Cell(row + 2, 5).Value = 8;
        }

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        var service = new RiderShiftService(db, new RiderWorkingIdHistoryService(db));
        if (compare)
        {
            var result = await service.CreateShiftComparisonsAsync(stream, new DateOnly(2026, 10, 4));
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value.Comparisons);
            Assert.Equal("UNKNOWN-001", Assert.Single(result.Value.Errors).WorkingId);
        }
        else
        {
            var result = await service.ImportShiftsFromExcelAsync(stream, new DateOnly(2026, 10, 4), 2);
            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.Value.TotalRecords);
            Assert.Equal(0, result.Value.SuccessCount);
            Assert.Equal("UNKNOWN-001", Assert.Single(result.Value.Errors).WorkingId);
        }

        Assert.Empty(await db.RiderShifts.ToListAsync());
        Assert.Empty(await db.TempRiderShiftComparisons.ToListAsync());
        Assert.Empty(await db.OutageShiftPerformances.ToListAsync());
    }

    [Fact]
    public async Task ExternalRider_CanBeManagedAndReceiveSeparatePerformanceUploads()
    {
        await using var db = CreateDbContext();
        var infoService = new OutRiderInfoService(db);
        var performanceService = new OutageShiftPerformanceService(db);
        var created = await infoService.CreateAsync(
            new CreateOutRiderInfoRequest("EXT-002", "External Rider", "0500000000"), "Test");
        Assert.True(created.IsSuccess);
        var duplicate = await infoService.CreateAsync(
            new CreateOutRiderInfoRequest("EXT-002", "Duplicate", "0500000001"), "Test");
        Assert.True(duplicate.IsFailure);

        var updated = await infoService.UpdateAsync(created.Value.Id,
            new UpdateOutRiderInfoRequest("EXT-002", "Updated Rider", "0500000002"));
        Assert.True(updated.IsSuccess);

        var imported = await performanceService.ImportAsync(new ImportOutageShiftPerformanceRequest(
            [new ImportOutageShiftPerformanceRow("EXT-002", 20, 1, 8)],
            new DateOnly(2026, 10, 4)), "Test");
        Assert.True(imported.IsSuccess);
        Assert.Equal(1, imported.Value.RecordsCreated);
        Assert.Empty(imported.Value.Warnings);

        var queried = await performanceService.GetAsync("EXT-002", null, null);
        Assert.True(queried.IsSuccess);
        var performance = Assert.Single(queried.Value);
        Assert.Equal(created.Value.Id, performance.OutRiderInfoId);
        Assert.Equal("Updated Rider", performance.Name);
        Assert.Equal(20, performance.AcceptedOrders);
        Assert.Equal(8f, performance.WorkingHours);
        Assert.True((await infoService.DeleteAsync(created.Value.Id)).IsFailure);
        Assert.True((await performanceService.DeleteAsync(performance.Id)).IsSuccess);
        Assert.True((await infoService.DeleteAsync(created.Value.Id)).IsSuccess);
    }

    private static ApplicationDbcontext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbcontext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
