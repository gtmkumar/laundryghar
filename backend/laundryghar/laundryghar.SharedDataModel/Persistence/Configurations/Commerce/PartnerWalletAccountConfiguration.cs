using laundryghar.SharedDataModel.Entities.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Commerce;

public sealed class PartnerWalletAccountConfiguration : IEntityTypeConfiguration<PartnerWalletAccount>
{
    public void Configure(EntityTypeBuilder<PartnerWalletAccount> b)
    {
        b.ToTable("partner_wallet_accounts", "commerce");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        // partner_id is a SCALAR cross-BC key → logistics.partners(id): no navigation, no FK.
        b.Property(e => e.PartnerId).HasColumnName("partner_id").IsRequired();
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.Balance).HasColumnName("balance").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.LockedBalance).HasColumnName("locked_balance").HasColumnType("numeric(14,2)").IsRequired();
        // GENERATED ALWAYS column — read-only; EF must never write it.
        b.Property(e => e.AvailableBalance).HasColumnName("available_balance").HasColumnType("numeric(14,2)")
            .ValueGeneratedOnAddOrUpdate();
        b.Property(e => e.LifetimeCredit).HasColumnName("lifetime_credit").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.LifetimeDebit).HasColumnName("lifetime_debit").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.LastTransactionAt).HasColumnName("last_transaction_at");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        // One wallet per partner (partner_id is the rls_partner isolation key).
        b.HasIndex(e => e.PartnerId)
            .IsUnique()
            .HasDatabaseName("partner_wallet_accounts_partner_id_key");
    }
}
