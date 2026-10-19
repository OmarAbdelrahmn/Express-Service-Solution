using Application.Service.Import;
using Application.Service.Riders;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Accounting.Tests;

public sealed class VehicleDocumentImportTests
{
    [Fact]
    public async Task Import_MapsInsuranceAndInspectionWithoutInventingRegistrationDate()
    {
        await using var db = CreateDb();
        var service = new ImportService(db, new RiderSub(db, new RiderWorkingIdHistoryService(db)));
        using var stream = Workbook(("Insurance Expiry Date", "2026-11-01"), ("Inspection Expiry Date", "2026-12-01"));
        var result = await service.ImportVehiclesAsync(new FormFile(stream, 0, stream.Length, "file", "vehicles.xlsx"), "test");

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.FailedRecords);
        var vehicle = await db.Vehicles.SingleAsync();
        Assert.Null(vehicle.LicenseExpiryDate);
        Assert.Equal(new DateOnly(2026, 11, 1), vehicle.InsuranceExpiryDate);
        Assert.Equal(new DateOnly(2026, 12, 1), vehicle.InspectionExpiryDate);
    }

    [Fact]
    public async Task Import_BlankDatesPreserveExistingDocuments()
    {
        await using var db = CreateDb();
        var expiry = new DateOnly(2027, 1, 1);
        db.Vehicles.Add(new Vehicle
        {
            VehicleNumber = "A", SerialNumber = 123, PlateNumberA = "ABC", PlateNumberE = "XYZ",
            LicenseExpiryDate = expiry, InsuranceExpiryDate = expiry, InspectionExpiryDate = expiry
        });
        await db.SaveChangesAsync();
        var service = new ImportService(db, new RiderSub(db, new RiderWorkingIdHistoryService(db)));
        using var stream = Workbook(("LicenseExpiryDate", ""), ("InsuranceExpiryDate", ""), ("InspectionExpiryDate", ""));
        var result = await service.ImportVehiclesAsync(new FormFile(stream, 0, stream.Length, "file", "vehicles.xlsx"), "test");

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.FailedRecords);
        db.ChangeTracker.Clear();
        var vehicle = await db.Vehicles.SingleAsync();
        Assert.Equal(expiry, vehicle.LicenseExpiryDate);
        Assert.Equal(expiry, vehicle.InsuranceExpiryDate);
        Assert.Equal(expiry, vehicle.InspectionExpiryDate);
    }

    [Theory]
    [InlineData("Expiry Date")]
    [InlineData("تاريخ انتهاء الاستمارة")]
    public async Task Import_RegistrationHeadersStillWork(string header)
    {
        await using var db = CreateDb();
        var service = new ImportService(db, new RiderSub(db, new RiderWorkingIdHistoryService(db)));
        using var stream = Workbook((header, "2027-01-01"));
        var result = await service.ImportVehiclesAsync(new FormFile(stream, 0, stream.Length, "file", "vehicles.xlsx"), "test");

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.FailedRecords);
        Assert.Equal(new DateOnly(2027, 1, 1), (await db.Vehicles.SingleAsync()).LicenseExpiryDate);
    }

    private static ApplicationDbcontext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbcontext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static MemoryStream Workbook(params (string Header, string Value)[] dates)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Vehicles");
        var columns = new[] { ("VehicleNumber", "A"), ("SerialNumber", "123"), ("PlateNumberA", "ABC"), ("PlateNumberE", "XYZ") }.Concat(dates).ToArray();
        for (var column = 0; column < columns.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = columns[column].Item1;
            sheet.Cell(2, column + 1).Value = columns[column].Item2;
        }
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
