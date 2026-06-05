using laundryghar.SharedDataModel.Entities.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce;

public sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> b)
    {
        b.ToTable("coupons", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.CouponType).HasColumnName("coupon_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.DiscountValue).HasColumnName("discount_value").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.MaxDiscountAmount).HasColumnName("max_discount_amount").HasColumnType("numeric(14,2)");
        b.Property(e => e.MinOrderValue).HasColumnName("min_order_value").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.ApplicableServices).HasColumnName("applicable_services").HasColumnType("uuid[]").IsRequired();
        b.Property(e => e.ApplicableStores).HasColumnName("applicable_stores").HasColumnType("uuid[]").IsRequired();
        b.Property(e => e.ApplicableFranchises).HasColumnName("applicable_franchises").HasColumnType("uuid[]").IsRequired();
        b.Property(e => e.CustomerEligibility).HasColumnName("customer_eligibility").HasMaxLength(30).IsRequired();
        b.Property(e => e.EligibleCustomerIds).HasColumnName("eligible_customer_ids").HasColumnType("uuid[]");
        b.Property(e => e.EligibleSegments).HasColumnName("eligible_segments").HasColumnType("text[]");
        b.Property(e => e.IsFirstOrderOnly).HasColumnName("is_first_order_only").IsRequired();
        b.Property(e => e.IsSingleUsePerCust).HasColumnName("is_single_use_per_cust").IsRequired();
        b.Property(e => e.MaxTotalUses).HasColumnName("max_total_uses");
        b.Property(e => e.MaxUsesPerCustomer).HasColumnName("max_uses_per_customer").IsRequired();
        b.Property(e => e.CurrentUsageCount).HasColumnName("current_usage_count").IsRequired();
        b.Property(e => e.IsStackable).HasColumnName("is_stackable").IsRequired();
        b.Property(e => e.IsPublic).HasColumnName("is_public").IsRequired();
        b.Property(e => e.IsAutoApply).HasColumnName("is_auto_apply").IsRequired();
        b.Property(e => e.ValidFrom).HasColumnName("valid_from").IsRequired();
        b.Property(e => e.ValidUntil).HasColumnName("valid_until");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => new { e.BrandId, e.Code })
            .IsUnique()
            .HasDatabaseName("coupons_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("coupons_brand_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
