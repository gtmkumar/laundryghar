namespace laundryghar.Commerce.Application;

// ── SubscriptionPlan ──────────────────────────────────────────────────────────

public sealed record SubscriptionPlanDto(
    Guid Id,
    Guid BrandId,
    string Code,
    string Name,
    string NameLocalized,
    string? Description,
    string Tier,
    string BillingInterval,
    short IntervalCount,
    decimal Price,
    decimal SetupFee,
    string CurrencyCode,
    short TrialDays,
    string QuotaType,
    decimal? QuotaValue,
    bool RolloverUnused,
    decimal? MaxRollover,
    decimal OverageDiscountPercent,
    Guid[] ApplicableServices,
    Guid[] ExcludedServices,
    bool PickupIncluded,
    bool DeliveryIncluded,
    bool ExpressIncluded,
    int? MaxActiveSubscribers,
    int CurrentSubscriberCount,
    string? Gateway,
    string? GatewayPlanId,
    string? TermsAndConditions,
    string? IconUrl,
    string? ColorHex,
    short DisplayOrder,
    bool IsPublic,
    bool IsFeatured,
    string Status,
    DateTimeOffset? AvailableFrom,
    DateTimeOffset? AvailableTo,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreateSubscriptionPlanRequest(
    string Code,
    string Name,
    string NameLocalized,
    string? Description,
    string Tier,
    string BillingInterval,
    short IntervalCount,
    decimal Price,
    decimal SetupFee,
    string CurrencyCode,
    short TrialDays,
    string QuotaType,
    decimal? QuotaValue,
    bool RolloverUnused,
    decimal? MaxRollover,
    decimal OverageDiscountPercent,
    Guid[]? ApplicableServices,
    Guid[]? ExcludedServices,
    bool PickupIncluded,
    bool DeliveryIncluded,
    bool ExpressIncluded,
    int? MaxActiveSubscribers,
    string? Gateway,
    string? GatewayPlanId,
    string? TermsAndConditions,
    string? IconUrl,
    string? ColorHex,
    short DisplayOrder,
    bool IsPublic,
    bool IsFeatured,
    DateTimeOffset? AvailableFrom,
    DateTimeOffset? AvailableTo
);

public sealed record PatchSubscriptionPlanStatusRequest(string Status);

public sealed record UpdateSubscriptionPlanRequest(
    string Name,
    string NameLocalized,
    string? Description,
    string Tier,
    decimal Price,
    decimal SetupFee,
    string QuotaType,
    decimal? QuotaValue,
    bool RolloverUnused,
    decimal? MaxRollover,
    decimal OverageDiscountPercent,
    Guid[]? ApplicableServices,
    Guid[]? ExcludedServices,
    bool PickupIncluded,
    bool DeliveryIncluded,
    bool ExpressIncluded,
    int? MaxActiveSubscribers,
    string? Gateway,
    string? GatewayPlanId,
    string? TermsAndConditions,
    string? IconUrl,
    string? ColorHex,
    short DisplayOrder,
    bool IsPublic,
    bool IsFeatured,
    string Status,
    DateTimeOffset? AvailableFrom,
    DateTimeOffset? AvailableTo
);

// ── CustomerSubscription ──────────────────────────────────────────────────────

public sealed record CustomerSubscriptionDto(
    Guid Id,
    Guid BrandId,
    Guid CustomerId,
    Guid PlanId,
    string SubscriptionNumber,
    decimal PriceSnapshot,
    string BillingInterval,
    short IntervalCount,
    string QuotaType,
    decimal? QuotaValue,
    string CurrencyCode,
    string Status,
    bool AutoRenew,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? NextBillingAt,
    decimal CreditsRemaining,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CancelledAt,
    short DunningAttempts,
    int TotalCyclesBilled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record SubscribeRequest(
    Guid PlanId,
    string MandateType,       // upi_autopay | emandate | card | nach
    decimal MaxMandateAmount, // max per-debit authorization
    string? UpiVpa,
    string? GatewayCustomerId
);

public sealed record CancelSubscriptionRequest(string? Reason);

// ── PaymentMandate ────────────────────────────────────────────────────────────

public sealed record PaymentMandateDto(
    Guid Id,
    Guid CustomerId,
    string MandateType,
    string Gateway,
    string? GatewayMandateId,
    decimal MaxAmount,
    string DebitFrequency,
    string? UpiVpa,
    string Status,
    DateTimeOffset? AuthenticatedAt,
    DateTimeOffset CreatedAt
);

public sealed record CreateMandateResult(
    Guid MandateId,
    string Status,
    string? GatewayMandateId,
    string? AuthorizationUrl   // redirect URL for UPI autopay flow
);
