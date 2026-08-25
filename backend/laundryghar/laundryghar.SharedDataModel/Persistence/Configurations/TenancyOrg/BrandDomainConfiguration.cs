using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.TenancyOrg;

public sealed class BrandDomainConfiguration : IEntityTypeConfiguration<BrandDomain>
{
    public void Configure(EntityTypeBuilder<BrandDomain> b)
    {
        b.ToTable("brand_domains", "tenancy_org");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        // citext, matching the migration: DNS is case-insensitive, so equality must be too.
        b.Property(e => e.Domain).HasColumnName("domain").HasColumnType("citext").IsRequired();
        b.Property(e => e.VerificationTxt).HasColumnName("verification_txt").HasMaxLength(128).IsRequired();
        b.Property(e => e.VerifiedAt).HasColumnName("verified_at");
        b.Property(e => e.SslStatus).HasColumnName("ssl_status").HasMaxLength(20).IsRequired();
        b.Property(e => e.IsPrimary).HasColumnName("is_primary").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        // Computed from VerifiedAt — never a column.
        b.Ignore(e => e.IsResolvable);

        b.HasIndex(e => e.Domain).IsUnique().HasDatabaseName("brand_domains_domain_key");
        b.HasIndex(e => e.BrandId).HasDatabaseName("idx_brand_domains_brand");

        // ON DELETE CASCADE in the migration: a brand's domains are meaningless without the brand,
        // unlike stores/franchises which are RESTRICTed to prevent accidental tenant destruction.
        b.HasOne<Brand>()
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("brand_domains_brand_id_fkey");
    }
}
