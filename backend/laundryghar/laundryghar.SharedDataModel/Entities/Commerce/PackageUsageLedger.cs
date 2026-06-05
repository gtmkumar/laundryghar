using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.Commerce;

/// <summary>Append-only debit/credit ledger for a customer package (commerce.package_usage_ledger).
/// Has created_at, created_by ONLY — no updated_at, no version, no deleted_at.
/// order_id + order_created_at → composite FK to order_lifecycle.orders(id, created_at).</summary>
public class PackageUsageLedger
{
    public Guid Id { get; set; }
    public Guid CustomerPackageId { get; set; }
    public Guid BrandId { get; set; }
    public Guid CustomerId { get; set; }

    /// <summary>Part of composite FK to order_lifecycle.orders(id, created_at).</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Partition-key column carried for composite FK.</summary>
    public DateTimeOffset? OrderCreatedAt { get; set; }

    public string TransactionType { get; set; } = null!;
    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public string? Notes { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public Guid? PerformedBy { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public CustomerPackage CustomerPackage { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
}
