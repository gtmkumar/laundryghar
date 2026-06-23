using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Photo taken during garment inspection (laundry_fulfillment.garment_inspection_photos).
/// Has created_at, created_by, deleted_at. No updated_at, no version.</summary>
public class GarmentInspectionPhoto : ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid InspectionId { get; set; }
    public Guid GarmentId { get; set; }
    public Guid BrandId { get; set; }
    public string S3Key { get; set; } = null!;
    public string? ThumbnailS3Key { get; set; }
    public string? CdnUrl { get; set; }
    public string View { get; set; } = null!;
    public string Annotations { get; set; } = null!;
    public int? WidthPx { get; set; }
    public int? HeightPx { get; set; }
    public int? Bytes { get; set; }
    public string MimeType { get; set; } = null!;
    public bool IsCompressed { get; set; }
    public bool HasExif { get; set; }
    public string? ExifData { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public Guid? CapturedBy { get; set; }
    public string? DeviceId { get; set; }
    public bool IsPrimary { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public GarmentInspection Inspection { get; set; } = null!;
    public Garment Garment { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
}
