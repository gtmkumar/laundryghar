using laundryghar.SharedDataModel.Common;
using NetTopologySuite.Geometries;

namespace laundryghar.SharedDataModel.Entities.TenancyOrg;

/// <summary>Geographic territory assigned to a brand (tenancy_org.territories).</summary>
public class Territory : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string CountryCode { get; set; } = null!;
    public string? State { get; set; }
    public string[] Cities { get; set; } = [];
    public string[] Pincodes { get; set; } = [];

    /// <summary>GEOGRAPHY(MultiPolygon, 4326) — nullable in DB.</summary>
    public MultiPolygon? Boundary { get; set; }

    public string ExclusivityType { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public ICollection<FranchiseAgreement> FranchiseAgreements { get; set; } = [];
    public ICollection<Franchise> Franchises { get; set; } = [];
}
