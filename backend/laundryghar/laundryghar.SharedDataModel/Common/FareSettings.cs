namespace laundryghar.SharedDataModel.Common;

/// <summary>
/// Per-tier delivery fare rates (₹). Mirrors the shape/role of
/// <see cref="RiderPayoutSettings"/> but is the CUSTOMER-facing charge (what the
/// customer pays), not the rider payout.
/// </summary>
public sealed class FareTierRate
{
    /// <summary>Flat base charge for the delivery leg before distance.</summary>
    public decimal BaseFare { get; set; }

    /// <summary>Per-kilometre charge for the delivery leg.</summary>
    public decimal PerKm { get; set; }

    /// <summary>Flat charge for the pickup leg (A→pickup).</summary>
    public decimal PickupFlat { get; set; }
}

/// <summary>
/// A time-of-day surge window. Matches when the quote instant falls on one of
/// <see cref="Days"/> (empty = every day) AND the hour is within [StartHour, EndHour).
/// An overnight window (StartHour &gt; EndHour, e.g. 22→2) wraps past midnight.
/// </summary>
public sealed class FareSurgeWindow
{
    /// <summary>Day-of-week numbers (0=Sunday … 6=Saturday). Empty = applies every day.</summary>
    public int[] Days { get; set; } = [];

    /// <summary>Inclusive start hour (0–23).</summary>
    public int StartHour { get; set; }

    /// <summary>Exclusive end hour (1–24). May be &lt;= StartHour to denote an overnight window.</summary>
    public int EndHour { get; set; }

    /// <summary>Multiplier applied to the computed fare while active (e.g. 1.5).</summary>
    public decimal Multiplier { get; set; } = 1m;

    public bool Matches(DateTimeOffset at)
    {
        if (Days.Length > 0 && !Days.Contains((int)at.DayOfWeek)) return false;
        var hour = at.Hour;
        // Normal window [start, end); overnight window wraps (start > end).
        return StartHour <= EndHour
            ? hour >= StartHour && hour < EndHour
            : hour >= StartHour || hour < EndHour;
    }
}

/// <summary>Result of a fare computation — the two order legs plus the surge applied.</summary>
public readonly record struct FareBreakdown(decimal PickupCharge, decimal DeliveryCharge, decimal SurgeMultiplier);

/// <summary>
/// Configurable distance + time + surge delivery pricing, persisted as JSON in
/// kernel.system_settings (category 'fare', key 'quote') — the same mechanism the
/// Settings panel uses for rider payout. The formula and defaults live in one place.
///
///   delivery = max(MinFare, (tier.BaseFare + tier.PerKm·km)) · surge
///   pickup   = tier.PickupFlat · surge
///   both rounded to the nearest RoundToNearest.
/// </summary>
public sealed class FareSettings
{
    public decimal MinFare { get; set; } = 30m;
    public decimal RoundToNearest { get; set; } = 5m;

    /// <summary>How long an issued quote stays valid (token expiry).</summary>
    public int QuoteTtlSeconds { get; set; } = 600;

    /// <summary>Per-vehicle-tier rates, keyed by <see cref="Enums.VehicleTier"/> value.</summary>
    public Dictionary<string, FareTierRate> TierRates { get; set; } = new()
    {
        ["foot"]          = new() { BaseFare = 20m, PerKm = 6m,  PickupFlat = 10m },
        ["cycle"]         = new() { BaseFare = 20m, PerKm = 6m,  PickupFlat = 10m },
        ["two_wheeler"]   = new() { BaseFare = 25m, PerKm = 8m,  PickupFlat = 15m },
        ["three_wheeler"] = new() { BaseFare = 35m, PerKm = 12m, PickupFlat = 20m },
        ["four_wheeler"]  = new() { BaseFare = 60m, PerKm = 18m, PickupFlat = 30m },
    };

    public List<FareSurgeWindow> Surge { get; set; } = [];

    /// <summary>
    /// Computes the pickup + delivery charge for a trip of <paramref name="distanceKm"/>
    /// using the rate for <paramref name="tier"/> (falls back to two_wheeler when the tier
    /// is unknown) and the surge active at <paramref name="at"/>.
    /// </summary>
    public FareBreakdown Compute(decimal distanceKm, string? tier, DateTimeOffset at)
    {
        var rate = tier is not null && TierRates.TryGetValue(tier, out var r)
            ? r
            : (TierRates.TryGetValue("two_wheeler", out var def) ? def : new FareTierRate());

        var km = distanceKm < 0m ? 0m : distanceKm;
        var surge = SurgeAt(at);

        var deliveryRaw = Math.Max(MinFare, rate.BaseFare + rate.PerKm * km) * surge;
        var pickupRaw   = rate.PickupFlat * surge;

        return new FareBreakdown(RoundFare(pickupRaw), RoundFare(deliveryRaw), surge);
    }

    /// <summary>Highest multiplier among the surge windows active at <paramref name="at"/> (1 when none).</summary>
    public decimal SurgeAt(DateTimeOffset at)
    {
        var max = 1m;
        foreach (var w in Surge)
            if (w.Matches(at) && w.Multiplier > max)
                max = w.Multiplier;
        return max;
    }

    private decimal RoundFare(decimal value)
    {
        var round = RoundToNearest <= 0m ? 1m : RoundToNearest;
        return Math.Round(value / round, MidpointRounding.AwayFromZero) * round;
    }
}
