namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// The unit a subscription plan's quota is metered in (<c>commerce.subscription_plans.quota_type</c>).
/// Neutralized in multi-vertical Phase 2 (slice 2E): the laundry-flavoured <c>order_count</c> gains a
/// vertical-neutral successor <c>job_count</c> (a "job" is any fulfilment unit of work — a wash order,
/// a salon appointment, a parcel trip). <c>order_count</c> is retained for data compatibility.
/// </summary>
public static class QuotaUnit
{
    /// <summary>Monetary credit consumed per use.</summary>
    public const string Credit = "credit";

    /// <summary>Laundry-flavoured per-order count — retained for data compatibility.</summary>
    public const string OrderCount = "order_count";

    /// <summary>Vertical-neutral per-job count (the successor to <see cref="OrderCount"/>).</summary>
    public const string JobCount = "job_count";

    /// <summary>Metered by weight in kilograms (laundry).</summary>
    public const string WeightKg = "weight_kg";

    /// <summary>Metered by service minutes (salon appointments). (Phase 4.)</summary>
    public const string ServiceMinutes = "service_minutes";

    /// <summary>No quota cap.</summary>
    public const string Unlimited = "unlimited";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Credit, OrderCount, JobCount, WeightKg, ServiceMinutes, Unlimited };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
