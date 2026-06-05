using laundryghar.SharedDataModel.Entities.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce;

public sealed class CustomerPackageConfiguration : IEntityTypeConfiguration<CustomerPackage>
{
    public void Configure(EntityTypeBuilder<CustomerPackage> b)
    {
        b.ToTable("customer_packages", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.PackageId).HasColumnName("package_id").IsRequired();
        b.Property(e => e.PurchaseOrderId).HasColumnName("purchase_order_id");
        b.Property(e => e.PurchaseOrderCreatedAt).HasColumnName("purchase_order_created_at");
        b.Property(e => e.PaymentId).HasColumnName("payment_id");
        b.Property(e => e.PurchaseAmount).HasColumnName("purchase_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.CreditValueTotal).HasColumnName("credit_value_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.CreditValueUsed).HasColumnName("credit_value_used").HasColumnType("numeric(14,2)").IsRequired();
        // GENERATED ALWAYS column — read-only; EF must never write it
        b.Property(e => e.CreditValueRemaining).HasColumnName("credit_value_remaining").HasColumnType("numeric(14,2)")
            .ValueGeneratedOnAddOrUpdate();
        b.Property(e => e.ActivatedAt).HasColumnName("activated_at").IsRequired();
        b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
        b.Property(e => e.IsUnlimitedValidity).HasColumnName("is_unlimited_validity").IsRequired();
        b.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
        b.Property(e => e.UsageCount).HasColumnName("usage_count").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.SuspendedAt).HasColumnName("suspended_at");
        b.Property(e => e.SuspendedReason).HasColumnName("suspended_reason");
        b.Property(e => e.RefundedAt).HasColumnName("refunded_at");
        b.Property(e => e.RefundedAmount).HasColumnName("refunded_amount").HasColumnType("numeric(14,2)");
        b.Property(e => e.RefundReason).HasColumnName("refund_reason");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("customer_packages_brand_id_fkey");

        b.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("customer_packages_customer_id_fkey");

        b.HasOne(e => e.Package)
            .WithMany(p => p.CustomerPackages)
            .HasForeignKey(e => e.PackageId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("customer_packages_package_id_fkey");

        // Composite FK to partitioned orders table — both columns required on DB side; scalar-nav only via HasForeignKey composite
        b.HasOne<global::laundryghar.SharedDataModel.Entities.OrderLifecycle.Order>()
            .WithMany()
            .HasForeignKey(e => new { e.PurchaseOrderId, e.PurchaseOrderCreatedAt })
            .HasPrincipalKey(o => new { o.Id, o.CreatedAt })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("customer_packages_purchase_order_id_fkey");

        b.HasOne(e => e.Payment)
            .WithMany(p => p.CustomerPackages)
            .HasForeignKey(e => e.PaymentId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("customer_packages_payment_id_fkey");
    }
}
