using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class OAuthClientConfiguration : IEntityTypeConfiguration<OAuthClient>
{
    public void Configure(EntityTypeBuilder<OAuthClient> b)
    {
        b.ToTable("oauth_clients", "identity_access");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.ClientId)
            .HasColumnName("client_id")
            .HasMaxLength(64)
            .IsRequired();

        b.HasIndex(e => e.ClientId)
            .IsUnique()
            .HasDatabaseName("idx_oauth_clients_client_id");

        b.Property(e => e.ClientName)
            .HasColumnName("client_name")
            .HasMaxLength(200)
            .IsRequired();

        b.Property(e => e.RedirectUris)
            .HasColumnName("redirect_uris")
            .HasColumnType("text[]")
            .IsRequired();

        b.Property(e => e.IsActive)
            .HasColumnName("is_active")
            .IsRequired()
            .HasDefaultValue(true);

        b.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
