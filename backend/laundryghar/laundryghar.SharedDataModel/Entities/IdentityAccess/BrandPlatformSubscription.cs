namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>
/// The BRAND's (tenant's) platform subscription — which priced <see cref="ModuleBundle"/> tier the brand
/// is on (identity_access.brand_platform_subscription). Created/updated by ApplyBundleToBrand (price etc.
/// snapshotted from the bundle at apply-time) and billed for renewals by BrandPlatformBillingService.
/// One row per brand.
///
/// Distinct from commerce.customer_subscriptions (a CUSTOMER of a brand) and
/// finance_royalty.franchise_subscriptions (a FRANCHISE's SaaS fee) — this is the PLATFORM→BRAND axis.
/// </summary>
public class BrandPlatformSubscription
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }

    /// <summary>The module_bundle tier the brand is subscribed to.</summary>
    public string BundleCode { get; set; } = null!;
    public string PlanName { get; set; } = null!;

    // Snapshot of the tier's commercials at subscribe/upgrade time (price changes don't rewrite history).
    public decimal Price { get; set; }
    public string BillingInterval { get; set; } = "monthly";
    public string CurrencyCode { get; set; } = "INR";

    /// <summary><c>active</c> | <c>cancelled</c>.</summary>
    public string Status { get; set; } = "active";

    public DateTimeOffset CurrentPeriodStart { get; set; }
    public DateTimeOffset CurrentPeriodEnd { get; set; }
    public DateTimeOffset NextBillingAt { get; set; }
    public bool AutoRenew { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
