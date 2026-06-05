using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.Commerce;

/// <summary>Discount coupon definition (commerce.coupons).
/// Has created_at, updated_at, created_by, updated_by, deleted_at — NO version column.</summary>
public class Coupon : ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string CouponType { get; set; } = null!;
    public decimal DiscountValue { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public decimal MinOrderValue { get; set; }

    /// <summary>uuid[] — applicable service IDs.</summary>
    public Guid[] ApplicableServices { get; set; } = [];

    /// <summary>uuid[] — applicable store IDs.</summary>
    public Guid[] ApplicableStores { get; set; } = [];

    /// <summary>uuid[] — applicable franchise IDs.</summary>
    public Guid[] ApplicableFranchises { get; set; } = [];

    public string CustomerEligibility { get; set; } = null!;

    /// <summary>uuid[] — specific customer IDs when eligibility = 'specific'.</summary>
    public Guid[]? EligibleCustomerIds { get; set; }

    /// <summary>text[] — segment names when eligibility = 'segment'.</summary>
    public string[]? EligibleSegments { get; set; }

    public bool IsFirstOrderOnly { get; set; }
    public bool IsSingleUsePerCust { get; set; }
    public int? MaxTotalUses { get; set; }
    public short MaxUsesPerCustomer { get; set; }
    public int CurrentUsageCount { get; set; }
    public bool IsStackable { get; set; }
    public bool IsPublic { get; set; }
    public bool IsAutoApply { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public ICollection<CouponRedemption> Redemptions { get; set; } = [];
    public ICollection<Promotion> Promotions { get; set; } = [];
}
