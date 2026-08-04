using Application.Contracts.KeetaBreaks;
using Application.Service.KeetaBreaks;
using Domain;
using Domain.Entities.Keeta;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Tests;

public class KeetaBreakCapacityPlanTests
{
    [Fact]
    public async Task CapacityPlan_UsesActualRiderTotalsAndWeakestShiftInEachPattern()
    {
        await using var db = CreateDbContext();
        var service = new KeetaBreakService(db);
        var configuration = await service.CreateConfigurationAsync(ConfigurationRequest(), "admin");

        var result = await service.CreateCapacityPlanAsync(new CreateKeetaBreakCapacityPlanRequest(
            new DateOnly(2025, 9, 7), new DateOnly(2025, 9, 7), configuration.Value.Id));

        Assert.True(result.IsSuccess);
        var patterns = result.Value.Dates.Single().Patterns.ToDictionary(x => x.Periods);
        Assert.Equal(2, patterns["00:00-03:00 + 16:00-20:00 + 20:00-00:00"].MaximumBreakRiders);
        Assert.Equal(0, patterns["08:00-12:00 + 16:00-20:00 + 20:00-00:00"].MaximumBreakRiders);
        Assert.Equal(0, patterns["03:00-08:00 + 16:00-20:00 + 20:00-00:00"].MaximumBreakRiders);
        Assert.Equal(42, result.Value.ShiftTotals.Single(x => x.Shift == "00:00-03:00").TotalRiders);
        Assert.Equal(78, result.Value.ShiftTotals.Single(x => x.Shift == "16:00-20:00").TotalRiders);
    }

    [Fact]
    public async Task CapacityPlan_ProhibitedDateReturnsExactReasonAndZeroCapacity()
    {
        await using var db = CreateDbContext();
        var service = new KeetaBreakService(db);
        var configuration = await service.CreateConfigurationAsync(ConfigurationRequest(), "admin");

        var result = await service.CreateCapacityPlanAsync(new CreateKeetaBreakCapacityPlanRequest(
            new DateOnly(2025, 9, 4), new DateOnly(2025, 9, 4), configuration.Value.Id));

        var date = result.Value.Dates.Single();
        Assert.False(date.IsEligible);
        Assert.Equal("يوم الخميس محظور", date.ProhibitionReason);
        Assert.All(date.Patterns, x => Assert.Equal(0, x.MaximumBreakRiders));
    }

    [Fact]
    public async Task Configuration_NormalizesSeparatorsAndRemovesDuplicatePatterns()
    {
        await using var db = CreateDbContext();
        var service = new KeetaBreakService(db);

        var configuration = await service.CreateConfigurationAsync(ConfigurationRequest(), "admin");

        Assert.True(configuration.IsSuccess);
        Assert.Equal(3, configuration.Value.ShiftPatterns.Count);
        Assert.Contains(configuration.Value.ShiftPatterns, x => x.Periods == "00:00-03:00 + 16:00-20:00 + 20:00-00:00" && x.RiderCount == 42);
    }

    private static CreateKeetaBreakConfigurationRequest ConfigurationRequest() => new(
        new DateOnly(2025, 1, 1), null, 5, KeetaBreakRoundingPolicy.Floor,
        [
            new("00:00～03:00", new TimeOnly(0, 0), new TimeOnly(3, 0), 40, 45),
            new("03:00～08:00", new TimeOnly(3, 0), new TimeOnly(8, 0), 17, 19),
            new("08:00～12:00", new TimeOnly(8, 0), new TimeOnly(12, 0), 19, 22),
            new("16:00～20:00", new TimeOnly(16, 0), new TimeOnly(20, 0), 73, 80),
            new("20:00～00:00", new TimeOnly(20, 0), new TimeOnly(0, 0), 73, 80)
        ],
        [
            new("00:00～03:00 + 16:00～20:00 + 20:00～00:00", 41),
            new("08:00 ～ 12:00 + 16:00～20:00 + 20:00～ 00:00", 19),
            new("03:00 ～ 08:00 + 16:00～20:00 + 20:00～ 00:00", 17),
            new("00:00～03:00 + 16:00～20:00 + 20:00～00:00", 1)
        ]);

    private static ApplicationDbcontext CreateDbContext() => new(new DbContextOptionsBuilder<ApplicationDbcontext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
