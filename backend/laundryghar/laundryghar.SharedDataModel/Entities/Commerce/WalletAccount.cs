using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.Commerce;

/// <summary>Customer wallet account (commerce.wallet_accounts).
/// Has created_at, updated_at, created_by, updated_by, version — NO deleted_at.
/// available_balance is GENERATED ALWAYS AS (balance - locked_balance) STORED — read-only.
/// customer_id is UNIQUE (one wallet per customer).</summary>
public class WalletAccount
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid CustomerId { get; set; }

    /// <summary>character(3) fixed-length currency code.</summary>
    public string CurrencyCode { get; set; } = null!;

    public decimal Balance { get; set; }
    public decimal LockedBalance { get; set; }

    /// <summary>GENERATED ALWAYS AS (balance - locked_balance) STORED — read-only.</summary>
    public decimal? AvailableBalance { get; set; }

    public decimal LifetimeCredit { get; set; }
    public decimal LifetimeDebit { get; set; }
    public DateTimeOffset? LastTransactionAt { get; set; }
    public bool IsFrozen { get; set; }
    public DateTimeOffset? FrozenAt { get; set; }
    public string? FrozenReason { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public ICollection<WalletTransaction> Transactions { get; set; } = [];
}
