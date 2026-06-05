using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.Commerce;

/// <summary>Append-only earn/burn ledger for loyalty points (commerce.loyalty_points_ledger).
/// Has created_at, created_by ONLY — no updated_at, no version, no deleted_at.
/// direction: 1 = credit, -1 = debit (smallint CHECK).
/// order_id + order_created_at → composite FK to order_lifecycle.orders(id, created_at).</summary>
public class LoyaltyPointsLedger
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid LoyaltyProgramId { get; set; }
    public string TransactionType { get; set; } = null!;

    /// <summary>1 = credit, -1 = debit.</summary>
    public short Direction { get; set; }

    public int Points { get; set; }
    public int BalanceBefore { get; set; }
    public int BalanceAfter { get; set; }
    public decimal? MonetaryEquivalent { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }

    /// <summary>Part of composite FK to order_lifecycle.orders(id, created_at).</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Partition-key column carried for composite FK.</summary>
    public DateTimeOffset? OrderCreatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Notes { get; set; }
    public Guid? PerformedBy { get; set; }
    public string? PerformedByType { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public LoyaltyProgram LoyaltyProgram { get; set; } = null!;
}
