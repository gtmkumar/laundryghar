namespace laundryghar.SharedDataModel.Entities.Logistics;

/// <summary>Admin-configured rider bonus rule (logistics.incentive_rules).
/// trips_target: bonus when daily completed deliveries hit Threshold.
/// surge_bonus:  bonus per delivery completed within a fare surge window.</summary>
public class IncentiveRule
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Name { get; set; } = null!;

    /// <summary>trips_target|surge_bonus — see <see cref="Enums.IncentiveRuleType"/>.</summary>
    public string RuleType { get; set; } = null!;

    public int Threshold { get; set; }
    public decimal RewardAmount { get; set; }
    public string Window { get; set; } = "daily";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }

    public string Metadata { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>A bonus awarded to a rider (logistics.rider_incentive_awards). Counts toward
/// the rider's withdrawable balance.</summary>
public class RiderIncentiveAward
{
    public Guid Id { get; set; }
    public Guid RiderId { get; set; }
    public Guid BrandId { get; set; }
    public Guid? RuleId { get; set; }
    public string RuleNameSnapshot { get; set; } = null!;
    public string RuleType { get; set; } = null!;
    public decimal Amount { get; set; }

    /// <summary>Bucket key for idempotency (e.g. IST day 'yyyy-MM-dd' for daily rules).</summary>
    public string PeriodKey { get; set; } = null!;
    public Guid? DeliveryAssignmentId { get; set; }
    public DateTimeOffset AwardedAt { get; set; }

    public string Metadata { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    public Rider Rider { get; set; } = null!;
    public IncentiveRule? Rule { get; set; }
}
