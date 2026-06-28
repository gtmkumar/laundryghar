using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class BrandPlatformSubscriptionConfiguration : IEntityTypeConfiguration<BrandPlatformSubscription>
{
    public void Configure(EntityTypeBuilder<BrandPlatformSubscription> b)
    {
        b.ToTable("brand_platform_subscription", "identity_access");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.BundleCode).HasColumnName("bundle_code").HasMaxLength(50).IsRequired();
        b.Property(e => e.PlanName).HasColumnName("plan_name").HasMaxLength(100).IsRequired();
        b.Property(e => e.Price).HasColumnName("price").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.BillingInterval).HasColumnName("billing_interval").HasMaxLength(20).IsRequired();
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CurrentPeriodStart).HasColumnName("current_period_start").IsRequired();
        b.Property(e => e.CurrentPeriodEnd).HasColumnName("current_period_end").IsRequired();
        b.Property(e => e.NextBillingAt).HasColumnName("next_billing_at").IsRequired();
        b.Property(e => e.AutoRenew).HasColumnName("auto_renew").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        // One platform subscription per brand.
        b.HasIndex(e => e.BrandId).IsUnique().HasDatabaseName("brand_platform_subscription_brand_id_key");
    }
}

public sealed class BrandPlatformInvoiceConfiguration : IEntityTypeConfiguration<BrandPlatformInvoice>
{
    public void Configure(EntityTypeBuilder<BrandPlatformInvoice> b)
    {
        b.ToTable("brand_platform_invoice", "identity_access");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.SubscriptionId).HasColumnName("subscription_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.BillingPeriodStart).HasColumnName("billing_period_start").IsRequired();
        b.Property(e => e.BillingPeriodEnd).HasColumnName("billing_period_end").IsRequired();
        b.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.IssuedAt).HasColumnName("issued_at").IsRequired();
        b.Property(e => e.DueAt).HasColumnName("due_at").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.RazorpayPaymentLinkId).HasColumnName("razorpay_payment_link_id").HasMaxLength(64);
        b.Property(e => e.PaymentLinkUrl).HasColumnName("payment_link_url").HasMaxLength(512);

        // Idempotency: one invoice per subscription per billing period.
        b.HasIndex(e => new { e.SubscriptionId, e.BillingPeriodStart })
            .IsUnique().HasDatabaseName("brand_platform_invoice_period_key");

        // Tell EF about the FK dependency so it orders inserts (subscription before its invoices)
        // within a single SaveChanges. No navigation properties — just the relationship.
        b.HasOne<BrandPlatformSubscription>()
            .WithMany()
            .HasForeignKey(e => e.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
