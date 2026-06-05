using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.CustomerCatalog;

public sealed class FabricTypeConfiguration : IEntityTypeConfiguration<FabricType>
{
    public void Configure(EntityTypeBuilder<FabricType> b)
    {
        b.ToTable("fabric_types", "customer_catalog");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(e => e.NameLocalized).HasColumnName("name_localized").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.CareInstructions).HasColumnName("care_instructions");
        b.Property(e => e.PriceMultiplier).HasColumnName("price_multiplier").HasColumnType("numeric(4,2)").IsRequired();
        b.Property(e => e.RequiresSpecialCare).HasColumnName("requires_special_care").IsRequired();
        b.Property(e => e.DisplayOrder).HasColumnName("display_order").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => new { e.BrandId, e.Code }).IsUnique().HasDatabaseName("fabric_types_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fabric_types_brand_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
