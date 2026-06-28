using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("roles", "identity_access");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id");
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.ScopeType).HasColumnName("scope_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.VerticalKey).HasColumnName("vertical_key").HasMaxLength(20);
        b.Property(e => e.IsSystem).HasColumnName("is_system").IsRequired();
        b.Property(e => e.IsAssignable).HasColumnName("is_assignable").IsRequired();
        b.Property(e => e.Priority).HasColumnName("priority").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasIndex(e => new { e.BrandId, e.Code }).IsUnique().HasDatabaseName("roles_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("roles_brand_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
