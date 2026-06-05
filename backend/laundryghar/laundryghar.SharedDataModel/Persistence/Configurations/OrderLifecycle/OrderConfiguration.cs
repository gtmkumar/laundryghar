using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders", "order_lifecycle");

        // Composite PK required by PG range partitioning on created_at
        b.HasKey(e => new { e.Id, e.CreatedAt });
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        b.Property(e => e.OrderNumber).HasColumnName("order_number").HasMaxLength(40).IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.PickupAddressId).HasColumnName("pickup_address_id");
        b.Property(e => e.DeliveryAddressId).HasColumnName("delivery_address_id");
        b.Property(e => e.PickupSlotId).HasColumnName("pickup_slot_id");
        b.Property(e => e.DeliverySlotId).HasColumnName("delivery_slot_id");
        b.Property(e => e.PickupRiderId).HasColumnName("pickup_rider_id");
        b.Property(e => e.DeliveryRiderId).HasColumnName("delivery_rider_id");
        b.Property(e => e.Channel).HasColumnName("channel").HasMaxLength(20).IsRequired();
        b.Property(e => e.OrderType).HasColumnName("order_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.IsExpress).HasColumnName("is_express").IsRequired();
        b.Property(e => e.RequiresPickup).HasColumnName("requires_pickup").IsRequired();
        b.Property(e => e.RequiresDelivery).HasColumnName("requires_delivery").IsRequired();
        b.Property(e => e.PickupOtp).HasColumnName("pickup_otp").HasMaxLength(10);
        b.Property(e => e.DeliveryOtp).HasColumnName("delivery_otp").HasMaxLength(10);
        b.Property(e => e.Subtotal).HasColumnName("subtotal").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.AddonTotal).HasColumnName("addon_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.ExpressSurcharge).HasColumnName("express_surcharge").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.PickupCharge).HasColumnName("pickup_charge").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.DeliveryCharge).HasColumnName("delivery_charge").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.DiscountTotal).HasColumnName("discount_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.CouponDiscount).HasColumnName("coupon_discount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.LoyaltyDiscount).HasColumnName("loyalty_discount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.PackageDiscount).HasColumnName("package_discount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.TaxableAmount).HasColumnName("taxable_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.TaxTotal).HasColumnName("tax_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Cgst).HasColumnName("cgst").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Sgst).HasColumnName("sgst").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Igst).HasColumnName("igst").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.RoundOff).HasColumnName("round_off").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.GrandTotal).HasColumnName("grand_total").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.AmountPaid).HasColumnName("amount_paid").HasColumnType("numeric(14,2)").IsRequired();
        // GENERATED ALWAYS column — read-only; EF must never write it
        b.Property(e => e.AmountDue).HasColumnName("amount_due").HasColumnType("numeric(14,2)")
            .ValueGeneratedOnAddOrUpdate();
        b.Property(e => e.RefundedAmount).HasColumnName("refunded_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.CouponId).HasColumnName("coupon_id");
        b.Property(e => e.CouponCode).HasColumnName("coupon_code").HasMaxLength(50);
        b.Property(e => e.PackageId).HasColumnName("package_id");
        b.Property(e => e.CustomerPackageId).HasColumnName("customer_package_id");
        b.Property(e => e.LoyaltyPointsUsed).HasColumnName("loyalty_points_used").IsRequired();
        b.Property(e => e.LoyaltyPointsEarned).HasColumnName("loyalty_points_earned").IsRequired();
        b.Property(e => e.TotalItems).HasColumnName("total_items").IsRequired();
        b.Property(e => e.TotalGarments).HasColumnName("total_garments").IsRequired();
        b.Property(e => e.TotalWeightGrams).HasColumnName("total_weight_grams");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        b.Property(e => e.SubStatus).HasColumnName("sub_status").HasMaxLength(50);
        b.Property(e => e.PaymentStatus).HasColumnName("payment_status").HasMaxLength(20).IsRequired();
        b.Property(e => e.PlacedAt).HasColumnName("placed_at").IsRequired();
        b.Property(e => e.PickupScheduledAt).HasColumnName("pickup_scheduled_at");
        b.Property(e => e.PickedUpAt).HasColumnName("picked_up_at");
        b.Property(e => e.ReceivedAt).HasColumnName("received_at");
        b.Property(e => e.QcCompletedAt).HasColumnName("qc_completed_at");
        b.Property(e => e.ReadyAt).HasColumnName("ready_at");
        b.Property(e => e.OutForDeliveryAt).HasColumnName("out_for_delivery_at");
        b.Property(e => e.DeliveredAt).HasColumnName("delivered_at");
        b.Property(e => e.CancelledAt).HasColumnName("cancelled_at");
        b.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
        b.Property(e => e.CancelledByType).HasColumnName("cancelled_by_type").HasMaxLength(20);
        b.Property(e => e.CancelledById).HasColumnName("cancelled_by_id");
        b.Property(e => e.PromisedDeliveryAt).HasColumnName("promised_delivery_at");
        b.Property(e => e.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(50);
        b.Property(e => e.InvoiceGeneratedAt).HasColumnName("invoice_generated_at");
        b.Property(e => e.InvoiceS3Key).HasColumnName("invoice_s3_key");
        b.Property(e => e.NotesCustomer).HasColumnName("notes_customer");
        b.Property(e => e.NotesInternal).HasColumnName("notes_internal");
        b.Property(e => e.Rating).HasColumnName("rating");
        b.Property(e => e.RatingComment).HasColumnName("rating_comment");
        b.Property(e => e.RatedAt).HasColumnName("rated_at");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.SourceIp).HasColumnName("source_ip").HasColumnType("inet");
        b.Property(e => e.SourceUserAgent).HasColumnName("source_user_agent");
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => new { e.OrderNumber, e.CreatedAt }).IsUnique().HasDatabaseName("orders_order_number_created_at_key");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("orders_brand_id_fkey");
        b.HasOne(e => e.Franchise).WithMany().HasForeignKey(e => e.FranchiseId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("orders_franchise_id_fkey");
        b.HasOne(e => e.Store).WithMany().HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("orders_store_id_fkey");
        b.HasOne(e => e.Warehouse).WithMany().HasForeignKey(e => e.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("orders_warehouse_id_fkey");
        b.HasOne(e => e.Customer).WithMany().HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("orders_customer_id_fkey");
        b.HasOne(e => e.PickupAddress).WithMany().HasForeignKey(e => e.PickupAddressId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("orders_pickup_address_id_fkey");
        b.HasOne(e => e.DeliveryAddress).WithMany().HasForeignKey(e => e.DeliveryAddressId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("orders_delivery_address_id_fkey");
        b.HasOne(e => e.PickupSlot).WithMany().HasForeignKey(e => e.PickupSlotId)
            .OnDelete(DeleteBehavior.SetNull).HasConstraintName("orders_pickup_slot_id_fkey");
        b.HasOne(e => e.DeliverySlot).WithMany().HasForeignKey(e => e.DeliverySlotId)
            .OnDelete(DeleteBehavior.SetNull).HasConstraintName("orders_delivery_slot_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
