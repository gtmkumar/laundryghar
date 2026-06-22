using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class GarmentConfiguration : IEntityTypeConfiguration<Garment>
{
    public void Configure(EntityTypeBuilder<Garment> b)
    {
        b.ToTable("garments", "order_lifecycle");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
        b.Property(e => e.OrderId).HasColumnName("order_id").IsRequired();
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at").IsRequired();
        b.Property(e => e.OrderItemId).HasColumnName("order_item_id").IsRequired();
        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.TagCode).HasColumnName("tag_code").HasMaxLength(50).IsRequired();
        b.Property(e => e.SecondaryTagCode).HasColumnName("secondary_tag_code").HasMaxLength(50);
        b.Property(e => e.ItemId).HasColumnName("item_id");
        b.Property(e => e.ItemVariantId).HasColumnName("item_variant_id");
        b.Property(e => e.ItemGroupId).HasColumnName("item_group_id");
        b.Property(e => e.FabricTypeId).HasColumnName("fabric_type_id");
        b.Property(e => e.Color).HasColumnName("color").HasMaxLength(50);
        b.Property(e => e.BrandName).HasColumnName("brand_name").HasMaxLength(100);
        b.Property(e => e.Size).HasColumnName("size").HasMaxLength(20);
        b.Property(e => e.WeightGrams).HasColumnName("weight_grams");
        b.Property(e => e.HasOrnaments).HasColumnName("has_ornaments").IsRequired();
        b.Property(e => e.HasLining).HasColumnName("has_lining").IsRequired();
        b.Property(e => e.IsDesignerWear).HasColumnName("is_designer_wear").IsRequired();
        b.Property(e => e.DeclaredValue).HasColumnName("declared_value").HasColumnType("numeric(14,2)");
        b.Property(e => e.CurrentStage).HasColumnName("current_stage").HasMaxLength(30).IsRequired();
        b.Property(e => e.CurrentLocationType).HasColumnName("current_location_type").HasMaxLength(20);
        b.Property(e => e.CurrentLocationId).HasColumnName("current_location_id");
        b.Property(e => e.CurrentBatchId).HasColumnName("current_batch_id");
        b.Property(e => e.LastScannedAt).HasColumnName("last_scanned_at");
        b.Property(e => e.LastScannedBy).HasColumnName("last_scanned_by");
        b.Property(e => e.ExpectedCompletionAt).HasColumnName("expected_completion_at");
        b.Property(e => e.ActualCompletionAt).HasColumnName("actual_completion_at");
        b.Property(e => e.RewashCount).HasColumnName("rewash_count").IsRequired();
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.CareInstructions).HasColumnName("care_instructions");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasIndex(e => e.TagCode).IsUnique().HasDatabaseName("garments_tag_code_key");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("garments_brand_id_fkey");
        b.HasOne(e => e.Franchise).WithMany().HasForeignKey(e => e.FranchiseId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("garments_franchise_id_fkey");
        b.HasOne(e => e.Store).WithMany().HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("garments_store_id_fkey");
        b.HasOne(e => e.Warehouse).WithMany().HasForeignKey(e => e.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("garments_warehouse_id_fkey");

        // Composite FK to partitioned orders table. No inverse navigation: the Order aggregate
        // is deliberately decoupled from the laundry-fulfilment Garment tree (multi-vertical
        // Phase 1 / Slice C). The FK column + constraint are unchanged; garments are reached via
        // the _db.Garments DbSet keyed by OrderId, never through Order.Garments.
        b.HasOne(e => e.Order)
            .WithMany()
            .HasForeignKey(e => new { e.OrderId, e.OrderCreatedAt })
            .HasPrincipalKey(o => new { o.Id, o.CreatedAt })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("garments_order_id_fkey");

        b.HasOne(e => e.OrderItem).WithMany(oi => oi.Garments)
            .HasForeignKey(e => e.OrderItemId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("garments_order_item_id_fkey");
        b.HasOne(e => e.Customer).WithMany().HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("garments_customer_id_fkey");
        b.HasOne(e => e.Item).WithMany().HasForeignKey(e => e.ItemId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("garments_item_id_fkey");
        b.HasOne(e => e.ItemVariant).WithMany().HasForeignKey(e => e.ItemVariantId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("garments_item_variant_id_fkey");
        b.HasOne(e => e.ItemGroup).WithMany().HasForeignKey(e => e.ItemGroupId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("garments_item_group_id_fkey");
        b.HasOne(e => e.FabricType).WithMany().HasForeignKey(e => e.FabricTypeId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("garments_fabric_type_id_fkey");
        b.HasOne(e => e.CurrentBatch).WithMany(wb => wb.Garments)
            .HasForeignKey(e => e.CurrentBatchId)
            .OnDelete(DeleteBehavior.SetNull).HasConstraintName("garments_current_batch_id_fkey");
    }
}
