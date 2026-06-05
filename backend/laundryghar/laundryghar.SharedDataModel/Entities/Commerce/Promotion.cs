using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.Commerce;

/// <summary>Marketing promotion (commerce.promotions).
/// Has created_at, updated_at, created_by, updated_by — NO version, NO deleted_at.</summary>
public class Promotion
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string PromotionType { get; set; } = null!;
    public string TargetAudience { get; set; } = null!;

    /// <summary>text[] — segment names for targeted promotions.</summary>
    public string[]? EligibleSegments { get; set; }

    /// <summary>jsonb — eligibility / trigger rules.</summary>
    public string Rules { get; set; } = null!;

    /// <summary>jsonb — reward configuration.</summary>
    public string RewardConfig { get; set; } = null!;

    /// <summary>FK to commerce.coupons — optional cross-table ref.</summary>
    public Guid? CouponId { get; set; }

    public string? BannerImageUrl { get; set; }
    public string? DeeplinkUrl { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public decimal? TotalBudget { get; set; }
    public decimal SpentBudget { get; set; }
    public int ImpressionsCount { get; set; }
    public int RedemptionsCount { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Coupon? Coupon { get; set; }
}
