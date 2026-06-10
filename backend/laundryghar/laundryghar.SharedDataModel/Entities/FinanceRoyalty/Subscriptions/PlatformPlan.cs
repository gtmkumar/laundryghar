using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;

/// <summary>SaaS tier the platform offers to franchises (finance_royalty.platform_plans).
/// brand_id NULL = global catalog plan; set = white-label brand's own tiers.
/// Managed only by platform_admin. Has version + soft-delete.</summary>
public class PlatformPlan : ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>NULL for platform-global plans; set for white-label per-brand tiers.</summary>
    public Guid? BrandId { get; set; }

    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Tier { get; set; } = null!;
    public string BillingInterval { get; set; } = null!;
    public short IntervalCount { get; set; }
    public decimal Price { get; set; }
    public decimal SetupFee { get; set; }
    public decimal AnnualDiscountPercent { get; set; }
    public string CurrencyCode { get; set; } = null!;
    public short TrialDays { get; set; }

    // Quota limits (NULL = unlimited)
    public int? MaxStores { get; set; }
    public int? MaxWarehouses { get; set; }
    public int? MaxUsers { get; set; }
    public int? MaxOrdersPerMonth { get; set; }
    public int? MaxRiders { get; set; }

    // Overage rates
    public decimal OveragePerOrder { get; set; }
    public decimal OveragePerStore { get; set; }
    public decimal OveragePerUser { get; set; }

    /// <summary>jsonb — feature-flag keys enabled by this plan.</summary>
    public string Features { get; set; } = null!;

    public string SupportLevel { get; set; } = null!;
    public bool IsPublic { get; set; }
    public bool IsFeatured { get; set; }
    public short DisplayOrder { get; set; }
    public string? Gateway { get; set; }
    public string? GatewayPlanId { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public Brand? Brand { get; set; }
    public ICollection<FranchiseSubscription> FranchiseSubscriptions { get; set; } = [];
}
