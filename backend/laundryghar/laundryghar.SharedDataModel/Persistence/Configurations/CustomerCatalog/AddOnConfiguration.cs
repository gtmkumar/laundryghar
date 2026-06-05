using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.CustomerCatalog;

public sealed class AddOnConfiguration : IEntityTypeConfiguration<AddOn>
{
    public void Configure(EntityTypeBuilder<AddOn> b)
    {
        b.ToTable("add_ons", "customer_catalog");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(e => e.NameLocalized).HasColumnName("name_localized").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.PricingType).HasColumnName("pricing_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.PriceValue).HasColumnName("price_value").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.MinCharge).HasColumnName("min_charge").HasColumnType("numeric(14,2)");
        b.Property(e => e.MaxCharge).HasColumnName("max_charge").HasColumnType("numeric(14,2)");
        b.Property(e => e.ApplicableServices).HasColumnName("applicable_services").HasColumnType("uuid[]").IsRequired();
        b.Property(e => e.ApplicableCategories).HasColumnName("applicable_categories").HasColumnType("uuid[]").IsRequired();
        b.Property(e => e.IsTaxable).HasColumnName("is_taxable").IsRequired();
        b.Property(e => e.TaxRatePercent).HasColumnName("tax_rate_percent").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.RequiresApproval).HasColumnName("requires_approval").IsRequired();
        b.Property(e => e.IconUrl).HasColumnName("icon_url");
        b.Property(e => e.DisplayOrder).HasColumnName("display_order").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => new { e.BrandId, e.Code }).IsUnique().HasDatabaseName("add_ons_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("add_ons_brand_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
