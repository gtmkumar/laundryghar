using laundryghar.SharedDataModel.Common;
using NetTopologySuite.Geometries;

namespace laundryghar.SharedDataModel.Entities.TenancyOrg;

/// <summary>Processing warehouse operated by a franchise (tenancy_org.warehouses).</summary>
public class Warehouse : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid FranchiseId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string WarehouseType { get; set; } = null!;
    public string AddressLine1 { get; set; } = null!;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = null!;
    public string State { get; set; } = null!;
    public string Pincode { get; set; } = null!;
    public string CountryCode { get; set; } = null!;

    /// <summary>GEOGRAPHY(Point, 4326) — nullable.</summary>
    public Point? GeoLocation { get; set; }

    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }

    /// <summary>FK to identity_access.users — cross-BC, scalar only.</summary>
    public Guid? ManagerUserId { get; set; }

    public int DailyThroughputTarget { get; set; }
    public int CurrentLoadCount { get; set; }
    public bool HasDryClean { get; set; }
    public bool HasSteamIron { get; set; }
    public bool HasShoeCleaning { get; set; }
    public bool HasCarpetCleaning { get; set; }
    public string[] Capabilities { get; set; } = [];
    public string OperatingHoursConfig { get; set; } = null!;
    public string Timezone { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string Config { get; set; } = null!;
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
}
