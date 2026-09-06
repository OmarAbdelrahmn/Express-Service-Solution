using Application.Service.Riders;
using ClosedXML.Excel;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public class RiderShiftBulkUpdateTests
{
    [Fact]
    public async Task MonthlyUpdate_UsesShiftRecordsWhenWorkingIdIsMissingFromDetailsAndHistory()
    {
        await using var db = CreateDbContext();
        var company = new Company { Id = 2, Name = "Keta" };
        var employee = CreateEmployee(1000000001, "Test Rider");
        var rider = new RiderDetails
        {
            Id = 10,
            WorkingId = "CURRENT-10",
            EmployeeIqamaNo = employee.IqamaNo,
            CompanyId = company.Id,
            Employee = employee,
            Company = company
        };

        db.AddRange(company, employee, rider);
        db.RiderShifts.AddRange(
            CreateShift(rider, company, "LEGACY-10", new DateOnly(2026, 6, 2), 3, 2),
            CreateShift(rider, company, "CURRENT-10", new DateOnly(2026, 6, 15), 4, 3));
        await db.SaveChangesAsync();

        var service = new RiderShiftService(db, new RiderWorkingIdHistoryService(db));
        await using var workbookStream = CreateUpdateWorkbook(
            "LEGACY-10",
            new DateOnly(2026, 6, 15),
            acceptedOrders: 19,
            workingHours: 9.5);

        var result = await service.UpdateShiftsFromExcelAsync(workbookStream);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.SuccessCount);
        Assert.Equal(0, result.Value.NotFoundCount);

        var targetShift = await db.RiderShifts.SingleAsync(s =>
            s.RiderId == rider.Id &&
            s.WorkingId == "CURRENT-10" &&
            s.ShiftDate == new DateOnly(2026, 6, 15));
        Assert.Equal(19, targetShift.AcceptedDailyOrders);
        Assert.Equal(9.5f, targetShift.WorkingHours);
        Assert.Equal(2, await db.RiderShifts.CountAsync());
    }

    [Fact]
    public async Task MonthlyUpdate_DoesNotUpdateWhenShiftOwnershipIsAmbiguous()
    {
        await using var db = CreateDbContext();
        var company = new Company { Id = 2, Name = "Keta" };
        var firstEmployee = CreateEmployee(1000000002, "First Rider");
        var secondEmployee = CreateEmployee(1000000003, "Second Rider");
        var firstRider = CreateRider(20, "CURRENT-20", firstEmployee, company);
        var secondRider = CreateRider(30, "CURRENT-30", secondEmployee, company);

        db.AddRange(company, firstEmployee, secondEmployee, firstRider, secondRider);
        db.RiderShifts.AddRange(
            CreateShift(firstRider, company, "REUSED-ID", new DateOnly(2026, 6, 2), 2, 2),
            CreateShift(secondRider, company, "REUSED-ID", new DateOnly(2026, 6, 2), 2, 2),
            CreateShift(firstRider, company, "CURRENT-20", new DateOnly(2026, 6, 15), 5, 4),
            CreateShift(secondRider, company, "CURRENT-30", new DateOnly(2026, 6, 15), 6, 4));
        await db.SaveChangesAsync();

        var service = new RiderShiftService(db, new RiderWorkingIdHistoryService(db));
        await using var workbookStream = CreateUpdateWorkbook(
            "REUSED-ID",
            new DateOnly(2026, 6, 15),
            acceptedOrders: 20,
            workingHours: 10);

        var result = await service.UpdateShiftsFromExcelAsync(workbookStream);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.SuccessCount);
        Assert.Equal(1, result.Value.NotFoundCount);
        Assert.Contains("multiple riders", result.Value.NotFoundShifts.Single().Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, (await db.RiderShifts.FindAsync(firstRider.Id, new DateOnly(2026, 6, 15), "CURRENT-20"))!.AcceptedDailyOrders);
        Assert.Equal(6, (await db.RiderShifts.FindAsync(secondRider.Id, new DateOnly(2026, 6, 15), "CURRENT-30"))!.AcceptedDailyOrders);
    }

    private static ApplicationDbcontext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbcontext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Employees CreateEmployee(long iqamaNo, string name) => new()
    {
        IqamaNo = iqamaNo,
        IqamaEndM = new DateOnly(2030, 1, 1),
        IqamaEndH = new DateOnly(2030, 1, 1),
        NameEN = name,
        NameAR = name,
        DateOfBirth = new DateOnly(1990, 1, 1)
    };

    private static RiderDetails CreateRider(
        int id,
        string workingId,
        Employees employee,
        Company company) => new()
    {
        Id = id,
        WorkingId = workingId,
        EmployeeIqamaNo = employee.IqamaNo,
        CompanyId = company.Id,
        Employee = employee,
        Company = company
    };

    private static RiderShift CreateShift(
        RiderDetails rider,
        Company company,
        string workingId,
        DateOnly date,
        int acceptedOrders,
        float workingHours) => new()
    {
        RiderId = rider.Id,
        Rider = rider,
        WorkingId = workingId,
        ShiftDate = date,
        AcceptedDailyOrders = acceptedOrders,
        WorkingHours = workingHours,
        CompanyId = company.Id,
        Company = company,
        ShiftStatus = ShiftStatus.Incomplete.ToString()
    };

    private static MemoryStream CreateUpdateWorkbook(
        string workingId,
        DateOnly shiftDate,
        int acceptedOrders,
        double workingHours)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Monthly performance");
        sheet.Cell(1, 1).Value = "Rider Id";
        sheet.Cell(1, 2).Value = "Completed Deliveries";
        sheet.Cell(1, 3).Value = "ShiftDate";
        sheet.Cell(1, 4).Value = "Actual Working Hours";
        sheet.Cell(2, 1).Value = workingId;
        sheet.Cell(2, 2).Value = acceptedOrders;
        sheet.Cell(2, 3).Value = shiftDate.ToDateTime(TimeOnly.MinValue);
        sheet.Cell(2, 4).Value = workingHours;

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
