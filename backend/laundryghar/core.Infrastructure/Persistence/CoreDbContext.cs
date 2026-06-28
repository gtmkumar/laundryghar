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
    public DbSet<BrandModule> BrandModules => _db.BrandModules;
    public DbSet<ModuleBundle> ModuleBundles => _db.ModuleBundles;
    public DbSet<ModuleBundleItem> ModuleBundleItems => _db.ModuleBundleItems;
    public DbSet<BrandPlatformSubscription> BrandPlatformSubscriptions => _db.BrandPlatformSubscriptions;
    public DbSet<BrandPlatformInvoice> BrandPlatformInvoices => _db.BrandPlatformInvoices;

    public DbSet<RefreshToken> RefreshTokens => _db.RefreshTokens;
    public DbSet<LoginHistory> LoginHistories => _db.LoginHistories;
    public DbSet<OtpCode> OtpCodes => _db.OtpCodes;
    public DbSet<PasswordReset> PasswordResets => _db.PasswordResets;

    public DbSet<Customer> Customers => _db.Customers;

    public DbSet<Rider> Riders => _db.Riders;

    public DbSet<SystemSetting> SystemSettings => _db.SystemSettings;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
