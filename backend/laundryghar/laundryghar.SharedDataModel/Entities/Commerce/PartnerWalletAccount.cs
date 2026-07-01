namespace laundryghar.SharedDataModel.Entities.Commerce;

/// <summary>RaaS partner prepaid wallet account (commerce.partner_wallet_accounts).
/// Cloned from <see cref="WalletAccount"/>, swapping customer_id → partner_id: there is exactly
/// ONE wallet per partner (partner_id is UNIQUE and the rls_partner isolation key).
///
/// partner_id is a SCALAR cross-BC reference to logistics.partners(id) — like
/// <see cref="WalletTransaction.OrderId"/> it carries NO navigation property and NO cross-BC EF
/// relationship; the commerce and logistics bounded contexts stay decoupled and isolation is
/// enforced by the rls_partner policy, not a foreign key.
///
/// available_balance is GENERATED ALWAYS AS (balance - locked_balance) STORED — read-only.
/// Has created_at, updated_at, created_by, updated_by, version — NO deleted_at.</summary>
public class PartnerWalletAccount
{
    public Guid Id { get; set; }

    /// <summary>The owning RaaS partner (logistics.partners.id). UNIQUE + the rls_partner key.</summary>
    public Guid PartnerId { get; set; }

    /// <summary>character(3) fixed-length currency code.</summary>
    public string CurrencyCode { get; set; } = null!;

    public decimal Balance { get; set; }
    public decimal LockedBalance { get; set; }

    /// <summary>GENERATED ALWAYS AS (balance - locked_balance) STORED — read-only.</summary>
    public decimal? AvailableBalance { get; set; }

    public decimal LifetimeCredit { get; set; }
    public decimal LifetimeDebit { get; set; }
    public DateTimeOffset? LastTransactionAt { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations (in-BC only — the append-only ledger for this wallet).
    public ICollection<PartnerWalletTransaction> Transactions { get; set; } = [];
}
