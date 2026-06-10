using laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.FinanceRoyalty.Subscriptions;

public sealed class FranchiseSubscriptionInvoiceConfiguration : IEntityTypeConfiguration<FranchiseSubscriptionInvoice>
{
    public void Configure(EntityTypeBuilder<FranchiseSubscriptionInvoice> b)
    {
        b.ToTable("franchise_subscription_invoices", "finance_royalty");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id").IsRequired();
        b.Property(e => e.FranchiseSubscriptionId).HasColumnName("franchise_subscription_id").IsRequired();
        b.Property(e => e.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(40).IsRequired();
        b.Property(e => e.BillingPeriodStart).HasColumnName("billing_period_start").IsRequired();
        b.Property(e => e.BillingPeriodEnd).HasColumnName("billing_period_end").IsRequired();
        b.Property(e => e.BaseAmount).HasColumnName("base_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.OverageAmount).HasColumnName("overage_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.SetupFee).HasColumnName("setup_fee").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.DiscountTotal).HasColumnName("discount_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Subtotal).HasColumnName("subtotal").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Cgst).HasColumnName("cgst").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Sgst).HasColumnName("sgst").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Igst).HasColumnName("igst").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.TaxTotal).HasColumnName("tax_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.GrandTotal).HasColumnName("grand_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.AmountPaid).HasColumnName("amount_paid").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.AmountDue).HasColumnName("amount_due").HasColumnType("numeric(14,2)")
            .ValueGeneratedOnAddOrUpdate(); // GENERATED ALWAYS AS STORED
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.UsageSnapshot).HasColumnName("usage_snapshot").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.PaymentId).HasColumnName("payment_id");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.AttemptCount).HasColumnName("attempt_count").IsRequired();
        b.Property(e => e.IssuedAt).HasColumnName("issued_at");
        b.Property(e => e.DueAt).HasColumnName("due_at");
        b.Property(e => e.PaidAt).HasColumnName("paid_at");
        b.Property(e => e.InvoiceS3Key).HasColumnName("invoice_s3_key");
        b.Property(e => e.InvoicePdfUrl).HasColumnName("invoice_pdf_url");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasIndex(e => e.InvoiceNumber).IsUnique()
            .HasDatabaseName("franchise_subscription_invoices_invoice_number_key");

        b.HasIndex(e => new { e.FranchiseSubscriptionId, e.BillingPeriodStart }).IsUnique()
            .HasDatabaseName("franchise_subscription_invoices_franchise_subscription_id_billing_period_start_key");

        b.HasOne(e => e.FranchiseSubscription)
            .WithMany(fs => fs.Invoices)
            .HasForeignKey(e => e.FranchiseSubscriptionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("franchise_subscription_invoices_franchise_subscription_id_fkey");
    }
}
