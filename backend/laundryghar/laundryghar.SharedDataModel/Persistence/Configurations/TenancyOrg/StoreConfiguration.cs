using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.TenancyOrg;

public sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> b)
    {
        b.ToTable("stores", "tenancy_org");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();

        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id").IsRequired();
        b.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        b.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(e => e.StoreType).HasColumnName("store_type").HasMaxLength(30).IsRequired();
        b.Property(e => e.AddressLine1).HasColumnName("address_line1").HasMaxLength(255).IsRequired();
        b.Property(e => e.AddressLine2).HasColumnName("address_line2").HasMaxLength(255);
        b.Property(e => e.Landmark).HasColumnName("landmark").HasMaxLength(200);
        b.Property(e => e.City).HasColumnName("city").HasMaxLength(100).IsRequired();
        b.Property(e => e.State).HasColumnName("state").HasMaxLength(100).IsRequired();
        b.Property(e => e.Pincode).HasColumnName("pincode").HasMaxLength(10).IsRequired();
        b.Property(e => e.CountryCode).HasColumnName("country_code").HasColumnType("character(2)").IsRequired();
        b.Property(e => e.GeoLocation).HasColumnName("geo_location").HasColumnType("geography(Point,4326)");
        b.Property(e => e.ServiceRadiusKm).HasColumnName("service_radius_km").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.ContactPhone).HasColumnName("contact_phone").HasMaxLength(20);
        b.Property(e => e.ContactEmail).HasColumnName("contact_email").HasColumnType("citext");
        b.Property(e => e.TollFreeNumber).HasColumnName("toll_free_number").HasMaxLength(20);
        b.Property(e => e.WhatsappNumber).HasColumnName("whatsapp_number").HasMaxLength(20);
        b.Property(e => e.ManagerUserId).HasColumnName("manager_user_id");
        b.Property(e => e.Timezone).HasColumnName("timezone").HasMaxLength(50).IsRequired();
        b.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasColumnType("character(3)").IsRequired();
        b.Property(e => e.DailyPickupCapacity).HasColumnName("daily_pickup_capacity").IsRequired();
        b.Property(e => e.DailyDeliveryCapacity).HasColumnName("daily_delivery_capacity").IsRequired();
        b.Property(e => e.SlotDurationMinutes).HasColumnName("slot_duration_minutes").IsRequired();
        b.Property(e => e.AcceptsExpress).HasColumnName("accepts_express").IsRequired();
        b.Property(e => e.AcceptsCod).HasColumnName("accepts_cod").IsRequired();
        b.Property(e => e.AcceptsWalkin).HasColumnName("accepts_walkin").IsRequired();
        b.Property(e => e.GooglePlaceId).HasColumnName("google_place_id").HasMaxLength(100);
        b.Property(e => e.RatingAverage).HasColumnName("rating_average").HasColumnType("numeric(3,2)");
        b.Property(e => e.RatingCount).HasColumnName("rating_count").IsRequired();
        b.Property(e => e.Config).HasColumnName("config").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.OpenedAt).HasColumnName("opened_at");
        b.Property(e => e.ClosedAt).HasColumnName("closed_at");
        b.Property(e => e.ClosureReason).HasColumnName("closure_reason");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.Version).HasColumnName("version").IsRequired();
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => new { e.BrandId, e.Code }).IsUnique().HasDatabaseName("stores_brand_id_code_key");

        b.HasOne(e => e.Brand)
            .WithMany(br => br.Stores)
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("stores_brand_id_fkey");

        b.HasOne(e => e.Franchise)
            .WithMany(f => f.Stores)
            .HasForeignKey(e => e.FranchiseId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("stores_franchise_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
