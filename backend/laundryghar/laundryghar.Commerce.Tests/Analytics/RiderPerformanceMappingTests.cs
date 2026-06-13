using laundryghar.Analytics.Endpoints;
using laundryghar.SharedDataModel.Entities.Analytics;

namespace laundryghar.Commerce.Tests.Analytics;

/// <summary>
/// DEF-3 regression: analytics.mv_rider_performance emits NULL for the four aggregate
/// columns when a rider has no qualifying rows for the day. The entity is nullable to
/// avoid an InvalidCastException; the API projection must coalesce null → 0 so the JSON
/// stays numeric (backward compatible).
/// </summary>
public sealed class RiderPerformanceMappingTests
{
    private static RiderPerformance Entity(
        decimal? totalKm, decimal? avgDuration, decimal? rating, decimal? completion) => new()
    {
        BrandId              = Guid.NewGuid(),
        FranchiseId          = Guid.NewGuid(),
        RiderId              = Guid.NewGuid(),
        RiderCode            = "RID-001",
        PerfDate             = new DateOnly(2026, 6, 13),
        AssignmentsTotal     = 0,
        AssignmentsCompleted = 0,
        AssignmentsFailed    = 0,
        PickupsDone          = 0,
        DeliveriesDone       = 0,
        TotalKm              = totalKm,
        AvgDurationMin       = avgDuration,
        RatingAverage        = rating,
        CompletionRate       = completion,
    };

    [Fact]
    public void From_NullAggregates_CoalesceToZero()
    {
        var dto = RiderPerformanceResponse.From(Entity(null, null, null, null));

        Assert.Equal(0m, dto.TotalKm);
        Assert.Equal(0m, dto.AvgDurationMin);
        Assert.Equal(0m, dto.RatingAverage);
        Assert.Equal(0m, dto.CompletionRate);
    }

    [Fact]
    public void From_PresentAggregates_ArePreserved()
    {
        var dto = RiderPerformanceResponse.From(Entity(12.5m, 34.2m, 4.8m, 0.91m));

        Assert.Equal(12.5m, dto.TotalKm);
        Assert.Equal(34.2m, dto.AvgDurationMin);
        Assert.Equal(4.8m,  dto.RatingAverage);
        Assert.Equal(0.91m, dto.CompletionRate);
    }

    [Fact]
    public void From_MixedNullAndPresent_HandledIndependently()
    {
        var dto = RiderPerformanceResponse.From(Entity(null, 10m, null, 0.5m));

        Assert.Equal(0m,  dto.TotalKm);
        Assert.Equal(10m, dto.AvgDurationMin);
        Assert.Equal(0m,  dto.RatingAverage);
        Assert.Equal(0.5m, dto.CompletionRate);
    }
}
