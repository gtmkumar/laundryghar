using laundryghar.SharedDataModel.Common;

namespace laundryghar.SharedDataModel.Entities.TenancyOrg;

/// <summary>Top-level SaaS platform (tenancy_org.platforms).</summary>
public class Platform : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? LegalName { get; set; }
    public string? Domain { get; set; }
    public string? SupportEmail { get; set; }
    public string? SupportPhone { get; set; }
    public string Config { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public ICollection<Brand> Brands { get; set; } = [];
}
