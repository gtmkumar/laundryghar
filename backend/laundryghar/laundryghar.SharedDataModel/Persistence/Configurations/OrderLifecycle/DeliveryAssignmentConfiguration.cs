using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.OrderLifecycle;

public sealed class DeliveryAssignmentConfiguration : IEntityTypeConfiguration<DeliveryAssignment>
{
    public void Configure(EntityTypeBuilder<DeliveryAssignment> b)
    {
        b.ToTable("delivery_assignments", "order_lifecycle");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(e => e.RiderId).HasColumnName("rider_id").IsRequired();
        // Scalar-only composite FK columns to orders (nullable FK — no composite nav)
        b.Property(e => e.OrderId).HasColumnName("order_id");
        b.Property(e => e.OrderCreatedAt).HasColumnName("order_created_at");
        b.Property(e => e.PickupRequestId).HasColumnName("pickup_request_id");
        b.Property(e => e.LegType).HasColumnName("leg_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.SequenceNumber).HasColumnName("sequence_number");
        b.Property(e => e.AssignedAt).HasColumnName("assigned_at").IsRequired();
        b.Property(e => e.AssignedBy).HasColumnName("assigned_by");
        b.Property(e => e.AcceptedAt).HasColumnName("accepted_at");
        b.Property(e => e.StartedAt).HasColumnName("started_at");
        b.Property(e => e.ArrivedAt).HasColumnName("arrived_at");
        b.Property(e => e.CollectedAt).HasColumnName("collected_at");
        b.Property(e => e.DroppedAt).HasColumnName("dropped_at");
        b.Property(e => e.CompletedAt).HasColumnName("completed_at");
        b.Property(e => e.CancelledAt).HasColumnName("cancelled_at");
        b.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
        b.Property(e => e.AddressSnapshot).HasColumnName("address_snapshot").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.GeoLocation).HasColumnName("geo_location").HasColumnType("geography(Point,4326)");
        b.Property(e => e.DistanceKm).HasColumnName("distance_km").HasColumnType("numeric(6,2)");
        b.Property(e => e.DurationMinutes).HasColumnName("duration_minutes");
        b.Property(e => e.CodAmount).HasColumnName("cod_amount").HasColumnType("numeric(10,2)");
        b.Property(e => e.CodCollectedAt).HasColumnName("cod_collected_at");
        b.Property(e => e.SettlementId).HasColumnName("settlement_id");
        b.Property(e => e.OtpVerified).HasColumnName("otp_verified").IsRequired();
        b.Property(e => e.OtpAttemptedAt).HasColumnName("otp_attempted_at");
        b.Property(e => e.SignatureS3Key).HasColumnName("signature_s3_key");
        b.Property(e => e.ProofPhotoS3Key).HasColumnName("proof_photo_s3_key");
        b.Property(e => e.CustomerSignature).HasColumnName("customer_signature");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("delivery_assignments_brand_id_fkey");
        b.HasOne(e => e.Store).WithMany().HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("delivery_assignments_store_id_fkey");
        b.HasOne(e => e.PickupRequest).WithMany(p => p.DeliveryAssignments)
            .HasForeignKey(e => e.PickupRequestId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("delivery_assignments_pickup_request_id_fkey");
        // order_id FK is composite + nullable — configured as scalar-only (no navigation to avoid EF composite nav constraints)
    }
}
