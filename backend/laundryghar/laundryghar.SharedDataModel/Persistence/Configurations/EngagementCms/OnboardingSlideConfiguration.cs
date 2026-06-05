using laundryghar.SharedDataModel.Entities.EngagementCms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.EngagementCms;

public sealed class OnboardingSlideConfiguration : IEntityTypeConfiguration<OnboardingSlide>
{
    public void Configure(EntityTypeBuilder<OnboardingSlide> b)
    {
        b.ToTable("onboarding_slides", "engagement_cms");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.AppType).HasColumnName("app_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        b.Property(e => e.TitleLocalized).HasColumnName("title_localized").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.DescriptionLocalized).HasColumnName("description_localized").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.ImageUrl).HasColumnName("image_url").IsRequired();
        b.Property(e => e.ImageDarkUrl).HasColumnName("image_dark_url");
        b.Property(e => e.AnimationUrl).HasColumnName("animation_url");
        b.Property(e => e.CtaText).HasColumnName("cta_text").HasMaxLength(50);
        b.Property(e => e.CtaDeeplink).HasColumnName("cta_deeplink");
        b.Property(e => e.BackgroundColor).HasColumnName("background_color").HasColumnType("character(7)");
        b.Property(e => e.TextColor).HasColumnName("text_color").HasColumnType("character(7)");
        b.Property(e => e.DisplayOrder).HasColumnName("display_order").IsRequired();
        b.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(e => e.ShowFrom).HasColumnName("show_from");
        b.Property(e => e.ShowUntil).HasColumnName("show_until");
        b.Property(e => e.MinAppVersion).HasColumnName("min_app_version").HasMaxLength(20);
        b.Property(e => e.MaxAppVersion).HasColumnName("max_app_version").HasMaxLength(20);
        b.Property(e => e.TargetSegments).HasColumnName("target_segments").HasColumnType("text[]");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasIndex(e => new { e.BrandId, e.AppType, e.DisplayOrder })
            .HasDatabaseName("idx_onbslide_active");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("onboarding_slides_brand_id_fkey");
    }
}
