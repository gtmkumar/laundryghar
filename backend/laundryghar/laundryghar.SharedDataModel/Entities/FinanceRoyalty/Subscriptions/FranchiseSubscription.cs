using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;

/// <summary>A franchise's SaaS subscription instance (finance_royalty.franchise_subscriptions).
/// One live subscription per franchise enforced by unique partial index idx_fransub_one_live.
/// Replaces franchise_agreements.technology_fee_monthly when present (ADR-010).</summary>
public class FranchiseSubscription
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid FranchiseId { get; set; }
    public Guid PlatformPlanId { get; set; }
    public string SubscriptionNumber { get; set; } = null!;

    // Snapshots
    public decimal PriceSnapshot { get; set; }
    public string BillingInterval { get; set; } = null!;
    public short IntervalCount { get; set; }
    public string CurrencyCode { get; set; } = null!;

    // Quota snapshots
    public int? MaxStores { get; set; }
    public int? MaxWarehouses { get; set; }
    public int? MaxUsers { get; set; }
    public int? MaxOrdersPerMonth { get; set; }
    public int? MaxRiders { get; set; }

    // Lifecycle
    public string Status { get; set; } = null!;
    public bool AutoRenew { get; set; }
    public string PaymentMethod { get; set; } = null!;
    public string? GatewayMandateId { get; set; }
    public string? GatewaySubscriptionId { get; set; }
    public DateTimeOffset? CurrentPeriodStart { get; set; }
    public DateTimeOffset? CurrentPeriodEnd { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? TrialEndsAt { get; set; }
    public int CurrentPeriodOrders { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelReason { get; set; }

    // Dunning / suspension
    public DateTimeOffset? PastDueSince { get; set; }
    public short DunningAttempts { get; set; }
    public DateTimeOffset? SuspendGraceUntil { get; set; }
    public DateTimeOffset? SuspendedAt { get; set; }
    public string? SuspendedReason { get; set; }
    public DateTimeOffset? ReactivatedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int TotalCyclesBilled { get; set; }

    /// <summary>jsonb — arbitrary metadata.</summary>
    public string Metadata { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }

    // Navigations
    public Franchise Franchise { get; set; } = null!;
    public PlatformPlan PlatformPlan { get; set; } = null!;
    public ICollection<FranchiseSubscriptionInvoice> Invoices { get; set; } = [];
    public ICollection<FranchiseSubscriptionEvent> Events { get; set; } = [];
}
