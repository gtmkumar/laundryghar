using laundryghar.SharedDataModel.Crypto;
using laundryghar.SharedDataModel.Entities.Logistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Logistics;

public sealed class RiderConfiguration : IEntityTypeConfiguration<Rider>
{
    public void Configure(EntityTypeBuilder<Rider> b)
    {
        b.ToTable("riders", "logistics");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id").IsRequired();
        b.Property(e => e.PrimaryStoreId).HasColumnName("primary_store_id");
        b.Property(e => e.RiderCode).HasColumnName("rider_code").HasMaxLength(30).IsRequired();
        b.Property(e => e.EmploymentType).HasColumnName("employment_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.AadhaarNumberMasked).HasColumnName("aadhaar_number_masked").HasMaxLength(20);
        b.Property(e => e.DrivingLicenseNumber).HasColumnName("driving_license_number").HasMaxLength(50);
        b.Property(e => e.DlExpiryDate).HasColumnName("dl_expiry_date");
        b.Property(e => e.VehicleType).HasColumnName("vehicle_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.VehicleNumber).HasColumnName("vehicle_number").HasMaxLength(20);
        b.Property(e => e.VehicleModel).HasColumnName("vehicle_model").HasMaxLength(100);
        b.Property(e => e.InsuranceExpiryDate).HasColumnName("insurance_expiry_date");
        b.Property(e => e.BankAccountName).HasColumnName("bank_account_name").HasMaxLength(200);

        // ── PII columns: AES-256-GCM encrypted at rest ──────────────────────────
        // Column type widened to text via db patch pii_encryption_column_widening.sql.
        // PiiValueConverter.Instance is set before the context is first constructed by
        // AddSharedDataModel(). Null guard allows unit tests that skip DI.
        if (PiiValueConverter.Instance is { } conv)
        {
            b.Property(e => e.PanNumber)
                .HasColumnName("pan_number")
                .HasConversion(conv);
            b.Property(e => e.BankAccountNumber)
                .HasColumnName("bank_account_number")
                .HasConversion(conv);
            b.Property(e => e.UpiId)
                .HasColumnName("upi_id")
                .HasConversion(conv);
        }
        else
        {
            b.Property(e => e.PanNumber).HasColumnName("pan_number").HasMaxLength(10);
            b.Property(e => e.BankAccountNumber).HasColumnName("bank_account_number").HasMaxLength(50);
            b.Property(e => e.UpiId).HasColumnName("upi_id").HasMaxLength(100);
        }

        // IFSC: publicly listed branch code, not encrypted.
        b.Property(e => e.BankIfsc).HasColumnName("bank_ifsc").HasMaxLength(11);
        b.Property(e => e.DailyPickupCapacity).HasColumnName("daily_pickup_capacity").IsRequired();
        b.Property(e => e.DailyDeliveryCapacity).HasColumnName("daily_delivery_capacity").IsRequired();
        b.Property(e => e.ServiceRadiusKm).HasColumnName("service_radius_km").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(e => e.RatingAverage).HasColumnName("rating_average").HasColumnType("numeric(3,2)");
        b.Property(e => e.RatingCount).HasColumnName("rating_count").IsRequired();
        b.Property(e => e.CompletionRate).HasColumnName("completion_rate").HasColumnType("numeric(5,2)");
        b.Property(e => e.LifetimeDeliveries).HasColumnName("lifetime_deliveries").IsRequired();
        b.Property(e => e.LastKnownLocation).HasColumnName("last_known_location").HasColumnType("geography(Point,4326)");
        b.Property(e => e.LastPingAt).HasColumnName("last_ping_at");
        b.Property(e => e.IsOnline).HasColumnName("is_online").IsRequired();
        b.Property(e => e.IsOnDuty).HasColumnName("is_on_duty").IsRequired();
        b.Property(e => e.OnDutySince).HasColumnName("on_duty_since");
        b.Property(e => e.CurrentLoad).HasColumnName("current_load").IsRequired();
        b.Property(e => e.KycStatus).HasColumnName("kyc_status").HasMaxLength(20).IsRequired();
        b.Property(e => e.KycVerifiedAt).HasColumnName("kyc_verified_at");
        b.Property(e => e.VehicleVerificationStatus).HasColumnName("vehicle_verification_status").HasMaxLength(20).IsRequired();
        b.Property(e => e.VehicleVerifiedAt).HasColumnName("vehicle_verified_at");
        b.Property(e => e.VehicleVerifiedBy).HasColumnName("vehicle_verified_by");
        b.Property(e => e.VehicleRejectionReason).HasColumnName("vehicle_rejection_reason");
        b.Property(e => e.OnboardedAt).HasColumnName("onboarded_at");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        b.Property(e => e.DeletedAt).HasColumnName("deleted_at");

        b.HasIndex(e => e.UserId).IsUnique().HasDatabaseName("riders_user_id_key");
        b.HasIndex(e => new { e.BrandId, e.RiderCode }).IsUnique().HasDatabaseName("riders_brand_id_rider_code_key");

        // FK: identity_access.users (cross-BC — nav property mapped in this library)
        b.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("riders_user_id_fkey");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("riders_brand_id_fkey");

        b.HasOne(e => e.Franchise).WithMany().HasForeignKey(e => e.FranchiseId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("riders_franchise_id_fkey");

        // ON DELETE not specified for primary_store_id FK in DB (defaults to NO ACTION)
        b.HasOne(e => e.PrimaryStore).WithMany().HasForeignKey(e => e.PrimaryStoreId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("riders_primary_store_id_fkey");

        b.HasQueryFilter(e => e.DeletedAt == null);
    }
}
