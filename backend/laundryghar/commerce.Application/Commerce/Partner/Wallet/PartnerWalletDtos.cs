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
