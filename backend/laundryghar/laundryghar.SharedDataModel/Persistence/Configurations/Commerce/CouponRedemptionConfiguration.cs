using laundryghar.SharedDataModel.Entities.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce;

public sealed class CouponRedemptionConfiguration : IEntityTypeConfiguration<CouponRedemption>
{
    public void Configure(EntityTypeBuilder<CouponRedemption> b)
    {
        b.ToTable("coupon_redemptions", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.CouponId).HasColumnName("coupon_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.OrderId).HasColumnName("order_id").IsRequired();
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at").IsRequired();
        b.Property(e => e.CouponCode).HasColumnName("coupon_code").HasMaxLength(50).IsRequired();
        b.Property(e => e.DiscountAmount).HasColumnName("discount_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.OrderSubtotalSnapshot).HasColumnName("order_subtotal_snapshot").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.RedeemedAt).HasColumnName("redeemed_at").IsRequired();
        b.Property(e => e.RevertedAt).HasColumnName("reverted_at");
        b.Property(e => e.RevertedReason).HasColumnName("reverted_reason");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.Coupon)
            .WithMany(c => c.Redemptions)
            .HasForeignKey(e => e.CouponId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("coupon_redemptions_coupon_id_fkey");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("coupon_redemptions_brand_id_fkey");

        b.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("coupon_redemptions_customer_id_fkey");

        // Composite FK to partitioned orders — scalar-only (both columns required on DB)
        b.HasOne<global::laundryghar.SharedDataModel.Entities.OrderLifecycle.Order>()
            .WithMany()
            .HasForeignKey(e => new { e.OrderId, e.OrderCreatedAt })
            .HasPrincipalKey(o => new { o.Id, o.CreatedAt })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("coupon_redemptions_order_id_fkey");
    }
}
