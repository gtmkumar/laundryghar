using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.CustomerCatalog;

public sealed class PriceListItemConfiguration : IEntityTypeConfiguration<PriceListItem>
{
    public void Configure(EntityTypeBuilder<PriceListItem> b)
    {
        b.ToTable("price_list_items", "customer_catalog");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.PriceListId).HasColumnName("price_list_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.ServiceId).HasColumnName("service_id").IsRequired();
        b.Property(e => e.ItemId).HasColumnName("item_id").IsRequired();
        b.Property(e => e.ItemVariantId).HasColumnName("item_variant_id");
        b.Property(e => e.FabricTypeId).HasColumnName("fabric_type_id");
        b.Property(e => e.ItemGroupId).HasColumnName("item_group_id");
        b.Property(e => e.BasePrice).HasColumnName("base_price").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.ExpressPrice).HasColumnName("express_price").HasColumnType("numeric(14,2)");
        b.Property(e => e.MinimumQuantity).HasColumnName("minimum_quantity").IsRequired();
        b.Property(e => e.TaxRatePercent).HasColumnName("tax_rate_percent").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.IsTaxable).HasColumnName("is_taxable").IsRequired();
        b.Property(e => e.DisplayLabel).HasColumnName("display_label").HasMaxLength(200);
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasIndex(e => new { e.PriceListId, e.ServiceId, e.ItemId, e.ItemVariantId, e.FabricTypeId })
            .IsUnique()
            .HasDatabaseName("price_list_items_price_list_id_service_id_item_id_item_vari_key");

        b.HasOne(e => e.PriceList)
            .WithMany(pl => pl.PriceListItems)
            .HasForeignKey(e => e.PriceListId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("price_list_items_price_list_id_fkey");

        b.HasOne(e => e.Service)
            .WithMany(s => s.PriceListItems)
            .HasForeignKey(e => e.ServiceId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("price_list_items_service_id_fkey");

        b.HasOne(e => e.Item)
            .WithMany(i => i.PriceListItems)
            .HasForeignKey(e => e.ItemId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("price_list_items_item_id_fkey");

        b.HasOne(e => e.ItemVariant)
            .WithMany(v => v.PriceListItems)
            .HasForeignKey(e => e.ItemVariantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("price_list_items_item_variant_id_fkey");

        b.HasOne(e => e.FabricType)
            .WithMany(f => f.PriceListItems)
            .HasForeignKey(e => e.FabricTypeId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("price_list_items_fabric_type_id_fkey");

        b.HasOne(e => e.ItemGroup)
            .WithMany(g => g.PriceListItems)
            .HasForeignKey(e => e.ItemGroupId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("price_list_items_item_group_id_fkey");
    }
}
