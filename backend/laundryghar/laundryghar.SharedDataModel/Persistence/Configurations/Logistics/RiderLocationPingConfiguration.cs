using laundryghar.SharedDataModel.Entities.Logistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Logistics;

public sealed class RiderLocationPingConfiguration : IEntityTypeConfiguration<RiderLocationPing>
{
    public void Configure(EntityTypeBuilder<RiderLocationPing> b)
    {
        b.ToTable("rider_location_pings", "logistics");

        // Composite PK required by PG range partitioning on pinged_at
        b.HasKey(e => new { e.Id, e.PingedAt });
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.PingedAt).HasColumnName("pinged_at").IsRequired();

        b.Property(e => e.RiderId).HasColumnName("rider_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.Location).HasColumnName("location").HasColumnType("geography(Point,4326)").IsRequired();
        b.Property(e => e.AccuracyMeters).HasColumnName("accuracy_meters").HasColumnType("numeric(8,2)");
        b.Property(e => e.SpeedKmph).HasColumnName("speed_kmph").HasColumnType("numeric(6,2)");
        b.Property(e => e.HeadingDegrees).HasColumnName("heading_degrees").HasColumnType("numeric(5,2)");
        b.Property(e => e.BatteryPercent).HasColumnName("battery_percent");
        b.Property(e => e.IsMoving).HasColumnName("is_moving");
        b.Property(e => e.ActivityType).HasColumnName("activity_type").HasMaxLength(20);
        b.Property(e => e.CurrentAssignmentId).HasColumnName("current_assignment_id");
        b.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasOne(e => e.Rider).WithMany(r => r.LocationPings).HasForeignKey(e => e.RiderId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("rider_location_pings_rider_id_fkey");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("rider_location_pings_brand_id_fkey");

        // ON DELETE SET NULL
        b.HasOne(e => e.CurrentAssignment).WithMany(a => a.LocationPings)
            .HasForeignKey(e => e.CurrentAssignmentId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("rider_location_pings_current_assignment_id_fkey");
    }
}
