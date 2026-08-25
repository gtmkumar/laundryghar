namespace core.Application.Identity.Entitlements.Dtos;

/// <summary>One FEATURE row in a brand's entitlement matrix (PLATFORM_STRATEGY.md §5).
/// Feature-keyed rather than module-keyed since migration 0005 — a brand buys features, and the
/// modules those features unlock are listed alongside so the console can say what a purchase gives.
/// </summary>
public sealed record BrandFeatureDto(
    string Key,
    string Name,
    string? Description,
    bool IsCore,
    /// <summary>On the price list. False for features auto-derived from existing modules — they
    /// exist so entitlement is total, not because they are sold on their own.</summary>
    bool IsSellable,
    bool Entitled,
    string? Source,         // 'bundle' | 'manual' | 'core' | null (not licensed)
    DateOnly? ValidUntil,
    /// <summary>The navigation modules this feature unlocks. Empty for a sellable feature with no
    /// menu at all (custom_domain, api_access, white_label_app). Carries KEYS as well as labels so
    /// the console can still answer "is this module licensed?" — the Roles matrix greys its rows by
    /// module key, and after the split that answer only exists via the feature behind it.</summary>
    IReadOnlyList<FeatureModuleDto> Modules);

/// <summary>A navigation module unlocked by a feature.</summary>
public sealed record FeatureModuleDto(string Key, string Label);

/// <summary>A brand's full entitlement view: every active feature + whether it's licensed.</summary>
public sealed record BrandEntitlementsDto(
    Guid BrandId,
    string BrandName,
    IReadOnlyList<BrandFeatureDto> Features);

public sealed record ModuleBundleItemDto(string Key, string Label);
public sealed record ModuleBundleDto(
    string Code, string Name, string? Description, IReadOnlyList<ModuleBundleItemDto> Items,
    string? VerticalKey = null,
    // Brand-tier pricing: what applying this bundle costs the tenant (null = unpriced/custom tier).
    decimal? Price = null, string? BillingInterval = null, string? CurrencyCode = null, bool IsPublic = true);

/// <summary>Toggle a single FEATURE's licensing for a brand (a 'manual' override / add-on).</summary>
public sealed record SetBrandFeatureRequest(string FeatureKey, bool Enabled, DateOnly? ValidUntil = null);

/// <summary>Apply a plan bundle to a brand: replace its 'bundle' rows with the bundle's items.</summary>
public sealed record ApplyBundleRequest(string BundleCode);

/// <summary>Mark a brand-platform invoice 'paid' or 'void'.</summary>
public sealed record SetInvoiceStatusRequest(string Status);

// ── Brand platform subscription (the brand's own platform tier + its invoices) ──
public sealed record BrandPlatformInvoiceDto(
    Guid Id, DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd,
    decimal Amount, string CurrencyCode, string Status, DateTimeOffset IssuedAt, DateTimeOffset DueAt,
    string? PaymentLinkUrl = null);

public sealed record BrandPlatformSubscriptionDto(
    Guid Id, Guid BrandId, string BundleCode, string PlanName,
    decimal Price, string BillingInterval, string CurrencyCode, string Status,
    DateTimeOffset CurrentPeriodStart, DateTimeOffset CurrentPeriodEnd, DateTimeOffset NextBillingAt,
    bool AutoRenew, IReadOnlyList<BrandPlatformInvoiceDto> Invoices);

// ── Platform billing summary (operator MRR view across all brands) ──────────
/// <summary>One tier's contribution to platform MRR (active brand subscriptions on that tier).</summary>
public sealed record TierMrrDto(string BundleCode, string PlanName, int ActiveCount, decimal MonthlyMrr);

/// <summary>Brand-platform invoice totals for one status (issued/paid/void).</summary>
public sealed record InvoiceStatusTotalDto(string Status, int Count, decimal TotalAmount);

/// <summary>Platform-wide SaaS revenue summary: what the platform earns from brands paying for tiers.
/// MRR is each active subscription's price normalised to a monthly figure. (Single currency for now.)</summary>
public sealed record PlatformBillingSummaryDto(
    string Currency,
    decimal MonthlyMrr,
    decimal AnnualRunRate,
    int ActiveTenants,
    int CancelledTenants,           // churned subscriptions (status = cancelled)
    decimal OutstandingAmount,      // sum of issued (not-yet-paid) invoices
    decimal CollectedAmount,        // sum of paid invoices
    IReadOnlyList<TierMrrDto> ByTier,
    IReadOnlyList<InvoiceStatusTotalDto> InvoicesByStatus);
