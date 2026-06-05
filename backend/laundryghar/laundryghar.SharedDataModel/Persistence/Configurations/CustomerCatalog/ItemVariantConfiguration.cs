using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.CustomerCatalog;

public sealed class ItemVariantConfiguration : IEntityTypeConfiguration<ItemVariant>
{
    public void Configure(EntityTypeBuilder<ItemVariant> b)
    {
        b.ToTable("item_variants", "customer_catalog");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.ItemId).HasColumnName("item_id").IsRequired();
        b.Property(e => e.FabricTypeId).HasColumnName("fabric_type_id");
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.VariantName).HasColumnName("variant_name").HasMaxLength(100).IsRequired();
        b.Property(e => e.Side).HasColumnName("side").HasMaxLength(10);
        b.Property(e => e.Size).HasColumnName("size").HasMaxLength(20);
        b.Property(e => e.Color).HasColumnName("color").HasMaxLength(50);
        b.Property(e => e.Sku).HasColumnName("sku").HasMaxLength(50);
        b.Property(e => e.Barcode).HasColumnName("barcode").HasMaxLength(50);
        b.Property(e => e.DisplayOrder).HasColumnName("display_order").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => new { e.BrandId, e.Code }).IsUnique().HasDatabaseName("item_variants_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("item_variants_brand_id_fkey");

        b.HasOne(e => e.Item)
            .WithMany(i => i.ItemVariants)
            .HasForeignKey(e => e.ItemId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("item_variants_item_id_fkey");

        b.HasOne(e => e.FabricType)
            .WithMany(f => f.ItemVariants)
            .HasForeignKey(e => e.FabricTypeId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("item_variants_fabric_type_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
