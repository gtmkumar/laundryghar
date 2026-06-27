using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;

/// <summary>Recurring subscription plan catalog entry (commerce.subscription_plans).
/// Has version + soft-delete. Quota resets each billing cycle — distinct from prepaid packages.</summary>
public class SubscriptionPlan : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;

    /// <summary>jsonb — localised name map.</summary>
    public string NameLocalized { get; set; } = null!;

    public string? Description { get; set; }
    public string Tier { get; set; } = null!;
    public string BillingInterval { get; set; } = null!;
    public short IntervalCount { get; set; }
    public decimal Price { get; set; }
    public decimal SetupFee { get; set; }
    public string CurrencyCode { get; set; } = null!;
    public short TrialDays { get; set; }
    public string QuotaType { get; set; } = null!;
    public decimal? QuotaValue { get; set; }
    public bool RolloverUnused { get; set; }
    public decimal? MaxRollover { get; set; }
    public decimal OverageDiscountPercent { get; set; }

    /// <summary>uuid[] — service IDs to which the plan applies.</summary>
    public Guid[] ApplicableServices { get; set; } = [];

    /// <summary>uuid[] — service IDs excluded from the plan.</summary>
    public Guid[] ExcludedServices { get; set; } = [];

    /// <summary>Which fulfilment legs the plan bundles in — a laundry/logistics-specific concept
    /// (a salon appointment plan has no pickup leg), stored as the <c>fulfillment_inclusions</c>
    /// jsonb off the generic plan spine. (Multi-vertical Phase 2 / slice 2E.)</summary>
    public FulfillmentInclusions Inclusions { get; set; } = new();

    public int? MaxActiveSubscribers { get; set; }
    public int CurrentSubscriberCount { get; set; }
    public string? Gateway { get; set; }
    public string? GatewayPlanId { get; set; }
    public string? TermsAndConditions { get; set; }
    public string? IconUrl { get; set; }
    public string? ColorHex { get; set; }
    public short DisplayOrder { get; set; }
    public bool IsPublic { get; set; }
    public bool IsFeatured { get; set; }
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
    public ICollection<CustomerSubscription> CustomerSubscriptions { get; set; } = [];
}

/// <summary>The fulfilment legs a <see cref="SubscriptionPlan"/> bundles in, stored as the
/// <c>fulfillment_inclusions</c> jsonb (owned type, ToJson). Demoted off the generic plan spine
/// in multi-vertical Phase 2 — these are leg-topology concepts that don't apply to every vertical.</summary>
public class FulfillmentInclusions
{
    public bool PickupIncluded { get; set; }
    public bool DeliveryIncluded { get; set; }
    public bool ExpressIncluded { get; set; }
}
