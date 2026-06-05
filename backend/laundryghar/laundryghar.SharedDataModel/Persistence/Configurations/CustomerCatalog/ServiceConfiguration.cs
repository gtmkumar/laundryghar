using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.CustomerCatalog;

public sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> b)
    {
        b.ToTable("services", "customer_catalog");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.CategoryId).HasColumnName("category_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(e => e.NameLocalized).HasColumnName("name_localized").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.PricingModel).HasColumnName("pricing_model").HasMaxLength(30).IsRequired();
        b.Property(e => e.BaseTatHours).HasColumnName("base_tat_hours").IsRequired();
        b.Property(e => e.ExpressTatHours).HasColumnName("express_tat_hours").IsRequired();
        b.Property(e => e.ExpressMultiplier).HasColumnName("express_multiplier").HasColumnType("numeric(4,2)").IsRequired();
        b.Property(e => e.IsExpressAvailable).HasColumnName("is_express_available").IsRequired();
        b.Property(e => e.RequiresInspection).HasColumnName("requires_inspection").IsRequired();
        b.Property(e => e.RequiresQc).HasColumnName("requires_qc").IsRequired();
        b.Property(e => e.IconUrl).HasColumnName("icon_url");
        b.Property(e => e.DisplayOrder).HasColumnName("display_order").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => new { e.BrandId, e.Code }).IsUnique().HasDatabaseName("services_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("services_brand_id_fkey");

        b.HasOne(e => e.Category)
            .WithMany(sc => sc.Services)
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("services_category_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
