using laundryghar.SharedDataModel.Entities.EngagementCms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.EngagementCms;

public sealed class MobileAppConfigConfiguration : IEntityTypeConfiguration<MobileAppConfig>
{
    public void Configure(EntityTypeBuilder<MobileAppConfig> b)
    {
        b.ToTable("mobile_app_config", "engagement_cms");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.AppType).HasColumnName("app_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.Platform).HasColumnName("platform").HasMaxLength(10).IsRequired();
        b.Property(e => e.ConfigKey).HasColumnName("config_key").HasMaxLength(100).IsRequired();
        b.Property(e => e.ConfigValue).HasColumnName("config_value").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.IsForceUpdate).HasColumnName("is_force_update").IsRequired();
        b.Property(e => e.MinAppVersion).HasColumnName("min_app_version").HasMaxLength(20);
        b.Property(e => e.MaxAppVersion).HasColumnName("max_app_version").HasMaxLength(20);
        b.Property(e => e.TargetSegments).HasColumnName("target_segments").HasColumnType("text[]");
        b.Property(e => e.RolloutPercent).HasColumnName("rollout_percent");
        b.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        // Unique: one config value per (brand_id, app_type, platform, config_key)
        b.HasIndex(e => new { e.BrandId, e.AppType, e.Platform, e.ConfigKey })
            .IsUnique()
            .HasDatabaseName("mobile_app_config_brand_id_app_type_platform_config_key_key");

        b.HasIndex(e => new { e.BrandId, e.AppType, e.Platform })
            .HasDatabaseName("idx_mobilecfg_lookup");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("mobile_app_config_brand_id_fkey");
    }
}
