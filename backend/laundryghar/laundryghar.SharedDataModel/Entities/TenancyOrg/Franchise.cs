using laundryghar.SharedDataModel.Common;

namespace laundryghar.SharedDataModel.Entities.TenancyOrg;

/// <summary>Franchisee entity operating under a brand (tenancy_org.franchises).</summary>
public class Franchise : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid? TerritoryId { get; set; }
    public Guid? FranchiseAgreementId { get; set; }

    /// <summary>FK to identity_access.users — cross-BC, scalar only.</summary>
    public Guid? OwnerUserId { get; set; }

    public string Code { get; set; } = null!;
    public string LegalName { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Gstin { get; set; }
    public string? Pan { get; set; }
    public string? Cin { get; set; }
    public string ContactPhone { get; set; } = null!;
    public string? ContactEmail { get; set; }
    public string BillingAddress { get; set; } = null!;
    public string? OperationalAddress { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfsc { get; set; }
    public string? BankName { get; set; }
    public decimal RoyaltyPercent { get; set; }
    public decimal MarketingFeePercent { get; set; }
    public string OnboardingStatus { get; set; } = null!;
    public DateTimeOffset? OnboardedAt { get; set; }
    public DateTimeOffset? SuspendedAt { get; set; }
    public string? SuspendedReason { get; set; }
    public DateTimeOffset? TerminatedAt { get; set; }
    public string Config { get; set; } = null!;
    public string Metadata { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Territory? Territory { get; set; }
    public FranchiseAgreement? FranchiseAgreement { get; set; }
    public ICollection<Store> Stores { get; set; } = [];
    public ICollection<Warehouse> Warehouses { get; set; } = [];
}
