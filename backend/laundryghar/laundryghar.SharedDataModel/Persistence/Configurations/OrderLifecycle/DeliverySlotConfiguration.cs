using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class DeliverySlotConfiguration : IEntityTypeConfiguration<DeliverySlot>
{
    public void Configure(EntityTypeBuilder<DeliverySlot> b)
    {
        b.ToTable("delivery_slots", "order_lifecycle");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(e => e.SlotDate).HasColumnName("slot_date").IsRequired();
        b.Property(e => e.SlotStart).HasColumnName("slot_start").HasColumnType("time without time zone").IsRequired();
        b.Property(e => e.SlotEnd).HasColumnName("slot_end").HasColumnType("time without time zone").IsRequired();
        b.Property(e => e.SlotType).HasColumnName("slot_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.Capacity).HasColumnName("capacity").IsRequired();
        b.Property(e => e.BookedCount).HasColumnName("booked_count").IsRequired();
        b.Property(e => e.IsExpress).HasColumnName("is_express").IsRequired();
        b.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(e => e.CutoffAt).HasColumnName("cutoff_at");
        b.Property(e => e.Notes).HasColumnName("notes").HasMaxLength(255);
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasIndex(e => new { e.StoreId, e.SlotDate, e.SlotStart, e.SlotType })
            .IsUnique().HasDatabaseName("delivery_slots_store_id_slot_date_slot_start_slot_type_key");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("delivery_slots_brand_id_fkey");
        b.HasOne(e => e.Store).WithMany().HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("delivery_slots_store_id_fkey");
    }
}
