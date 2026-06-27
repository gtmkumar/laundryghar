namespace laundryghar.SharedDataModel.Common;

/// <summary>
/// The shared GST tax breakdown contract, stored as a <c>tax_breakdown</c> jsonb on every invoice
/// table across Orders, Commerce, and Finance. Introduced in multi-vertical Phase 2 (slice 2F) to
/// replace the per-table <c>cgst</c>/<c>sgst</c>/<c>igst</c> columns with ONE shared shape — the
/// "three-way tax coordination" (blueprint §8 Risk #4): landing a single common contract keeps the
/// three tax schemas from diverging.
///
/// <para>Mapped as an EF owned type via <c>ToJson("tax_breakdown")</c>. Amount-only invoices leave
/// the <c>*Rate</c> fields at 0 (they never tracked rates); the orders invoice carries both.</para>
/// </summary>
public class TaxBreakdown
{
    public decimal CgstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstRate { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstRate { get; set; }
    public decimal IgstAmount { get; set; }

    /// <summary>Total tax = CGST + SGST + IGST amounts.</summary>
    public decimal Total => CgstAmount + SgstAmount + IgstAmount;
}
