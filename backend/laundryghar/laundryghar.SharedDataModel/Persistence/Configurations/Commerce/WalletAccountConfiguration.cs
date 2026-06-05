using laundryghar.SharedDataModel.Entities.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce;

public sealed class WalletAccountConfiguration : IEntityTypeConfiguration<WalletAccount>
{
    public void Configure(EntityTypeBuilder<WalletAccount> b)
    {
        b.ToTable("wallet_accounts", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.Balance).HasColumnName("balance").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.LockedBalance).HasColumnName("locked_balance").HasColumnType("numeric(14,2)").IsRequired();
        // GENERATED ALWAYS column — read-only; EF must never write it
        b.Property(e => e.AvailableBalance).HasColumnName("available_balance").HasColumnType("numeric(14,2)")
            .ValueGeneratedOnAddOrUpdate();
        b.Property(e => e.LifetimeCredit).HasColumnName("lifetime_credit").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.LifetimeDebit).HasColumnName("lifetime_debit").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.LastTransactionAt).HasColumnName("last_transaction_at");
        b.Property(e => e.IsFrozen).HasColumnName("is_frozen").IsRequired();
        b.Property(e => e.FrozenAt).HasColumnName("frozen_at");
        b.Property(e => e.FrozenReason).HasColumnName("frozen_reason");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        // One wallet per customer
        b.HasIndex(e => e.CustomerId)
            .IsUnique()
            .HasDatabaseName("wallet_accounts_customer_id_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("wallet_accounts_brand_id_fkey");

        b.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("wallet_accounts_customer_id_fkey");
    }
}
