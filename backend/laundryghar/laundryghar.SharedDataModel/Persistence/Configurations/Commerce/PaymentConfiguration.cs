using laundryghar.SharedDataModel.Entities.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id");
        b.Property(e => e.StoreId).HasColumnName("store_id");
        b.Property(e => e.CustomerId).HasColumnName("customer_id");
        b.Property(e => e.OrderId).HasColumnName("order_id");
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at");
        b.Property(e => e.PaymentMethodId).HasColumnName("payment_method_id");
        b.Property(e => e.PaymentPurpose).HasColumnName("payment_purpose").HasMaxLength(30).IsRequired();
        b.Property(e => e.PaymentNumber).HasColumnName("payment_number").HasMaxLength(40).IsRequired();
        b.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.ConvenienceFee).HasColumnName("convenience_fee").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.GatewayCharge).HasColumnName("gateway_charge").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.NetAmount).HasColumnName("net_amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.Direction).HasColumnName("direction").IsRequired();
        b.Property(e => e.Gateway).HasColumnName("gateway").HasMaxLength(30);
        b.Property(e => e.GatewayOrderId).HasColumnName("gateway_order_id").HasMaxLength(100);
        b.Property(e => e.GatewayPaymentId).HasColumnName("gateway_payment_id").HasMaxLength(100);
        b.Property(e => e.GatewaySignature).HasColumnName("gateway_signature");
        b.Property(e => e.GatewayResponse).HasColumnName("gateway_response").HasColumnType("jsonb");
        b.Property(e => e.UpiVpa).HasColumnName("upi_vpa").HasMaxLength(100);
        b.Property(e => e.CardLast4).HasColumnName("card_last4").HasColumnType("character(4)");
        b.Property(e => e.CardNetwork).HasColumnName("card_network").HasMaxLength(20);
        b.Property(e => e.BankName).HasColumnName("bank_name").HasMaxLength(100);
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.FailureCode).HasColumnName("failure_code").HasMaxLength(50);
        b.Property(e => e.FailureMessage).HasColumnName("failure_message");
        b.Property(e => e.InitiatedAt).HasColumnName("initiated_at").IsRequired();
        b.Property(e => e.CompletedAt).HasColumnName("completed_at");
        b.Property(e => e.FailedAt).HasColumnName("failed_at");
        b.Property(e => e.ReconciledAt).HasColumnName("reconciled_at");
        b.Property(e => e.SettlementId).HasColumnName("settlement_id").HasMaxLength(100);
        b.Property(e => e.SettledAt).HasColumnName("settled_at");
        b.Property(e => e.SettledAmount).HasColumnName("settled_amount").HasColumnType("numeric(14,2)");
        b.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100);
        b.Property(e => e.IpAddress).HasColumnName("ip_address").HasColumnType("inet");
        b.Property(e => e.UserAgent).HasColumnName("user_agent");
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("payments_idempotency_key_key");

        b.HasIndex(e => e.PaymentNumber)
            .IsUnique()
            .HasDatabaseName("payments_payment_number_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("payments_brand_id_fkey");

        b.HasOne(e => e.Franchise)
            .WithMany()
            .HasForeignKey(e => e.FranchiseId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("payments_franchise_id_fkey");

        b.HasOne(e => e.Store)
            .WithMany()
            .HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("payments_store_id_fkey");

        b.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("payments_customer_id_fkey");

        b.HasOne(e => e.PaymentMethod)
            .WithMany(pm => pm.Payments)
            .HasForeignKey(e => e.PaymentMethodId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("payments_payment_method_id_fkey");

        // Composite FK to partitioned orders — scalar-only
        b.HasOne<global::laundryghar.SharedDataModel.Entities.OrderLifecycle.Order>()
            .WithMany()
            .HasForeignKey(e => new { e.OrderId, e.OrderCreatedAt })
            .HasPrincipalKey(o => new { o.Id, o.CreatedAt })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("payments_order_id_fkey");
    }
}
