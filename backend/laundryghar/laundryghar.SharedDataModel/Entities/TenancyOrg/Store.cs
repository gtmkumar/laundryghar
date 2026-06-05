using laundryghar.SharedDataModel.Common;
using NetTopologySuite.Geometries;

namespace laundryghar.SharedDataModel.Entities.TenancyOrg;

/// <summary>Physical or virtual store operated by a franchise (tenancy_org.stores).</summary>
public class Store : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid FranchiseId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string StoreType { get; set; } = null!;
    public string AddressLine1 { get; set; } = null!;
    public string? AddressLine2 { get; set; }
    public string? Landmark { get; set; }
    public string City { get; set; } = null!;
    public string State { get; set; } = null!;
    public string Pincode { get; set; } = null!;
    public string CountryCode { get; set; } = null!;

    /// <summary>GEOGRAPHY(Point, 4326) — nullable.</summary>
    public Point? GeoLocation { get; set; }

    public decimal ServiceRadiusKm { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? TollFreeNumber { get; set; }
    public string? WhatsappNumber { get; set; }

    /// <summary>FK to identity_access.users — cross-BC, scalar only.</summary>
    public Guid? ManagerUserId { get; set; }

    public string Timezone { get; set; } = null!;
    public string CurrencyCode { get; set; } = null!;
    public int DailyPickupCapacity { get; set; }
    public int DailyDeliveryCapacity { get; set; }
    public int SlotDurationMinutes { get; set; }
    public bool AcceptsExpress { get; set; }
    public bool AcceptsCod { get; set; }
    public bool AcceptsWalkin { get; set; }
    public string? GooglePlaceId { get; set; }
    public decimal? RatingAverage { get; set; }
    public int RatingCount { get; set; }
    public string Config { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? ClosureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Franchise Franchise { get; set; } = null!;
    public ICollection<StoreWarehouseMapping> StoreWarehouseMappings { get; set; } = [];
    public ICollection<OperatingHour> OperatingHours { get; set; } = [];
}
