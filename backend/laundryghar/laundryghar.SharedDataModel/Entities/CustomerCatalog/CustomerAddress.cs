using laundryghar.SharedDataModel.Entities.TenancyOrg;
using NetTopologySuite.Geometries;

namespace laundryghar.SharedDataModel.Entities.CustomerCatalog;

/// <summary>Saved delivery address for a customer (customer_catalog.customer_addresses).
/// Has created_at, updated_at, created_by, updated_by, deleted_at, status.
/// No version — does NOT implement IAuditableEntity.</summary>
public class CustomerAddress
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid BrandId { get; set; }
    public string Label { get; set; } = null!;
    public string? CustomLabel { get; set; }
    public string? RecipientName { get; set; }
    public string? RecipientPhone { get; set; }
    public string AddressLine1 { get; set; } = null!;
    public string? AddressLine2 { get; set; }
    public string? Landmark { get; set; }
    public string? Floor { get; set; }
    public string? FlatNumber { get; set; }
    public string? BuildingName { get; set; }
    public string? Society { get; set; }
    public string? Area { get; set; }
    public string City { get; set; } = null!;
    public string State { get; set; } = null!;
    public string Pincode { get; set; } = null!;
    public string CountryCode { get; set; } = null!;

    /// <summary>GEOGRAPHY(Point,4326) — nullable.</summary>
    public Point? GeoLocation { get; set; }

    public string? DeliveryInstructions { get; set; }
    public bool IsDefault { get; set; }
    public bool IsVerified { get; set; }
    public Guid? ServiceableStoreId { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public int UseCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public Customer Customer { get; set; } = null!;
    public Store? ServiceableStore { get; set; }
}
