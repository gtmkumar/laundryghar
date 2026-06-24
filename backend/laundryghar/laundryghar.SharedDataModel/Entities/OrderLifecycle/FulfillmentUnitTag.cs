using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Physical QR/barcode/RFID tag inventory (laundry_fulfillment.garment_tags).
/// Has created_at, updated_at, created_by. No updated_by, no version, no deleted_at.</summary>
public class FulfillmentUnitTag
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid? StoreId { get; set; }
    public string TagCode { get; set; } = null!;
    public string TagFormat { get; set; } = null!;
    public string? BatchNumber { get; set; }
    public DateTimeOffset? PrintedAt { get; set; }
    public Guid? PrintedBy { get; set; }
    public string? PrinterId { get; set; }
    public Guid? AssignedToFulfillmentUnitId { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public Guid? AssignedBy { get; set; }
    public bool IsDamaged { get; set; }
    public bool IsReprinted { get; set; }
    public short ReprintCount { get; set; }
    public string Status { get; set; } = null!;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Store? Store { get; set; }
    public FulfillmentUnit? AssignedToFulfillmentUnit { get; set; }
}
