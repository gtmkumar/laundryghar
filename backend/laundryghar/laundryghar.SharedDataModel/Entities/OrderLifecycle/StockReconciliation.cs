using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Garment stock reconciliation session (order_lifecycle.stock_reconciliations).
/// Has created_at, updated_at, created_by, updated_by. No version, no deleted_at.</summary>
public class StockReconciliation
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? StoreId { get; set; }
    public DateOnly ReconDate { get; set; }
    public string ReconType { get; set; } = null!;
    public DateTimeOffset StartedAt { get; set; }
    public Guid StartedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CompletedBy { get; set; }
    public int ExpectedCount { get; set; }
    public int ScannedCount { get; set; }
    public int MatchedCount { get; set; }
    public int MissingCount { get; set; }
    public int UnexpectedCount { get; set; }
    public int DamagedCount { get; set; }
    public int ResolvedMissingCount { get; set; }
    public string Summary { get; set; } = null!;
    public string? Notes { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Warehouse? Warehouse { get; set; }
    public Store? Store { get; set; }
    public ICollection<StockReconciliationItem> Items { get; set; } = [];
}
