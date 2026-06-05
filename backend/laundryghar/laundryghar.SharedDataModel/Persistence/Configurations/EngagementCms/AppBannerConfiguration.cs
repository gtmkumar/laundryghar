using laundryghar.SharedDataModel.Entities.EngagementCms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.EngagementCms;

public sealed class AppBannerConfiguration : IEntityTypeConfiguration<AppBanner>
{
    public void Configure(EntityTypeBuilder<AppBanner> b)
    {
        b.ToTable("app_banners", "engagement_cms");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.AppType).HasColumnName("app_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.Placement).HasColumnName("placement").HasMaxLength(50).IsRequired();
        b.Property(e => e.Title).HasColumnName("title").HasMaxLength(200);
        b.Property(e => e.TitleLocalized).HasColumnName("title_localized").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Subtitle).HasColumnName("subtitle").HasMaxLength(300);
        b.Property(e => e.SubtitleLocalized).HasColumnName("subtitle_localized").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.ImageUrl).HasColumnName("image_url").IsRequired();
        b.Property(e => e.ImageDarkUrl).HasColumnName("image_dark_url");
        b.Property(e => e.CtaText).HasColumnName("cta_text").HasMaxLength(50);
        b.Property(e => e.CtaDeeplink).HasColumnName("cta_deeplink");
        b.Property(e => e.ExternalUrl).HasColumnName("external_url");
        b.Property(e => e.PromotionId).HasColumnName("promotion_id");
        b.Property(e => e.CouponId).HasColumnName("coupon_id");
        b.Property(e => e.BackgroundColor).HasColumnName("background_color").HasColumnType("character(7)");
        b.Property(e => e.DisplayOrder).HasColumnName("display_order").IsRequired();
        b.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(e => e.ShowFrom).HasColumnName("show_from");
        b.Property(e => e.ShowUntil).HasColumnName("show_until");
        b.Property(e => e.TargetAudience).HasColumnName("target_audience").HasMaxLength(30);
        b.Property(e => e.TargetSegments).HasColumnName("target_segments").HasColumnType("text[]");
        b.Property(e => e.TargetCities).HasColumnName("target_cities").HasColumnType("text[]");
        b.Property(e => e.ImpressionsCount).HasColumnName("impressions_count").IsRequired();
        b.Property(e => e.ClicksCount).HasColumnName("clicks_count").IsRequired();
        b.Property(e => e.MinAppVersion).HasColumnName("min_app_version").HasMaxLength(20);
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasIndex(e => new { e.BrandId, e.Placement, e.DisplayOrder })
            .HasDatabaseName("idx_banner_active");

        b.HasIndex(e => new { e.BrandId, e.ShowFrom, e.ShowUntil })
            .HasDatabaseName("idx_banner_active_range");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("app_banners_brand_id_fkey");

        b.HasOne(e => e.Promotion)
            .WithMany()
            .HasForeignKey(e => e.PromotionId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("app_banners_promotion_id_fkey");

        b.HasOne(e => e.Coupon)
            .WithMany()
            .HasForeignKey(e => e.CouponId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("app_banners_coupon_id_fkey");
    }
}
