using laundryghar.Worker.Services;

namespace laundryghar.Worker.Tests;

/// <summary>
/// DEFECT 5b — unit tests for the partition-maintenance planner
/// (<see cref="PartitionMaintenanceService.PlanPartitionNames"/>). The actual DDL is
/// performed by the SECURITY DEFINER function logistics.ensure_rider_ping_partitions
/// (verified live via psql); these cover the day-window naming logic.
/// </summary>
public sealed class PartitionMaintenancePlannerTests
{
    [Fact]
    public void Plan_IncludesTodayThroughTodayPlusN_Inclusive()
    {
        var today = new DateOnly(2026, 6, 13);
        var names = PartitionMaintenanceService.PlanPartitionNames(today, daysAhead: 14);

        // today + 14 ahead = 15 daily partitions.
        Assert.Equal(15, names.Count);
        Assert.Equal("rider_location_pings_p20260613", names[0]);
        Assert.Equal("rider_location_pings_p20260627", names[^1]);
    }

    [Fact]
    public void Plan_NamesMatchYyyymmddConvention()
    {
        var names = PartitionMaintenanceService.PlanPartitionNames(new DateOnly(2026, 1, 5), 0);
        Assert.Single(names);
        Assert.Equal("rider_location_pings_p20260105", names[0]); // zero-padded month/day
    }

    [Fact]
    public void Plan_NegativeDaysAhead_ClampedToToday()
    {
        var names = PartitionMaintenanceService.PlanPartitionNames(new DateOnly(2026, 6, 13), -5);
        Assert.Single(names);
        Assert.Equal("rider_location_pings_p20260613", names[0]);
    }
}
