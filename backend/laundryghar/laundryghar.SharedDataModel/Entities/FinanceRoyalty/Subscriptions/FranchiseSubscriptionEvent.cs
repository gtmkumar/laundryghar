namespace laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;

/// <summary>Lifecycle audit event for a franchise subscription (finance_royalty.franchise_subscription_events).
/// APPEND-ONLY per ADR-006. Every lifecycle change must write an event row.</summary>
public class FranchiseSubscriptionEvent
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid FranchiseSubscriptionId { get; set; }
    public Guid FranchiseId { get; set; }
    public string EventType { get; set; } = null!;
    public Guid? FromPlanId { get; set; }
    public Guid? ToPlanId { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public decimal? Amount { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public string ActorType { get; set; } = null!;
    public Guid? ActorId { get; set; }

    /// <summary>jsonb — arbitrary metadata.</summary>
    public string Metadata { get; set; } = null!;

    public DateTimeOffset OccurredAt { get; set; }

    // Navigations
    public FranchiseSubscription FranchiseSubscription { get; set; } = null!;
}
