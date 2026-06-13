namespace laundryghar.Commerce.Application;

// ── PaymentMethod ─────────────────────────────────────────────────────────────

public sealed record PaymentMethodDto(
    Guid Id,
    Guid BrandId,
    string Code,
    string Name,
    string NameLocalized,
    string MethodType,
    string? Gateway,
    string? IconUrl,
    decimal? MinimumAmount,
    decimal? MaximumAmount,
    string? ConvenienceFeeType,
    decimal? ConvenienceFeeValue,
    bool IsOnline,
    bool IsRefundable,
    bool IsActive,
    short DisplayOrder,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreatePaymentMethodRequest(
    string Code,
    string Name,
    string NameLocalized,
    string MethodType,
    string? Gateway,
    string? IconUrl,
    decimal? MinimumAmount,
    decimal? MaximumAmount,
    string? ConvenienceFeeType,
    decimal? ConvenienceFeeValue,
    bool IsOnline,
    bool IsRefundable,
    bool IsActive,
    short DisplayOrder
);

public sealed record UpdatePaymentMethodRequest(
    string Name,
    string NameLocalized,
    string? Gateway,
    string? IconUrl,
    decimal? MinimumAmount,
    decimal? MaximumAmount,
    string? ConvenienceFeeType,
    decimal? ConvenienceFeeValue,
    bool IsOnline,
    bool IsRefundable,
    bool IsActive,
    short DisplayOrder,
    string Status
);

// ── Package ────────────────────────────────────────────────────────────────────

public sealed record PackageDto(
    Guid Id,
    Guid BrandId,
    string Code,
    string Name,
    string NameLocalized,
    string Tier,
    string? Description,
    decimal Price,
    decimal CreditValue,
    decimal DiscountPercent,
    decimal CreditMultiplier,
    int? ValidityDays,
    bool IsUnlimitedValidity,
    Guid[] ApplicableServices,
    Guid[] ExcludedServices,
    decimal? MinimumOrderValue,
    decimal? MaxUsagePerOrder,
    int? MaxPurchasesPerCust,
    string? IconUrl,
    string? ColorHex,
    short DisplayOrder,
    bool IsFeatured,
    string? TermsAndConditions,
    string Status,
    DateTimeOffset? AvailableFrom,
    DateTimeOffset? AvailableTo,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreatePackageRequest(
    string Code,
    string Name,
    string NameLocalized,
    string Tier,
    string? Description,
    decimal Price,
    decimal CreditValue,
    decimal DiscountPercent,
    decimal CreditMultiplier,
    int? ValidityDays,
    bool IsUnlimitedValidity,
    Guid[]? ApplicableServices,
    Guid[]? ExcludedServices,
    decimal? MinimumOrderValue,
    decimal? MaxUsagePerOrder,
    int? MaxPurchasesPerCust,
    string? IconUrl,
    string? ColorHex,
    short DisplayOrder,
    bool IsFeatured,
    string? TermsAndConditions,
    DateTimeOffset? AvailableFrom,
    DateTimeOffset? AvailableTo
);

public sealed record UpdatePackageRequest(
    string Name,
    string NameLocalized,
    string? Description,
    decimal Price,
    decimal CreditValue,
    decimal DiscountPercent,
    decimal CreditMultiplier,
    int? ValidityDays,
    bool IsUnlimitedValidity,
    Guid[]? ApplicableServices,
    Guid[]? ExcludedServices,
    decimal? MinimumOrderValue,
    decimal? MaxUsagePerOrder,
    int? MaxPurchasesPerCust,
    string? IconUrl,
    string? ColorHex,
    short DisplayOrder,
    bool IsFeatured,
    string? TermsAndConditions,
    DateTimeOffset? AvailableFrom,
    DateTimeOffset? AvailableTo,
    string Status
);

// ── Promotion ─────────────────────────────────────────────────────────────────

public sealed record PromotionDto(
    Guid Id,
    Guid BrandId,
    string Code,
    string Name,
    string? Description,
    string PromotionType,
    string TargetAudience,
    string[]? EligibleSegments,
    string Rules,
    string RewardConfig,
    Guid? CouponId,
    string? BannerImageUrl,
    string? DeeplinkUrl,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidUntil,
    decimal? TotalBudget,
    decimal SpentBudget,
    int RedemptionsCount,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreatePromotionRequest(
    string Code,
    string Name,
    string? Description,
    string PromotionType,
    string TargetAudience,
    string[]? EligibleSegments,
    string Rules,
    string RewardConfig,
    Guid? CouponId,
    string? BannerImageUrl,
    string? DeeplinkUrl,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidUntil,
    decimal? TotalBudget
);

public sealed record UpdatePromotionRequest(
    string Name,
    string? Description,
    string TargetAudience,
    string[]? EligibleSegments,
    string Rules,
    string RewardConfig,
    Guid? CouponId,
    string? BannerImageUrl,
    string? DeeplinkUrl,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidUntil,
    decimal? TotalBudget,
    string Status
);

// ── Coupon ────────────────────────────────────────────────────────────────────

public sealed record CouponDto(
    Guid Id,
    Guid BrandId,
    string Code,
    string Name,
    string? Description,
    string CouponType,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    decimal MinOrderValue,
    Guid[] ApplicableServices,
    Guid[] ApplicableStores,
    Guid[] ApplicableFranchises,
    string CustomerEligibility,
    bool IsFirstOrderOnly,
    bool IsSingleUsePerCust,
    int? MaxTotalUses,
    short MaxUsesPerCustomer,
    int CurrentUsageCount,
    bool IsStackable,
    bool IsPublic,
    bool IsAutoApply,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidUntil,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreateCouponRequest(
    string Code,
    string Name,
    string? Description,
    string CouponType,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    decimal MinOrderValue,
    Guid[]? ApplicableServices,
    Guid[]? ApplicableStores,
    Guid[]? ApplicableFranchises,
    string CustomerEligibility,
    Guid[]? EligibleCustomerIds,
    string[]? EligibleSegments,
    bool IsFirstOrderOnly,
    bool IsSingleUsePerCust,
    int? MaxTotalUses,
    short MaxUsesPerCustomer,
    bool IsStackable,
    bool IsPublic,
    bool IsAutoApply,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidUntil
);

public sealed record UpdateCouponRequest(
    string Name,
    string? Description,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    decimal MinOrderValue,
    Guid[]? ApplicableServices,
    Guid[]? ApplicableStores,
    Guid[]? ApplicableFranchises,
    string CustomerEligibility,
    Guid[]? EligibleCustomerIds,
    string[]? EligibleSegments,
    bool IsFirstOrderOnly,
    bool IsSingleUsePerCust,
    int? MaxTotalUses,
    short MaxUsesPerCustomer,
    bool IsStackable,
    bool IsPublic,
    bool IsAutoApply,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidUntil,
    string Status
);

// ── LoyaltyProgram ────────────────────────────────────────────────────────────

public sealed record LoyaltyProgramDto(
    Guid Id,
    Guid BrandId,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    decimal EarnRate,
    string EarnBasis,
    decimal BurnRate,
    int MinBurnPoints,
    decimal MaxBurnPerOrderPct,
    decimal MinOrderForEarn,
    Guid[] ExcludedServices,
    short? PointExpiryMonths,
    int WelcomeBonus,
    int ReferralBonusReferrer,
    int ReferralBonusReferee,
    int BirthdayBonus,
    string TierConfig,
    string? Terms,
    DateTimeOffset? LaunchedAt,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreateLoyaltyProgramRequest(
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    decimal EarnRate,
    string EarnBasis,
    decimal BurnRate,
    int MinBurnPoints,
    decimal MaxBurnPerOrderPct,
    decimal MinOrderForEarn,
    Guid[]? ExcludedServices,
    short? PointExpiryMonths,
    int WelcomeBonus,
    int ReferralBonusReferrer,
    int ReferralBonusReferee,
    int BirthdayBonus,
    string TierConfig,
    string? Terms
);

public sealed record UpdateLoyaltyProgramRequest(
    string Name,
    string? Description,
    bool IsActive,
    decimal EarnRate,
    string EarnBasis,
    decimal BurnRate,
    int MinBurnPoints,
    decimal MaxBurnPerOrderPct,
    decimal MinOrderForEarn,
    Guid[]? ExcludedServices,
    short? PointExpiryMonths,
    int WelcomeBonus,
    int ReferralBonusReferrer,
    int ReferralBonusReferee,
    int BirthdayBonus,
    string TierConfig,
    string? Terms,
    string Status
);

// ── Payment ───────────────────────────────────────────────────────────────────

public sealed record PaymentDto(
    Guid Id,
    Guid BrandId,
    Guid? CustomerId,
    string PaymentPurpose,
    string PaymentNumber,
    decimal Amount,
    decimal ConvenienceFee,
    decimal GatewayCharge,
    decimal NetAmount,
    string CurrencyCode,
    short Direction,
    string? Gateway,
    string? GatewayOrderId,
    string? GatewayPaymentId,
    string Status,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset InitiatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    string? IdempotencyKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    /// <summary>
    /// Refund information visible to the customer. Null when no refunds exist.
    /// Populated only on paths that include refund data (VerifyPayment result
    /// does not load refunds; use the payment detail endpoint for the latest state).
    /// </summary>
    PaymentRefundSummaryDto? RefundSummary = null
);

/// <summary>
/// Lightweight refund status visible to customers. Allows the mobile app to
/// surface pending/completed refund state without a separate API call.
/// </summary>
public sealed record PaymentRefundSummaryDto(
    string Status,
    decimal Amount,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt
);

public sealed record InitiatePaymentRequest(
    decimal Amount,
    string CurrencyCode,
    string PaymentPurpose,
    Guid? PaymentMethodId,
    Guid? OrderId,
    DateTimeOffset? OrderCreatedAt,
    string? Notes
);

public sealed record VerifyPaymentRequest(
    Guid PaymentId,
    string GatewayOrderId,
    string GatewayPaymentId,
    string GatewaySignature
);

// ── PaymentRefund ─────────────────────────────────────────────────────────────

public sealed record PaymentRefundDto(
    Guid Id,
    Guid BrandId,
    Guid OriginalPaymentId,
    Guid? CustomerId,
    string RefundNumber,
    string RefundType,
    decimal Amount,
    string Reason,
    string? ReasonText,
    string? RefundMethod,
    string? GatewayRefundId,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record IssueRefundRequest(
    Guid OriginalPaymentId,
    decimal Amount,
    string Reason,
    string? ReasonText,
    string RefundType,  // "gateway" or "wallet"
    string? Notes,
    /// <summary>
    /// Optional client-supplied idempotency key. When provided, a second call
    /// with the same key returns the original refund without creating a duplicate.
    /// </summary>
    string? IdempotencyKey = null
);

// ── WalletAccount + Transaction ────────────────────────────────────────────────

public sealed record WalletAccountDto(
    Guid Id,
    Guid BrandId,
    Guid CustomerId,
    string CurrencyCode,
    decimal Balance,
    decimal LockedBalance,
    decimal? AvailableBalance,
    decimal LifetimeCredit,
    decimal LifetimeDebit,
    DateTimeOffset? LastTransactionAt,
    bool IsFrozen,
    int Version,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record WalletTransactionDto(
    Guid Id,
    Guid WalletAccountId,
    Guid BrandId,
    Guid CustomerId,
    string TransactionType,
    short Direction,
    decimal Amount,
    decimal BalanceBefore,
    decimal BalanceAfter,
    string? ReferenceType,
    Guid? ReferenceId,
    string? Description,
    string? Notes,
    string? IdempotencyKey,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt
);

public sealed record AdminWalletAdjustRequest(
    Guid CustomerId,
    decimal Amount,
    short Direction,  // 1 = credit, -1 = debit
    string TransactionType,
    string? Description,
    string? Notes,
    string? IdempotencyKey
);

public sealed record WalletTopUpRequest(
    decimal Amount,
    string CurrencyCode,
    Guid? PaymentMethodId,
    string? Notes
);

// ── CustomerPackage ───────────────────────────────────────────────────────────

public sealed record CustomerPackageDto(
    Guid Id,
    Guid BrandId,
    Guid CustomerId,
    Guid PackageId,
    string PackageName,
    decimal PurchaseAmount,
    decimal CreditValueTotal,
    decimal CreditValueUsed,
    decimal? CreditValueRemaining,
    DateTimeOffset ActivatedAt,
    DateTimeOffset? ExpiresAt,
    bool IsUnlimitedValidity,
    DateTimeOffset? LastUsedAt,
    int UsageCount,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record PackageUsageLedgerDto(
    Guid Id,
    Guid CustomerPackageId,
    string TransactionType,
    decimal Amount,
    decimal BalanceBefore,
    decimal BalanceAfter,
    string? Notes,
    string? ReferenceType,
    Guid? ReferenceId,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt
);

public sealed record PurchasePackageRequest(
    Guid PackageId,
    Guid? PaymentMethodId,
    string? Notes
);

// ── Loyalty Ledger ────────────────────────────────────────────────────────────

public sealed record LoyaltyPointsLedgerDto(
    Guid Id,
    Guid BrandId,
    Guid CustomerId,
    Guid LoyaltyProgramId,
    string TransactionType,
    short Direction,
    int Points,
    int BalanceBefore,
    int BalanceAfter,
    decimal? MonetaryEquivalent,
    string? ReferenceType,
    Guid? ReferenceId,
    string? Notes,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt
);

public sealed record LoyaltyBalanceDto(
    Guid CustomerId,
    int CurrentBalance,
    IReadOnlyList<LoyaltyPointsLedgerDto> RecentHistory
);

// ── CouponRedemption ──────────────────────────────────────────────────────────

public sealed record CouponRedemptionDto(
    Guid Id,
    Guid CouponId,
    string CouponCode,
    Guid CustomerId,
    Guid OrderId,
    DateTimeOffset OrderCreatedAt,
    decimal DiscountAmount,
    decimal OrderSubtotalSnapshot,
    DateTimeOffset RedeemedAt,
    DateTimeOffset? RevertedAt,
    DateTimeOffset CreatedAt
);

public sealed record ValidateCouponRequest(
    string CouponCode,
    Guid OrderId,
    DateTimeOffset OrderCreatedAt,
    decimal OrderSubtotal
);
