using laundryghar.SharedDataModel.Common;

namespace laundryghar.SharedDataModel.Entities.TenancyOrg;

/// <summary>Legal franchise agreement document (tenancy_org.franchise_agreements).</summary>
public class FranchiseAgreement : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string AgreementNumber { get; set; } = null!;
    public string AgreementType { get; set; } = null!;
    public string FranchiseeLegalName { get; set; } = null!;
    public string? FranchiseePan { get; set; }
    public string? FranchiseeGstin { get; set; }
    public string? FranchiseePhone { get; set; }
    public string? FranchiseeEmail { get; set; }
    public decimal InitialFranchiseFee { get; set; }
    public decimal RoyaltyPercent { get; set; }
    public decimal MarketingFeePercent { get; set; }
    public decimal TechnologyFeeMonthly { get; set; }
    public Guid? TerritoryId { get; set; }
    public short TermYears { get; set; }
    public bool RenewalOption { get; set; }
    public bool ExclusivityClause { get; set; }
    public short MinimumStores { get; set; }
    public short? MaximumStores { get; set; }
    public string SlaTerms { get; set; } = null!;
    public string? DocumentS3Key { get; set; }
    public DateTimeOffset? SignedAt { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly EffectiveTo { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset? TerminatedAt { get; set; }
    public string? TerminationReason { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Territory? Territory { get; set; }
    public ICollection<Franchise> Franchises { get; set; } = [];
}
