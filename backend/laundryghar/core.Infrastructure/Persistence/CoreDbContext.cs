using core.Application.Common.Interfaces;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace core.Infrastructure.Persistence;

/// <summary>
/// Adapts the shared <see cref="LaundryGharDbContext"/> to <see cref="ICoreDbContext"/>, exposing
/// only the entity sets the core slices use. Lets Application handlers depend on the context
/// surface they own without taking a dependency on the shared concrete context.
/// </summary>
public sealed class CoreDbContext : ICoreDbContext
{
    private readonly LaundryGharDbContext _db;

    public CoreDbContext(LaundryGharDbContext db) => _db = db;

    public DbSet<AppBanner> AppBanners => _db.AppBanners;
    public DbSet<OnboardingSlide> OnboardingSlides => _db.OnboardingSlides;
    public DbSet<MobileAppConfig> MobileAppConfigs => _db.MobileAppConfigs;
    public DbSet<NotificationTemplate> NotificationTemplates => _db.NotificationTemplates;
    public DbSet<NotificationOutbox> NotificationOutboxes => _db.NotificationOutboxes;
    public DbSet<NotificationLog> NotificationLogs => _db.NotificationLogs;
    public DbSet<WhatsAppMessageLog> WhatsAppMessageLogs => _db.WhatsAppMessageLogs;
    public DbSet<Promotion> Promotions => _db.Promotions;
    public DbSet<Coupon> Coupons => _db.Coupons;
    public DbSet<Brand> Brands => _db.Brands;
    public DbSet<BrandDomain> BrandDomains => _db.BrandDomains;

    public DbSet<Platform> Platforms => _db.Platforms;
    public DbSet<Franchise> Franchises => _db.Franchises;
    public DbSet<FranchiseAgreement> FranchiseAgreements => _db.FranchiseAgreements;
    public DbSet<Store> Stores => _db.Stores;
    public DbSet<Warehouse> Warehouses => _db.Warehouses;

    public DbSet<Role> Roles => _db.Roles;
    public DbSet<User> Users => _db.Users;
    public DbSet<UserProfile> UserProfiles => _db.UserProfiles;
    public DbSet<UserScopeMembership> UserScopeMemberships => _db.UserScopeMemberships;
    public DbSet<Permission> Permissions => _db.Permissions;
    public DbSet<RolePermission> RolePermissions => _db.RolePermissions;
    public DbSet<UserPermissionOverride> UserPermissionOverrides => _db.UserPermissionOverrides;
    public DbSet<AppModule> Modules => _db.Modules;
    public DbSet<AppFeature> Features => _db.Features;
    public DbSet<VerticalTerm> VerticalTerms => _db.VerticalTerms;
    public DbSet<VerticalTemplate> VerticalTemplates => _db.VerticalTemplates;
    public DbSet<RolePreset> RolePresets => _db.RolePresets;
    public DbSet<PermissionGroup> PermissionGroups => _db.PermissionGroups;
    public DbSet<ServiceCategory> ServiceCategories => _db.ServiceCategories;
    public DbSet<Item> Items => _db.Items;
    public DbSet<BrandFeature> BrandFeatures => _db.BrandFeatures;
    public DbSet<ModuleBundle> ModuleBundles => _db.ModuleBundles;
    public DbSet<BundleFeature> BundleFeatures => _db.BundleFeatures;
    public DbSet<BrandPlatformSubscription> BrandPlatformSubscriptions => _db.BrandPlatformSubscriptions;
    public DbSet<BrandPlatformInvoice> BrandPlatformInvoices => _db.BrandPlatformInvoices;

    public DbSet<RefreshToken> RefreshTokens => _db.RefreshTokens;
    public DbSet<LoginHistory> LoginHistories => _db.LoginHistories;
    public DbSet<OtpCode> OtpCodes => _db.OtpCodes;
    public DbSet<PasswordReset> PasswordResets => _db.PasswordResets;

    public DbSet<Customer> Customers => _db.Customers;
    public DbSet<CustomerIdentity> CustomerIdentities => _db.CustomerIdentities;

    public DbSet<Rider> Riders => _db.Riders;

    public DbSet<Partner> Partners => _db.Partners;
    public DbSet<PartnerUser> PartnerUsers => _db.PartnerUsers;

    public DbSet<ImpersonationGrant> ImpersonationGrants => _db.Set<ImpersonationGrant>();
    public DbSet<laundryghar.SharedDataModel.Entities.TenancyOrg.OnboardingProgress> OnboardingProgresses => _db.Set<laundryghar.SharedDataModel.Entities.TenancyOrg.OnboardingProgress>();
    public DbSet<ApiKey> ApiKeys => _db.Set<ApiKey>();
    public DbSet<ApiKeyUsage> ApiKeyUsages => _db.Set<ApiKeyUsage>();
    public DbSet<laundryghar.SharedDataModel.Entities.TenancyOrg.BrandCancellation> BrandCancellations => _db.Set<laundryghar.SharedDataModel.Entities.TenancyOrg.BrandCancellation>();
    public DbSet<SystemSetting> SystemSettings => _db.SystemSettings;

    /// <inheritdoc />
    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        // Reuse an ambient transaction rather than nesting: a caller that already opened one owns
        // the commit, and starting a second here would silently break its atomicity.
        if (_db.Database.CurrentTransaction is not null) return await action(ct);

        // MUST go through the execution strategy. The context is configured with
        // NpgsqlRetryingExecutionStrategy (retry-on-transient-failure), which refuses a
        // user-initiated transaction outright:
        //     The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does not support
        //     user-initiated transactions.
        // The reason is not pedantry: on a transient fault the strategy retries, and a retry that
        // resumed mid-transaction would double-apply the work already done. Handing it the WHOLE
        // transaction makes the unit of retry the same as the unit of atomicity.
        //
        // The corollary is that `action` may run more than once. That is safe here because each
        // attempt begins after a full rollback, so it always starts from the same state.
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var result = await action(ct);
            await tx.CommitAsync(ct);
            return result;
        });
    }


    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
