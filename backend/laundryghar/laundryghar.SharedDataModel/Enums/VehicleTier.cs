namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Vehicle tier vocabulary — the single source of truth shared by rider vehicle typing
/// (<c>logistics.riders.vehicle_type</c>) and a job's requested tier
/// (<c>orders.requested_vehicle_tier</c>).
///
/// Each tier has an ordinal <see cref="Rank"/> (cycle/foot smallest → four_wheeler largest)
/// used for the dispatch "upgrade ladder": a rider may take a job whose required tier rank
/// is &lt;= the rider's own (a larger vehicle can carry a smaller job, never the reverse).
/// </summary>
public static class VehicleTier
{
    public const string Foot         = "foot";
    public const string Cycle        = "cycle";
    public const string TwoWheeler   = "two_wheeler";
    public const string ThreeWheeler = "three_wheeler";
    public const string FourWheeler  = "four_wheeler";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
        { Foot, Cycle, TwoWheeler, ThreeWheeler, FourWheeler };

    private static readonly IReadOnlyDictionary<string, int> Ranks = new Dictionary<string, int>
    {
        [Foot]         = 0,
        [Cycle]        = 1,
        [TwoWheeler]   = 2,
        [ThreeWheeler] = 3,
        [FourWheeler]  = 4,
    };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);

    /// <summary>Ordinal capacity rank; unknown tiers rank -1 so they never satisfy a constraint.</summary>
    public static int Rank(string? tier) => tier is not null && Ranks.TryGetValue(tier, out var r) ? r : -1;

    /// <summary>
    /// True when a rider of <paramref name="riderTier"/> may serve a job requiring
    /// <paramref name="requiredTier"/>. A null/empty requirement means "no constraint".
    /// Otherwise the rider's vehicle must rank at least as high as the requirement
    /// (upgrade ladder — a bigger vehicle can carry a smaller job).
    /// </summary>
    public static bool CanServe(string? riderTier, string? requiredTier)
    {
        if (string.IsNullOrEmpty(requiredTier)) return true;
        return Rank(riderTier) >= Rank(requiredTier);
    }
}
