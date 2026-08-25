namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>
/// A launchable vertical (identity_access.vertical_templates) — PLATFORM_STRATEGY.md §3's
/// "mode + terminology pack + preset catalog structure + default feature set", and what §7 promises
/// a provider picks so they are "live on a sub-domain in minutes".
/// </summary>
public class VerticalTemplate
{
    public string Key { get; set; } = null!;
    public string VerticalKey { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>The order state machine a brand on this template runs.</summary>
    public string FulfillmentMode { get; set; } = null!;

    /// <summary>The tier a new brand starts on. Null when the template exists but is unpriced.</summary>
    public string? DefaultBundleCode { get; set; }

    /// <summary>Preset catalogue as JSON: <c>[{ "category": "…", "items": ["…"] }]</c>. Kept small on
    /// purpose — enough that a provider's first screen is not empty, not an attempt to author their
    /// price list (§8.1: the platform does not set a company's customer prices).</summary>
    public string CatalogSeed { get; set; } = "[]";

    /// <summary>Offered at signup. False while a template's fulfilment mode has no strategy — a
    /// provider must never be able to launch a business that cannot take an order.</summary>
    public bool IsPublic { get; set; } = true;

    public int SortOrder { get; set; } = 100;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
