using laundryghar.SharedDataModel.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Analytics;

public sealed class CustomerLtvConfiguration : IEntityTypeConfiguration<CustomerLtv>
{
    public void Configure(EntityTypeBuilder<CustomerLtv> b)
    {
        b.HasNoKey().ToView("mv_customer_ltv", "analytics");

        b.Property(e => e.BrandId).HasColumnName("brand_id");
        b.Property(e => e.CustomerId).HasColumnName("customer_id");
        b.Property(e => e.CustomerSegment).HasColumnName("customer_segment").HasMaxLength(30);
        b.Property(e => e.LifetimeOrders).HasColumnName("lifetime_orders");
        b.Property(e => e.LifetimeRevenue).HasColumnName("lifetime_revenue").HasColumnType("numeric");
        b.Property(e => e.AvgOrderValue).HasColumnName("avg_order_value").HasColumnType("numeric");
        b.Property(e => e.FirstOrderAt).HasColumnName("first_order_at");
        b.Property(e => e.LastOrderAt).HasColumnName("last_order_at");
        b.Property(e => e.DaysSinceLastOrder).HasColumnName("days_since_last_order").HasColumnType("numeric");
        b.Property(e => e.ExpressOrders).HasColumnName("express_orders");
        b.Property(e => e.CancelledOrders).HasColumnName("cancelled_orders");
        b.Property(e => e.ActivePackages).HasColumnName("active_packages");
        b.Property(e => e.LoyaltyPointsBalance).HasColumnName("loyalty_points_balance");
        b.Property(e => e.WalletBalance).HasColumnName("wallet_balance").HasColumnType("numeric(14,2)");
    }
}
