using laundryghar.SharedDataModel.Entities.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce;

public sealed class PartnerWalletTransactionConfiguration : IEntityTypeConfiguration<PartnerWalletTransaction>
{
    public void Configure(EntityTypeBuilder<PartnerWalletTransaction> b)
    {
        b.ToTable("partner_wallet_transactions", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.PartnerWalletAccountId).HasColumnName("partner_wallet_account_id").IsRequired();
        // partner_id is a SCALAR cross-BC key → logistics.partners(id): no navigation, no FK.
        b.Property(e => e.PartnerId).HasColumnName("partner_id").IsRequired();
        b.Property(e => e.Direction).HasColumnName("direction").IsRequired();
        b.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.BalanceBefore).HasColumnName("balance_before").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.BalanceAfter).HasColumnName("balance_after").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.ReferenceType).HasColumnName("reference_type").HasMaxLength(30);
        b.Property(e => e.ReferenceId).HasColumnName("reference_id");
        b.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100);
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasIndex(e => e.PartnerId)
            .HasDatabaseName("idx_partner_wallet_transactions_partner");

        b.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("partner_wallet_transactions_idempotency_key_key");

        // In-BC FK to the wallet account (same schema).
        b.HasOne(e => e.PartnerWalletAccount)
            .WithMany(wa => wa.Transactions)
            .HasForeignKey(e => e.PartnerWalletAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("partner_wallet_transactions_partner_wallet_account_id_fkey");
    }
}
