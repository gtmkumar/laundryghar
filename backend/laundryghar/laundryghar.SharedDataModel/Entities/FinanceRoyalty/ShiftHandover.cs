using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.SharedDataModel.Entities.IdentityAccess;

namespace laundryghar.SharedDataModel.Entities.FinanceRoyalty;

/// <summary>Shift-end cash handover record (finance_royalty.shift_handovers).
/// Has created_at, created_by ONLY — no updated_at, no version, no deleted_at.
/// cash_variance is a generated column: COALESCE(cash_counted_by_to_user, 0) - cash_handed_over.</summary>
public class ShiftHandover
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid StoreId { get; set; }
    public Guid FromUserId { get; set; }
    public Guid? ToUserId { get; set; }
    public Guid? CashBookId { get; set; }
    public DateTimeOffset HandoverAt { get; set; }
    public decimal CashHandedOver { get; set; }
    public decimal? CashCountedByToUser { get; set; }

    /// <summary>Generated column: COALESCE(cash_counted_by_to_user, 0) - cash_handed_over. Read-only.</summary>
    public decimal? CashVariance { get; set; }

    public int PendingOrdersCount { get; set; }
    public int OpenComplaintsCount { get; set; }

    /// <summary>Fulfilment-leg shift counters (pickups/deliveries remaining) — laundry/logistics
    /// specific, stored as the <c>operational_snapshot</c> jsonb off the generic handover spine.
    /// (Multi-vertical Phase 2 / slice 2G.)</summary>
    public OperationalSnapshot Operational { get; set; } = new();

    public string? NotesFrom { get; set; }
    public string? NotesTo { get; set; }

    /// <summary>jsonb — array of pending item descriptors.</summary>
    public string PendingItems { get; set; } = null!;

    public DateTimeOffset? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedBy { get; set; }

    /// <summary>CHECK: pending, acknowledged, disputed, closed.</summary>
    public string Status { get; set; } = null!;

    public string? DisputeReason { get; set; }
    public Guid? ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Store Store { get; set; } = null!;
    public User FromUser { get; set; } = null!;
    public User? ToUser { get; set; }
    public CashBook? CashBook { get; set; }
}

/// <summary>Fulfilment-leg operational counters captured at shift handover, stored as the
/// <c>operational_snapshot</c> jsonb (owned type, ToJson). Demoted off the generic handover spine
/// in multi-vertical Phase 2 — pickups/deliveries don't apply to a salon vertical.</summary>
public class OperationalSnapshot
{
    public int PickupsRemaining { get; set; }
    public int DeliveriesRemaining { get; set; }
}
