using laundryghar.SharedDataModel.Common;

namespace laundryghar.SharedDataModel.Entities.TenancyOrg;

/// <summary>Franchise brand under a platform (tenancy_org.brands).</summary>
public class Brand : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid PlatformId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? LegalName { get; set; }
    public string? Tagline { get; set; }
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? AccentColor { get; set; }
    public string CurrencyCode { get; set; } = null!;
    public string CountryCode { get; set; } = null!;
    public string Timezone { get; set; } = null!;
    public string LocaleDefault { get; set; } = null!;
    public string[] LocalesEnabled { get; set; } = [];
    public string? SupportEmail { get; set; }
    public string? SupportPhone { get; set; }
    public string? TollFreeNumber { get; set; }
    public string? WhatsappNumber { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? PrivacyPolicyUrl { get; set; }
    public string? TermsUrl { get; set; }
    public string? PlayStoreUrl { get; set; }
    public string? AppStoreUrl { get; set; }
    public string Config { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset? LaunchedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public Platform Platform { get; set; } = null!;
    public ICollection<Territory> Territories { get; set; } = [];
    public ICollection<FranchiseAgreement> FranchiseAgreements { get; set; } = [];
    public ICollection<Franchise> Franchises { get; set; } = [];
    public ICollection<Store> Stores { get; set; } = [];
    public ICollection<Warehouse> Warehouses { get; set; } = [];
}
