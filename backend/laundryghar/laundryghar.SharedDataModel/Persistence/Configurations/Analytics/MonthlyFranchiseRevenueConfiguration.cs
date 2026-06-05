using laundryghar.SharedDataModel.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Analytics;

public sealed class MonthlyFranchiseRevenueConfiguration : IEntityTypeConfiguration<MonthlyFranchiseRevenue>
{
    public void Configure(EntityTypeBuilder<MonthlyFranchiseRevenue> b)
    {
        b.HasNoKey().ToView("mv_monthly_franchise_revenue", "analytics");

        b.Property(e => e.BrandId).HasColumnName("brand_id");
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id");
        b.Property(e => e.RevenueMonth).HasColumnName("revenue_month");
        b.Property(e => e.OrdersCount).HasColumnName("orders_count");
        b.Property(e => e.UniqueCustomers).HasColumnName("unique_customers");
        b.Property(e => e.GrossRevenue).HasColumnName("gross_revenue").HasColumnType("numeric");
        b.Property(e => e.NetRevenue).HasColumnName("net_revenue").HasColumnType("numeric");
        b.Property(e => e.CollectedAmount).HasColumnName("collected_amount").HasColumnType("numeric");
        b.Property(e => e.RefundAmount).HasColumnName("refund_amount").HasColumnType("numeric");
        b.Property(e => e.TotalTax).HasColumnName("total_tax").HasColumnType("numeric");
        b.Property(e => e.AvgOrderValue).HasColumnName("avg_order_value").HasColumnType("numeric");
        b.Property(e => e.ExpressOrders).HasColumnName("express_orders");
    }
}
