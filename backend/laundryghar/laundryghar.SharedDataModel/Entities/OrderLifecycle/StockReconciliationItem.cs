using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Individual garment line within a stock reconciliation (order_lifecycle.stock_reconciliation_items).
/// Has created_at, created_by, flagged_at only — immutable per-row.</summary>
public class StockReconciliationItem
{
    public Guid Id { get; set; }
    public Guid ReconciliationId { get; set; }
    public Guid BrandId { get; set; }
    public Guid? GarmentId { get; set; }
    public string TagCode { get; set; } = null!;
    public string? ExpectedStage { get; set; }
    public string? ExpectedLocationType { get; set; }
    public Guid? ExpectedLocationId { get; set; }
    public string? FoundStage { get; set; }
    public string? FoundLocationType { get; set; }
    public Guid? FoundLocationId { get; set; }
    public string Status { get; set; } = null!;
    public string? LastKnownHolderType { get; set; }
    public Guid? LastKnownHolderId { get; set; }
    public DateTimeOffset? LastScannedAt { get; set; }
    public string? ResolutionAction { get; set; }
    public string? ResolutionNotes { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
    public DateTimeOffset FlaggedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public StockReconciliation Reconciliation { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
    public Garment? Garment { get; set; }
}
