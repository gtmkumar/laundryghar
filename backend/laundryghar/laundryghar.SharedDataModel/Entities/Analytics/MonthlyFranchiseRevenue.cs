namespace laundryghar.SharedDataModel.Entities.Analytics;

/// <summary>
/// Read-only projection of analytics.mv_monthly_franchise_revenue.
/// Keyless entity — never tracked for writes.
/// </summary>
public sealed class MonthlyFranchiseRevenue
{
    public Guid BrandId           { get; set; }
    public Guid FranchiseId       { get; set; }
    public DateOnly RevenueMonth  { get; set; }
    public long OrdersCount       { get; set; }
    public long UniqueCustomers   { get; set; }
    public decimal GrossRevenue   { get; set; }
    public decimal NetRevenue     { get; set; }
    public decimal CollectedAmount { get; set; }
    public decimal RefundAmount   { get; set; }
    public decimal TotalTax       { get; set; }
    public decimal AvgOrderValue  { get; set; }
    public long ExpressOrders     { get; set; }
}
