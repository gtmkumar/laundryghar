using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class PickupRequestConfiguration : IEntityTypeConfiguration<PickupRequest>
{
    public void Configure(EntityTypeBuilder<PickupRequest> b)
    {
        b.ToTable("pickup_requests", "order_lifecycle");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.RequestNumber).HasColumnName("request_number").HasMaxLength(40).IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id");
        b.Property(e => e.StoreId).HasColumnName("store_id");
        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.AddressId).HasColumnName("address_id").IsRequired();
        b.Property(e => e.PickupSlotId).HasColumnName("pickup_slot_id");
        b.Property(e => e.PickupDate).HasColumnName("pickup_date").IsRequired();
        b.Property(e => e.PickupWindowStart).HasColumnName("pickup_window_start").HasColumnType("time without time zone").IsRequired();
        b.Property(e => e.PickupWindowEnd).HasColumnName("pickup_window_end").HasColumnType("time without time zone").IsRequired();
        b.Property(e => e.IsExpress).HasColumnName("is_express").IsRequired();
        b.Property(e => e.EstimatedItems).HasColumnName("estimated_items");
        b.Property(e => e.EstimatedAmount).HasColumnName("estimated_amount").HasColumnType("numeric(14,2)");
        b.Property(e => e.ServicesRequested).HasColumnName("services_requested").HasColumnType("uuid[]").IsRequired();
        b.Property(e => e.CustomerNotes).HasColumnName("customer_notes");
        b.Property(e => e.RequestedItems).HasColumnName("requested_items").HasColumnType("jsonb").IsRequired()
            .HasDefaultValueSql("'[]'::jsonb");
        b.Property(e => e.PaymentPreference).HasColumnName("payment_preference").HasMaxLength(20).IsRequired()
            .HasDefaultValue("cod");

        b.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(150);
        b.Property(e => e.Source).HasColumnName("source").HasMaxLength(20).IsRequired()
            .HasDefaultValue("app");

        // Coupon code stored at booking; threaded into the order on admin conversion.
        b.Property(e => e.CouponCode).HasColumnName("coupon_code").HasMaxLength(50);

        // Scalar-only composite FK columns to orders — no navigation configured (nullable composite FK)
        b.Property(e => e.ConvertedOrderId).HasColumnName("converted_order_id");
        b.Property(e => e.ConvertedOrderCreatedAt).HasColumnName("converted_order_created_at");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        b.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
        b.Property(e => e.CancelledByType).HasColumnName("cancelled_by_type").HasMaxLength(20);
        b.Property(e => e.CancelledById).HasColumnName("cancelled_by_id");
        b.Property(e => e.RescheduledFromId).HasColumnName("rescheduled_from_id");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => e.RequestNumber).IsUnique().HasDatabaseName("pickup_requests_request_number_key");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("pickup_requests_brand_id_fkey");
        b.HasOne(e => e.Franchise).WithMany().HasForeignKey(e => e.FranchiseId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("pickup_requests_franchise_id_fkey");
        b.HasOne(e => e.Store).WithMany().HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("pickup_requests_store_id_fkey");
        b.HasOne(e => e.Customer).WithMany().HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("pickup_requests_customer_id_fkey");
        b.HasOne(e => e.Address).WithMany().HasForeignKey(e => e.AddressId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("pickup_requests_address_id_fkey");
        b.HasOne(e => e.PickupSlot).WithMany().HasForeignKey(e => e.PickupSlotId)
            .OnDelete(DeleteBehavior.SetNull).HasConstraintName("pickup_requests_pickup_slot_id_fkey");
        // Self-referential FK
        b.HasOne(e => e.RescheduledFrom).WithMany().HasForeignKey(e => e.RescheduledFromId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("pickup_requests_rescheduled_from_id_fkey");
    }
}
