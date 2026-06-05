using laundryghar.SharedDataModel.Entities.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce;

public sealed class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> b)
    {
        b.ToTable("packages", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(e => e.NameLocalized).HasColumnName("name_localized").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Tier).HasColumnName("tier").HasMaxLength(30).IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.Price).HasColumnName("price").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.CreditValue).HasColumnName("credit_value").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.DiscountPercent).HasColumnName("discount_percent").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.CreditMultiplier).HasColumnName("credit_multiplier").HasColumnType("numeric(4,2)").IsRequired();
        b.Property(e => e.ValidityDays).HasColumnName("validity_days");
        b.Property(e => e.IsUnlimitedValidity).HasColumnName("is_unlimited_validity").IsRequired();
        b.Property(e => e.ApplicableServices).HasColumnName("applicable_services").HasColumnType("uuid[]").IsRequired();
        b.Property(e => e.ExcludedServices).HasColumnName("excluded_services").HasColumnType("uuid[]").IsRequired();
        b.Property(e => e.MinimumOrderValue).HasColumnName("minimum_order_value").HasColumnType("numeric(14,2)");
        b.Property(e => e.MaxUsagePerOrder).HasColumnName("max_usage_per_order").HasColumnType("numeric(14,2)");
        b.Property(e => e.MaxPurchasesPerCust).HasColumnName("max_purchases_per_cust");
        b.Property(e => e.IconUrl).HasColumnName("icon_url");
        b.Property(e => e.ColorHex).HasColumnName("color_hex").HasColumnType("character(7)");
        b.Property(e => e.DisplayOrder).HasColumnName("display_order").IsRequired();
        b.Property(e => e.IsFeatured).HasColumnName("is_featured").IsRequired();
        b.Property(e => e.TermsAndConditions).HasColumnName("terms_and_conditions");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.AvailableFrom).HasColumnName("available_from");
        b.Property(e => e.AvailableTo).HasColumnName("available_to");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => new { e.BrandId, e.Code })
            .IsUnique()
            .HasDatabaseName("packages_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("packages_brand_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
