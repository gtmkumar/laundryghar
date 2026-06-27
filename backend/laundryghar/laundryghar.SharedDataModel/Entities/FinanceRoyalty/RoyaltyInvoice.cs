using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.FinanceRoyalty;

/// <summary>Monthly royalty invoice issued to a franchisee (finance_royalty.royalty_invoices).
/// Has created_at, updated_at, created_by, updated_by — NO version.
/// UNIQUE(franchise_id, period_start, period_end) — one invoice per franchise per billing period.
/// UNIQUE(invoice_number).
/// amount_due is a generated column: grand_total - amount_paid — read-only.
/// currency_code is character(3) — fixed-length.</summary>
public class RoyaltyInvoice
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid FranchiseId { get; set; }
    public Guid? FranchiseAgreementId { get; set; }
    public string InvoiceNumber { get; set; } = null!;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal GrossRevenue { get; set; }
    public decimal EligibleRevenue { get; set; }
    public decimal RoyaltyPercent { get; set; }
    public decimal RoyaltyAmount { get; set; }
    public decimal MarketingFeePercent { get; set; }
    public decimal MarketingFeeAmount { get; set; }
    public decimal TechnologyFeeAmount { get; set; }
    public decimal OtherCharges { get; set; }
    public decimal Adjustments { get; set; }
    public decimal Subtotal { get; set; }
    /// <summary>Shared GST breakdown (tax_breakdown jsonb) — multi-vertical Phase 2 slice 2F.</summary>
    public TaxBreakdown Tax { get; set; } = new();
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal AmountPaid { get; set; }

    /// <summary>Generated column: grand_total - amount_paid. Read-only — do not set.</summary>
    public decimal? AmountDue { get; set; }

    /// <summary>character(3) fixed-length currency code.</summary>
    public string CurrencyCode { get; set; } = null!;

    public int TotalOrders { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? InvoiceS3Key { get; set; }
    public string? InvoicePdfUrl { get; set; }

    /// <summary>jsonb — line items array.</summary>
    public string LineItems { get; set; } = null!;

    public string? Notes { get; set; }

    /// <summary>CHECK: draft, issued, sent, viewed, partial, paid, overdue, disputed, void.</summary>
    public string Status { get; set; } = null!;

    public DateTimeOffset? DisputedAt { get; set; }
    public string? DisputeReason { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>jsonb — arbitrary metadata.</summary>
    public string Metadata { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Franchise Franchise { get; set; } = null!;
    public FranchiseAgreement? FranchiseAgreement { get; set; }
    public ICollection<RoyaltyCalculation> Calculations { get; set; } = [];
}
