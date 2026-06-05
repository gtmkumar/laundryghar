using laundryghar.SharedDataModel.Entities.IdentityAccess;

namespace laundryghar.SharedDataModel.Entities.CustomerCatalog;

/// <summary>DPDP Act consent record (customer_catalog.dpdp_consents).
/// Has created_at, created_by — no updated_at, no version, no deleted_at (immutable log).</summary>
public class DpdpConsent
{
    public Guid Id { get; set; }
    public Guid? CustomerId { get; set; }

    /// <summary>FK to identity_access.users — nav included since IdentityAccess is in scope.</summary>
    public Guid? UserId { get; set; }

    public Guid BrandId { get; set; }
    public string Purpose { get; set; } = null!;
    public string PurposeDescription { get; set; } = null!;
    public string[] DataCategories { get; set; } = [];
    public string ConsentStatus { get; set; } = null!;
    public string ConsentMethod { get; set; } = null!;
    public string PrivacyPolicyVersion { get; set; } = null!;
    public string? TermsVersion { get; set; }
    public string? ConsentTextSnapshot { get; set; }
    public DateTimeOffset? GrantedAt { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>Stored as varchar(100) in DB — not a geography type (named "geo_location" but is varchar).</summary>
    public string? GeoLocation { get; set; }

    public string? EvidenceS3Key { get; set; }
    public string Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Customer? Customer { get; set; }
    public User? User { get; set; }
}
