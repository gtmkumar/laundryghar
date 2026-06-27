using laundryghar.SharedDataModel.Common;

namespace laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;

/// <summary>One invoice per billing cycle for a customer subscription (commerce.subscription_invoices).
/// amount_due is GENERATED ALWAYS AS (grand_total - amount_paid) STORED — mapped ValueGeneratedOnAddOrUpdate.</summary>
public class SubscriptionInvoice
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid CustomerSubscriptionId { get; set; }
    public Guid CustomerId { get; set; }
    public string InvoiceNumber { get; set; } = null!;
    public DateTimeOffset BillingPeriodStart { get; set; }
    public DateTimeOffset BillingPeriodEnd { get; set; }
    public decimal Subtotal { get; set; }
    public decimal SetupFee { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxableAmount { get; set; }
    /// <summary>Shared GST breakdown (tax_breakdown jsonb) — multi-vertical Phase 2 slice 2F.</summary>
    public TaxBreakdown Tax { get; set; } = new();
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal AmountPaid { get; set; }

    /// <summary>GENERATED ALWAYS AS (grand_total - amount_paid) STORED — read-only.</summary>
    public decimal? AmountDue { get; set; }

    public string CurrencyCode { get; set; } = null!;
    public Guid? PaymentId { get; set; }
    public string Status { get; set; } = null!;
    public short AttemptCount { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? GatewayInvoiceId { get; set; }
    public string? InvoiceS3Key { get; set; }

    /// <summary>jsonb — arbitrary metadata.</summary>
    public string Metadata { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigations
    public CustomerSubscription CustomerSubscription { get; set; } = null!;
    public Payment? Payment { get; set; }
    public ICollection<SubscriptionBillingAttempt> BillingAttempts { get; set; } = [];
}
