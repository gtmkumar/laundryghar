using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class OAuthAuthorizationCodeConfiguration : IEntityTypeConfiguration<OAuthAuthorizationCode>
{
    public void Configure(EntityTypeBuilder<OAuthAuthorizationCode> b)
    {
        b.ToTable("oauth_authorization_codes", "identity_access");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.CodeHash)
            .HasColumnName("code_hash")
            .IsRequired();

        b.HasIndex(e => e.CodeHash)
            .IsUnique()
            .HasDatabaseName("idx_oauth_codes_hash");

        b.Property(e => e.ClientId)
            .HasColumnName("client_id")
            .HasMaxLength(64)
            .IsRequired();

        b.Property(e => e.RedirectUri)
            .HasColumnName("redirect_uri")
            .IsRequired();

        b.Property(e => e.CodeChallenge)
            .HasColumnName("code_challenge")
            .IsRequired();

        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();

        b.Property(e => e.Scope)
            .HasColumnName("scope")
            .IsRequired()
            .HasDefaultValue("mcp:booking");

        b.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();
        b.Property(e => e.ConsumedAt).HasColumnName("consumed_at");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasOne(e => e.Client)
            .WithMany()
            .HasForeignKey(e => e.ClientId)
            .HasPrincipalKey(c => c.ClientId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("oauth_authorization_codes_client_id_fkey");
    }
}
