using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.CustomerCatalog;

public sealed class DpdpConsentConfiguration : IEntityTypeConfiguration<DpdpConsent>
{
    public void Configure(EntityTypeBuilder<DpdpConsent> b)
    {
        b.ToTable("dpdp_consents", "customer_catalog");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.CustomerId).HasColumnName("customer_id");
        b.Property(e => e.UserId).HasColumnName("user_id");
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Purpose).HasColumnName("purpose").HasMaxLength(50).IsRequired();
        b.Property(e => e.PurposeDescription).HasColumnName("purpose_description").IsRequired();
        b.Property(e => e.DataCategories).HasColumnName("data_categories").HasColumnType("text[]").IsRequired();
        b.Property(e => e.ConsentStatus).HasColumnName("consent_status").HasMaxLength(20).IsRequired();
        b.Property(e => e.ConsentMethod).HasColumnName("consent_method").HasMaxLength(30).IsRequired();
        b.Property(e => e.PrivacyPolicyVersion).HasColumnName("privacy_policy_version").HasMaxLength(20).IsRequired();
        b.Property(e => e.TermsVersion).HasColumnName("terms_version").HasMaxLength(20);
        b.Property(e => e.ConsentTextSnapshot).HasColumnName("consent_text_snapshot");
        b.Property(e => e.GrantedAt).HasColumnName("granted_at");
        b.Property(e => e.WithdrawnAt).HasColumnName("withdrawn_at");
        b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
        b.Property(e => e.IpAddress).HasColumnName("ip_address").HasColumnType("inet");
        b.Property(e => e.UserAgent).HasColumnName("user_agent");
        // geo_location is varchar(100) in DB — NOT a geography type despite the name
        b.Property(e => e.GeoLocation).HasColumnName("geo_location").HasMaxLength(100);
        b.Property(e => e.EvidenceS3Key).HasColumnName("evidence_s3_key");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.Customer)
            .WithMany(c => c.DpdpConsents)
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("dpdp_consents_customer_id_fkey");

        b.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("dpdp_consents_user_id_fkey");
    }
}
