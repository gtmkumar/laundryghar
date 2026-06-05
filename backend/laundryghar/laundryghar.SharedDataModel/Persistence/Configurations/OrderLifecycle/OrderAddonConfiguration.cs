using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class OrderAddonConfiguration : IEntityTypeConfiguration<OrderAddon>
{
    public void Configure(EntityTypeBuilder<OrderAddon> b)
    {
        b.ToTable("order_addons", "order_lifecycle");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.OrderId).HasColumnName("order_id").IsRequired();
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at").IsRequired();
        b.Property(e => e.OrderItemId).HasColumnName("order_item_id");
        b.Property(e => e.AddonId).HasColumnName("addon_id").IsRequired();
        b.Property(e => e.AddonNameSnapshot).HasColumnName("addon_name_snapshot").HasMaxLength(200).IsRequired();
        b.Property(e => e.PricingType).HasColumnName("pricing_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Quantity).HasColumnName("quantity").HasColumnType("numeric(10,2)").IsRequired();
        b.Property(e => e.TotalCharge).HasColumnName("total_charge").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        // Composite FK to partitioned orders table
        b.HasOne(e => e.Order)
            .WithMany(o => o.OrderAddons)
            .HasForeignKey(e => new { e.OrderId, e.OrderCreatedAt })
            .HasPrincipalKey(o => new { o.Id, o.CreatedAt })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("order_addons_order_id_fkey");

        b.HasOne(e => e.OrderItem).WithMany(oi => oi.OrderAddons)
            .HasForeignKey(e => e.OrderItemId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("order_addons_order_item_id_fkey");

        b.HasOne(e => e.AddOn).WithMany().HasForeignKey(e => e.AddonId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("order_addons_addon_id_fkey");
    }
}
