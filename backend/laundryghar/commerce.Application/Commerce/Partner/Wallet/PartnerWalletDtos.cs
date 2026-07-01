namespace commerce.Application.Commerce.Partner.Wallet;

/// <summary>Read model for a RaaS partner's prepaid wallet (commerce.partner_wallet_accounts).
/// No brand_id — a partner token carries only partner_id (the rls_partner isolation key).</summary>
public sealed record PartnerWalletDto(
    Guid Id,
    Guid PartnerId,
    string CurrencyCode,
    decimal Balance,
    decimal LockedBalance,
    decimal? AvailableBalance,
    decimal LifetimeCredit,
    decimal LifetimeDebit,
    DateTimeOffset? LastTransactionAt,
    int Version,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Read model for one append-only partner wallet ledger row.</summary>
public sealed record PartnerWalletTransactionDto(
    Guid Id,
    Guid PartnerWalletAccountId,
    Guid PartnerId,
    short Direction,
    decimal Amount,
    decimal BalanceBefore,
    decimal BalanceAfter,
    string? ReferenceType,
    Guid? ReferenceId,
    string? IdempotencyKey,
    string? Notes,
    DateTimeOffset CreatedAt);

/// <summary>Body for POST /api/v1/partner/wallet/top-up (PartnerAdmin manual prepaid credit).</summary>
public sealed record TopUpPartnerWalletRequest(
    decimal Amount,
    string IdempotencyKey,
    string? Notes);

/// <summary>Body for POST /api/v1/partner/wallet/top-up/link (PartnerAdmin Razorpay-backed credit).
/// The wallet is NOT credited here — a payment link is returned; the credit happens on webhook-
/// confirmed payment (or a pull sync), keyed by <see cref="IdempotencyKey"/> so it is applied once.</summary>
public sealed record TopUpPartnerWalletViaLinkRequest(
    decimal Amount,
    string IdempotencyKey,
    string? Notes);

/// <summary>Response for POST /api/v1/partner/wallet/top-up/link — the payable Razorpay short URL.
/// Poll POST /wallet/top-up/link/{linkId}/sync (or rely on the webhook) to apply the credit.</summary>
public sealed record TopUpPartnerWalletViaLinkResponse(
    string LinkId,
    string PayUrl,
    string IdempotencyKey);

/// <summary>Response for POST /api/v1/partner/wallet/top-up/link/{linkId}/sync. When the link is paid
/// the credit is applied (once) and <see cref="Transaction"/> carries the ledger row; otherwise it is
/// null and <see cref="LinkStatus"/> reports the current Razorpay status.</summary>
public sealed record SyncPartnerWalletTopUpResponse(
    string LinkStatus,
    PartnerWalletTransactionDto? Transaction);
