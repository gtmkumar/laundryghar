using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.Commerce;

/// <summary>Prepaid laundry package definition (commerce.packages).
/// Has created_at, updated_at, created_by, updated_by, version, deleted_at.</summary>
public class Package : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;

    /// <summary>jsonb — localised name map.</summary>
    public string NameLocalized { get; set; } = null!;

    public string Tier { get; set; } = null!;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public decimal CreditValue { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal CreditMultiplier { get; set; }
    public int? ValidityDays { get; set; }
    public bool IsUnlimitedValidity { get; set; }

    /// <summary>uuid[] — service IDs to which the package applies.</summary>
    public Guid[] ApplicableServices { get; set; } = [];

    /// <summary>uuid[] — service IDs excluded from the package.</summary>
    public Guid[] ExcludedServices { get; set; } = [];

    public decimal? MinimumOrderValue { get; set; }
    public decimal? MaxUsagePerOrder { get; set; }
    public int? MaxPurchasesPerCust { get; set; }
    public string? IconUrl { get; set; }
    public string? ColorHex { get; set; }
    public short DisplayOrder { get; set; }
    public bool IsFeatured { get; set; }
    public string? TermsAndConditions { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset? AvailableFrom { get; set; }
    public DateTimeOffset? AvailableTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public ICollection<CustomerPackage> CustomerPackages { get; set; } = [];
}
