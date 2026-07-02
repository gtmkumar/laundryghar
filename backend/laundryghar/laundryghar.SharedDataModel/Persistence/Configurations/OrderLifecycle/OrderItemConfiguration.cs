using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("order_items", "order_lifecycle");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.OrderId).HasColumnName("order_id").IsRequired();
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(e => e.LineNumber).HasColumnName("line_number").IsRequired();
        b.Property(e => e.ServiceId).HasColumnName("service_id").IsRequired();
        b.Property(e => e.ItemId).HasColumnName("item_id").IsRequired();
        b.Property(e => e.ItemVariantId).HasColumnName("item_variant_id");
        b.Property(e => e.FabricTypeId).HasColumnName("fabric_type_id");
        b.Property(e => e.PriceListItemId).HasColumnName("price_list_item_id");
        b.Property(e => e.ItemNameSnapshot).HasColumnName("item_name_snapshot").HasMaxLength(200).IsRequired();
        b.Property(e => e.ServiceNameSnapshot).HasColumnName("service_name_snapshot").HasMaxLength(200).IsRequired();
        b.Property(e => e.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.DeclaredValue).HasColumnName("declared_value").HasColumnType("numeric(14,2)");
        b.Property(e => e.AppliedSlabPrice).HasColumnName("applied_slab_price").HasColumnType("numeric(14,2)");
        b.Property(e => e.Quantity).HasColumnName("quantity").HasColumnType("numeric(10,2)").IsRequired();
        b.Property(e => e.UnitOfMeasure).HasColumnName("unit_of_measure").HasMaxLength(10).IsRequired();
        b.Property(e => e.LineSubtotal).HasColumnName("line_subtotal").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.LineDiscount).HasColumnName("line_discount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.LineAddonsTotal).HasColumnName("line_addons_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.LineTax).HasColumnName("line_tax").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.LineTotal).HasColumnName("line_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.IsExpress).HasColumnName("is_express").IsRequired();
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        // Composite FK: order_items(order_id, order_created_at) → orders(id, created_at) ON DELETE CASCADE
        b.HasOne(e => e.Order)
            .WithMany(o => o.OrderItems)
            .HasForeignKey(e => new { e.OrderId, e.OrderCreatedAt })
            .HasPrincipalKey(o => new { o.Id, o.CreatedAt })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("order_items_order_id_fkey");

        b.HasOne(e => e.Store).WithMany().HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("order_items_store_id_fkey");
        b.HasOne(e => e.Service).WithMany().HasForeignKey(e => e.ServiceId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("order_items_service_id_fkey");
        b.HasOne(e => e.Item).WithMany().HasForeignKey(e => e.ItemId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("order_items_item_id_fkey");
        b.HasOne(e => e.ItemVariant).WithMany().HasForeignKey(e => e.ItemVariantId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("order_items_item_variant_id_fkey");
        b.HasOne(e => e.FabricType).WithMany().HasForeignKey(e => e.FabricTypeId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("order_items_fabric_type_id_fkey");
        b.HasOne(e => e.PriceListItem).WithMany().HasForeignKey(e => e.PriceListItemId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("order_items_price_list_item_id_fkey");
    }
}
