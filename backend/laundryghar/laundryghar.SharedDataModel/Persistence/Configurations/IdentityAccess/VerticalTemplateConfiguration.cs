using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class VerticalTemplateConfiguration : IEntityTypeConfiguration<VerticalTemplate>
{
    public void Configure(EntityTypeBuilder<VerticalTemplate> b)
    {
        b.ToTable("vertical_templates", "identity_access");
        b.HasKey(e => e.Key);
        b.Property(e => e.Key).HasColumnName("key").HasMaxLength(48).IsRequired();
        b.Property(e => e.VerticalKey).HasColumnName("vertical_key").HasMaxLength(20).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.FulfillmentMode).HasColumnName("fulfillment_mode").HasMaxLength(20).IsRequired();
        b.Property(e => e.DefaultBundleCode).HasColumnName("default_bundle_code");
        b.Property(e => e.CatalogSeed).HasColumnName("catalog_seed").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.IsPublic).HasColumnName("is_public").IsRequired();
        b.Property(e => e.SortOrder).HasColumnName("sort_order").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
