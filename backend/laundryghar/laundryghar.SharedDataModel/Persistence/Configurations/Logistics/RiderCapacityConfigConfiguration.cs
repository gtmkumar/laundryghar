using laundryghar.SharedDataModel.Entities.Logistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Logistics;

public sealed class RiderCapacityConfigConfiguration : IEntityTypeConfiguration<RiderCapacityConfig>
{
    public void Configure(EntityTypeBuilder<RiderCapacityConfig> b)
    {
        b.ToTable("rider_capacity_config", "logistics");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.RiderId).HasColumnName("rider_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id");
        b.Property(e => e.DayOfWeek).HasColumnName("day_of_week");
        b.Property(e => e.SlotStart).HasColumnName("slot_start").HasColumnType("time without time zone");
        b.Property(e => e.SlotEnd).HasColumnName("slot_end").HasColumnType("time without time zone");
        b.Property(e => e.MaxPickupsPerSlot).HasColumnName("max_pickups_per_slot").IsRequired();
        b.Property(e => e.MaxDeliveriesPerSlot).HasColumnName("max_deliveries_per_slot").IsRequired();
        b.Property(e => e.MaxConcurrentOrders).HasColumnName("max_concurrent_orders").IsRequired();
        b.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(e => e.EffectiveFrom).HasColumnName("effective_from").IsRequired();
        b.Property(e => e.EffectiveTo).HasColumnName("effective_to");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasOne(e => e.Rider).WithMany(r => r.CapacityConfigs).HasForeignKey(e => e.RiderId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("rider_capacity_config_rider_id_fkey");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("rider_capacity_config_brand_id_fkey");

        b.HasOne(e => e.Store).WithMany().HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("rider_capacity_config_store_id_fkey");
    }
}
