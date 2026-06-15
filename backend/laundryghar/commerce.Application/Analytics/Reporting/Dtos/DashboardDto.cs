namespace commerce.Application.Analytics.Reporting.Dtos;

/// <summary>
/// Admin dashboard summary. Shape preserved verbatim from the legacy anonymous projection:
/// today's revenue totals (mv_daily_store_revenue), this-month totals
/// (mv_monthly_franchise_revenue), and the top 5 customers by lifetime revenue (mv_customer_ltv).
/// </summary>
public sealed record DashboardDto(
    DashboardTodayDto Today,
    DashboardThisMonthDto ThisMonth,
    IReadOnlyList<DashboardTopCustomerDto> TopCustomersByLtv);

public sealed record DashboardTodayDto(
    long OrdersCount,
    decimal GrossRevenue,
    decimal CollectedAmount,
    long UniqueCustomers);

public sealed record DashboardThisMonthDto(
    long OrdersCount,
    decimal GrossRevenue,
    decimal NetRevenue);

public sealed record DashboardTopCustomerDto(
    Guid CustomerId,
    string? CustomerSegment,
    decimal LifetimeRevenue,
    long LifetimeOrders);
