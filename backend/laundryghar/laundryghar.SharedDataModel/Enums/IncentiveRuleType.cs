namespace laundryghar.SharedDataModel.Enums;

/// <summary>Rider incentive rule types (logistics.incentive_rules.rule_type).</summary>
public static class IncentiveRuleType
{
    /// <summary>Award the reward once a rider's daily completed deliveries reach the threshold.</summary>
    public const string TripsTarget = "trips_target";

    /// <summary>Award the reward per delivery completed within a fare surge window.</summary>
    public const string SurgeBonus = "surge_bonus";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { TripsTarget, SurgeBonus };
    public static bool IsValid(string? v) => v is not null && All.Contains(v);
}

/// <summary>Rider payout (withdrawal) request states.</summary>
public static class RiderPayoutRequestStatus
{
    public const string Requested = "requested";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Paid = "paid";
}
