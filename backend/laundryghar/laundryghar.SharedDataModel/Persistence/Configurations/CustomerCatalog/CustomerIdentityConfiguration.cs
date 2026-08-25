using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.CustomerCatalog;

public sealed class CustomerIdentityConfiguration : IEntityTypeConfiguration<CustomerIdentity>
{
    public void Configure(EntityTypeBuilder<CustomerIdentity> b)
    {
        b.ToTable("customer_identities", "customer_catalog");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Provider).HasColumnName("provider").HasMaxLength(20).IsRequired();
        b.Property(e => e.ProviderUid).HasColumnName("provider_uid").HasMaxLength(255).IsRequired();
        b.Property(e => e.Email).HasColumnName("email").HasColumnType("citext");
        b.Property(e => e.EmailVerified).HasColumnName("email_verified").IsRequired();
        b.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(200);
        b.Property(e => e.AvatarUrl).HasColumnName("avatar_url");
        b.Property(e => e.LastLoginAt).HasColumnName("last_login_at");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => new { e.BrandId, e.Provider, e.ProviderUid })
            .IsUnique()
            .HasDatabaseName("customer_identities_brand_id_provider_provider_uid_key");
        b.HasIndex(e => new { e.CustomerId, e.Provider })
            .IsUnique()
            .HasDatabaseName("customer_identities_customer_id_provider_key");

        b.HasOne(e => e.Customer)
            .WithMany(c => c.Identities)
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("customer_identities_customer_id_fkey");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("customer_identities_brand_id_fkey");
    }
}
