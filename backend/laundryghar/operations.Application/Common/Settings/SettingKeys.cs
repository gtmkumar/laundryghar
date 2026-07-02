namespace operations.Application.Common.Settings;

/// <summary>
/// Category names for scope-aware business-rule settings stored in
/// <c>kernel.system_settings</c> (category + setting_key uniquely identify a rule within a scope).
/// Kept as constants so producers (the admin settings API) and consumers (order pricing, fare,
/// logistics) agree on the exact strings — a typo silently yields "no row → fallback".
/// </summary>
public static class SettingCategories
{
    public const string Orders    = "orders";
    public const string Catalog   = "catalog";
    public const string Logistics = "logistics";
}

/// <summary>
/// The business-rule setting keys the platform recognises, grouped by category.
/// Only the five keys marked "(seeded)" have a platform default row — every other key
/// resolves to <c>null</c> until an operator sets it (deliberate: e.g. min_order_value must
/// have no default anywhere, per product decision).
/// </summary>
public static class SettingKeys
{
    // ── category "orders" ────────────────────────────────────────────────────
    public const string TaxRatePercent                 = "tax_rate_percent";              // (seeded 18)
    public const string ExpressSurchargePercent        = "express_surcharge_percent";     // (seeded 50)
    public const string DefaultTatHours                = "default_tat_hours";             // (seeded 48)
    public const string ExpressTatHours                = "express_tat_hours";             // (seeded 24)
    public const string CurrencyCode                   = "currency_code";                 // (seeded INR)
    public const string MinOrderValue                  = "min_order_value";               // no default anywhere
    public const string CancellationFee                = "cancellation_fee";
    public const string CancellationFreeWindowMinutes  = "cancellation_free_window_minutes";

    // ── category "catalog" ───────────────────────────────────────────────────
    public const string HighValueGarmentThreshold      = "high_value_garment_threshold";

    // ── category "logistics" ─────────────────────────────────────────────────
    public const string FreePickupRadiusKm             = "free_pickup_radius_km";
    public const string WaitingFreeMinutes             = "waiting_free_minutes";
    public const string WaitingPerMinuteRate           = "waiting_per_minute_rate";
}
