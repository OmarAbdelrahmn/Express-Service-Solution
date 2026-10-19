using Application.Service.Import;
using Application.Service.Petrol;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Domain.Entities.Petrol;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Accounting.Tests;

public class PetrolServiceTests
{
    [Fact]
    public async Task FailedEmployeePetrolAssignment_DiscardsMinimalProfileAndUnassignedCompany()
    {
        await using var db = new SaveCheckingDb(new DbContextOptionsBuilder<ApplicationDbcontext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Seed(db);
        await db.SaveChangesAsync();
        var cost = await AddCost(db, "V1", 11);
        var service = new PetrolService(db);
        Assert.True((await service.AttributeSingleByIdAsync(cost.Id)).IsSuccess);
        db.FailNextSave = true;
        Assert.True((await service.AssignRiderToUnattributedAsync("AA111", cost.Date, 101, "Admin")).IsFailure);
        Assert.False(db.ChangeTracker.HasChanges());
        await db.SaveChangesAsync();
        Assert.Empty(await db.RiderDetails.ToListAsync());
        Assert.Empty(await db.Companies.ToListAsync());
        Assert.Null((await db.RiderPetrolCosts.SingleAsync()).RiderIqamaNo);
        Assert.False((await db.VehiclePetrolCosts.SingleAsync()).IsAttributed);
    }

    [Fact]
    public async Task AssignPetrolToEmployee_CreatesMinimalProfileOnce_WithoutChangingEmploymentClassification()
    {
        await using var db = CreateDb();
        Seed(db);
        db.Employees.Local.Single(e => e.IqamaNo == 101).IsEmployee = true;
        await db.SaveChangesAsync();
        var first = await AddCost(db, "V1", 11);
        var second = await AddCost(db, "V1", 12);
        var service = new PetrolService(db);
        Assert.True((await service.AttributePendingAsync()).IsSuccess);
        Assert.True((await service.AssignRiderToUnattributedAsync("AA111", first.Date, 101, "Admin")).IsSuccess);
        Assert.True((await service.AssignRiderToUnattributedAsync("AA111", second.Date, 101, "Admin")).IsSuccess);
        var profile = await db.RiderDetails.Include(r => r.Company).SingleAsync();
        Assert.Equal(101, profile.EmployeeIqamaNo);
        Assert.Null(profile.WorkingId);
        Assert.Null(profile.LicenseNumber);
        Assert.Null(profile.TshirtSize);
        Assert.Equal("Unassigned", profile.Company.Name);
        Assert.True((await db.Employees.SingleAsync(e => e.IqamaNo == 101)).IsEmployee);
        Assert.All(await db.RiderPetrolCosts.ToListAsync(), r => Assert.Equal(101, r.RiderIqamaNo));
        Assert.Equal(1, await db.Companies.CountAsync());
        await AssertConserved(db);
    }

    [Fact]
    public async Task UnknownPlates_AreExcludedFromNotAttached_WithoutLosingCostsOrSummaryIdentity()
    {
        await using var db = CreateDb();
        Seed(db);
        await db.SaveChangesAsync();
        var date = new DateOnly(2026, 9, 10);
        var service = new PetrolService(db);
        Assert.True((await service.ProcessUploadAsync(PetrolFile(("UNKNOWN123", "10.25"), ("MISSING456", "5.50")), date, "Admin")).IsSuccess);
        Assert.Empty((await service.GetUnattributedCostsAsync(2026, 9)).Value);
        var daily = (await service.GetDailyReportAsync(date)).Value;
        Assert.Equal(15.75m, daily.TotalCost);
        Assert.Equal(0, daily.TotalUnattributedRows);
        Assert.Equal(2, daily.TotalUnknownVehicleRows);
        Assert.Equal(15.75m, daily.UnknownVehicleCost);
        Assert.Equal(2, daily.Vehicles.Count);
        Assert.All(daily.Vehicles, c => Assert.True(c.HasResolutionError));
        var summary = (await service.GetAllVehiclesSummaryAsync(2026, 9)).Value;
        Assert.Equal(2, summary.Count);
        Assert.Equal(new[] { "MISSING456", "UNKNOWN123" }, summary.Select(r => r.PlateNumberE).Order());
        await AssertConserved(db);
    }

    [Theory]
    [InlineData("31/10/2026", true)]
    [InlineData("invalid-date", false)]
    public async Task AssignmentDates_AcceptDocumentedDayFirstFormatAndRejectInvalidValues(string start, bool valid)
    {
        await using var db = CreateImportDb();
        Seed(db);
        db.RiderDetails.Add(new RiderDetails { EmployeeIqamaNo = 101, CompanyId = 1, VehicleNumber = "V1" });
        db.RiderVehicleStatus.Add(Take("V1", 101, At(1), active: true));
        await db.SaveChangesAsync();
        var stream = new MemoryStream();
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("Assignments");
            sheet.Cell(1, 1).Value = "IqamaNumber"; sheet.Cell(1, 2).Value = "VehicleNumber";
            sheet.Cell(1, 3).Value = "PermissionStartDate"; sheet.Cell(1, 4).Value = "PermissionEndDate";
            sheet.Cell(2, 1).Value = 101; sheet.Cell(2, 2).Value = "V1";
            sheet.Cell(2, 3).Value = start; sheet.Cell(2, 4).Value = "31/12/2026";
            book.SaveAs(stream);
        }
        stream.Position = 0;
        var result = await new ImportService(db, null!).ImportVehicleAssignmentsAsync(
            new FormFile(stream, 0, stream.Length, "file", "assignments.xlsx"), "Admin");
        Assert.Equal(valid, result.IsSuccess);
        var take = await db.RiderVehicleStatus.SingleAsync();
        Assert.Equal(At(1), take.Timestamp);
        Assert.Equal(valid ? new DateTime(2026, 10, 31) : (DateTime?)null, take.PermissionStartDate);
    }

