using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Common.Interfaces;

/// <summary>
/// The core context's data-access surface, exposed to Application handlers as an interface
/// (no repositories). Backed by the shared <c>LaundryGharDbContext</c> via an adapter in
/// core.Infrastructure. Handlers inject this and write EF Core LINQ directly.
/// Only the entity sets the core slices touch are surfaced here.
/// </summary>
public interface ICoreDbContext
{
    DbSet<AppBanner> AppBanners { get; }
    DbSet<OnboardingSlide> OnboardingSlides { get; }
    DbSet<MobileAppConfig> MobileAppConfigs { get; }
    DbSet<NotificationTemplate> NotificationTemplates { get; }
    DbSet<NotificationOutbox> NotificationOutboxes { get; }
    DbSet<NotificationLog> NotificationLogs { get; }
    DbSet<WhatsAppMessageLog> WhatsAppMessageLogs { get; }
    DbSet<Promotion> Promotions { get; }
    DbSet<Coupon> Coupons { get; }
    DbSet<Brand> Brands { get; }
    DbSet<BrandDomain> BrandDomains { get; }

    // ─── Tenancy org hierarchy (AdminTenancy / Onboarding) ───────────────────
    DbSet<Platform> Platforms { get; }
    DbSet<Franchise> Franchises { get; }
    DbSet<FranchiseAgreement> FranchiseAgreements { get; }
    DbSet<Store> Stores { get; }
    DbSet<Warehouse> Warehouses { get; }

    // ─── Identity access (onboarding owner invite + admin user/access-control) ─
    DbSet<Role> Roles { get; }
    DbSet<User> Users { get; }
    DbSet<UserProfile> UserProfiles { get; }
    DbSet<UserScopeMembership> UserScopeMemberships { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<UserPermissionOverride> UserPermissionOverrides { get; }
    DbSet<AppModule> Modules { get; }
    DbSet<AppFeature> Features { get; }
    DbSet<VerticalTerm> VerticalTerms { get; }
    DbSet<VerticalTemplate> VerticalTemplates { get; }
    DbSet<RolePreset> RolePresets { get; }
    DbSet<PermissionGroup> PermissionGroups { get; }

    // ─── Catalogue (signup seeds a template's preset categories/items) ───────
    DbSet<ServiceCategory> ServiceCategories { get; }
    DbSet<Item> Items { get; }
    DbSet<BrandFeature> BrandFeatures { get; }
    DbSet<ModuleBundle> ModuleBundles { get; }
    DbSet<BundleFeature> BundleFeatures { get; }
    DbSet<BrandPlatformSubscription> BrandPlatformSubscriptions { get; }
    DbSet<BrandPlatformInvoice> BrandPlatformInvoices { get; }

    // ─── Identity access (system auth: login / OTP / refresh / password reset) ─
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<LoginHistory> LoginHistories { get; }
    DbSet<OtpCode> OtpCodes { get; }
    DbSet<PasswordReset> PasswordResets { get; }

    // ─── Customer catalog (customer mobile auth: OTP / refresh / /me) ─────────
    DbSet<Customer> Customers { get; }

    // ─── Customer catalog (social sign-in: Google/Apple identity links) ───────
    DbSet<CustomerIdentity> CustomerIdentities { get; }

    // ─── Logistics (rider counts in access-control franchise cards) ──────────
    DbSet<Rider> Riders { get; }

    // ─── Logistics (RaaS partner login: resolve the partner user + org by phone) ──
    // Read-only surface for the CORE host, which mints partner tokens. Writes to these
    // tables belong to the operations host (rls_partner-isolated).
    DbSet<Partner> Partners { get; }
    DbSet<PartnerUser> PartnerUsers { get; }

    // ─── Kernel (system settings store — Admin Settings) ─────────────────────
    DbSet<ImpersonationGrant> ImpersonationGrants { get; }
    DbSet<laundryghar.SharedDataModel.Entities.TenancyOrg.OnboardingProgress> OnboardingProgresses { get; }
    DbSet<ApiKey> ApiKeys { get; }
    DbSet<ApiKeyUsage> ApiKeyUsages { get; }
    DbSet<laundryghar.SharedDataModel.Entities.TenancyOrg.BrandCancellation> BrandCancellations { get; }

    DbSet<SystemSetting> SystemSettings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="action"/> inside a single database transaction, committing if it returns
    /// and rolling back if it throws.
    ///
    /// <para>Exists because some writes need SEVERAL SaveChanges calls that must still be atomic.
    /// Provider signup is the case in point: EF cannot infer the insert order between `brands` and
    /// `brand_feature` (the FK is on a natural key with no navigation property), so the brand must be
    /// saved before its features — but a brand that exists with no features, no owner and no plan is
    /// worse than no brand at all.</para>
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
}
