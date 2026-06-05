using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.TenancyOrg;

public sealed class TerritoryConfiguration : IEntityTypeConfiguration<Territory>
{
    public void Configure(EntityTypeBuilder<Territory> b)
    {
        b.ToTable("territories", "tenancy_org");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(e => e.Description).HasColumnName("description");
        b.Property(e => e.CountryCode).HasColumnName("country_code").HasColumnType("character(2)").IsRequired();
        b.Property(e => e.State).HasColumnName("state").HasMaxLength(100);
        b.Property(e => e.Cities).HasColumnName("cities").HasColumnType("text[]").IsRequired();
        b.Property(e => e.Pincodes).HasColumnName("pincodes").HasColumnType("text[]").IsRequired();
        b.Property(e => e.Boundary).HasColumnName("boundary").HasColumnType("geography(MultiPolygon,4326)");
        b.Property(e => e.ExclusivityType).HasColumnName("exclusivity_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => new { e.BrandId, e.Code }).IsUnique().HasDatabaseName("territories_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany(br => br.Territories)
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("territories_brand_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
