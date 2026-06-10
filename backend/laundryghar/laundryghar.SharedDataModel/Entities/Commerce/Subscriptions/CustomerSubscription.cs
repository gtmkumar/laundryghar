using laundryghar.SharedDataModel.Entities.CustomerCatalog;

namespace laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;

/// <summary>An active recurring subscription instance for a customer (commerce.customer_subscriptions).
/// Holds price/interval/quota snapshots so mid-term plan changes do not retroactively shift terms.
/// Dunning fields: past_due_since, dunning_attempts, failed_payment_count.</summary>
public class CustomerSubscription
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid PlanId { get; set; }
    public Guid? MandateId { get; set; }
    public string SubscriptionNumber { get; set; } = null!;

    // Snapshots (locked at subscription time)
    public decimal PriceSnapshot { get; set; }
    public string BillingInterval { get; set; } = null!;
    public short IntervalCount { get; set; }
    public string QuotaType { get; set; } = null!;
    public decimal? QuotaValue { get; set; }
    public string CurrencyCode { get; set; } = null!;

    // Lifecycle
    public string Status { get; set; } = null!;
    public bool AutoRenew { get; set; }
    public DateTimeOffset? CurrentPeriodStart { get; set; }
    public DateTimeOffset? CurrentPeriodEnd { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? TrialEndsAt { get; set; }
    public decimal CreditsRemaining { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public DateTimeOffset? PauseResumesAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    // Dunning
    public DateTimeOffset? PastDueSince { get; set; }
    public short DunningAttempts { get; set; }
    public short FailedPaymentCount { get; set; }
    public int TotalCyclesBilled { get; set; }
    public string? GatewaySubscriptionId { get; set; }

    /// <summary>jsonb — arbitrary metadata.</summary>
    public string Metadata { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }

    // Navigations
    public Customer Customer { get; set; } = null!;
    public SubscriptionPlan Plan { get; set; } = null!;
    public PaymentMandate? Mandate { get; set; }
    public ICollection<SubscriptionInvoice> Invoices { get; set; } = [];
    public ICollection<SubscriptionBillingAttempt> BillingAttempts { get; set; } = [];
    public ICollection<SubscriptionUsageLedger> UsageLedger { get; set; } = [];
}
