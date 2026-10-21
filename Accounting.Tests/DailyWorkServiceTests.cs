using Application.Contracts.DailyWork;
using Application.Service.DailyWork;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public class DailyWorkServiceTests
{
    private static readonly DateOnly Date = new(2026, 10, 20);

    [Fact]
    public async Task Edit_ResolvesBothIds_AndDoesNotModifyShifts()
    {
        await using var db = await CreateDb();
        var service = new DailyWorkService(db);
        var result = await service.EditAsync(new(Date,
            [new(10, null, 15, 8.25m), new(null, 1000000002, 0, 0)]));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Equal(10, result.Value.Items[0].RiderId);
        Assert.Equal(1000000001, result.Value.Items[0].EmployeeIqamaNo);
        Assert.Null(result.Value.Items[1].RiderId);
        Assert.Equal(8.25m, result.Value.Items[0].WorkingHours);
        var shift = await db.RiderShifts.SingleAsync();
        Assert.Equal(99, shift.AcceptedDailyOrders);
        Assert.Equal(12, shift.WorkingHours);
    }

    [Fact]
    public async Task Edit_IsIdempotent_AndPreservesOtherEmployeesAndDates()
    {
        await using var db = await CreateDb();
        var service = new DailyWorkService(db);
        await service.EditAsync(new(Date, [new(10, null, 15, 8), new(null, 1000000002, 7, 4)]));
        await service.EditAsync(new(Date.AddDays(-1), [new(10, null, 5, 2)]));
        var request = new EditDailyWorkRequest(Date, [new(null, 1000000001, 20, 9)]);
        await service.EditAsync(request);
        await service.EditAsync(request);
        db.ChangeTracker.Clear();

        Assert.Equal(3, await db.DailyWorkEntries.CountAsync());
        var result = await service.GetAsync(Date);
        Assert.Equal(20, result.Value.Items[0].Orders);
        Assert.Equal(7, result.Value.Items[1].Orders);
        Assert.Equal(5, (await service.GetAsync(Date.AddDays(-1))).Value.Items.Single().Orders);
    }

    [Fact]
    public async Task Edit_RejectsAliasesForTheSameEmployee_BeforeTrackingChanges()
    {
        await using var db = await CreateDb();
        var result = await new DailyWorkService(db).EditAsync(new(Date,
            [new(10, null, 15, 8), new(null, 1000000001, 20, 9)]));

        Assert.Equal("DailyWork.DuplicateEmployee", result.Error.Code);
        Assert.Empty(db.ChangeTracker.Entries<DailyWorkEntry>());
        await db.SaveChangesAsync();
        Assert.Empty(await db.DailyWorkEntries.ToListAsync());
    }

    [Fact]
    public async Task Edit_UnknownPerson_DoesNotPartiallyUpdateExistingEntry()
    {
        await using var db = await CreateDb();
        var service = new DailyWorkService(db);
        await service.EditAsync(new(Date, [new(10, null, 15, 8)]));
        db.ChangeTracker.Clear();
        var result = await service.EditAsync(new(Date,
            [new(10, null, 20, 9), new(null, 9999999999, 10, 5)]));

        Assert.Equal(404, result.Error.StatuesCode);
        Assert.Empty(db.ChangeTracker.Entries<DailyWorkEntry>());
        await db.SaveChangesAsync();
        Assert.Equal(15, (await service.GetAsync(Date)).Value.Items.Single().Orders);
    }

    [Theory]
    [InlineData(null, null, 1, 8)]
    [InlineData(10, 1000000001L, 1, 8)]
    [InlineData(0, null, 1, 8)]
    [InlineData(null, -1L, 1, 8)]
    [InlineData(10, null, -1, 8)]
    [InlineData(10, null, 1, -1)]
    [InlineData(10, null, 1, 25)]
    [InlineData(10, null, 1, 8.125)]
    public async Task Edit_RejectsInvalidItems(int? riderId, long? employeeId, int orders, double hours)
    {
        await using var db = await CreateDb();
        var result = await new DailyWorkService(db).EditAsync(new(Date,
            [new(riderId, employeeId, orders, (decimal)hours)]));

        Assert.Equal("DailyWork.InvalidItem", result.Error.Code);
        Assert.Empty(db.ChangeTracker.Entries<DailyWorkEntry>());
    }

    [Fact]
    public async Task Get_EmptyDateReturnsEmptyList_AndMissingDateIsRejected()
    {
        await using var db = await CreateDb();
        var service = new DailyWorkService(db);
        var result = await service.GetAsync(Date);
        Assert.True(result.IsSuccess);
        Assert.Equal(Date, result.Value.Date);
        Assert.Empty(result.Value.Items);
        Assert.Equal("DailyWork.InvalidDate", (await service.GetAsync(default)).Error.Code);
        Assert.Equal("DailyWork.InvalidDate", (await service.EditAsync(new(default, []))).Error.Code);
    }

    [Fact]
    public async Task Edit_RejectsNullEmptyAndOversizedLists()
    {
        await using var db = await CreateDb();
        var service = new DailyWorkService(db);
        foreach (var items in new List<DailyWorkItem>[] { null!, [], Enumerable.Repeat(new DailyWorkItem(10, null, 1, 1), 1001).ToList() })
            Assert.Equal("DailyWork.InvalidItems", (await service.EditAsync(new(Date, items))).Error.Code);
        Assert.Equal("DailyWork.InvalidItem", (await service.EditAsync(new(Date, [null!]))).Error.Code);
    }

    private static async Task<ApplicationDbcontext> CreateDb()
    {
        var db = new ApplicationDbcontext(new DbContextOptionsBuilder<ApplicationDbcontext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var employee = new Employees { IqamaNo = 1000000001, NameEN = "Rider" };
        var company = new Company { Id = 1, Name = "Test" };
        var rider = new RiderDetails
        {
            Id = 10, EmployeeIqamaNo = employee.IqamaNo, Employee = employee,
            WorkingId = "PLATFORM-10", CompanyId = 1, Company = company
        };
        db.AddRange(employee, company, rider, new Employees { IqamaNo = 1000000002, NameEN = "Employee" });
        db.RiderShifts.Add(new RiderShift
        {
            RiderId = 10, Rider = rider, ShiftDate = Date, WorkingId = "PLATFORM-10",
            CompanyId = 1, Company = company, AcceptedDailyOrders = 99, WorkingHours = 12
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }
}