    [Fact]
    public async Task RepeatedAutomaticAttribution_KeepsAllocationIdentityAndCreatedTime()
    {
        await using var db = CreateDb();
        Seed(db);
        db.RiderVehicleStatus.Add(Take("V1", 101, At(1), active: true));
        await db.SaveChangesAsync();
        var cost = await AddCost(db, "V1", 10);
        var service = new PetrolService(db);
        Assert.True((await service.AttributeSingleByIdAsync(cost.Id)).IsSuccess);
        var row = await db.RiderPetrolCosts.AsNoTracking().SingleAsync();
        db.ChangeTracker.Clear();
        Assert.True((await service.AttributeSingleByIdAsync(cost.Id)).IsSuccess);
        var again = await db.RiderPetrolCosts.AsNoTracking().SingleAsync();
        Assert.Equal(row.Id, again.Id);
        Assert.Equal(row.CreatedAt, again.CreatedAt);
    }

    [Fact]
    public async Task UnassignedReport_ShowsReturnedReasonAndPreviouslyHiddenPlate()
    {
        await using var db = CreateDb();
        Seed(db);
        db.Vehicles.Local.Single(v => v.VehicleNumber == "V1").PlateNumberE = "4762GUR";
        db.RiderVehicleStatus.AddRange(Take("V1", 101, At(1)), Event("V1", 101, VehicleStatusType.Returned, At(5)));
        await db.SaveChangesAsync();
        var cost = await AddCost(db, "V1", 10);
        var service = new PetrolService(db);
        Assert.True((await service.AttributeSingleByIdAsync(cost.Id)).IsSuccess);
        var report = await service.GetUnattributedCostsAsync(2026, 9);
        var row = Assert.Single(report.Value);
        Assert.Equal("ReturnedBeforeReportDate", row.ReasonCode);
        Assert.Equal(cost.Id, row.VehiclePetrolCostId);
        Assert.Equal(VehicleStatusType.Returned, row.StatusType);
        Assert.Equal(At(5), row.StatusRecordedAt);
        Assert.Equal("4762GUR", row.PlateNumberE);
    }

    [Fact]
    public async Task OlderReturn_DoesNotCloseLaterTakeWithBackdatedPermission()
    {
        await using var db = CreateDb();
        Seed(db);
        db.RiderVehicleStatus.AddRange(Take("V1", 101, At(1), At(1), At(10)),
            Event("V1", 101, VehicleStatusType.Returned, At(10, 12)),
            Take("V1", 101, At(10, 13), At(1), At(30), true));
        await db.SaveChangesAsync();
        await AddCost(db, "V1", 5);
        await AddCost(db, "V1", 11);
        Assert.True((await new PetrolService(db).AttributePendingAsync()).IsSuccess);
        Assert.All(await db.RiderPetrolCosts.ToListAsync(), r => Assert.Equal(101, r.RiderIqamaNo));
        await AssertConserved(db);
    }

