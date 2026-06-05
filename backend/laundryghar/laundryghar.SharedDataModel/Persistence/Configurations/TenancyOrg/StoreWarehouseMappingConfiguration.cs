using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.TenancyOrg;

public sealed class StoreWarehouseMappingConfiguration : IEntityTypeConfiguration<StoreWarehouseMapping>
{
    public void Configure(EntityTypeBuilder<StoreWarehouseMapping> b)
    {
        b.ToTable("store_warehouse_mappings", "tenancy_org");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(e => e.WarehouseId).HasColumnName("warehouse_id").IsRequired();
        b.Property(e => e.IsPrimary).HasColumnName("is_primary").IsRequired();
        b.Property(e => e.ServiceTypes).HasColumnName("service_types").HasColumnType("text[]").IsRequired();
        b.Property(e => e.Priority).HasColumnName("priority").IsRequired();
        b.Property(e => e.CutoffTime).HasColumnName("cutoff_time").HasColumnType("time without time zone");
        b.Property(e => e.TravelTimeMinutes).HasColumnName("travel_time_minutes");
        b.Property(e => e.DistanceKm).HasColumnName("distance_km").HasColumnType("numeric(6,2)");
        b.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(e => e.EffectiveFrom).HasColumnName("effective_from").IsRequired();
        b.Property(e => e.EffectiveTo).HasColumnName("effective_to");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        // Unique index — service_types is an array; EF maps it but index is for reference only
        b.HasIndex(e => new { e.StoreId, e.WarehouseId, e.ServiceTypes })
            .IsUnique()
            .HasDatabaseName("store_warehouse_mappings_store_id_warehouse_id_service_type_key");

        b.HasOne(e => e.Store)
            .WithMany(s => s.StoreWarehouseMappings)
            .HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("store_warehouse_mappings_store_id_fkey");

        b.HasOne(e => e.Warehouse)
            .WithMany(w => w.StoreWarehouseMappings)
            .HasForeignKey(e => e.WarehouseId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("store_warehouse_mappings_warehouse_id_fkey");
    }
}
