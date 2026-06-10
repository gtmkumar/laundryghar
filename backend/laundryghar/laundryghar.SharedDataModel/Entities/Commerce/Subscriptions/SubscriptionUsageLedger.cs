namespace laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;

/// <summary>Per-cycle quota allocation and consumption (commerce.subscription_usage_ledger).
/// APPEND-ONLY per ADR-006. Tracks balance before/after each ledger entry.
/// transaction_type: allocate (cycle start) | consume (order drawn down) | rollover | expire | adjustment | refund.</summary>
public class SubscriptionUsageLedger
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid CustomerSubscriptionId { get; set; }
    public Guid CustomerId { get; set; }
    public DateTimeOffset BillingPeriodStart { get; set; }
    public DateTimeOffset BillingPeriodEnd { get; set; }
    public string TransactionType { get; set; } = null!;
    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public Guid? OrderId { get; set; }
    public DateTimeOffset? OrderCreatedAt { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Notes { get; set; }
    public string? PerformedByType { get; set; }
    public Guid? PerformedById { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    // Navigations
    public CustomerSubscription CustomerSubscription { get; set; } = null!;
}