    [Fact]
    public async Task BackdatedRetake_DoesNotOverlapAnotherRidersCompletedPossession()
    {
        await using var db = CreateDb();
        Seed(db);
        db.RiderVehicleStatus.AddRange(Take("V1", 101, At(1)),
            Event("V1", 101, VehicleStatusType.Returned, At(10)),
            Take("V1", 102, At(12), At(5), At(30), true));
        await db.SaveChangesAsync();
        await AddCost(db, "V1", 6);
        await AddCost(db, "V1", 13);
        Assert.True((await new PetrolService(db).AttributePendingAsync()).IsSuccess);
        Assert.Equal(101, (await db.RiderPetrolCosts.SingleAsync(r => r.Date.Day == 6)).RiderIqamaNo);
        Assert.Equal(102, (await db.RiderPetrolCosts.SingleAsync(r => r.Date.Day == 13)).RiderIqamaNo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssignmentSheet_UsesChassisKey_AndPreservesUnchangedTake(bool fullSnapshot)
    {
        await using var db = CreateImportDb();
        Seed(db);
        db.RiderDetails.AddRange(new RiderDetails { EmployeeIqamaNo = 101, CompanyId = 1, VehicleNumber = "V1" },
            new RiderDetails { EmployeeIqamaNo = 102, CompanyId = 1, VehicleNumber = "V2" });
        db.RiderVehicleStatus.AddRange(Take("V1", 101, At(1), active: true), Take("V2", 102, At(1), active: true));
        await db.SaveChangesAsync();
        var result = await new ImportService(db, null!).ImportVehicleAssignmentsAsync(AssignmentFile((101, " v 1 ")), "Admin", fullSnapshot);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Description : null);
        Assert.Equal("V1", (await db.RiderDetails.SingleAsync(r => r.EmployeeIqamaNo == 101)).VehicleNumber);
        Assert.Equal(At(1), (await db.RiderVehicleStatus.SingleAsync(s => s.VehicleNumber == "V1")).Timestamp);
        Assert.Equal(fullSnapshot ? null : "V2", (await db.RiderDetails.SingleAsync(r => r.EmployeeIqamaNo == 102)).VehicleNumber);
        Assert.Equal(fullSnapshot ? 1 : 0, await db.RiderVehicleStatus.CountAsync(s => s.StatusType == VehicleStatusType.Returned));
    }

    [Fact]
    public async Task InvalidAssignmentSnapshot_DoesNotReturnOrChangeAnyVehicle()
    {
        await using var db = CreateImportDb();
        Seed(db);
        db.RiderDetails.Add(new RiderDetails { EmployeeIqamaNo = 101, CompanyId = 1, VehicleNumber = "V1" });
        db.RiderVehicleStatus.Add(Take("V1", 101, At(1), active: true));
        await db.SaveChangesAsync();
        var result = await new ImportService(db, null!).ImportVehicleAssignmentsAsync(
            AssignmentFile((101, "V2"), (102, "UNKNOWN")), "Admin", true);
        Assert.True(result.IsFailure);
        Assert.Equal("V1", (await db.RiderDetails.SingleAsync()).VehicleNumber);
        Assert.True((await db.RiderVehicleStatus.SingleAsync()).IsActive);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task AssignmentSheet_SwapsVehiclesWithoutLeavingFormerRidersAssigned()
    {
        await using var db = CreateImportDb();
        Seed(db);
        db.RiderDetails.AddRange(new RiderDetails { EmployeeIqamaNo = 101, CompanyId = 1, VehicleNumber = "V1" },
            new RiderDetails { EmployeeIqamaNo = 102, CompanyId = 1, VehicleNumber = "V2" });
        db.RiderVehicleStatus.AddRange(Take("V1", 101, At(1), active: true), Take("V2", 102, At(1), active: true));
        await db.SaveChangesAsync();
        Assert.True((await new ImportService(db, null!).ImportVehicleAssignmentsAsync(
            AssignmentFile((101, "V2"), (102, "V1")), "Admin")).IsSuccess);
        Assert.Equal("V2", (await db.RiderDetails.SingleAsync(r => r.EmployeeIqamaNo == 101)).VehicleNumber);
        Assert.Equal("V1", (await db.RiderDetails.SingleAsync(r => r.EmployeeIqamaNo == 102)).VehicleNumber);
        Assert.Equal(2, await db.RiderVehicleStatus.CountAsync(s => s.IsActive));
        Assert.Equal(2, await db.RiderVehicleStatus.CountAsync(s => s.StatusType == VehicleStatusType.Returned));
    }

    private static IFormFile AssignmentFile(params (long Iqama, string Vehicle)[] rows)
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Assignments");
            sheet.Cell(1, 1).Value = "IqamaNumber";
            sheet.Cell(1, 2).Value = "VehicleNumber";
            for (var i = 0; i < rows.Length; i++)
            { sheet.Cell(i + 2, 1).Value = rows[i].Iqama; sheet.Cell(i + 2, 2).Value = rows[i].Vehicle; }
            workbook.SaveAs(stream);
        }
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "assignments.xlsx");
    }

    [Fact]
    public async Task ImportedReplacement_ClosesStalePermission_AndAllowsTwoVehiclesOnlyOnSwitchDay()
    {
        await using var db = CreateDb();
        Seed(db);
        db.RiderVehicleStatus.AddRange(
            Take("V1", 101, At(1), At(1), At(30)),
            Event("V1", 101, VehicleStatusType.Returned, At(10, 12)),
            Take("V2", 101, At(10, 12), active: true));
        await db.SaveChangesAsync();
        foreach (var day in new[] { 10, 11, 12, 13 })
            foreach (var vehicle in new[] { "V1", "V2" }) await AddCost(db, vehicle, day);

        Assert.True((await new PetrolService(db).AttributePendingAsync()).IsSuccess);
        var rows = await db.RiderPetrolCosts.Where(r => r.RiderIqamaNo == 101).ToListAsync();
        Assert.Equal(new[] { "V1", "V2" }, rows.Where(r => r.Date.Day == 10).Select(r => r.VehicleNumber).OrderBy(v => v));
        Assert.All(rows.Where(r => r.Date.Day > 10), r => Assert.Equal("V2", r.VehicleNumber));
        Assert.Equal(5, rows.Count);
        await AssertConserved(db);
    }

    [Fact]
    public async Task TakingAnotherVehicle_ClosesOldTakeEvenWithoutReturnEvent()
    {
        await using var db = CreateDb();
        Seed(db);
        db.RiderVehicleStatus.AddRange(Take("V1", 101, At(1), At(1), At(30)), Take("V2", 101, At(10), active: true));
        await db.SaveChangesAsync();
        await AddCost(db, "V1", 11);
        await AddCost(db, "V2", 11);
        Assert.True((await new PetrolService(db).AttributePendingAsync()).IsSuccess);
        Assert.Equal("V2", Assert.Single(await db.RiderPetrolCosts.Where(r => r.RiderIqamaNo == 101).ToListAsync()).VehicleNumber);
        await AssertConserved(db);
    }

    [Theory]
    [InlineData(VehicleStatusType.Returned)]
    [InlineData(VehicleStatusType.Problem)]
    [InlineData(VehicleStatusType.Stolen)]
    [InlineData(VehicleStatusType.BreakUp)]
    [InlineData(VehicleStatusType.OutOfService)]
    public async Task ClosingStatus_EndsPossessionDespiteStalePermission(VehicleStatusType type)
    {
        await using var db = CreateDb();
        Seed(db);
        db.RiderVehicleStatus.AddRange(Take("V1", 101, At(1), At(1), At(30)), Event("V1", null, type, At(10, 12)));
        await db.SaveChangesAsync();
        await AddCost(db, "V1", 9);
        await AddCost(db, "V1", 10);
        await AddCost(db, "V1", 11);
        Assert.True((await new PetrolService(db).AttributePendingAsync()).IsSuccess);
        Assert.Equal(new[] { 9, 10 }, await db.RiderPetrolCosts.Where(r => r.RiderIqamaNo == 101).OrderBy(r => r.Date).Select(r => r.Date.Day).ToArrayAsync());
        Assert.Null(Assert.Single(await db.RiderPetrolCosts.Where(r => r.Date.Day == 11).ToListAsync()).RiderIqamaNo);
        await AssertConserved(db);
    }

    [Fact]
    public async Task ForcedReassignment_DoesNotRetainFormerRiders_InPermissionOrTimelinePaths()
    {
        await using var db = CreateDb();
        Seed(db);
        db.RiderVehicleStatus.AddRange(Take("V1", 101, At(1)), Take("V1", 102, At(10, 12), active: true));
        await db.SaveChangesAsync();
        await AddCost(db, "V1", 10);
        await AddCost(db, "V1", 11);
        Assert.True((await new PetrolService(db).AttributePendingAsync()).IsSuccess);
        var day10 = await db.RiderPetrolCosts.Where(r => r.Date.Day == 10).OrderBy(r => r.RiderIqamaNo).ToListAsync();
        Assert.Equal(new long?[] { 101, 102 }, day10.Select(r => r.RiderIqamaNo));
        Assert.All(day10, r => Assert.Equal(50m, r.Cost));
        Assert.Equal(102, Assert.Single(await db.RiderPetrolCosts.Where(r => r.Date.Day == 11).ToListAsync()).RiderIqamaNo);
        await AssertConserved(db);
    }

    [Fact]
    public async Task MixedPermissionAndTimeline_SplitsAllActualSameDayUsage()
    {
        await using var db = CreateDb();
        Seed(db);
        db.RiderVehicleStatus.AddRange(Take("V1", 101, At(1)), Event("V1", 101, VehicleStatusType.Returned, At(10, 6)),
            Take("V1", 102, At(10, 6), At(10, 6), At(30), true));
        await db.SaveChangesAsync();
        var cost = await AddCost(db, "V1", 10);
        Assert.True((await new PetrolService(db).AttributeSingleByIdAsync(cost.Id)).IsSuccess);
        var rows = await db.RiderPetrolCosts.OrderBy(r => r.RiderIqamaNo).ToListAsync();
        Assert.Equal(new[] { 25m, 75m }, rows.Select(r => r.Cost));
        Assert.Equal(PetrolAttributionSource.VehicleStatusTimeline, rows[0].AttributionSource);
        Assert.Equal(PetrolAttributionSource.Permission, rows[1].AttributionSource);
        await AssertConserved(db);
    }

    [Fact]
    public async Task RepeatedSameDayTakes_AggregateRiderDuration_AndLateReturnDoesNotCloseNewRider()
    {
        await using var db = CreateDb();
        Seed(db);
        db.RiderVehicleStatus.AddRange(Take("V1", 101, At(1)), Take("V1", 102, At(10, 6)),
            Event("V1", 101, VehicleStatusType.Returned, At(10, 7)), Take("V1", 101, At(10, 18), active: true));
        await db.SaveChangesAsync();
        var cost = await AddCost(db, "V1", 10);
        Assert.True((await new PetrolService(db).AttributeSingleByIdAsync(cost.Id)).IsSuccess);
        var rows = await db.RiderPetrolCosts.OrderBy(r => r.RiderIqamaNo).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(50m, r.Cost));
        await AssertConserved(db);
    }

    [Fact]
    public async Task ActiveAssignment_ContinuesAfterPermitExpiry_AndMidnightReturnDoesNotChargeFollowingDay()
    {
        await using var db = CreateDb();
        Seed(db);
        db.RiderVehicleStatus.AddRange(Take("V1", 101, At(1), At(1), At(5), true), Event("V1", 101, VehicleStatusType.Returned, At(12)));
        await db.SaveChangesAsync();
        await AddCost(db, "V1", 11);
        await AddCost(db, "V1", 12);
        Assert.True((await new PetrolService(db).AttributePendingAsync()).IsSuccess);
        Assert.Equal(101, Assert.Single(await db.RiderPetrolCosts.Where(r => r.Date.Day == 11).ToListAsync()).RiderIqamaNo);
        Assert.Null(Assert.Single(await db.RiderPetrolCosts.Where(r => r.Date.Day == 12).ToListAsync()).RiderIqamaNo);
    }

    [Fact]
    public async Task BackdatedImport_CoversHistoricalDaysWithoutUsingCurrentAssignmentForEarlierDates()
    {
        await using var db = CreateDb();
        Seed(db);
        db.RiderVehicleStatus.Add(Take("V1", 101, At(20), At(5), At(30), true));
        await db.SaveChangesAsync();
        await AddCost(db, "V1", 4);
        await AddCost(db, "V1", 6);
        Assert.True((await new PetrolService(db).AttributePendingAsync()).IsSuccess);
        Assert.Null(Assert.Single(await db.RiderPetrolCosts.Where(r => r.Date.Day == 4).ToListAsync()).RiderIqamaNo);
        Assert.Equal(101, Assert.Single(await db.RiderPetrolCosts.Where(r => r.Date.Day == 6).ToListAsync()).RiderIqamaNo);
    }

    [Fact]
    public async Task RepeatedAttribution_RepairsDuplicateRowsAndStaleAssignmentsWithoutChangingRawCost()
    {
        await using var db = CreateDb();
        Seed(db);
        var status = Take("V1", 101, At(1), active: true);
        db.RiderVehicleStatus.Add(status);
        await db.SaveChangesAsync();
        var cost = await AddCost(db, "V1", 11);
        var service = new PetrolService(db);
        Assert.True((await service.AttributeSingleByIdAsync(cost.Id)).IsSuccess);
        db.RiderPetrolCosts.Add(new RiderPetrolCost { VehiclePetrolCostId = cost.Id, VehicleNumber = "V1", Date = cost.Date, RiderIqamaNo = 101, Cost = 100 });
        status.EmployeeIqamaNo = 102;
        await db.SaveChangesAsync();
        Assert.True((await service.AttributePendingAsync()).IsSuccess);
        Assert.True((await service.AttributeSingleByIdAsync(cost.Id)).IsSuccess);
        Assert.True((await service.AttributeSingleByIdAsync(cost.Id)).IsSuccess);
        Assert.Equal(102, Assert.Single(await db.RiderPetrolCosts.ToListAsync()).RiderIqamaNo);
        Assert.Equal(100m, (await service.GetRiderMonthlyReportAsync(102, 2026, 9)).Value.TotalCost);
        Assert.Equal(100m, (await service.GetVehicleMonthlyReportAsync("V1", 2026, 9)).Value.TotalCost);
        await AssertConserved(db);
    }

    [Fact]
    public async Task ManualOverride_SurvivesRebuild_WhileAutomaticDuplicateIsRemoved()
    {
        await using var db = CreateDb();
        Seed(db);
        var cost = await AddCost(db, "V1", 11);
        var service = new PetrolService(db);
        Assert.True((await service.AttributeSingleByIdAsync(cost.Id)).IsSuccess);
        Assert.True((await service.AssignRiderToUnattributedAsync("AA111", cost.Date, 101, "Admin")).IsSuccess);
        var manual = await db.RiderPetrolCosts.SingleAsync();
        var notes = manual.Notes;
        db.RiderVehicleStatus.Add(Take("V1", 102, At(1), active: true));
        db.RiderPetrolCosts.Add(new RiderPetrolCost { VehiclePetrolCostId = cost.Id, VehicleNumber = "V1", Date = cost.Date, RiderIqamaNo = 102, Cost = 100 });
        await db.SaveChangesAsync();
        Assert.True((await service.AttributePendingAsync()).IsSuccess);
        Assert.True((await service.AttributeSingleByIdAsync(cost.Id)).IsSuccess);
        var row = await db.RiderPetrolCosts.SingleAsync();
        Assert.Equal(manual.Id, row.Id);
        Assert.Equal(101, row.RiderIqamaNo);
        Assert.Equal(PetrolAttributionSource.ManualOverride, row.AttributionSource);
        Assert.Equal(notes, row.Notes);
        await AssertConserved(db);
    }

    [Theory]
    [InlineData("bad-number")]
    [InlineData("-1")]
    [InlineData("1.001")]
    [InlineData("")]
    public async Task InvalidExcelCost_RejectsEntireUploadAndIdentifiesRow(string badCost)
    {
        await using var db = CreateDb();
        Seed(db);
        await db.SaveChangesAsync();
        var result = await new PetrolService(db).ProcessUploadAsync(PetrolFile(("AA111", "100"), ("BB222", badCost)), new DateOnly(2026, 9, 11), "Admin");
        Assert.True(result.IsFailure);
        Assert.Equal("InvalidFile", result.Error.Code);
        Assert.Contains("row 3", result.Error.Description);
        Assert.Empty(await db.VehiclePetrolCosts.ToListAsync());
        Assert.Empty(await db.RiderPetrolCosts.ToListAsync());
    }

    [Fact]
    public async Task SupplementalUpload_AddsMissingVehicle_WithoutDuplicatingPreviouslyUploadedCost()
    {
        await using var db = CreateDb();
        Seed(db);
        db.RiderVehicleStatus.AddRange(Take("V1", 101, At(1), active: true), Take("V2", 102, At(1), active: true));
        await db.SaveChangesAsync();
        var service = new PetrolService(db);
        var date = new DateOnly(2026, 9, 11);
        Assert.True((await service.ProcessUploadAsync(PetrolFile(("AA111", "100")), date, "Admin")).IsSuccess);
        var added = await service.ProcessUploadAsync(PetrolFile(("AA111", "100"), ("BB222", "200")), date, "Admin");
        Assert.True(added.IsSuccess);
        Assert.Single(added.Value.Rows);
        Assert.Equal(1, added.Value.TotalRows);
        Assert.Equal(2, await db.VehiclePetrolCosts.CountAsync());
        var duplicate = await service.ProcessUploadAsync(PetrolFile(("AA111", "100")), date, "Admin");
        Assert.Equal("DuplicateUpload", duplicate.Error.Code);
        var conflicting = await service.ProcessUploadAsync(PetrolFile(("AA111", "101")), date, "Admin");
        Assert.Equal("DuplicateUpload", conflicting.Error.Code);
        Assert.Equal(300m, await db.VehiclePetrolCosts.SumAsync(c => c.Cost));
        await AssertConserved(db);
    }

    [Fact]
    public async Task DuplicateNormalizedPlates_RejectUploadInsteadOfDoubleCharging()
    {
        await using var db = CreateDb();
        Seed(db);
        await db.SaveChangesAsync();
        var result = await new PetrolService(db).ProcessUploadAsync(PetrolFile(("AA111", "100"), ("111 aa", "200")), new DateOnly(2026, 9, 11), "Admin");
        Assert.True(result.IsFailure);
        Assert.Equal("InvalidFile", result.Error.Code);
        Assert.Empty(await db.VehiclePetrolCosts.ToListAsync());
    }

    [Fact]
    public async Task UnresolvedPlate_RemainsInDailyReportAcrossRetries_AndResolvesWhenVehicleIsAdded()
    {
        await using var db = CreateDb();
        Seed(db);
        await db.SaveChangesAsync();
        var service = new PetrolService(db);
        var upload = await service.ProcessUploadAsync(PetrolFile(("UNKNOWN999", "100"), ("AA111", "200")), new DateOnly(2026, 9, 11), "Admin");
        Assert.True(upload.IsSuccess);
        Assert.Equal(1, upload.Value.UnresolvedVehicles);
        Assert.DoesNotContain((await service.GetUnattributedCostsAsync(2026, 9)).Value, r => r.PlateNumberE == "UNKNOWN999");
        Assert.Contains((await service.GetDailyReportAsync(new DateOnly(2026, 9, 11))).Value.Vehicles,
            r => r.PlateNumberE == "UNKNOWN999" && r.HasResolutionError);
        Assert.True((await service.AttributePendingAsync()).IsSuccess);
        Assert.True((await service.AttributePendingAsync()).IsSuccess);
        Assert.Equal(2, await db.RiderPetrolCosts.CountAsync());
        await AssertConserved(db);
        db.Vehicles.Add(new Vehicle { VehicleNumber = "V3", PlateNumberE = "UNKNOWN999", PlateNumberA = "A3" });
        db.RiderVehicleStatus.Add(Take("V3", 101, At(1), active: true));
        await db.SaveChangesAsync();
        Assert.True((await service.AttributePendingAsync()).IsSuccess);
        var raw = await db.VehiclePetrolCosts.SingleAsync(c => c.PlateNumberE == "UNKNOWN999");
        Assert.Equal("V3", raw.VehicleNumber);
        Assert.False(raw.HasResolutionError);
        Assert.True(raw.IsAttributed);
        Assert.Equal(101, (await db.RiderPetrolCosts.SingleAsync(r => r.VehiclePetrolCostId == raw.Id)).RiderIqamaNo);
        await AssertConserved(db);
    }

    [Fact]
    public async Task MissingAllocationAndMissingEmployee_RetainCostsForReview()
    {
        await using var db = CreateDb();
        Seed(db);
        db.RiderVehicleStatus.Add(Take("V1", 999, At(1), active: true));
        await db.SaveChangesAsync();
        await AddCost(db, "V1", 11);
        Assert.True((await new PetrolService(db).AttributePendingAsync()).IsSuccess);
        var row = await db.RiderPetrolCosts.SingleAsync();
        Assert.Null(row.RiderIqamaNo);
        Assert.Contains("employee", row.Notes);
        await AssertConserved(db);
    }

    [Fact]
    public async Task SmallCostAcrossManyRiders_NeverProducesNegativeShares()
    {
        await using var db = CreateDb();
        Seed(db);
        db.Employees.AddRange(new Employees { IqamaNo = 103 }, new Employees { IqamaNo = 104 });
        db.RiderVehicleStatus.AddRange(Take("V1", 101, At(11)), Take("V1", 102, At(11, 6)),
            Take("V1", 103, At(11, 12)), Take("V1", 104, At(11, 18), active: true));
        await db.SaveChangesAsync();
        var cost = await AddCost(db, "V1", 11, .02m);
        Assert.True((await new PetrolService(db).AttributeSingleByIdAsync(cost.Id)).IsSuccess);
        Assert.Equal(4, await db.RiderPetrolCosts.CountAsync());
        Assert.All(await db.RiderPetrolCosts.ToListAsync(), r => Assert.InRange(r.Cost, 0m, .02m));
        await AssertConserved(db);
    }

    [Fact]
    public async Task Upload_CommitsRawCostsAndAllocationsWithOneSave_AndFailedSaveLeavesNoStagedPetrol()
    {
        await using var db = new SaveCheckingDb(new DbContextOptionsBuilder<ApplicationDbcontext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Seed(db);
        db.RiderVehicleStatus.Add(Take("V1", 101, At(1), active: true));
        await db.SaveChangesAsync();
        db.SaveCalls = 0;
        db.FailNextSave = true;
        var service = new PetrolService(db);
        var failed = await service.ProcessUploadAsync(PetrolFile(("AA111", "100")), new DateOnly(2026, 9, 11), "Admin");
        Assert.True(failed.IsFailure);
        Assert.Empty(await db.VehiclePetrolCosts.ToListAsync());
        Assert.Empty(await db.RiderPetrolCosts.ToListAsync());
        await db.SaveChangesAsync();
        Assert.Empty(await db.VehiclePetrolCosts.ToListAsync());
        db.SaveCalls = 0;
        Assert.True((await service.ProcessUploadAsync(PetrolFile(("AA111", "100")), new DateOnly(2026, 9, 11), "Admin")).IsSuccess);
        Assert.Equal(1, db.SaveCalls);
        await AssertConserved(db);
    }

    private static ApplicationDbcontext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbcontext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task HistoricalPossession_IsNotCutOffByPermitExpiryBeforeRecordedReturn()
    {
        await using var db = CreateDb();
        Seed(db);
        db.RiderVehicleStatus.AddRange(Take("V1", 101, At(1), At(1), At(5)),
            Event("V1", 101, VehicleStatusType.Returned, At(20)));
        await db.SaveChangesAsync();
        var cost = await AddCost(db, "V1", 10);
        Assert.True((await new PetrolService(db).AttributeSingleByIdAsync(cost.Id)).IsSuccess);
        Assert.Equal(101, (await db.RiderPetrolCosts.SingleAsync()).RiderIqamaNo);
        await AssertConserved(db);
    }

    [Fact]
    public async Task ManualAssignment_RejectsSecondVehicleWithoutSwitch_ButAllowsActualSwitch()
    {
        await using var db = CreateDb();
        Seed(db);
        var service = new PetrolService(db);
        await AddCost(db, "V1", 11);
        await AddCost(db, "V2", 11);
        Assert.True((await service.AttributePendingAsync()).IsSuccess);
        Assert.True((await service.AssignRiderToUnattributedAsync("AA111", new DateOnly(2026, 9, 11), 101, "Admin")).IsSuccess);
        var conflict = await service.AssignRiderToUnattributedAsync("BB222", new DateOnly(2026, 9, 11), 101, "Admin");
        Assert.Equal("AssignmentConflict", conflict.Error.Code);
        db.RiderVehicleStatus.AddRange(Take("V1", 101, At(1)), Take("V2", 101, At(11, 12), active: true));
        await db.SaveChangesAsync();
        Assert.True((await service.AssignRiderToUnattributedAsync("BB222", new DateOnly(2026, 9, 11), 101, "Admin")).IsSuccess);
        await AssertConserved(db);
    }

    [Fact]
    public async Task AutomaticAllocation_DoesNotAddSecondVehicleToUnrelatedManualOverride()
    {
        await using var db = CreateDb();
        Seed(db);
        var service = new PetrolService(db);
        var manualCost = await AddCost(db, "V1", 11);
        Assert.True((await service.AttributeSingleByIdAsync(manualCost.Id)).IsSuccess);
        Assert.True((await service.AssignRiderToUnattributedAsync("AA111", manualCost.Date, 101, "Admin")).IsSuccess);
        db.RiderVehicleStatus.Add(Take("V2", 101, At(1), active: true));
        await db.SaveChangesAsync();
        var automaticCost = await AddCost(db, "V2", 11);
        Assert.True((await service.AttributeSingleByIdAsync(automaticCost.Id)).IsSuccess);
        Assert.Null((await db.RiderPetrolCosts.SingleAsync(r => r.VehiclePetrolCostId == automaticCost.Id)).RiderIqamaNo);
        var review = Assert.Single((await service.GetUnattributedCostsAsync(2026, 9)).Value);
        Assert.Equal("ManualOverrideConflict", review.ReasonCode);
        Assert.Equal(new[] { manualCost.Id }, review.ConflictingVehiclePetrolCostIds);
        Assert.StartsWith("ManualOverrideConflict:", (await db.RiderPetrolCosts.SingleAsync(r => r.VehiclePetrolCostId == automaticCost.Id)).Notes);
        await AssertConserved(db);
    }

    [Fact]
    public async Task VehicleImport_ClosesBothFormerAssignmentsAndClearsDisplacedRider()
    {
        await using var db = CreateImportDb();
        Seed(db);
        db.RiderDetails.AddRange(new RiderDetails { EmployeeIqamaNo = 101, CompanyId = 1, VehicleNumber = "V1" },
            new RiderDetails { EmployeeIqamaNo = 102, CompanyId = 1, VehicleNumber = "V2" });
        var old = Take("V1", 101, At(1), At(1), At(30).AddYears(1), true);
        var displaced = Take("V2", 102, At(1), At(1), At(30).AddYears(1), true);
        db.RiderVehicleStatus.AddRange(old, displaced);
        await db.SaveChangesAsync();
        var result = await new ImportService(db, null!).ImportVehiclesAsync(VehicleFile("V2", "Taken", 101), "Admin");
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.AssignedToRiders);
        Assert.Equal("V2", (await db.RiderDetails.SingleAsync(r => r.EmployeeIqamaNo == 101)).VehicleNumber);
        Assert.Null((await db.RiderDetails.SingleAsync(r => r.EmployeeIqamaNo == 102)).VehicleNumber);
        var current = await db.RiderVehicleStatus.SingleAsync(s => s.IsActive && s.StatusType == VehicleStatusType.Taken);
        Assert.False(old.IsActive);
        Assert.False(displaced.IsActive);
        Assert.Equal(current.Timestamp, old.PermissionEndDate);
        Assert.Equal(current.Timestamp, displaced.PermissionEndDate);
        Assert.Equal(2, await db.RiderVehicleStatus.CountAsync(s => s.StatusType == VehicleStatusType.Returned));
        var day = DateOnly.FromDateTime(current.Timestamp).AddDays(1);
        db.VehiclePetrolCosts.AddRange(new VehiclePetrolCost { VehicleNumber = "V1", PlateNumberE = "AA111", Date = day, Cost = 100 },
            new VehiclePetrolCost { VehicleNumber = "V2", PlateNumberE = "BB222", Date = day, Cost = 100 });
        await db.SaveChangesAsync();
        Assert.True((await new PetrolService(db).AttributePendingAsync()).IsSuccess);
        Assert.Equal("V2", Assert.Single(await db.RiderPetrolCosts.Where(r => r.RiderIqamaNo == 101).ToListAsync()).VehicleNumber);
        Assert.False(await db.RiderPetrolCosts.AnyAsync(r => r.RiderIqamaNo == 102));
        await AssertConserved(db);
    }

    [Fact]
    public async Task AvailableStatusImport_ClearsRiderAndRecordsReturn()
    {
        await using var db = CreateImportDb();
        Seed(db);
        db.RiderDetails.Add(new RiderDetails { EmployeeIqamaNo = 101, CompanyId = 1, VehicleNumber = "V1" });
        var take = Take("V1", 101, At(1), At(1), At(30).AddYears(1), true);
        db.RiderVehicleStatus.Add(take);
        await db.SaveChangesAsync();
        var result = await new ImportService(db, null!).ImportVehiclesAsync(VehicleFile("V1", "Available"), "Admin");
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.FailedRecords);
        Assert.Null((await db.RiderDetails.SingleAsync()).VehicleNumber);
        Assert.False(take.IsActive);
        var returned = await db.RiderVehicleStatus.SingleAsync(s => s.StatusType == VehicleStatusType.Returned);
        Assert.Equal(returned.Timestamp, take.PermissionEndDate);
    }

    [Fact]
    public async Task RepeatedAssignmentImport_PreservesExistingTakeTimestamp()
    {
        await using var db = CreateImportDb();
        Seed(db);
        db.RiderDetails.Add(new RiderDetails { EmployeeIqamaNo = 101, CompanyId = 1, VehicleNumber = "V1" });
        db.RiderVehicleStatus.Add(Take("V1", 101, At(1), active: true));
        await db.SaveChangesAsync();
        var result = await new ImportService(db, null!).ImportVehiclesAsync(VehicleFile("V1", "Taken", 101), "Admin");
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.AssignedToRiders);
        Assert.Equal(At(1), (await db.RiderVehicleStatus.SingleAsync()).Timestamp);
    }

    private static ApplicationDbcontext CreateImportDb() => new(new DbContextOptionsBuilder<ApplicationDbcontext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static IFormFile VehicleFile(string vehicle, string status, long? rider = null)
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Vehicles");
            string[] headers = ["VehicleNumber", "SerialNumber", "PlateNumberA", "PlateNumberE", "Status", "RiderIqamaNo"];
            string[] values = [vehicle, vehicle == "V1" ? "1" : "2", vehicle == "V1" ? "A1" : "A2",
                vehicle == "V1" ? "AA111" : "BB222", status, rider?.ToString() ?? ""];
            for (var i = 0; i < headers.Length; i++)
            {
                sheet.Cell(1, i + 1).Value = headers[i];
                sheet.Cell(2, i + 1).Value = values[i];
            }
            workbook.SaveAs(stream);
        }
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "vehicles.xlsx");
    }

    private static void Seed(ApplicationDbcontext db)
    {
        db.Employees.AddRange(new Employees { IqamaNo = 101, NameEN = "Rider A" }, new Employees { IqamaNo = 102, NameEN = "Rider B" });
        db.Vehicles.AddRange(new Vehicle { VehicleNumber = "V1", PlateNumberE = "AA111", PlateNumberA = "A1", SerialNumber = 1 },
            new Vehicle { VehicleNumber = "V2", PlateNumberE = "BB222", PlateNumberA = "A2", SerialNumber = 2 });
    }

    private static DateTime At(int day, int hour = 0) => new(2026, 9, day, hour, 0, 0);

    private static RiderVehicleStatus Take(string vehicle, long rider, DateTime timestamp,
        DateTime? start = null, DateTime? end = null, bool active = false) => new()
        { VehicleNumber = vehicle, EmployeeIqamaNo = rider, StatusType = VehicleStatusType.Taken,
          Timestamp = timestamp, PermissionStartDate = start, PermissionEndDate = end, IsActive = active };

    private static RiderVehicleStatus Event(string vehicle, long? rider, VehicleStatusType type, DateTime at) => new()
        { VehicleNumber = vehicle, EmployeeIqamaNo = rider, StatusType = type, Timestamp = at };

    private static async Task<VehiclePetrolCost> AddCost(ApplicationDbcontext db, string vehicle, int day, decimal amount = 100m)
    {
        var row = new VehiclePetrolCost { VehicleNumber = vehicle, PlateNumberE = vehicle == "V1" ? "AA111" : "BB222",
            Date = new DateOnly(2026, 9, day), Cost = amount };
        db.VehiclePetrolCosts.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    private static async Task AssertConserved(ApplicationDbcontext db)
    {
        var costs = await db.VehiclePetrolCosts.AsNoTracking().ToListAsync();
        var rows = await db.RiderPetrolCosts.AsNoTracking().ToListAsync();
        Assert.All(costs, c => Assert.Equal(c.Cost, rows.Where(r => r.VehiclePetrolCostId == c.Id).Sum(r => r.Cost)));
    }

    private static IFormFile PetrolFile(params (string Plate, string Cost)[] rows)
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Petrol");
            sheet.Cell(1, 1).Value = "Plate";
            sheet.Cell(1, 2).Value = "Cost";
            for (var i = 0; i < rows.Length; i++)
            {
                sheet.Cell(i + 2, 1).Value = rows[i].Plate;
                sheet.Cell(i + 2, 2).Value = rows[i].Cost;
            }
            workbook.SaveAs(stream);
        }
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "petrol.xlsx");
    }

    private sealed class SaveCheckingDb : ApplicationDbcontext
    {
        [SetsRequiredMembers]
        public SaveCheckingDb(DbContextOptions<ApplicationDbcontext> options) : base(options) { }

        public int SaveCalls { get; set; }
        public bool FailNextSave { get; set; }
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
        {
            SaveCalls++;
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new InvalidOperationException("Synthetic save failure");
            }
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        }
    }
}
