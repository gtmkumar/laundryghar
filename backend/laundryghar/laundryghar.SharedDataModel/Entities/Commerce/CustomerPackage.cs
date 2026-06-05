using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.Commerce;

/// <summary>A purchased package instance belonging to a customer (commerce.customer_packages).
/// Has created_at, updated_at, created_by, updated_by — NO version, NO deleted_at.
/// credit_value_remaining is GENERATED ALWAYS — mapped ValueGeneratedOnAddOrUpdate.
/// purchase_order_id + purchase_order_created_at → composite FK to order_lifecycle.orders(id, created_at).</summary>
public class CustomerPackage
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid PackageId { get; set; }

    /// <summary>Part of composite FK to order_lifecycle.orders(id, created_at). Nullable — package may be gifted.</summary>
    public Guid? PurchaseOrderId { get; set; }

    /// <summary>Partition-key column for composite FK; required when PurchaseOrderId is present.</summary>
    public DateTimeOffset? PurchaseOrderCreatedAt { get; set; }

    /// <summary>FK to commerce.payments — scalar only (cross-table; payment may be null on gift).</summary>
    public Guid? PaymentId { get; set; }

    public decimal PurchaseAmount { get; set; }
    public decimal CreditValueTotal { get; set; }
    public decimal CreditValueUsed { get; set; }

    /// <summary>GENERATED ALWAYS AS (credit_value_total - credit_value_used) STORED — read-only.</summary>
    public decimal? CreditValueRemaining { get; set; }

    public DateTimeOffset ActivatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsUnlimitedValidity { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public int UsageCount { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset? SuspendedAt { get; set; }
    public string? SuspendedReason { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public decimal? RefundedAmount { get; set; }
    public string? RefundReason { get; set; }

    /// <summary>jsonb — arbitrary metadata.</summary>
    public string Metadata { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public Package Package { get; set; } = null!;

    /// <summary>FK to commerce.payments.</summary>
    public Payment? Payment { get; set; }

    public ICollection<PackageUsageLedger> UsageLedgerEntries { get; set; } = [];
}
