using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class RolePresetConfiguration : IEntityTypeConfiguration<RolePreset>
{
    public void Configure(EntityTypeBuilder<RolePreset> b)
    {
        b.ToTable("role_presets", "identity_access");
        b.HasKey(e => e.Key);
        b.Property(e => e.Key).HasColumnName("key").HasMaxLength(32).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        b.Property(e => e.Does).HasColumnName("does").IsRequired();
        b.Property(e => e.DoesNot).HasColumnName("does_not").IsRequired();
        b.Property(e => e.RoleCode).HasColumnName("role_code").HasMaxLength(50);
        b.Property(e => e.RequiresFeature).HasColumnName("requires_feature").HasMaxLength(64);
        b.Property(e => e.IsPlatform).HasColumnName("is_platform").IsRequired();
        b.Property(e => e.SortOrder).HasColumnName("sort_order").IsRequired();
    }
}

public sealed class PermissionGroupConfiguration : IEntityTypeConfiguration<PermissionGroup>
{
    public void Configure(EntityTypeBuilder<PermissionGroup> b)
    {
        b.ToTable("permission_groups", "identity_access");
        b.HasKey(e => e.Key);
        b.Property(e => e.Key).HasColumnName("key").HasMaxLength(48).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        b.Property(e => e.PermissionModules).HasColumnName("permission_modules").HasColumnType("text[]");
        b.Property(e => e.SortOrder).HasColumnName("sort_order").IsRequired();
    }
}
