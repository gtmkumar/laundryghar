using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.CustomerCatalog;

public sealed class ValuePriceSlabConfiguration : IEntityTypeConfiguration<ValuePriceSlab>
{
    public void Configure(EntityTypeBuilder<ValuePriceSlab> b)
    {
        b.ToTable("value_price_slabs", "customer_catalog");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.ServiceId).HasColumnName("service_id");
        b.Property(e => e.MinValue).HasColumnName("min_value").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.MaxValue).HasColumnName("max_value").HasColumnType("numeric(14,2)");
        b.Property(e => e.Price).HasColumnName("price").HasColumnType("numeric(14,2)").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();

        b.HasIndex(e => new { e.BrandId, e.ServiceId, e.MinValue });

        b.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("value_price_slabs_brand_id_fkey");

        b.HasOne(e => e.Service)
            .WithMany()
            .HasForeignKey(e => e.ServiceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("value_price_slabs_service_id_fkey");
    }
}
