using laundryghar.SharedDataModel.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace laundryghar.SharedDataModel.Persistence.Configurations.Analytics;

public sealed class DailyStoreRevenueConfiguration : IEntityTypeConfiguration<DailyStoreRevenue>
{
    public void Configure(EntityTypeBuilder<DailyStoreRevenue> b)
    {
        b.HasNoKey().ToView("mv_daily_store_revenue", "analytics");

        b.Property(e => e.BrandId).HasColumnName("brand_id");
        b.Property(e => e.FranchiseId).HasColumnName("franchise_id");
        b.Property(e => e.StoreId).HasColumnName("store_id");
        b.Property(e => e.RevenueDate).HasColumnName("revenue_date");
        b.Property(e => e.OrdersCount).HasColumnName("orders_count");
        b.Property(e => e.DeliveredOrders).HasColumnName("delivered_orders");
        b.Property(e => e.CancelledOrders).HasColumnName("cancelled_orders");
        b.Property(e => e.ExpressOrders).HasColumnName("express_orders");
        b.Property(e => e.GrossRevenue).HasColumnName("gross_revenue").HasColumnType("numeric");
        b.Property(e => e.CollectedAmount).HasColumnName("collected_amount").HasColumnType("numeric");
        b.Property(e => e.OutstandingAmount).HasColumnName("outstanding_amount").HasColumnType("numeric");
        b.Property(e => e.RefundAmount).HasColumnName("refund_amount").HasColumnType("numeric");
        b.Property(e => e.TotalDiscount).HasColumnName("total_discount").HasColumnType("numeric");
        b.Property(e => e.TotalTax).HasColumnName("total_tax").HasColumnType("numeric");
        b.Property(e => e.AvgOrderValue).HasColumnName("avg_order_value").HasColumnType("numeric");
        b.Property(e => e.UniqueCustomers).HasColumnName("unique_customers");
    }
}
