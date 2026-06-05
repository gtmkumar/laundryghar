using laundryghar.SharedDataModel.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Analytics;

public sealed class WarehouseThroughputConfiguration : IEntityTypeConfiguration<WarehouseThroughput>
{
    public void Configure(EntityTypeBuilder<WarehouseThroughput> b)
    {
        b.HasNoKey().ToView("mv_warehouse_throughput", "analytics");

        b.Property(e => e.BrandId).HasColumnName("brand_id");
        b.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
        b.Property(e => e.ThroughputDate).HasColumnName("throughput_date");
        b.Property(e => e.GarmentsReceived).HasColumnName("garments_received");
        b.Property(e => e.GarmentsDelivered).HasColumnName("garments_delivered");
        b.Property(e => e.IssuesCount).HasColumnName("issues_count");
        b.Property(e => e.RewashCount).HasColumnName("rewash_count");
        b.Property(e => e.AvgTatHours).HasColumnName("avg_tat_hours").HasColumnType("numeric");
    }
}
