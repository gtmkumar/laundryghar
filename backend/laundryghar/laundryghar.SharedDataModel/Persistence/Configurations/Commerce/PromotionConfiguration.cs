using laundryghar.SharedDataModel.Entities.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce;

public sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> b)
    {
        b.ToTable("promotions", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.PromotionType).HasColumnName("promotion_type").HasMaxLength(30).IsRequired();
        b.Property(e => e.TargetAudience).HasColumnName("target_audience").HasMaxLength(30).IsRequired();
        b.Property(e => e.EligibleSegments).HasColumnName("eligible_segments").HasColumnType("text[]");
        b.Property(e => e.Rules).HasColumnName("rules").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.RewardConfig).HasColumnName("reward_config").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CouponId).HasColumnName("coupon_id");
        b.Property(e => e.BannerImageUrl).HasColumnName("banner_image_url");
        b.Property(e => e.DeeplinkUrl).HasColumnName("deeplink_url");
        b.Property(e => e.ValidFrom).HasColumnName("valid_from").IsRequired();
        b.Property(e => e.ValidUntil).HasColumnName("valid_until");
        b.Property(e => e.TotalBudget).HasColumnName("total_budget").HasColumnType("numeric(14,2)");
        b.Property(e => e.SpentBudget).HasColumnName("spent_budget").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.ImpressionsCount).HasColumnName("impressions_count").IsRequired();
        b.Property(e => e.RedemptionsCount).HasColumnName("redemptions_count").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => new { e.BrandId, e.Code })
            .IsUnique()
            .HasDatabaseName("promotions_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("promotions_brand_id_fkey");

        b.HasOne(e => e.Coupon)
            .WithMany(c => c.Promotions)
            .HasForeignKey(e => e.CouponId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("promotions_coupon_id_fkey");
    }
}
