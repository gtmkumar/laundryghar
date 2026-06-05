using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.Commerce;

/// <summary>Append-only wallet debit/credit ledger (commerce.wallet_transactions).
/// Has created_at, created_by ONLY — no updated_at, no version, no deleted_at.
/// idempotency_key has a UNIQUE constraint.
/// direction: 1 = credit, -1 = debit (smallint CHECK).
/// order_id + order_created_at → composite FK to order_lifecycle.orders(id, created_at) ON DELETE RESTRICT.</summary>
public class WalletTransaction
{
    public Guid Id { get; set; }
    public Guid WalletAccountId { get; set; }
    public Guid BrandId { get; set; }
    public Guid CustomerId { get; set; }
    public string TransactionType { get; set; } = null!;

    /// <summary>1 = credit, -1 = debit.</summary>
    public short Direction { get; set; }

    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }

    /// <summary>Part of composite FK to order_lifecycle.orders(id, created_at).</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Partition-key column carried for composite FK.</summary>
    public DateTimeOffset? OrderCreatedAt { get; set; }

    public Guid? PaymentId { get; set; }
    public Guid? RefundId { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public string? PerformedByType { get; set; }
    public Guid? PerformedById { get; set; }

    /// <summary>Unique idempotency key — prevents duplicate transaction creation.</summary>
    public string? IdempotencyKey { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public WalletAccount WalletAccount { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public Payment? Payment { get; set; }
    public PaymentRefund? Refund { get; set; }
}
