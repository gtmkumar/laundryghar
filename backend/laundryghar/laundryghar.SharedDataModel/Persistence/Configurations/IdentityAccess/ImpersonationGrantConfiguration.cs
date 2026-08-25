using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.IdentityAccess;

public class ImpersonationGrantConfiguration : IEntityTypeConfiguration<ImpersonationGrant>
{
    public void Configure(EntityTypeBuilder<ImpersonationGrant> b)
    {
        b.ToTable("impersonation_grants", "identity_access");
        b.HasKey(e => e.Id);

        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.BrandId).HasColumnName("brand_id");
        b.Property(e => e.SupportUserId).HasColumnName("support_user_id");
        b.Property(e => e.Reason).HasColumnName("reason").IsRequired();
        b.Property(e => e.Scope).HasColumnName("scope").HasMaxLength(16).IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        b.Property(e => e.RequestedAt).HasColumnName("requested_at");
        b.Property(e => e.ApprovedByUserId).HasColumnName("approved_by_user_id");
        b.Property(e => e.ApprovedAt).HasColumnName("approved_at");
        b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
        b.Property(e => e.RevokedAt).HasColumnName("revoked_at");
        b.Property(e => e.RevokedByUserId).HasColumnName("revoked_by_user_id");
        b.Property(e => e.RevokeReason).HasColumnName("revoke_reason");
        b.Property(e => e.CreatedAt).HasColumnName("created_at");
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => new { e.BrandId, e.Status, e.RequestedAt })
            .HasDatabaseName("idx_impersonation_grants_brand");
    }
}
