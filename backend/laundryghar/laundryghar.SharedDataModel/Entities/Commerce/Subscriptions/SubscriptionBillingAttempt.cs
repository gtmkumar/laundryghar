namespace laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;

/// <summary>Each charge attempt against a mandate for dunning (commerce.subscription_billing_attempts).
/// APPEND-ONLY per ADR-006: no UPDATE operations are permitted on this table.
/// idempotency_key guarantees no double-debit.</summary>
public class SubscriptionBillingAttempt
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid CustomerSubscriptionId { get; set; }
    public Guid SubscriptionInvoiceId { get; set; }
    public Guid? MandateId { get; set; }
    public short AttemptNumber { get; set; }
    public decimal Amount { get; set; }
    public string? Gateway { get; set; }
    public string? GatewayPaymentId { get; set; }
    public string Status { get; set; } = null!;
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }

    /// <summary>jsonb — raw gateway response.</summary>
    public string? GatewayResponse { get; set; }

    public DateTimeOffset AttemptedAt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigations
    public CustomerSubscription CustomerSubscription { get; set; } = null!;
    public SubscriptionInvoice SubscriptionInvoice { get; set; } = null!;
    public PaymentMandate? Mandate { get; set; }
}
