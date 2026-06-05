using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.CustomerCatalog;

/// <summary>Versioned price list scoped to brand/franchise/store (customer_catalog.price_lists).
/// Has created_at, updated_at, created_by, updated_by, version, deleted_at.
/// Note: version here is the EF row-version column; version_number is a separate business field.</summary>
public class PriceList : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid? FranchiseId { get; set; }
    public Guid? StoreId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string CurrencyCode { get; set; } = null!;
    public string ScopeType { get; set; } = null!;

    /// <summary>Business version number — distinct from the EF concurrency Version column.</summary>
    public int VersionNumber { get; set; }

    /// <summary>Self-referential FK for price list inheritance.</summary>
    public Guid? ParentPriceListId { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public bool IsDefault { get; set; }
    public bool IsPublished { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? PublishedBy { get; set; }
    public string Status { get; set; } = null!;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Franchise? Franchise { get; set; }
    public Store? Store { get; set; }
    public PriceList? ParentPriceList { get; set; }
    public ICollection<PriceListItem> PriceListItems { get; set; } = [];
}
