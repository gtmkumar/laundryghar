using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce.Subscriptions;

public sealed class SubscriptionInvoiceConfiguration : IEntityTypeConfiguration<SubscriptionInvoice>
{
    public void Configure(EntityTypeBuilder<SubscriptionInvoice> b)
    {
        b.ToTable("subscription_invoices", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.CustomerSubscriptionId).HasColumnName("customer_subscription_id").IsRequired();
        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(40).IsRequired();
        b.Property(e => e.BillingPeriodStart).HasColumnName("billing_period_start").IsRequired();
        b.Property(e => e.BillingPeriodEnd).HasColumnName("billing_period_end").IsRequired();
        b.Property(e => e.Subtotal).HasColumnName("subtotal").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.SetupFee).HasColumnName("setup_fee").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.DiscountTotal).HasColumnName("discount_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.TaxableAmount).HasColumnName("taxable_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.OwnsOne(e => e.Tax, TaxBreakdownMapping.MapTax);
        b.Navigation(e => e.Tax).IsRequired();
        b.Property(e => e.TaxTotal).HasColumnName("tax_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.GrandTotal).HasColumnName("grand_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.AmountPaid).HasColumnName("amount_paid").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.AmountDue).HasColumnName("amount_due").HasColumnType("numeric(14,2)")
            .ValueGeneratedOnAddOrUpdate();  // GENERATED ALWAYS AS STORED
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.PaymentId).HasColumnName("payment_id");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.AttemptCount).HasColumnName("attempt_count").IsRequired();
        b.Property(e => e.IssuedAt).HasColumnName("issued_at");
        b.Property(e => e.DueAt).HasColumnName("due_at");
        b.Property(e => e.PaidAt).HasColumnName("paid_at");
        b.Property(e => e.GatewayInvoiceId).HasColumnName("gateway_invoice_id").HasMaxLength(100);
        b.Property(e => e.InvoiceS3Key).HasColumnName("invoice_s3_key");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        b.HasIndex(e => e.InvoiceNumber).IsUnique()
            .HasDatabaseName("subscription_invoices_invoice_number_key");

        b.HasIndex(e => new { e.CustomerSubscriptionId, e.BillingPeriodStart }).IsUnique()
            .HasDatabaseName("subscription_invoices_customer_subscription_id_billing_period_start_key");

        b.HasOne(e => e.CustomerSubscription)
            .WithMany(cs => cs.Invoices)
            .HasForeignKey(e => e.CustomerSubscriptionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("subscription_invoices_customer_subscription_id_fkey");

        b.HasOne(e => e.Payment)
            .WithMany()
            .HasForeignKey(e => e.PaymentId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("subscription_invoices_payment_id_fkey");
    }
}
