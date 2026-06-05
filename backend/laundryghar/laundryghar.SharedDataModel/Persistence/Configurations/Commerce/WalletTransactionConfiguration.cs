using laundryghar.SharedDataModel.Entities.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce;

public sealed class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> b)
    {
        b.ToTable("wallet_transactions", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.WalletAccountId).HasColumnName("wallet_account_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.TransactionType).HasColumnName("transaction_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.Direction).HasColumnName("direction").IsRequired();
        b.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.BalanceBefore).HasColumnName("balance_before").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.BalanceAfter).HasColumnName("balance_after").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.ReferenceType).HasColumnName("reference_type").HasMaxLength(30);
        b.Property(e => e.ReferenceId).HasColumnName("reference_id");
        b.Property(e => e.OrderId).HasColumnName("order_id");
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at");
        b.Property(e => e.PaymentId).HasColumnName("payment_id");
        b.Property(e => e.RefundId).HasColumnName("refund_id");
        b.Property(e => e.Description).HasColumnName("description").HasMaxLength(255);
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.PerformedByType).HasColumnName("performed_by_type").HasMaxLength(20);
        b.Property(e => e.PerformedById).HasColumnName("performed_by_id");
        b.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100);
        b.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("wallet_transactions_idempotency_key_key");

        b.HasOne(e => e.WalletAccount)
            .WithMany(wa => wa.Transactions)
            .HasForeignKey(e => e.WalletAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("wallet_transactions_wallet_account_id_fkey");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("wallet_transactions_brand_id_fkey");

        b.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("wallet_transactions_customer_id_fkey");

        b.HasOne(e => e.Payment)
            .WithMany(p => p.WalletTransactions)
            .HasForeignKey(e => e.PaymentId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("wallet_transactions_payment_id_fkey");

        b.HasOne(e => e.Refund)
            .WithMany(r => r.WalletTransactions)
            .HasForeignKey(e => e.RefundId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("wallet_transactions_refund_id_fkey");

        // Composite FK to partitioned orders — scalar-only
        b.HasOne<global::laundryghar.SharedDataModel.Entities.OrderLifecycle.Order>()
            .WithMany()
            .HasForeignKey(e => new { e.OrderId, e.OrderCreatedAt })
            .HasPrincipalKey(o => new { o.Id, o.CreatedAt })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("wallet_transactions_order_id_fkey");
    }
}
