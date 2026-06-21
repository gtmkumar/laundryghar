using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class UserPermissionOverrideConfiguration : IEntityTypeConfiguration<UserPermissionOverride>
{
    public void Configure(EntityTypeBuilder<UserPermissionOverride> b)
    {
        b.ToTable("user_permission_override", "identity_access");

        b.HasKey(e => new { e.UserId, e.PermissionId });
        b.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        b.Property(e => e.PermissionId).HasColumnName("permission_id").IsRequired();
        b.Property(e => e.Effect).HasColumnName("effect").HasMaxLength(16).IsRequired();
        b.Property(e => e.GrantedAt).HasColumnName("granted_at").IsRequired();
        b.Property(e => e.GrantedBy).HasColumnName("granted_by");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasOne(e => e.Permission).WithMany().HasForeignKey(e => e.PermissionId);
        b.HasIndex(e => e.UserId);
    }
}
