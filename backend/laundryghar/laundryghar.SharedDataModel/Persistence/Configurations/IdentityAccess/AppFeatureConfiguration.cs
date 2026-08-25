using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class AppFeatureConfiguration : IEntityTypeConfiguration<AppFeature>
{
    public void Configure(EntityTypeBuilder<AppFeature> b)
    {
        b.ToTable("features", "identity_access");

        b.HasKey(e => e.Key);
        b.Property(e => e.Key).HasColumnName("key").HasMaxLength(64).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.VerticalKey).HasColumnName("vertical_key").HasMaxLength(20);
        b.Property(e => e.IsCore).HasColumnName("is_core").IsRequired();
        b.Property(e => e.IsSellable).HasColumnName("is_sellable").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(e => e.SortOrder).HasColumnName("sort_order").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}

public sealed class BrandFeatureConfiguration : IEntityTypeConfiguration<BrandFeature>
{
    public void Configure(EntityTypeBuilder<BrandFeature> b)
    {
        b.ToTable("brand_feature", "identity_access");

        b.HasKey(e => new { e.BrandId, e.FeatureKey });
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FeatureKey).HasColumnName("feature_key").HasMaxLength(64).IsRequired();
        b.Property(e => e.Enabled).HasColumnName("enabled").IsRequired();
        b.Property(e => e.ValidUntil).HasColumnName("valid_until");
        b.Property(e => e.Source).HasColumnName("source").HasMaxLength(32).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => e.BrandId);
    }
}
