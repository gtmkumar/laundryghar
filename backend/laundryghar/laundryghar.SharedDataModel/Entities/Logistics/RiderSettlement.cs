namespace laundryghar.SharedDataModel.Entities.Logistics;

/// <summary>
/// A rider's batched COD cash handover to the store/franchise (logistics.rider_settlements).
/// Logistics-only source of truth for cash reconciliation (Phase 3); admin-recorded.
/// Clearing a settlement stamps settlement_id on the delivery_assignments it covers.
/// Has created_at/updated_at/created_by/updated_by. No version, no deleted_at.
/// </summary>
public class RiderSettlement
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid FranchiseId { get; set; }
    public Guid RiderId { get; set; }
    public Guid? StoreId { get; set; }
    public decimal TotalAmount { get; set; }
    public int CollectionCount { get; set; }
    public string? Reference { get; set; }
    public string Status { get; set; } = "settled";   // settled | disputed | reversed
    public DateTimeOffset SettledAt { get; set; }
    public Guid? SettledBy { get; set; }
    public string? Notes { get; set; }
    public string Metadata { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
