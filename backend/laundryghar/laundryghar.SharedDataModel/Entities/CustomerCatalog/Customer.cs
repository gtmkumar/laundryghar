using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.CustomerCatalog;

/// <summary>End-customer registered under a brand (customer_catalog.customers).
/// Has all IAuditableEntity columns (created_at, updated_at, created_by, updated_by, version)
/// and deleted_at — implements both interfaces.</summary>
public class Customer : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string CustomerCode { get; set; } = null!;
    public string PhoneE164 { get; set; } = null!;
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? AvatarUrl { get; set; }
    public string Locale { get; set; } = null!;
    public string Timezone { get; set; } = null!;

    /// <summary>FK to tenancy_org.stores — navigation included (within-library).</summary>
    public Guid? PrimaryStoreId { get; set; }

    public string? ReferralCode { get; set; }

    /// <summary>Self-referential FK — nullable.</summary>
    public Guid? ReferredByCustomerId { get; set; }

    public int LifetimeOrders { get; set; }
    public decimal LifetimeSpend { get; set; }
    public decimal? AvgOrderValue { get; set; }
    public DateTimeOffset? LastOrderAt { get; set; }
    public DateTimeOffset? FirstOrderAt { get; set; }
    public int LoyaltyPointsBalance { get; set; }
    public decimal WalletBalance { get; set; }
    public string? CustomerSegment { get; set; }
    public string? RiskFlag { get; set; }
    public string[] Tags { get; set; } = [];
    public DateTimeOffset? PhoneVerifiedAt { get; set; }
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public DateTimeOffset? OnboardingCompletedAt { get; set; }
    public DateTimeOffset? LastActiveAt { get; set; }
    public bool MarketingOptIn { get; set; }
    public bool SmsOptIn { get; set; }
    public bool WhatsappOptIn { get; set; }
    public bool EmailOptIn { get; set; }
    public bool PushOptIn { get; set; }
    public string Status { get; set; } = null!;
    public string Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Store? PrimaryStore { get; set; }
    public Customer? ReferredByCustomer { get; set; }
    public ICollection<CustomerAddress> Addresses { get; set; } = [];
    public ICollection<CustomerDevice> Devices { get; set; } = [];
    public ICollection<DpdpConsent> DpdpConsents { get; set; } = [];
    public ICollection<AccountDeletionRequest> AccountDeletionRequests { get; set; } = [];
}
