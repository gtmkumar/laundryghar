namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>
/// The SELLABLE catalogue (identity_access.features) — what a plan grants and a brand owns.
/// PLATFORM_STRATEGY.md §5.
///
/// <para>Split from <see cref="AppModule"/> (the NAVIGATION catalogue) by migration 0005, because
/// the two were one table and that made 8 of the 17 features §5 sells unmodellable: a provider buys
/// <c>custom_domain</c>, <c>api_access</c> and <c>white_label_app</c>, but none of them is a menu
/// entry. A feature may have many modules, one, or none at all.</para>
/// </summary>
public class AppFeature
{
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>The vertical this feature belongs to (<c>laundry</c>/<c>salon</c>/<c>logistics</c>/
    /// <c>tiffin</c>), or <c>null</c> for a vertical-neutral feature available to every brand.</summary>
    public string? VerticalKey { get; set; }

    /// <summary>Always-on: entitlement is bypassed, so a brand can never "unbuy" it and lock its
    /// own admins out.</summary>
    public bool IsCore { get; set; }

    /// <summary>On the price list. False for the features auto-created from existing modules by
    /// 0005 — they exist so entitlement is total, not because they are sold on their own.</summary>
    public bool IsSellable { get; set; } = true;

    public string Status { get; set; } = "active";
    public int SortOrder { get; set; } = 100;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// PaaS entitlement: which features a brand has licensed (identity_access.brand_feature).
/// Effective access = entitlement ∩ authorization. Brand-scoped (RLS).
/// Replaces the module-keyed <c>brand_module</c> as of migration 0005.
/// </summary>
public class BrandFeature
{
    public Guid BrandId { get; set; }
    public string FeatureKey { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    /// <summary>NULL = perpetual; a past date = expired (treated as not entitled).</summary>
    public DateOnly? ValidUntil { get; set; }
    /// <summary>'bundle' = granted by a plan; 'manual' = a per-brand add-on or exception. A plan
    /// change re-expands the 'bundle' rows and must never disturb the 'manual' ones.</summary>
    public string Source { get; set; } = "manual";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
