namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>Platform catalogue of plan→feature bundles (identity_access.module_bundle).
/// Used to expand into per-brand <see cref="BrandFeature"/> rows at onboarding / plan
/// change. Global (no RLS), like <see cref="AppFeature"/>.
/// <para>Its items became FEATURES rather than modules in migration 0005 — the table keeps its
/// historical name, but a plan now packages what a provider BUYS, not what appears in their
/// menu.</para></summary>
public class ModuleBundle
{
    public string Code { get; set; } = null!;   // 'starter','pro','enterprise'
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    /// <summary>The vertical this bundle targets (<c>laundry</c>/<c>salon</c>/<c>logistics</c>),
    /// or <c>null</c> for a vertical-neutral tier bundle applicable to any vertical. (Phase 2.)</summary>
    public string? VerticalKey { get; set; }

    // ── Brand-tier commercial metadata ──────────────────────────────────────
    // A bundle is the BRAND-level platform tier: applying it both licenses features (BrandFeature)
    // and records what that tier costs the tenant — keeping price ↔ features on ONE object in the
    // same (core) context as entitlement. Distinct from finance_royalty.platform_plans, which are
    // the FRANCHISE-level SaaS tiers (a different payer/axis).

    /// <summary>Recurring price the platform charges a brand for this tier; null = unpriced/custom.</summary>
    public decimal? Price { get; set; }
    /// <summary>Billing cadence for <see cref="Price"/> (e.g. <c>monthly</c>/<c>quarterly</c>/<c>yearly</c>).</summary>
    public string? BillingInterval { get; set; }
    /// <summary>ISO currency for <see cref="Price"/> (e.g. <c>INR</c>).</summary>
    public string? CurrencyCode { get; set; }
    /// <summary>Whether the tier is offered in the public/self-serve catalogue (vs internal-only).</summary>
    public bool IsPublic { get; set; } = true;

    public ICollection<BundleFeature> Items { get; set; } = new List<BundleFeature>();
}

/// <summary>A feature included in a <see cref="ModuleBundle"/> (identity_access.bundle_feature).
/// Replaces the module-keyed <c>module_bundle_item</c> as of migration 0005.</summary>
public class BundleFeature
{
    public string BundleCode { get; set; } = null!;
    public string FeatureKey { get; set; } = null!;
}
