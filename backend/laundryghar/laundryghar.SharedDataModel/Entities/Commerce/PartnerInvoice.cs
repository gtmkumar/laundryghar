using laundryghar.SharedDataModel.Common;

namespace laundryghar.SharedDataModel.Entities.Commerce;

/// <summary>
/// A periodic RaaS partner invoice (commerce.partner_invoices) — the bookings a partner is billed
/// for over a billing period. Cloned in shape from
/// <see cref="FinanceRoyalty.Subscriptions.FranchiseSubscriptionInvoice"/> (the platform→tenant SaaS
/// invoice) but keyed by <see cref="PartnerId"/> instead of a franchise/subscription.
///
/// <para><see cref="PartnerId"/> is a SCALAR cross-BC reference to logistics.partners(id): NO
/// navigation and NO foreign key — commerce and logistics are separate bounded contexts (mirrors
/// <see cref="PartnerWalletAccount"/>). Isolation is enforced by the <c>rls_partner</c> policy on
/// <c>partner_id</c>, not referential integrity.</para>
///
/// <para><see cref="AmountDue"/> is <c>GENERATED ALWAYS AS (grand_total - amount_paid) STORED</c> —
/// read-only; EF must never write it (see the configuration).</para>
/// </summary>
public class PartnerInvoice
{
    public Guid Id { get; set; }

    /// <summary>The owning RaaS partner (logistics.partners.id) — the rls_partner isolation key.</summary>
    public Guid PartnerId { get; set; }

    /// <summary>Human-facing invoice number (unique).</summary>
    public string InvoiceNumber { get; set; } = null!;

    public DateTimeOffset BillingPeriodStart { get; set; }
    public DateTimeOffset BillingPeriodEnd { get; set; }

    /// <summary>jsonb — the line items billed on this invoice (typically the partner_bookings for the period).</summary>
    public string LineItems { get; set; } = "[]";

    public decimal Subtotal { get; set; }

    /// <summary>Shared GST breakdown (tax_breakdown jsonb) — the same owned type every invoice table uses.</summary>
    public TaxBreakdown Tax { get; set; } = new();
    public decimal TaxTotal { get; set; }

    public decimal GrandTotal { get; set; }
    public decimal AmountPaid { get; set; }

    /// <summary>GENERATED ALWAYS AS (grand_total - amount_paid) STORED — read-only.</summary>
    public decimal? AmountDue { get; set; }

    /// <summary>character(3) fixed-length currency code.</summary>
    public string CurrencyCode { get; set; } = "INR";

    /// <summary><c>draft</c> | <c>issued</c> | <c>paid</c> | <c>void</c>. Only an <c>issued</c> invoice is collectible.</summary>
    public string Status { get; set; } = "issued";

    /// <summary>Stored URL of the rendered invoice PDF (set out-of-band; the PDF renderer is a follow-up).</summary>
    public string? InvoicePdfUrl { get; set; }

    // Razorpay Payment Link collection (set when a link is generated for AmountDue).
    public string? RazorpayPaymentLinkId { get; set; }
    public string? PaymentLinkUrl { get; set; }

    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
