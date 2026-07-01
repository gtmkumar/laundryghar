namespace laundryghar.SharedDataModel.Entities.Commerce;

/// <summary>Append-only RaaS partner wallet ledger (commerce.partner_wallet_transactions).
/// Cloned from <see cref="WalletTransaction"/>, swapping customer_id → partner_id.
/// Has created_at, created_by ONLY — no updated_at, no version, no deleted_at.
///
/// idempotency_key has a UNIQUE constraint (prevents duplicate credit/debit rows for the same
/// logical operation — e.g. a booking debit keyed by the booking id).
/// direction: 1 = credit, -1 = debit (smallint CHECK).
/// reference_type discriminates the source: 'topup' (prepaid credit) | 'partner_booking' (debit).
///
/// partner_id is the SCALAR rls_partner isolation key; partner_wallet_account_id is an in-BC FK
/// to <see cref="PartnerWalletAccount"/> (same schema).</summary>
public class PartnerWalletTransaction
{
    public Guid Id { get; set; }

    /// <summary>In-BC FK → commerce.partner_wallet_accounts.id.</summary>
    public Guid PartnerWalletAccountId { get; set; }

    /// <summary>The owning RaaS partner (logistics.partners.id) — the rls_partner key.</summary>
    public Guid PartnerId { get; set; }

    /// <summary>1 = credit, -1 = debit.</summary>
    public short Direction { get; set; }

    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }

    /// <summary>Source discriminator: 'topup' | 'partner_booking'.</summary>
    public string? ReferenceType { get; set; }

    /// <summary>The source entity id (e.g. the partner_booking id for a booking debit).</summary>
    public Guid? ReferenceId { get; set; }

    /// <summary>Unique idempotency key — prevents duplicate ledger rows.</summary>
    public string? IdempotencyKey { get; set; }

    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations (in-BC only).
    public PartnerWalletAccount PartnerWalletAccount { get; set; } = null!;
}
