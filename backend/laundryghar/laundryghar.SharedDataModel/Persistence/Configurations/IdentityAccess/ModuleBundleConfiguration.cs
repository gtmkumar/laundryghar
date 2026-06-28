using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class ModuleBundleConfiguration : IEntityTypeConfiguration<ModuleBundle>
{
    public void Configure(EntityTypeBuilder<ModuleBundle> b)
    {
        b.ToTable("module_bundle", "identity_access");
        b.HasKey(e => e.Code);
        b.Property(e => e.Code).HasColumnName("code").IsRequired();
        b.Property(e => e.Name).HasColumnName("name").IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.VerticalKey).HasColumnName("vertical_key").HasMaxLength(20);
        b.Property(e => e.Price).HasColumnName("price").HasColumnType("numeric(14,2)");
        b.Property(e => e.BillingInterval).HasColumnName("billing_interval").HasMaxLength(20);
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)");
        b.Property(e => e.IsPublic).HasColumnName("is_public").HasDefaultValue(true);
        b.HasMany(e => e.Items).WithOne().HasForeignKey(i => i.BundleCode);
    }
}

public sealed class ModuleBundleItemConfiguration : IEntityTypeConfiguration<ModuleBundleItem>
{
    public void Configure(EntityTypeBuilder<ModuleBundleItem> b)
    {
        b.ToTable("module_bundle_item", "identity_access");
        b.HasKey(e => new { e.BundleCode, e.ModuleKey });
        b.Property(e => e.BundleCode).HasColumnName("bundle_code").IsRequired();
        b.Property(e => e.ModuleKey).HasColumnName("module_key").IsRequired();
    }
}
