namespace laundryghar.SharedDataModel.Entities.Analytics;

/// <summary>
/// Read-only projection of analytics.mv_customer_ltv.
/// Keyless entity — never tracked for writes.
/// </summary>
public sealed class CustomerLtv
{
    public Guid BrandId               { get; set; }
    public Guid CustomerId            { get; set; }
    public string CustomerSegment     { get; set; } = null!;
    public long LifetimeOrders        { get; set; }
    public decimal LifetimeRevenue    { get; set; }
    public decimal AvgOrderValue      { get; set; }
    public DateTimeOffset FirstOrderAt  { get; set; }
    public DateTimeOffset LastOrderAt   { get; set; }
    public decimal DaysSinceLastOrder   { get; set; }
    public long ExpressOrders         { get; set; }
    public long CancelledOrders       { get; set; }
    public long ActivePackages        { get; set; }
    public int LoyaltyPointsBalance   { get; set; }
    public decimal WalletBalance      { get; set; }
}
