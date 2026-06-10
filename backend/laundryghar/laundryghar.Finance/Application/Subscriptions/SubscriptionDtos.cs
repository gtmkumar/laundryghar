namespace laundryghar.Finance.Application.Subscriptions;

// ── PlatformPlan ──────────────────────────────────────────────────────────────

public sealed record PlatformPlanDto(
    Guid Id,
    Guid? BrandId,
    string Code,
    string Name,
    string? Description,
    string Tier,
    string BillingInterval,
    short IntervalCount,
    decimal Price,
    decimal SetupFee,
    decimal AnnualDiscountPercent,
    string CurrencyCode,
    short TrialDays,
    int? MaxStores,
    int? MaxWarehouses,
    int? MaxUsers,
    int? MaxOrdersPerMonth,
    int? MaxRiders,
    decimal OveragePerOrder,
    decimal OveragePerStore,
    decimal OveragePerUser,
    string Features,
    string SupportLevel,
    bool IsPublic,
    bool IsFeatured,
    short DisplayOrder,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreatePlatformPlanRequest(
    Guid? BrandId,
    string Code,
    string Name,
    string? Description,
    string Tier,
    string BillingInterval,
    short IntervalCount,
    decimal Price,
    decimal SetupFee,
    decimal AnnualDiscountPercent,
    string CurrencyCode,
    short TrialDays,
    int? MaxStores,
    int? MaxWarehouses,
    int? MaxUsers,
    int? MaxOrdersPerMonth,
    int? MaxRiders,
    decimal OveragePerOrder,
    decimal OveragePerStore,
    decimal OveragePerUser,
    string Features,
    string SupportLevel,
    bool IsPublic,
    bool IsFeatured,
    short DisplayOrder
);

public sealed record UpdatePlatformPlanRequest(
    string Name,
    string? Description,
    string Tier,
    decimal Price,
    decimal SetupFee,
    decimal AnnualDiscountPercent,
    int? MaxStores,
    int? MaxWarehouses,
    int? MaxUsers,
    int? MaxOrdersPerMonth,
    int? MaxRiders,
    decimal OveragePerOrder,
    decimal OveragePerStore,
    decimal OveragePerUser,
    string Features,
    string SupportLevel,
    bool IsPublic,
    bool IsFeatured,
    short DisplayOrder,
    string Status
);

// ── FranchiseSubscription ─────────────────────────────────────────────────────

public sealed record FranchiseSubscriptionDto(
    Guid Id,
    Guid BrandId,
    Guid FranchiseId,
    Guid PlatformPlanId,
    string SubscriptionNumber,
    decimal PriceSnapshot,
    string BillingInterval,
    string Status,
    bool AutoRenew,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? NextBillingAt,
    short DunningAttempts,
    DateTimeOffset? SuspendedAt,
    int TotalCyclesBilled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record AssignFranchisePlanRequest(
    Guid FranchiseId,
    Guid PlatformPlanId,
    string PaymentMethod,
    bool AutoRenew
);

public sealed record CancelFranchiseSubscriptionRequest(string? Reason);
