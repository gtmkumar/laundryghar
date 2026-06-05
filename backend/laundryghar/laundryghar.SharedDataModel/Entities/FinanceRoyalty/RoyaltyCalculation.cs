using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;

namespace laundryghar.SharedDataModel.Entities.FinanceRoyalty;

/// <summary>Per-order/per-adjustment royalty calculation line (finance_royalty.royalty_calculations).
/// Has created_at, created_by ONLY — no updated_at, no version, no deleted_at. Append-only.
/// order_id + order_created_at → composite FK to order_lifecycle.orders(id, created_at) ON DELETE RESTRICT.</summary>
public class RoyaltyCalculation
{
    public Guid Id { get; set; }
    public Guid RoyaltyInvoiceId { get; set; }
    public Guid BrandId { get; set; }
    public Guid FranchiseId { get; set; }
    public Guid? StoreId { get; set; }

    /// <summary>Part of composite FK to order_lifecycle.orders(id, created_at).</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Partition-key column carried for composite FK.</summary>
    public DateTimeOffset? OrderCreatedAt { get; set; }

    public DateOnly CalculationDate { get; set; }
    public Guid? ServiceCategoryId { get; set; }

    /// <summary>CHECK: order, package, adjustment, refund.</summary>
    public string RevenueType { get; set; } = null!;

    public decimal GrossAmount { get; set; }
    public decimal ExcludedAmount { get; set; }
    public string? ExclusionReason { get; set; }
    public decimal EligibleAmount { get; set; }
    public decimal RoyaltyRate { get; set; }
    public decimal RoyaltyAmount { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public RoyaltyInvoice RoyaltyInvoice { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
    public Franchise Franchise { get; set; } = null!;
    public Store? Store { get; set; }
    public ServiceCategory? ServiceCategory { get; set; }
}
