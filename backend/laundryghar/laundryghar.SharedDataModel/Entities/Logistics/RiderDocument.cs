namespace laundryghar.SharedDataModel.Entities.Logistics;

/// <summary>A rider KYC document upload (logistics.rider_documents) with a per-document
/// review state. The binary lives in object storage; <see cref="StorageKey"/> is the
/// opaque key (never exposed raw — streamed via an endpoint).</summary>
public class RiderDocument
{
    public Guid Id { get; set; }
    public Guid RiderId { get; set; }
    public Guid BrandId { get; set; }

    /// <summary>license|rc|insurance|id|photo — see <see cref="Enums.RiderDocumentType"/>.</summary>
    public string DocType { get; set; } = null!;

    public string StorageKey { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string MimeType { get; set; } = null!;
    public long Bytes { get; set; }

    /// <summary>pending|approved|rejected — see <see cref="Enums.RiderDocumentStatus"/>.</summary>
    public string Status { get; set; } = "pending";
    public string? RejectionReason { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }

    public string Metadata { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigation
    public Rider Rider { get; set; } = null!;
}
