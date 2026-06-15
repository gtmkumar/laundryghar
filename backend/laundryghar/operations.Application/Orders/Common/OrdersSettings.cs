namespace operations.Application.Orders.Common;

/// <summary>
/// Bound from the <c>Orders</c> configuration section. Drives tax rate, express surcharge,
/// default currency, and the TAT / "stuck"-queue thresholds for the Orders sub-domain.
/// When no <c>Orders</c> section is present the defaults below apply.
/// </summary>
public sealed class OrdersSettings
{
    public const string SectionName = "Orders";
    public decimal TaxRatePercent { get; set; } = 18m;
    public decimal ExpressSurchargePercent { get; set; } = 50m;
    public string DefaultCurrencyCode { get; set; } = "INR";

    // ── TAT / Promised-date settings ─────────────────────────────────────────
    /// <summary>
    /// Default turnaround time in hours for standard orders when no service-level
    /// TAT is available (e.g. legacy orders with unknown services).
    /// Corresponds to <c>Orders:DefaultTatHours</c> in appsettings.
    /// </summary>
    public int DefaultTatHours { get; set; } = 48;

    /// <summary>
    /// Fallback TAT in hours for express orders when no service-level express TAT
    /// is available.  Corresponds to <c>Orders:ExpressTatHours</c> in appsettings.
    /// </summary>
    public int ExpressTatHours { get; set; } = 24;

    /// <summary>
    /// An order whose last <c>order_status_history</c> entry is older than this many
    /// hours (and is still non-terminal) is surfaced in the "stuck" ops queue.
    /// Corresponds to <c>Orders:StuckThresholdHours</c> in appsettings.
    /// </summary>
    public int StuckThresholdHours { get; set; } = 24;
}
