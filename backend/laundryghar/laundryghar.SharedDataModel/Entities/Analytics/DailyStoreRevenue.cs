namespace laundryghar.SharedDataModel.Entities.Analytics;

/// <summary>
/// Read-only projection of analytics.mv_daily_store_revenue.
/// Keyless entity — never tracked for writes.
/// </summary>
public sealed class DailyStoreRevenue
{
    public Guid BrandId           { get; set; }
    public Guid FranchiseId       { get; set; }
    public Guid StoreId           { get; set; }
    public DateOnly RevenueDate   { get; set; }
    public long OrdersCount       { get; set; }
    public long DeliveredOrders   { get; set; }
    public long CancelledOrders   { get; set; }
    public long ExpressOrders     { get; set; }
    public decimal GrossRevenue   { get; set; }
    public decimal CollectedAmount  { get; set; }
    public decimal OutstandingAmount { get; set; }
    public decimal RefundAmount   { get; set; }
    public decimal TotalDiscount  { get; set; }
    public decimal TotalTax       { get; set; }
    public decimal AvgOrderValue  { get; set; }
    public long UniqueCustomers   { get; set; }
}
