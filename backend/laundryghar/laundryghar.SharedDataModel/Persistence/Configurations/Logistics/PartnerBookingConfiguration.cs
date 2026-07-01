using laundryghar.SharedDataModel.Entities.Logistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Logistics;

public sealed class PartnerBookingConfiguration : IEntityTypeConfiguration<PartnerBooking>
{
    public void Configure(EntityTypeBuilder<PartnerBooking> b)
    {
        b.ToTable("partner_bookings", "logistics");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.PartnerId).HasColumnName("partner_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id");
        b.Property(e => e.CreatedByPartnerUserId).HasColumnName("created_by_partner_user_id").IsRequired();
        b.Property(e => e.PickupSnapshot).HasColumnName("pickup_snapshot").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.DropSnapshot).HasColumnName("drop_snapshot").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.QuotedFare).HasColumnName("quoted_fare").HasColumnType("numeric(14,2)");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");
        b.Property(e => e.UpdatedBy).HasColumnName("updated_by");

        b.HasIndex(e => e.PartnerId).HasDatabaseName("idx_partner_bookings_partner");
        b.HasIndex(e => new { e.PartnerId, e.Status }).HasDatabaseName("idx_partner_bookings_partner_status");

        b.HasOne(e => e.Partner).WithMany(p => p.Bookings).HasForeignKey(e => e.PartnerId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("partner_bookings_partner_id_fkey");

        // created_by_partner_user_id → partner_users(id). RESTRICT: keep the audit trail intact
        // (a partner user cannot be hard-deleted while their bookings exist).
        b.HasOne(e => e.CreatedByPartnerUser).WithMany().HasForeignKey(e => e.CreatedByPartnerUserId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("partner_bookings_created_by_partner_user_id_fkey");
    }
}
