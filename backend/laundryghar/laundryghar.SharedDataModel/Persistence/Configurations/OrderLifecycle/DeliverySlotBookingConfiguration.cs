using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class DeliverySlotBookingConfiguration : IEntityTypeConfiguration<DeliverySlotBooking>
{
    public void Configure(EntityTypeBuilder<DeliverySlotBooking> b)
    {
        b.ToTable("delivery_slot_bookings", "order_lifecycle");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.SlotId).HasColumnName("slot_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id").IsRequired();
        // Scalar-only composite FK columns to orders (nullable)
        b.Property(e => e.OrderId).HasColumnName("order_id");
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at");
        b.Property(e => e.PickupRequestId).HasColumnName("pickup_request_id");
        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.BookingType).HasColumnName("booking_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.BookedAt).HasColumnName("booked_at").IsRequired();
        b.Property(e => e.CancelledAt).HasColumnName("cancelled_at");
        b.Property(e => e.CancelledReason).HasColumnName("cancelled_reason");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.Slot).WithMany(s => s.Bookings)
            .HasForeignKey(e => e.SlotId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("delivery_slot_bookings_slot_id_fkey");
        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("delivery_slot_bookings_brand_id_fkey");
        b.HasOne(e => e.Store).WithMany().HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("delivery_slot_bookings_store_id_fkey");
        b.HasOne(e => e.Customer).WithMany().HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("delivery_slot_bookings_customer_id_fkey");
        b.HasOne(e => e.PickupRequest).WithMany(p => p.SlotBookings)
            .HasForeignKey(e => e.PickupRequestId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("delivery_slot_bookings_pickup_request_id_fkey");
    }
}
