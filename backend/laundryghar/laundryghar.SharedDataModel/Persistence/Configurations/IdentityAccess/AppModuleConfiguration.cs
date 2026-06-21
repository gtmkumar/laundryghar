using laundryghar.SharedDataModel.Entities.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.IdentityAccess;

public sealed class AppModuleConfiguration : IEntityTypeConfiguration<AppModule>
{
    public void Configure(EntityTypeBuilder<AppModule> b)
    {
        b.ToTable("modules", "identity_access");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.Key).HasColumnName("key").HasMaxLength(64).IsRequired();
        b.Property(e => e.Label).HasColumnName("label").HasMaxLength(128).IsRequired();
        b.Property(e => e.Icon).HasColumnName("icon").HasMaxLength(64);
        b.Property(e => e.Route).HasColumnName("route").HasMaxLength(160);
        b.Property(e => e.Section).HasColumnName("section").HasMaxLength(64);
        b.Property(e => e.NavOrder).HasColumnName("nav_order").IsRequired();
        b.Property(e => e.MatrixOrder).HasColumnName("matrix_order").IsRequired();
        b.Property(e => e.ShowInNav).HasColumnName("show_in_nav").IsRequired();
        b.Property(e => e.ShowInMatrix).HasColumnName("show_in_matrix").IsRequired();
        b.Property(e => e.RequiredPermission).HasColumnName("required_permission").HasMaxLength(128);
        b.Property(e => e.PermissionModules).HasColumnName("permission_modules").HasColumnType("text[]");
        b.Property(e => e.IsCore).HasColumnName("is_core").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        b.HasIndex(e => e.Key).IsUnique();
    }
}
