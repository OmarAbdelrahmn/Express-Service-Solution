using Application.Contracts.Employees;
using Application.Service.Empolyee;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public sealed class VehicleDocumentSheetImportTests
{
    private static readonly string[] Headers = ["الرقم التسلسلي", "تاريخ انتهاء رخصة السير", "تاريخ انتهاء الفحص"];

    [Fact]
    public async Task ArabicSheet_ConvertsHijriMatchesSerialAndPreservesInsuranceAndOtherFields()
    {
        await using var db = CreateDb();
        db.Vehicles.Add(new Vehicle
        {
            VehicleNumber = "VIN-A", SerialNumber = 819465020, PlateNumberA = "ABC",
            Location = "Riyadh", InsuranceExpiryDate = new DateOnly(2028, 1, 1)
        });
        await db.SaveChangesAsync();
        using var stream = Sheet([819465020, "1448-08-03", "1449-03-28"]);

        var report = (await Service(db).ImportExpiryDatesAsync(stream)).Value;

        Assert.Equal(1, report.UpdatedCount);
        Assert.Equal("vehicle_list", report.WorksheetName);
        var row = Assert.Single(report.Rows);
        Assert.Equal(2, row.RowNumber);
        Assert.Equal("VIN-A", row.VehicleNumber);
        Assert.Equal(2, row.Changes.Count);
        Assert.All(row.Changes, c => Assert.Equal("Hijri", c.SourceCalendar));
        Assert.Equal("1448-08-03", row.Changes.Single(c => c.Field == "licenseExpiryDate").SourceValue);
        db.ChangeTracker.Clear();
        var saved = await db.Vehicles.SingleAsync();
        Assert.Equal(new DateOnly(2027, 1, 11), saved.LicenseExpiryDate);
        Assert.Equal(new DateOnly(2027, 8, 30), saved.InspectionExpiryDate);
        Assert.Equal(new DateOnly(2028, 1, 1), saved.InsuranceExpiryDate);
        Assert.Equal("Riyadh", saved.Location);
        Assert.Equal("ABC", saved.PlateNumberA);
    }

    [Fact]
    public async Task DryRun_DoesNotMutateTrackedEntities_AndReuploadIsUnchanged()
    {
        await using var db = CreateDb();
        var vehicle = new Vehicle { VehicleNumber = "A", SerialNumber = 123, LicenseExpiryDate = new DateOnly(2026, 1, 1) };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        var originalAuditCount = await db.SystemAuditEvents.CountAsync();
        using var stream = Sheet([123, "2027-01-11", "-"]);
        var service = Service(db);

        var preview = (await service.ImportExpiryDatesAsync(stream, dryRun: true)).Value;
        Assert.True(preview.DryRun);
        Assert.Equal(0, preview.UpdatedCount);
        Assert.Equal(1, preview.WouldUpdateCount);
        Assert.Equal(new DateOnly(2026, 1, 1), vehicle.LicenseExpiryDate);
        Assert.Equal(EntityState.Unchanged, db.Entry(vehicle).State);
        Assert.Equal(originalAuditCount, await db.SystemAuditEvents.CountAsync());

        stream.Position = 0;
        Assert.Equal(1, (await service.ImportExpiryDatesAsync(stream)).Value.UpdatedCount);
        stream.Position = 0;
        Assert.Equal(1, (await service.ImportExpiryDatesAsync(stream)).Value.UnchangedCount);
    }

    [Fact]
    public async Task BlankAndDashDates_PreserveRecordedValues()
    {
        await using var db = CreateDb();
        var date = new DateOnly(2027, 1, 1);
        db.Vehicles.AddRange(new Vehicle { VehicleNumber = "A", SerialNumber = 1, InspectionExpiryDate = date },
            new Vehicle { VehicleNumber = "B", SerialNumber = 2, LicenseExpiryDate = date, InspectionExpiryDate = date });
        await db.SaveChangesAsync();
        using var stream = Sheet([1, "2028-01-01", "-"], [2, "", "—"]);

        var report = (await Service(db).ImportExpiryDatesAsync(stream)).Value;
        Assert.Equal(1, report.UpdatedCount);
        Assert.Equal(1, report.SkippedCount);
        db.ChangeTracker.Clear();
        Assert.All(await db.Vehicles.ToListAsync(), v => Assert.Equal(date, v.InspectionExpiryDate));
        Assert.Equal(date, (await db.Vehicles.SingleAsync(v => v.VehicleNumber == "B")).LicenseExpiryDate);
    }

    [Fact]
    public async Task MixedRows_ReportUnknownInvalidAndAmbiguousWithoutCreatingOrPartiallyUpdating()
    {
        await using var db = CreateDb();
        db.Vehicles.AddRange(new Vehicle { VehicleNumber = "valid", SerialNumber = 1 },
            new Vehicle { VehicleNumber = "bad-date", SerialNumber = 2 },
            new Vehicle { VehicleNumber = "dup-1", SerialNumber = 3 },
            new Vehicle { VehicleNumber = "dup-2", SerialNumber = 3 });
        await db.SaveChangesAsync();
        using var stream = Sheet([1, "2027-01-01", "-"], [999, "2027-01-01", "-"],
            [2, "2027-01-01", "1448-13-01"], [3, "2027-01-01", "-"], ["bad", "2027-01-01", "-"]);

        var report = (await Service(db).ImportExpiryDatesAsync(stream)).Value;
        Assert.Equal(5, report.TotalRows);
        Assert.Equal(1, report.UpdatedCount);
        Assert.Equal(1, report.NotFoundCount);
        Assert.Equal(2, report.InvalidCount);
        Assert.Equal(1, report.AmbiguousCount);
        Assert.All(report.Rows.Where(r => r.Status != VehicleDocumentSheetRowStatus.Updated), r => Assert.NotEmpty(r.Errors));
        db.ChangeTracker.Clear();
        Assert.Equal(4, await db.Vehicles.CountAsync());
        Assert.Null((await db.Vehicles.SingleAsync(v => v.SerialNumber == 2)).LicenseExpiryDate);
        Assert.All(await db.Vehicles.Where(v => v.SerialNumber == 3).ToListAsync(), v => Assert.Null(v.LicenseExpiryDate));
    }

    [Fact]
    public async Task DuplicateWorksheetSerials_RejectEveryOccurrenceEvenWhenOneDateIsInvalid()
    {
        await using var db = CreateDb();
        db.Vehicles.Add(new Vehicle { VehicleNumber = "A", SerialNumber = 123 });
        await db.SaveChangesAsync();
        using var stream = Sheet(["00123", "2027-01-01", "-"], [123, "nonsense", "-"]);

        var report = (await Service(db).ImportExpiryDatesAsync(stream)).Value;
        Assert.Equal(2, report.InvalidCount);
        Assert.Equal(0, report.UpdatedCount);
        Assert.All(report.Rows, r => Assert.Contains(r.Errors, e => e.Contains("repeated")));
        Assert.Null((await db.Vehicles.SingleAsync()).LicenseExpiryDate);
    }

    [Fact]
    public async Task EnglishReorderedHeaders_ArabicDigits_DayFirstAndExcelDatesAreSupported()
    {
        await using var db = CreateDb();
        db.Vehicles.AddRange(new Vehicle { VehicleNumber = "A", SerialNumber = 123 },
            new Vehicle { VehicleNumber = "B", SerialNumber = 456 });
        await db.SaveChangesAsync();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("dates");
        sheet.Cell(3, 1).Value = "inspection_expiry_date";
        sheet.Cell(3, 2).Value = "serialnumber";
        sheet.Cell(3, 3).Value = "LICENSE EXPIRY DATE";
        sheet.Cell(4, 1).Value = "١٤٤٩-٠٣-٢٨";
        sheet.Cell(4, 2).Value = "١٢٣";
        sheet.Cell(4, 3).Value = "11/01/2027";
        sheet.Cell(5, 1).Value = new DateTime(2028, 2, 3);
        sheet.Cell(5, 2).Value = 456;
        sheet.Cell(5, 3).Value = new DateTime(2027, 1, 11).ToOADate();
        using var stream = Save(workbook);

        var report = (await Service(db).ImportExpiryDatesAsync(stream)).Value;
        Assert.Equal(2, report.UpdatedCount);
        Assert.All(await db.Vehicles.ToListAsync(), v => Assert.Equal(new DateOnly(2027, 1, 11), v.LicenseExpiryDate));
        Assert.Equal(new DateOnly(2028, 2, 3), (await db.Vehicles.SingleAsync(v => v.SerialNumber == 456)).InspectionExpiryDate);
    }

    [Fact]
    public async Task SingleDocumentColumn_PreservesOtherDocuments()
    {
        await using var db = CreateDb();
        var date = new DateOnly(2026, 11, 1);
        db.Vehicles.Add(new Vehicle { VehicleNumber = "A", SerialNumber = 1, LicenseExpiryDate = date, InsuranceExpiryDate = date });
        await db.SaveChangesAsync();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("one");
        sheet.Cell(1, 1).Value = "SerialNumber";
        sheet.Cell(1, 2).Value = "InspectionExpiryDate";
        sheet.Cell(2, 1).Value = 1;
        sheet.Cell(2, 2).Value = "2027-02-01";
        using var stream = Save(workbook);

        Assert.Equal(1, (await Service(db).ImportExpiryDatesAsync(stream)).Value.UpdatedCount);
        var saved = await db.Vehicles.SingleAsync();
        Assert.Equal(date, saved.LicenseExpiryDate);
        Assert.Equal(date, saved.InsuranceExpiryDate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NumericExcelDates_RespectWorkbookDateSystem(bool use1904)
    {
        await using var db = CreateDb();
        db.Vehicles.Add(new Vehicle { VehicleNumber = "A", SerialNumber = 1 });
        await db.SaveChangesAsync();
        using var workbook = new XLWorkbook { Use1904DateSystem = use1904 };
        var expected = new DateTime(2027, 1, 11);
        Populate(workbook.AddWorksheet("dates"), [[1, expected.ToOADate() - (use1904 ? 1462 : 0), "-"]]);
        using var stream = Save(workbook);

        var report = (await Service(db).ImportExpiryDatesAsync(stream)).Value;
        Assert.Equal(1, report.UpdatedCount);
        Assert.Equal(DateOnly.FromDateTime(expected), (await db.Vehicles.SingleAsync()).LicenseExpiryDate);
    }

    [Fact]
    public async Task MultipleMatchingSheets_RequireSelection_AndInvalidSheetDoesNotWrite()
    {
        await using var db = CreateDb();
        db.Vehicles.Add(new Vehicle { VehicleNumber = "A", SerialNumber = 1 });
        await db.SaveChangesAsync();
        using var workbook = new XLWorkbook();
        Populate(workbook.AddWorksheet("first"), [[1, "2027-01-01", "-"]]);
        Populate(workbook.AddWorksheet("second"), [[1, "2028-01-01", "-"]]);
        using var stream = Save(workbook);
        var service = Service(db);

        Assert.True((await service.ImportExpiryDatesAsync(stream)).IsFailure);
        Assert.Null((await db.Vehicles.SingleAsync()).LicenseExpiryDate);
        stream.Position = 0;
        Assert.True((await service.ImportExpiryDatesAsync(stream, worksheetName: "absent")).IsFailure);
        stream.Position = 0;
        Assert.Equal(1, (await service.ImportExpiryDatesAsync(stream, worksheetName: "second")).Value.UpdatedCount);
        Assert.Equal(new DateOnly(2028, 1, 1), (await db.Vehicles.SingleAsync()).LicenseExpiryDate);
    }

    [Fact]
    public async Task InvalidHeadersEmptyAndMalformedFiles_Return400()
    {
        await using var db = CreateDb();
        var service = Service(db);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("invalid");
        sheet.Cell(1, 1).Value = "Unrelated header";
        using var badHeaders = Save(workbook);
        using var malformed = new MemoryStream([1, 2, 3, 4]);
        using var empty = new MemoryStream();
        foreach (var stream in new[] { badHeaders, malformed, empty })
        {
            var result = await service.ImportExpiryDatesAsync(stream);
            Assert.True(result.IsFailure);
            Assert.Equal(400, result.Error.StatuesCode);
        }
        Assert.Empty(await db.Vehicles.ToListAsync());
    }

    [Fact]
    public async Task FormulaCellsAndFractionalSerials_AreRejected()
    {
        await using var db = CreateDb();
        db.Vehicles.Add(new Vehicle { VehicleNumber = "A", SerialNumber = 1 });
        await db.SaveChangesAsync();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("formulas");
        Populate(sheet, [[1, "2027-01-01", "-"], [1.5, "2027-01-01", "-"], ["2147483648", "2027-01-01", "-"]]);
        sheet.Cell(2, 2).FormulaA1 = "DATE(2027,1,1)";
        using var stream = Save(workbook);

        var report = (await Service(db).ImportExpiryDatesAsync(stream)).Value;
        Assert.Equal(3, report.InvalidCount);
        Assert.Equal(0, report.UpdatedCount);
    }

    [Fact]
    public async Task RowLimitAndDuplicateHeaders_AreRejected()
    {
        await using var db = CreateDb();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("large");
        Populate(sheet, []);
        sheet.Cell(5002, 1).Value = 1;
        using var oversized = Save(workbook);
        Assert.True((await Service(db).ImportExpiryDatesAsync(oversized)).IsFailure);
        sheet.Cell(5002, 1).Clear();
        sheet.Cell(1, 4).Value = "LicenseExpiryDate";
        sheet.Cell(2, 1).Value = 1;
        using var duplicateHeaders = Save(workbook);
        Assert.True((await Service(db).ImportExpiryDatesAsync(duplicateHeaders)).IsFailure);
    }

    [Fact]
    public async Task CancelledImport_DoesNotWrite()
    {
        await using var db = CreateDb();
        using var stream = Sheet([1, "2027-01-01", "-"]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(db).ImportExpiryDatesAsync(stream, ct: cancellation.Token));
        Assert.Empty(await db.Vehicles.ToListAsync());
    }

    private static ApplicationDbcontext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbcontext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
    private static VehicleDocumentService Service(ApplicationDbcontext db) => new(db, TimeProvider.System);

    private static MemoryStream Sheet(params object[][] rows)
    {
        using var workbook = new XLWorkbook();
        Populate(workbook.AddWorksheet("vehicle_list"), rows);
        return Save(workbook);
    }

    private static void Populate(IXLWorksheet sheet, object[][] rows)
    {
        for (var column = 0; column < Headers.Length; column++) sheet.Cell(1, column + 1).Value = Headers[column];
        for (var row = 0; row < rows.Length; row++)
            for (var column = 0; column < rows[row].Length; column++)
                sheet.Cell(row + 2, column + 1).Value = XLCellValue.FromObject(rows[row][column]);
    }

    private static MemoryStream Save(XLWorkbook workbook)
    {
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
