using laundryghar.SharedDataModel.Entities.Logistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Logistics;

public sealed class RiderAssignmentConfiguration : IEntityTypeConfiguration<RiderAssignment>
{
    public void Configure(EntityTypeBuilder<RiderAssignment> b)
    {
        b.ToTable("rider_assignments", "logistics");

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.RiderId).HasColumnName("rider_id").IsRequired();
        b.Property(e => e.BrandId).HasColumnName("brand_id").IsRequired();
        b.Property(e => e.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(e => e.ShiftDate).HasColumnName("shift_date").IsRequired();
        b.Property(e => e.ShiftStart).HasColumnName("shift_start").HasColumnType("time without time zone").IsRequired();
        b.Property(e => e.ShiftEnd).HasColumnName("shift_end").HasColumnType("time without time zone").IsRequired();
        b.Property(e => e.ActualStartAt).HasColumnName("actual_start_at");
        b.Property(e => e.ActualEndAt).HasColumnName("actual_end_at");
        b.Property(e => e.MaxPickups).HasColumnName("max_pickups").IsRequired();
        b.Property(e => e.MaxDeliveries).HasColumnName("max_deliveries").IsRequired();
        b.Property(e => e.CompletedPickups).HasColumnName("completed_pickups").IsRequired();
        b.Property(e => e.CompletedDeliveries).HasColumnName("completed_deliveries").IsRequired();
        b.Property(e => e.FailedAttempts).HasColumnName("failed_attempts").IsRequired();
        b.Property(e => e.TotalDistanceKm).HasColumnName("total_distance_km").HasColumnType("numeric(8,2)");
        b.Property(e => e.Earnings).HasColumnName("earnings").HasColumnType("numeric(14,2)");
        b.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(e => e.Notes).HasColumnName("notes");
        b.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(e => e.CreatedBy).HasColumnName("created_by");

        b.HasIndex(e => new { e.RiderId, e.ShiftDate, e.ShiftStart }).IsUnique()
            .HasDatabaseName("rider_assignments_rider_id_shift_date_shift_start_key");

        b.HasOne(e => e.Rider).WithMany(r => r.Assignments).HasForeignKey(e => e.RiderId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("rider_assignments_rider_id_fkey");

        b.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("rider_assignments_brand_id_fkey");

        b.HasOne(e => e.Store).WithMany().HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("rider_assignments_store_id_fkey");
    }
}
