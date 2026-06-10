using laundryghar.SharedDataModel.Entities.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce;

public sealed class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> b)
    {
        b.ToTable("payment_refunds", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.OriginalPaymentId).HasColumnName("original_payment_id").IsRequired();
        b.Property(e => e.OrderId).HasColumnName("order_id");
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at");
        b.Property(e => e.CustomerId).HasColumnName("customer_id");
        b.Property(e => e.RefundNumber).HasColumnName("refund_number").HasMaxLength(40).IsRequired();
        b.Property(e => e.RefundType).HasColumnName("refund_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(100).IsRequired();
        b.Property(e => e.ReasonText).HasColumnName("reason_text");
        b.Property(e => e.RefundMethod).HasColumnName("refund_method").HasMaxLength(30);
        b.Property(e => e.GatewayRefundId).HasColumnName("gateway_refund_id").HasMaxLength(100);
        b.Property(e => e.GatewayResponse).HasColumnName("gateway_response").HasColumnType("jsonb");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.RequestedBy).HasColumnName("requested_by");
        b.Property(e => e.RequestedAt).HasColumnName("requested_at").IsRequired();
        b.Property(e => e.ApprovedBy).HasColumnName("approved_by");
        b.Property(e => e.ApprovedAt).HasColumnName("approved_at");
        b.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        b.Property(e => e.CompletedAt).HasColumnName("completed_at");
        b.Property(e => e.FailureReason).HasColumnName("failure_reason");
        b.Property(e => e.CustomerNotifiedAt).HasColumnName("customer_notified_at");
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(150);
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasIndex(e => e.RefundNumber)
            .IsUnique()
            .HasDatabaseName("payment_refunds_refund_number_key");

        // Partial unique index: idempotency_key is unique only when not null.
        // This mirrors how WalletTransaction handles its idempotency_key.
        // The HasFilter expression is applied in the idempotent patch SQL because
        // EF Core's HasFilter does not generate a CREATE INDEX IF NOT EXISTS.
        b.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("payment_refunds_idempotency_key_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("payment_refunds_brand_id_fkey");

        b.HasOne(e => e.OriginalPayment)
            .WithMany(p => p.Refunds)
            .HasForeignKey(e => e.OriginalPaymentId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("payment_refunds_original_payment_id_fkey");

        b.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("payment_refunds_customer_id_fkey");

        // Composite FK to partitioned orders — scalar-only
        b.HasOne<global::laundryghar.SharedDataModel.Entities.OrderLifecycle.Order>()
            .WithMany()
            .HasForeignKey(e => new { e.OrderId, e.OrderCreatedAt })
            .HasPrincipalKey(o => new { o.Id, o.CreatedAt })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("payment_refunds_order_id_fkey");
    }
}
