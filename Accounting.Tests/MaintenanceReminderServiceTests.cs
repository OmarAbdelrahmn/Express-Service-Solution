using Application.Service.Reminder;
using Domain;
using Domain.Entities;
using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public class MaintenanceReminderServiceTests
{
    [Fact]
    public async Task GlobalReminders_IncludeCompanyAndHousingUsageForAllDueVehicles()
    {
        await using var db = new ApplicationDbcontext(
            new DbContextOptionsBuilder<ApplicationDbcontext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);

        db.SpareParts.AddRange(
            new SparePart { Id = 1, Name = "Oil Filter", Location = "الشركة" },
            new SparePart { Id = 2, Name = "Oil Filter", Location = "Housing A" },
            new SparePart { Id = 3, Name = "Other Part", Location = "Housing A" });
        db.MaintenanceIntervals.Add(new MaintenanceInterval
        {
            Id = 1,
            ItemType = MaintenanceItemType.SparePart,
            SparePartId = 1,
            ItemName = "Oil Filter",
            IntervalDays = 30,
            IsActive = true
        });
        db.Vehicles.AddRange(
            new Vehicle { VehicleNumber = "V1", Location = "Housing A" },
            new Vehicle { VehicleNumber = "V2", Location = "Housing A" },
            new Vehicle { VehicleNumber = "V3", Location = "الشركة" });
        db.SparePartUsages.AddRange(
            new SparePartUsage { SparePartId = 1, VehicleNumber = "V1", UsedAt = new DateTime(2026, 6, 1), Location = "الشركة" },
            new SparePartUsage { SparePartId = 2, VehicleNumber = "V1", UsedAt = new DateTime(2026, 8, 1), Location = "Housing A" },
            new SparePartUsage { SparePartId = 2, VehicleNumber = "V2", UsedAt = new DateTime(2026, 8, 2), Location = "Housing A" },
            new SparePartUsage { SparePartId = 1, VehicleNumber = "V3", UsedAt = new DateTime(2026, 8, 3), Location = "الشركة" });
        await db.SaveChangesAsync();

        var result = await new ReminderService(db)
            .GetAllDueMaintenanceAsync(new DateOnly(2026, 9, 29));

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalAffectedVehicles);
        Assert.Equal(3, result.Value.TotalOverdueItems);
        Assert.Equal(new[] { "V1", "V2", "V3" },
            result.Value.VehicleReminders.Select(v => v.VehicleNumber).OrderBy(n => n));
        Assert.Equal(new DateTime(2026, 8, 1),
            result.Value.VehicleReminders.Single(v => v.VehicleNumber == "V1")
                .DueItems.Single().LastDoneAt);
    }

    [Fact]
    public async Task GlobalReminders_RespectIntervalLocationAcrossMatchingStockRecords()
    {
        await using var db = new ApplicationDbcontext(
            new DbContextOptionsBuilder<ApplicationDbcontext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);

        db.SpareParts.AddRange(
            new SparePart { Id = 1, Name = "Oil Filter", Location = "الشركة" },
            new SparePart { Id = 2, Name = "Oil Filter", Location = "Housing A" },
            new SparePart { Id = 3, Name = "Oil Filter", Location = "Housing B" });
        db.MaintenanceIntervals.Add(new MaintenanceInterval
        {
            Id = 1,
            ItemType = MaintenanceItemType.SparePart,
            SparePartId = 1,
            ItemName = "Oil Filter",
            IntervalDays = 30,
            Location = "Housing A",
            IsActive = true
        });
        db.Vehicles.AddRange(
            new Vehicle { VehicleNumber = "A", Location = "Housing A" },
            new Vehicle { VehicleNumber = "B", Location = "Housing B" });
        db.SparePartUsages.AddRange(
            new SparePartUsage { SparePartId = 2, VehicleNumber = "A", UsedAt = new DateTime(2026, 8, 1), Location = "Housing A" },
            new SparePartUsage { SparePartId = 3, VehicleNumber = "B", UsedAt = new DateTime(2026, 8, 1), Location = "Housing B" });
        await db.SaveChangesAsync();

        var result = await new ReminderService(db)
            .GetAllDueMaintenanceAsync(new DateOnly(2026, 9, 29));

        Assert.True(result.IsSuccess);
        Assert.Equal("A", Assert.Single(result.Value.VehicleReminders).VehicleNumber);
    }
}
