using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.TenancyOrg;

public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.ToTable("warehouses", "tenancy_org");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(e => e.WarehouseType).HasColumnName("warehouse_type").HasMaxLength(30).IsRequired();
        b.Property(e => e.AddressLine1).HasColumnName("address_line1").HasMaxLength(255).IsRequired();
        b.Property(e => e.AddressLine2).HasColumnName("address_line2").HasMaxLength(255);
        b.Property(e => e.City).HasColumnName("city").HasMaxLength(100).IsRequired();
        b.Property(e => e.State).HasColumnName("state").HasMaxLength(100).IsRequired();
        b.Property(e => e.Pincode).HasColumnName("pincode").HasMaxLength(10).IsRequired();
        b.Property(e => e.CountryCode).HasColumnName("country_code").HasColumnType("character(2)").IsRequired();
        b.Property(e => e.GeoLocation).HasColumnName("geo_location").HasColumnType("geography(Point,4326)");
        b.Property(e => e.ContactPhone).HasColumnName("contact_phone").HasMaxLength(20);
        b.Property(e => e.ContactEmail).HasColumnName("contact_email").HasColumnType("citext");
        b.Property(e => e.ManagerUserId).HasColumnName("manager_user_id");
        b.Property(e => e.DailyThroughputTarget).HasColumnName("daily_throughput_target").IsRequired();
        b.Property(e => e.CurrentLoadCount).HasColumnName("current_load_count").IsRequired();
        b.Property(e => e.HasDryClean).HasColumnName("has_dry_clean").IsRequired();
        b.Property(e => e.HasSteamIron).HasColumnName("has_steam_iron").IsRequired();
        b.Property(e => e.HasShoeCleaning).HasColumnName("has_shoe_cleaning").IsRequired();
        b.Property(e => e.HasCarpetCleaning).HasColumnName("has_carpet_cleaning").IsRequired();
        b.Property(e => e.Capabilities).HasColumnName("capabilities").HasColumnType("text[]").IsRequired();
        b.Property(e => e.OperatingHoursConfig).HasColumnName("operating_hours_config").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Timezone).HasColumnName("timezone").HasMaxLength(50).IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.Config).HasColumnName("config").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => new { e.BrandId, e.Code }).IsUnique().HasDatabaseName("warehouses_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany(br => br.Warehouses)
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("warehouses_brand_id_fkey");

        b.HasOne(e => e.Franchise)
            .WithMany(f => f.Warehouses)
            .HasForeignKey(e => e.FranchiseId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("warehouses_franchise_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
