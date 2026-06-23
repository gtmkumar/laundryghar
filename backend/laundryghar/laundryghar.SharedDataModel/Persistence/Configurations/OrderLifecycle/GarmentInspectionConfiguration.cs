using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class GarmentInspectionConfiguration : IEntityTypeConfiguration<GarmentInspection>
{
    public void Configure(EntityTypeBuilder<GarmentInspection> b)
    {
        b.ToTable("garment_inspections", "laundry_fulfillment");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.GarmentId).HasColumnName("garment_id").IsRequired();
        // Scalar-only composite FK columns — nav skipped to avoid conflict with garment→order nav
        b.Property(e => e.OrderId).HasColumnName("order_id").IsRequired();
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at").IsRequired();
        b.Property(e => e.InspectedByUserId).HasColumnName("inspected_by_user_id");
        b.Property(e => e.InspectedByType).HasColumnName("inspected_by_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.InspectionType).HasColumnName("inspection_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.InspectedAt).HasColumnName("inspected_at").IsRequired();
        b.Property(e => e.LocationType).HasColumnName("location_type").HasMaxLength(20);
        b.Property(e => e.LocationId).HasColumnName("location_id");
        b.Property(e => e.GeoLocation).HasColumnName("geo_location").HasColumnType("geography(Point,4326)");
        b.Property(e => e.Conditions).HasColumnName("conditions").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.OverallCondition).HasColumnName("overall_condition").HasMaxLength(20);
        b.Property(e => e.IssuesCount).HasColumnName("issues_count").IsRequired();
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.CustomerAcknowledged).HasColumnName("customer_acknowledged").IsRequired();
        b.Property(e => e.CustomerAcknowledgedAt).HasColumnName("customer_acknowledged_at");
        b.Property(e => e.CustomerSignatureS3Key).HasColumnName("customer_signature_s3_key");
        b.Property(e => e.CustomerOtpVerified).HasColumnName("customer_otp_verified").IsRequired();
        b.Property(e => e.QcResult).HasColumnName("qc_result").HasMaxLength(20);
        b.Property(e => e.QcFailureReason).HasColumnName("qc_failure_reason");
        b.Property(e => e.RewashCount).HasColumnName("rewash_count").IsRequired();
        b.Property(e => e.RequiresSpecialCare).HasColumnName("requires_special_care").IsRequired();
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("garment_inspections_brand_id_fkey");
        b.HasOne(e => e.Garment).WithMany(g => g.Inspections)
            .HasForeignKey(e => e.GarmentId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("garment_inspections_garment_id_fkey");
    }
}
