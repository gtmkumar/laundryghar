namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Granular vehicle review gate on a rider. Combined with KYC verification, 'Approved'
/// is required before a rider may receive dispatch offers/assignments.
/// </summary>
public static class VehicleVerificationStatus
{
    public const string Pending = "pending";
    public const string UnderReview = "under_review";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Pending, UnderReview, Approved, Rejected };

    public static bool IsValid(string? v) => v is not null && All.Contains(v);
}

/// <summary>Rider KYC document type vocabulary (logistics.rider_documents.doc_type).</summary>
public static class RiderDocumentType
{
    public const string License = "license";
    public const string Rc = "rc";
    public const string Insurance = "insurance";
    public const string Id = "id";
    public const string Photo = "photo";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { License, Rc, Insurance, Id, Photo };

    public static bool IsValid(string? v) => v is not null && All.Contains(v);
}

/// <summary>Per-document review state (logistics.rider_documents.status).</summary>
public static class RiderDocumentStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}
