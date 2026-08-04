using Application.Service.KeetaBreaks;
using Domain.Entities.Keeta;
using Xunit;

namespace Accounting.Tests;

public class KeetaBreakSchedulerTests
{
    private static readonly SchedulerShift Main = new("08:00-12:00", 19, 22);
    private static readonly SchedulerShift Evening = new("16:00-20:00", 0, 80);

    [Fact]
    public void AllProhibitedDates_ProducesNoAssignments() =>
        Assert.Empty(Schedule(new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 6), Riders(40)).Assignments);

    [Fact]
    public void SundayOnlyEligible_AssignsOnSundayOnly()
    {
        var result = Schedule(new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 7), Riders(40));
        Assert.All(result.Assignments, x => Assert.Equal(DayOfWeek.Sunday, x.Date.DayOfWeek));
    }

    [Theory]
    [InlineData(2026, 2, 26)]
    [InlineData(2028, 2, 27)]
    [InlineData(2026, 4, 28)]
    [InlineData(2026, 1, 29)]
    public void LastThreeDays_AreProhibited(int year, int month, int day) => Assert.False(KeetaBreakScheduler.IsEligible(new DateOnly(year, month, day)));

    [Theory]
    [InlineData(2025, 9, 4)]
    [InlineData(2025, 9, 5)]
    [InlineData(2025, 9, 6)]
    public void ThursdayFridaySaturday_AreProhibited(int year, int month, int day) => Assert.False(KeetaBreakScheduler.IsEligible(new DateOnly(year, month, day)));

    [Fact]
    public void StrictFloor_AllowsOneOfNineteenAndTwoOfForty()
    {
        var date = new DateOnly(2025, 9, 7);
        Assert.Empty(Schedule(date, date, Riders(19)).Assignments);
        Assert.Equal(2, Schedule(date, date, Riders(40)).Assignments.Count);
    }

    [Fact]
    public void MinimumStaffing_BlocksBreak() => Assert.Empty(Schedule(new DateOnly(2025, 9, 7), new DateOnly(2025, 9, 7), Riders(19)).Assignments);

    [Fact]
    public void ThreeShifts_ConsumeCapacityInEveryShift()
    {
        var riders = Riders(40).Select((x, i) => new SchedulerRider(x.Identifier, x.Name, x.HousingGroup, [Main.Key, Evening.Key])).ToList();
        var result = new KeetaBreakScheduler().Schedule(new DateOnly(2025, 9, 7), new DateOnly(2025, 9, 7), riders, [Main, Evening], 5, KeetaBreakRoundingPolicy.Floor, []);
        var assigned = result.Assignments.Single(x => x.RiderIdentifier == riders[0].Identifier);
        Assert.Equal(2, assigned.Shifts.Count);
        Assert.All(result.Capacities.Where(x => assigned.Shifts.Contains(x.Shift)), x => Assert.Equal(result.Assignments.Count, x.PlannedBreaks));
    }

    [Fact]
    public void MonthlyLimit_ExcludesRiderWithThreeConfirmedBreaks()
    {
        var rider = Riders(40).First();
        var result = Schedule(new DateOnly(2025, 9, 7), new DateOnly(2025, 9, 7), Riders(40), [new(rider.Identifier, new DateOnly(2025, 9, 1)), new(rider.Identifier, new DateOnly(2025, 9, 2)), new(rider.Identifier, new DateOnly(2025, 9, 3))]);
        Assert.DoesNotContain(result.Assignments, x => x.RiderIdentifier == rider.Identifier);
    }

    [Fact]
    public void Output_IsDeterministicAndUsesStableIdentifierTieBreak()
    {
        var date = new DateOnly(2025, 9, 7); var riders = Riders(40).Reverse().ToArray();
        var first = Schedule(date, date, riders); var second = Schedule(date, date, riders);
        Assert.Equal(first.Assignments.Select(x => x.RiderIdentifier), second.Assignments.Select(x => x.RiderIdentifier));
        Assert.Equal(["00000000000000001", "00000000000000002"], first.Assignments.Select(x => x.RiderIdentifier));
    }

    private static ScheduleResult Schedule(DateOnly start, DateOnly end, IReadOnlyList<SchedulerRider> riders, IReadOnlyList<ExistingBreak>? existing = null) => new KeetaBreakScheduler().Schedule(start, end, riders, [Main], 5, KeetaBreakRoundingPolicy.Floor, existing ?? []);
    private static IReadOnlyList<SchedulerRider> Riders(int count) => Enumerable.Range(1, count).Select(i => new SchedulerRider(i.ToString("D17"), $"Rider {i}", "A", [Main.Key])).ToArray();
}
