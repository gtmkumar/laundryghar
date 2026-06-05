using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.TenancyOrg;

public sealed class FranchiseAgreementConfiguration : IEntityTypeConfiguration<FranchiseAgreement>
{
    public void Configure(EntityTypeBuilder<FranchiseAgreement> b)
    {
        b.ToTable("franchise_agreements", "tenancy_org");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.AgreementNumber).HasColumnName("agreement_number").HasMaxLength(50).IsRequired();
        b.Property(e => e.AgreementType).HasColumnName("agreement_type").HasMaxLength(30).IsRequired();
        b.Property(e => e.FranchiseeLegalName).HasColumnName("franchisee_legal_name").HasMaxLength(200).IsRequired();
        b.Property(e => e.FranchiseePan).HasColumnName("franchisee_pan").HasMaxLength(10);
        b.Property(e => e.FranchiseeGstin).HasColumnName("franchisee_gstin").HasMaxLength(15);
        b.Property(e => e.FranchiseePhone).HasColumnName("franchisee_phone").HasMaxLength(20);
        b.Property(e => e.FranchiseeEmail).HasColumnName("franchisee_email").HasColumnType("citext");
        b.Property(e => e.InitialFranchiseFee).HasColumnName("initial_franchise_fee").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.RoyaltyPercent).HasColumnName("royalty_percent").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.MarketingFeePercent).HasColumnName("marketing_fee_percent").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.TechnologyFeeMonthly).HasColumnName("technology_fee_monthly").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.TerritoryId).HasColumnName("territory_id");
        b.Property(e => e.TermYears).HasColumnName("term_years").IsRequired();
        b.Property(e => e.RenewalOption).HasColumnName("renewal_option").IsRequired();
        b.Property(e => e.ExclusivityClause).HasColumnName("exclusivity_clause").IsRequired();
        b.Property(e => e.MinimumStores).HasColumnName("minimum_stores").IsRequired();
        b.Property(e => e.MaximumStores).HasColumnName("maximum_stores");
        b.Property(e => e.SlaTerms).HasColumnName("sla_terms").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.DocumentS3Key).HasColumnName("document_s3_key");
        b.Property(e => e.SignedAt).HasColumnName("signed_at");
        b.Property(e => e.EffectiveFrom).HasColumnName("effective_from").IsRequired();
        b.Property(e => e.EffectiveTo).HasColumnName("effective_to").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.TerminatedAt).HasColumnName("terminated_at");
        b.Property(e => e.TerminationReason).HasColumnName("termination_reason");
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => e.AgreementNumber).IsUnique().HasDatabaseName("franchise_agreements_agreement_number_key");

        b.HasOne(e => e.Brand)
            .WithMany(br => br.FranchiseAgreements)
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("franchise_agreements_brand_id_fkey");

        b.HasOne(e => e.Territory)
            .WithMany(t => t.FranchiseAgreements)
            .HasForeignKey(e => e.TerritoryId)
            .OnDelete(DeleteBehavior.NoAction)   // DB: no ON DELETE clause → RESTRICT by default
            .HasConstraintName("franchise_agreements_territory_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
