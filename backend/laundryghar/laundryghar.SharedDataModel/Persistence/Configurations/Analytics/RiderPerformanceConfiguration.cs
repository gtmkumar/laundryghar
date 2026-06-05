using laundryghar.SharedDataModel.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Analytics;

public sealed class RiderPerformanceConfiguration : IEntityTypeConfiguration<RiderPerformance>
{
    public void Configure(EntityTypeBuilder<RiderPerformance> b)
    {
        b.HasNoKey().ToView("mv_rider_performance", "analytics");

        b.Property(e => e.BrandId).HasColumnName("brand_id");
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id");
        b.Property(e => e.RiderId).HasColumnName("rider_id");
        b.Property(e => e.RiderCode).HasColumnName("rider_code").HasMaxLength(30);
        b.Property(e => e.PerfDate).HasColumnName("perf_date");
        b.Property(e => e.AssignmentsTotal).HasColumnName("assignments_total");
        b.Property(e => e.AssignmentsCompleted).HasColumnName("assignments_completed");
        b.Property(e => e.AssignmentsFailed).HasColumnName("assignments_failed");
        b.Property(e => e.PickupsDone).HasColumnName("pickups_done");
        b.Property(e => e.DeliveriesDone).HasColumnName("deliveries_done");
        b.Property(e => e.TotalKm).HasColumnName("total_km").HasColumnType("numeric");
        b.Property(e => e.AvgDurationMin).HasColumnName("avg_duration_min").HasColumnType("numeric");
        b.Property(e => e.RatingAverage).HasColumnName("rating_average").HasColumnType("numeric(3,2)");
        b.Property(e => e.CompletionRate).HasColumnName("completion_rate").HasColumnType("numeric(5,2)");
    }
}
