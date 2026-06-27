using laundryghar.SharedDataModel.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations;

/// <summary>
/// Shared EF mapping for the <see cref="TaxBreakdown"/> owned type → the <c>tax_breakdown</c> jsonb
/// column. Used identically by every invoice table across Orders/Commerce/Finance so the three tax
/// schemas stay in lockstep (multi-vertical Phase 2 / slice 2F, blueprint §8 Risk #4).
/// </summary>
public static class TaxBreakdownMapping
{
    public static void MapTax<TOwner>(OwnedNavigationBuilder<TOwner, TaxBreakdown> a) where TOwner : class
    {
        a.ToJson("tax_breakdown");
        a.Property(x => x.CgstRate).HasJsonPropertyName("cgst_rate");
        a.Property(x => x.CgstAmount).HasJsonPropertyName("cgst_amount");
        a.Property(x => x.SgstRate).HasJsonPropertyName("sgst_rate");
        a.Property(x => x.SgstAmount).HasJsonPropertyName("sgst_amount");
        a.Property(x => x.IgstRate).HasJsonPropertyName("igst_rate");
        a.Property(x => x.IgstAmount).HasJsonPropertyName("igst_amount");
    }
}
