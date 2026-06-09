namespace laundryghar.SharedDataModel.Common;

/// <summary>
/// Configurable rider per-leg payout rates (₹), persisted as JSON in
/// kernel.system_settings (category 'payout', key 'rider'). Shared between Identity
/// (reads/writes via the Settings panel) and Logistics (computes per-leg payouts),
/// so the formula and defaults live in exactly one place.
///
///   payout = base + perKm·km + (express? expressBonus) + (cod? codBonus),
///   rounded to the nearest RoundToNearest.
/// </summary>
public sealed class RiderPayoutSettings
{
    public decimal BaseFare { get; set; } = 40m;
    public decimal PerKm { get; set; } = 7m;
    public decimal ExpressBonus { get; set; } = 20m;
    public decimal CodBonus { get; set; } = 10m;
    public decimal RoundToNearest { get; set; } = 5m;

    /// <summary>Compute a leg's payout. Distance defaults to 0 km when unknown.</summary>
    public decimal Compute(decimal? distanceKm, bool isExpress, bool hasCod)
    {
        var km = distanceKm ?? 0m;
        var raw = BaseFare + PerKm * km + (isExpress ? ExpressBonus : 0m) + (hasCod ? CodBonus : 0m);
        var round = RoundToNearest <= 0m ? 1m : RoundToNearest;
        return Math.Round(raw / round, MidpointRounding.AwayFromZero) * round;
    }
}
