using laundryghar.SharedDataModel.Entities.Logistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Logistics;

/// <summary>
/// Maps <see cref="PartnerDispatch"/> to logistics.partner_dispatches (snake_case). Indexed on
/// partner_id + brand_id + partner_booking_id — the two RLS arms plus the booking lookup. The only
/// FK is partner_booking_id → partner_bookings(id); brand_id and rider_id are scalar-only
/// cross-references (no FK), matching the partner_bookings conventions.
/// </summary>
public sealed class PartnerDispatchConfiguration : IEntityTypeConfiguration<PartnerDispatch>
{
    public void Configure(EntityTypeBuilder<PartnerDispatch> b)
    {
        b.ToTable("partner_dispatches", "logistics");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.PartnerId).HasColumnName("partner_id").IsRequired();
        b.Property(e => e.PartnerBookingId).HasColumnName("partner_booking_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id");
        b.Property(e => e.RiderId).HasColumnName("rider_id");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        b.Property(e => e.PickupOtp).HasColumnName("pickup_otp").HasMaxLength(10);
        b.Property(e => e.DropOtp).HasColumnName("drop_otp").HasMaxLength(10);
        b.Property(e => e.PickupVerifiedAt).HasColumnName("pickup_verified_at");
        b.Property(e => e.DropVerifiedAt).HasColumnName("drop_verified_at");

        b.Property(e => e.ProofPhotoUrl).HasColumnName("proof_photo_url").HasMaxLength(1000);
        b.Property(e => e.ProofSignatureUrl).HasColumnName("proof_signature_url").HasMaxLength(1000);

        b.Property(e => e.LastKnownLat).HasColumnName("last_known_lat").HasColumnType("numeric(10,7)");
        b.Property(e => e.LastKnownLng).HasColumnName("last_known_lng").HasColumnType("numeric(10,7)");
        b.Property(e => e.LastLocationAt).HasColumnName("last_location_at");

        b.Property(e => e.AssignedAt).HasColumnName("assigned_at");

        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => e.PartnerId).HasDatabaseName("idx_partner_dispatches_partner");
        b.HasIndex(e => e.BrandId).HasDatabaseName("idx_partner_dispatches_brand");
        b.HasIndex(e => e.PartnerBookingId).HasDatabaseName("idx_partner_dispatches_booking");

        // Only FK: partner_booking_id → partner_bookings(id). Scalar (no navigation) — the FK RI
        // check runs as the table owner and so is unaffected by rls_partner on partner_bookings.
        // ON DELETE CASCADE: a booking's dispatches are part of its subtree.
        b.HasOne<PartnerBooking>()
            .WithMany()
            .HasForeignKey(e => e.PartnerBookingId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("partner_dispatches_partner_booking_id_fkey");
    }
}
