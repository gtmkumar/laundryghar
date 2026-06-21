using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class BrandModuleConfiguration : IEntityTypeConfiguration<BrandModule>
{
    public void Configure(EntityTypeBuilder<BrandModule> b)
    {
        b.ToTable("brand_module", "identity_access");

        b.HasKey(e => new { e.BrandId, e.ModuleKey });
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.ModuleKey).HasColumnName("module_key").IsRequired();
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
