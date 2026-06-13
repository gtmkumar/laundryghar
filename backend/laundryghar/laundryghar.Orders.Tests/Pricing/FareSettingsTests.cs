using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Enums;

namespace laundryghar.Orders.Tests.Pricing;

/// <summary>
/// Unit tests for the distance + time + surge fare engine (<see cref="FareSettings.Compute"/>).
/// Pure POCO — no DB, no DI.
/// </summary>
public sealed class FareSettingsTests
{
    private static FareSettings Defaults() => new()
    {
        MinFare = 30m,
        RoundToNearest = 5m,
        TierRates = new()
        {
            [VehicleTier.TwoWheeler]  = new() { BaseFare = 25m, PerKm = 8m,  PickupFlat = 15m },
            [VehicleTier.FourWheeler] = new() { BaseFare = 60m, PerKm = 18m, PickupFlat = 30m },
        },
    };

    // A fixed off-peak instant (Wednesday 10:00 UTC) so default (no surge) applies.
    private static readonly DateTimeOffset OffPeak = new(2026, 6, 10, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Compute_UsesTierRate_AndDistance()
    {
        // two_wheeler: base 25 + 8/km · 5km = 65 → rounds to nearest 5 = 65; pickup 15.
        var f = Defaults().Compute(distanceKm: 5m, VehicleTier.TwoWheeler, OffPeak);
        Assert.Equal(65m, f.DeliveryCharge);
        Assert.Equal(15m, f.PickupCharge);
        Assert.Equal(1m, f.SurgeMultiplier);
    }

    [Fact]
    public void Compute_LargerTier_CostsMore()
    {
        var bike  = Defaults().Compute(5m, VehicleTier.TwoWheeler, OffPeak);
        var truck = Defaults().Compute(5m, VehicleTier.FourWheeler, OffPeak);
        Assert.True(truck.DeliveryCharge > bike.DeliveryCharge);
    }

    [Fact]
    public void Compute_ZeroDistance_AppliesMinFare()
    {
        var f = Defaults().Compute(0m, VehicleTier.TwoWheeler, OffPeak);
        // base 25 < MinFare 30 → floored at MinFare 30.
        Assert.Equal(30m, f.DeliveryCharge);
    }

    [Fact]
    public void Compute_UnknownTier_FallsBackToTwoWheeler()
    {
        var known   = Defaults().Compute(3m, VehicleTier.TwoWheeler, OffPeak);
        var unknown = Defaults().Compute(3m, "spaceship", OffPeak);
        Assert.Equal(known.DeliveryCharge, unknown.DeliveryCharge);
    }

    [Fact]
    public void Compute_AppliesSurge_WithinWindow()
    {
        var s = Defaults();
        // Evening peak 18:00–21:00 every day, ×2.
        s.Surge.Add(new FareSurgeWindow { Days = [], StartHour = 18, EndHour = 21, Multiplier = 2m });

        var peak    = new DateTimeOffset(2026, 6, 10, 19, 0, 0, TimeSpan.Zero);
        var offHour = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

        var atPeak = s.Compute(5m, VehicleTier.TwoWheeler, peak);
        var atOff  = s.Compute(5m, VehicleTier.TwoWheeler, offHour);

        Assert.Equal(2m, atPeak.SurgeMultiplier);
        Assert.Equal(1m, atOff.SurgeMultiplier);
        Assert.True(atPeak.DeliveryCharge > atOff.DeliveryCharge);
    }

    [Fact]
    public void Surge_OvernightWindow_WrapsMidnight()
    {
        var w = new FareSurgeWindow { Days = [], StartHour = 22, EndHour = 2, Multiplier = 1.5m };
        Assert.True(w.Matches(new DateTimeOffset(2026, 6, 10, 23, 0, 0, TimeSpan.Zero)));  // 23:00 in
        Assert.True(w.Matches(new DateTimeOffset(2026, 6, 10, 1, 0, 0, TimeSpan.Zero)));   // 01:00 in
        Assert.False(w.Matches(new DateTimeOffset(2026, 6, 10, 5, 0, 0, TimeSpan.Zero)));  // 05:00 out
    }

    [Fact]
    public void Surge_DayFilter_RestrictsToListedDays()
    {
        // Weekend-only surge (Sat=6, Sun=0).
        var w = new FareSurgeWindow { Days = [0, 6], StartHour = 0, EndHour = 24, Multiplier = 1.5m };
        Assert.True(w.Matches(new DateTimeOffset(2026, 6, 13, 12, 0, 0, TimeSpan.Zero)));  // Sat
        Assert.False(w.Matches(new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero))); // Wed
    }
}
