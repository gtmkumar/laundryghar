using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.TenancyOrg;

public sealed class FranchiseConfiguration : IEntityTypeConfiguration<Franchise>
{
    public void Configure(EntityTypeBuilder<Franchise> b)
    {
        b.ToTable("franchises", "tenancy_org");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.TerritoryId).HasColumnName("territory_id");
        b.Property(e => e.FranchiseAgreementId).HasColumnName("franchise_agreement_id");
        b.Property(e => e.OwnerUserId).HasColumnName("owner_user_id");
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.LegalName).HasColumnName("legal_name").HasMaxLength(200).IsRequired();
        b.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(200);
        b.Property(e => e.Gstin).HasColumnName("gstin").HasMaxLength(15);
        b.Property(e => e.Pan).HasColumnName("pan").HasMaxLength(10);
        b.Property(e => e.Cin).HasColumnName("cin").HasMaxLength(21);
        b.Property(e => e.ContactPhone).HasColumnName("contact_phone").HasMaxLength(20).IsRequired();
        b.Property(e => e.ContactEmail).HasColumnName("contact_email").HasColumnType("citext");
        b.Property(e => e.BillingAddress).HasColumnName("billing_address").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.OperationalAddress).HasColumnName("operational_address").HasColumnType("jsonb");
        b.Property(e => e.BankAccountName).HasColumnName("bank_account_name").HasMaxLength(200);
        b.Property(e => e.BankAccountNumber).HasColumnName("bank_account_number").HasMaxLength(50);
        b.Property(e => e.BankIfsc).HasColumnName("bank_ifsc").HasMaxLength(11);
        b.Property(e => e.BankName).HasColumnName("bank_name").HasMaxLength(100);
        b.Property(e => e.RoyaltyPercent).HasColumnName("royalty_percent").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.MarketingFeePercent).HasColumnName("marketing_fee_percent").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.OnboardingStatus).HasColumnName("onboarding_status").HasMaxLength(30).IsRequired();
        b.Property(e => e.OnboardedAt).HasColumnName("onboarded_at");
        b.Property(e => e.SuspendedAt).HasColumnName("suspended_at");
        b.Property(e => e.SuspendedReason).HasColumnName("suspended_reason");
        b.Property(e => e.TerminatedAt).HasColumnName("terminated_at");
        b.Property(e => e.Config).HasColumnName("config").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => new { e.BrandId, e.Code }).IsUnique().HasDatabaseName("franchises_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany(br => br.Franchises)
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("franchises_brand_id_fkey");

        b.HasOne(e => e.Territory)
            .WithMany(t => t.Franchises)
            .HasForeignKey(e => e.TerritoryId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("franchises_territory_id_fkey");

        b.HasOne(e => e.FranchiseAgreement)
            .WithMany(fa => fa.Franchises)
            .HasForeignKey(e => e.FranchiseAgreementId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("franchises_franchise_agreement_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
