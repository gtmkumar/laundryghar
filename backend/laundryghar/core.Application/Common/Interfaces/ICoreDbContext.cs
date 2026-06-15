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
    DbSet<AppModule> Modules { get; }

    // ─── Identity access (system auth: login / OTP / refresh / password reset) ─
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<LoginHistory> LoginHistories { get; }
    DbSet<OtpCode> OtpCodes { get; }
    DbSet<PasswordReset> PasswordResets { get; }

    // ─── Customer catalog (customer mobile auth: OTP / refresh / /me) ─────────
    DbSet<Customer> Customers { get; }

    // ─── Logistics (rider counts in access-control franchise cards) ──────────
    DbSet<Rider> Riders { get; }

    // ─── Kernel (system settings store — Admin Settings) ─────────────────────
    DbSet<SystemSetting> SystemSettings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
