using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.CustomerCatalog;

public sealed class ServiceCategoryConfiguration : IEntityTypeConfiguration<ServiceCategory>
{
    public void Configure(EntityTypeBuilder<ServiceCategory> b)
    {
        b.ToTable("service_categories", "customer_catalog");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(e => e.NameLocalized).HasColumnName("name_localized").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.IconUrl).HasColumnName("icon_url");
        b.Property(e => e.ImageUrl).HasColumnName("image_url");
        b.Property(e => e.ColorHex).HasColumnName("color_hex").HasColumnType("character(7)");
        b.Property(e => e.DisplayOrder).HasColumnName("display_order").IsRequired();
        b.Property(e => e.IsVisibleMobile).HasColumnName("is_visible_mobile").IsRequired();
        b.Property(e => e.IsVisiblePos).HasColumnName("is_visible_pos").IsRequired();
        b.Property(e => e.RequiresWarehouseCap).HasColumnName("requires_warehouse_cap").HasColumnType("text[]").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.SeasonalFrom).HasColumnName("seasonal_from");
        b.Property(e => e.SeasonalTo).HasColumnName("seasonal_to");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => new { e.BrandId, e.Code }).IsUnique().HasDatabaseName("service_categories_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("service_categories_brand_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
