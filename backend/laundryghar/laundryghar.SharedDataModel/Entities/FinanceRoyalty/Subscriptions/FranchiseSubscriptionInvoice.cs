namespace laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;

/// <summary>Monthly SaaS invoice (base + overage) for a franchise subscription
/// (finance_royalty.franchise_subscription_invoices).
/// amount_due is GENERATED ALWAYS AS (grand_total - amount_paid) STORED — read-only.</summary>
public class FranchiseSubscriptionInvoice
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid FranchiseId { get; set; }
    public Guid FranchiseSubscriptionId { get; set; }
    public string InvoiceNumber { get; set; } = null!;
    public DateTimeOffset BillingPeriodStart { get; set; }
    public DateTimeOffset BillingPeriodEnd { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal OverageAmount { get; set; }
    public decimal SetupFee { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Cgst { get; set; }
    public decimal Sgst { get; set; }
    public decimal Igst { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal AmountPaid { get; set; }

    /// <summary>GENERATED ALWAYS AS (grand_total - amount_paid) STORED — read-only.</summary>
    public decimal? AmountDue { get; set; }

    public string CurrencyCode { get; set; } = null!;

    /// <summary>jsonb — {orders, stores, users, warehouses, riders} usage snapshot.</summary>
    public string UsageSnapshot { get; set; } = null!;

    public Guid? PaymentId { get; set; }
    public string Status { get; set; } = null!;
    public short AttemptCount { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? InvoiceS3Key { get; set; }
    public string? InvoicePdfUrl { get; set; }

    /// <summary>jsonb — arbitrary metadata.</summary>
    public string Metadata { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public FranchiseSubscription FranchiseSubscription { get; set; } = null!;
}
