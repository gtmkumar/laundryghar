namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>
/// Immutable GST tax invoice record for a single order (order_lifecycle.invoices).
/// Generated on demand (never auto-generated); all supplier/customer/line data is
/// snapshotted at generation time so later changes to franchises/customers do not
/// alter historical invoices.
///
/// Tax treatment:
///   Intra-state:  CGST 9% + SGST 9%  (default)
///   Inter-state:  IGST 18%            (CgstAmount = SgstAmount = 0; IgstAmount carries full tax)
///   SAC code 999712 — Laundry / dry-cleaning services.
/// </summary>
public class Invoice
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }

    /// <summary>One-to-one FK to order_lifecycle.orders.</summary>
    public Guid OrderId { get; set; }

    /// <summary>INV-&lt;FY&gt;-&lt;storeCode&gt;-&lt;NNNNNN&gt; e.g. INV-2627-LGG-S45-001-000001</summary>
    public string InvoiceNumber { get; set; } = null!;

    public DateOnly InvoiceDate { get; set; }

    // ── Supplier snapshot ─────────────────────────────────────────────────────
    public string SupplierName { get; set; } = null!;
    public string SupplierAddress { get; set; } = null!;

    /// <summary>Franchise GSTIN at time of generation. Null for unregistered suppliers.</summary>
    public string? SupplierGstin { get; set; }

    // ── Customer snapshot ─────────────────────────────────────────────────────
    public string CustomerName { get; set; } = null!;
    public string CustomerPhone { get; set; } = null!;

    /// <summary>B2B: customer GSTIN. Null for B2C.</summary>
    public string? CustomerGstin { get; set; }

    // ── GST classification ────────────────────────────────────────────────────
    /// <summary>State of customer address; used to determine CGST/SGST vs IGST.</summary>
    public string PlaceOfSupply { get; set; } = null!;

    /// <summary>SAC 999712 — Laundry / dry-cleaning services.</summary>
    public string SacCode { get; set; } = "999712";

    /// <summary>JSONB snapshot of line items: [{description,qty,unit_price,taxable_value}].</summary>
    public string LineItems { get; set; } = "[]";

    // ── Totals ────────────────────────────────────────────────────────────────
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxableTotal { get; set; }
    public decimal CgstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstRate { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstRate { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }

    /// <summary>issued | cancelled</summary>
    public string Status { get; set; } = "issued";

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
