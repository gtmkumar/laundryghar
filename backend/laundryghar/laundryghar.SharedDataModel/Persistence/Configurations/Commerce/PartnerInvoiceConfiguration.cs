using laundryghar.SharedDataModel.Entities.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce;

/// <summary>EF mapping for <see cref="PartnerInvoice"/> → commerce.partner_invoices. Mirrors
/// <c>FranchiseSubscriptionInvoiceConfiguration</c> (tax_breakdown owned jsonb, generated amount_due),
/// but keyed by <c>partner_id</c> (scalar cross-BC — no navigation/FK).</summary>
public sealed class PartnerInvoiceConfiguration : IEntityTypeConfiguration<PartnerInvoice>
{
    public void Configure(EntityTypeBuilder<PartnerInvoice> b)
    {
        b.ToTable("partner_invoices", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        // partner_id is a SCALAR cross-BC key → logistics.partners(id): no navigation, no FK.
        b.Property(e => e.PartnerId).HasColumnName("partner_id").IsRequired();
        b.Property(e => e.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(40).IsRequired();
        b.Property(e => e.BillingPeriodStart).HasColumnName("billing_period_start").IsRequired();
        b.Property(e => e.BillingPeriodEnd).HasColumnName("billing_period_end").IsRequired();
        b.Property(e => e.LineItems).HasColumnName("line_items").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Subtotal).HasColumnName("subtotal").HasColumnType("numeric(14,2)").IsRequired();

        b.OwnsOne(e => e.Tax, TaxBreakdownMapping.MapTax);
        b.Navigation(e => e.Tax).IsRequired();

        b.Property(e => e.TaxTotal).HasColumnName("tax_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.GrandTotal).HasColumnName("grand_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.AmountPaid).HasColumnName("amount_paid").HasColumnType("numeric(14,2)").IsRequired();
        // GENERATED ALWAYS AS (grand_total - amount_paid) STORED — read-only; EF must never write it.
        b.Property(e => e.AmountDue).HasColumnName("amount_due").HasColumnType("numeric(14,2)")
            .ValueGeneratedOnAddOrUpdate();
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.InvoicePdfUrl).HasColumnName("invoice_pdf_url").HasMaxLength(512);
        b.Property(e => e.RazorpayPaymentLinkId).HasColumnName("razorpay_payment_link_id").HasMaxLength(64);
        b.Property(e => e.PaymentLinkUrl).HasColumnName("payment_link_url").HasMaxLength(512);
        b.Property(e => e.IssuedAt).HasColumnName("issued_at");
        b.Property(e => e.DueAt).HasColumnName("due_at");
        b.Property(e => e.PaidAt).HasColumnName("paid_at");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => e.InvoiceNumber).IsUnique()
            .HasDatabaseName("partner_invoices_invoice_number_key");

        // Query path: a partner's invoices, newest period first.
        b.HasIndex(e => e.PartnerId).HasDatabaseName("idx_partner_invoices_partner");
    }
}
