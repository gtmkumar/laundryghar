namespace core.Application.Identity.Entitlements.Dtos;

/// <summary>One module row in a brand's entitlement matrix.</summary>
public sealed record BrandModuleDto(
    string Key,
    string Label,
    string? Section,
    bool IsCore,
    bool Entitled,
    string? Source,         // 'bundle' | 'manual' | null (not licensed)
    DateOnly? ValidUntil);

/// <summary>A brand's full entitlement view: every active module + whether it's licensed.</summary>
public sealed record BrandEntitlementsDto(
    Guid BrandId,
    string BrandName,
    IReadOnlyList<BrandModuleDto> Modules);

public sealed record ModuleBundleItemDto(string Key, string Label);
public sealed record ModuleBundleDto(
    string Code, string Name, string? Description, IReadOnlyList<ModuleBundleItemDto> Items,
    string? VerticalKey = null,
    // Brand-tier pricing: what applying this bundle costs the tenant (null = unpriced/custom tier).
    decimal? Price = null, string? BillingInterval = null, string? CurrencyCode = null, bool IsPublic = true);

/// <summary>Toggle a single module's licensing for a brand (a 'manual' override).</summary>
public sealed record SetBrandModuleRequest(string ModuleKey, bool Enabled, DateOnly? ValidUntil = null);

/// <summary>Apply a plan bundle to a brand: replace its 'bundle' rows with the bundle's items.</summary>
public sealed record ApplyBundleRequest(string BundleCode);

/// <summary>Mark a brand-platform invoice 'paid' or 'void'.</summary>
public sealed record SetInvoiceStatusRequest(string Status);

// ── Brand platform subscription (the brand's own platform tier + its invoices) ──
public sealed record BrandPlatformInvoiceDto(
    Guid Id, DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd,
    decimal Amount, string CurrencyCode, string Status, DateTimeOffset IssuedAt, DateTimeOffset DueAt);

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
    decimal OutstandingAmount,      // sum of issued (not-yet-paid) invoices
    decimal CollectedAmount,        // sum of paid invoices
    IReadOnlyList<TierMrrDto> ByTier,
    IReadOnlyList<InvoiceStatusTotalDto> InvoicesByStatus);
