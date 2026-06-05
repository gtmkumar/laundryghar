using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.CustomerCatalog;

public sealed class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> b)
    {
        b.ToTable("customer_addresses", "customer_catalog");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.CustomerId).HasColumnName("customer_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Label).HasColumnName("label").HasMaxLength(50).IsRequired();
        b.Property(e => e.CustomLabel).HasColumnName("custom_label").HasMaxLength(100);
        b.Property(e => e.RecipientName).HasColumnName("recipient_name").HasMaxLength(200);
        b.Property(e => e.RecipientPhone).HasColumnName("recipient_phone").HasMaxLength(20);
        b.Property(e => e.AddressLine1).HasColumnName("address_line1").HasMaxLength(255).IsRequired();
        b.Property(e => e.AddressLine2).HasColumnName("address_line2").HasMaxLength(255);
        b.Property(e => e.Landmark).HasColumnName("landmark").HasMaxLength(200);
        b.Property(e => e.Floor).HasColumnName("floor").HasMaxLength(20);
        b.Property(e => e.FlatNumber).HasColumnName("flat_number").HasMaxLength(50);
        b.Property(e => e.BuildingName).HasColumnName("building_name").HasMaxLength(200);
        b.Property(e => e.Society).HasColumnName("society").HasMaxLength(200);
        b.Property(e => e.Area).HasColumnName("area").HasMaxLength(200);
        b.Property(e => e.City).HasColumnName("city").HasMaxLength(100).IsRequired();
        b.Property(e => e.State).HasColumnName("state").HasMaxLength(100).IsRequired();
        b.Property(e => e.Pincode).HasColumnName("pincode").HasMaxLength(10).IsRequired();
        b.Property(e => e.CountryCode).HasColumnName("country_code").HasColumnType("character(2)").IsRequired();
        b.Property(e => e.GeoLocation).HasColumnName("geo_location").HasColumnType("geography(Point,4326)");
        b.Property(e => e.DeliveryInstructions).HasColumnName("delivery_instructions");
        b.Property(e => e.IsDefault).HasColumnName("is_default").IsRequired();
        b.Property(e => e.IsVerified).HasColumnName("is_verified").IsRequired();
        b.Property(e => e.ServiceableStoreId).HasColumnName("serviceable_store_id");
        b.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
        b.Property(e => e.UseCount).HasColumnName("use_count").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.HasOne(e => e.Customer)
            .WithMany(c => c.Addresses)
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("customer_addresses_customer_id_fkey");

        b.HasOne(e => e.ServiceableStore)
            .WithMany()
            .HasForeignKey(e => e.ServiceableStoreId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("customer_addresses_serviceable_store_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
