using laundryghar.SharedDataModel.Entities.Analytics;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.SharedDataModel.Persistence;

/// <summary>
/// Shared EF Core DbContext mapping the live PostgreSQL database (database-first).
/// Do NOT run migrations against this context — the DB schema is canonical.
///
/// Soft-delete query filters (HasQueryFilter(e => e.DeletedAt == null)):
///   tenancy_org: Platform, Brand, Territory, FranchiseAgreement, Franchise, Store, Warehouse
///   identity_access: User, Role
///   kernel: FileAttachment
///   customer_catalog: Customer, CustomerAddress, ServiceCategory, Service, FabricType,
///                     ItemGroup, Item, ItemVariant, PriceList, AddOn
///   order_lifecycle: Order, OrderNote, FulfillmentUnitInspectionPhoto
///   commerce: Package, Coupon
///   finance_royalty: Expense, ExpenseAttachment
/// engagement_cms entities do not have deleted_at and have no global filter.
/// All other entities do not have deleted_at and have no global filter.
/// Use IgnoreQueryFilters() when you need to see soft-deleted rows.
/// </summary>
public class LaundryGharDbContext : DbContext
{
    public LaundryGharDbContext(DbContextOptions<LaundryGharDbContext> options)
        : base(options) { }

    // tenancy_org
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Territory> Territories => Set<Territory>();
    public DbSet<FranchiseAgreement> FranchiseAgreements => Set<FranchiseAgreement>();
    public DbSet<Franchise> Franchises => Set<Franchise>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StoreWarehouseMapping> StoreWarehouseMappings => Set<StoreWarehouseMapping>();
    public DbSet<OperatingHour> OperatingHours => Set<OperatingHour>();
    public DbSet<Holiday> Holidays => Set<Holiday>();

    // identity_access
    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserScopeMembership> UserScopeMemberships => Set<UserScopeMembership>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();
    public DbSet<AppModule> Modules => Set<AppModule>();
    public DbSet<BrandModule> BrandModules => Set<BrandModule>();
    public DbSet<ModuleBundle> ModuleBundles => Set<ModuleBundle>();
    public DbSet<ModuleBundleItem> ModuleBundleItems => Set<ModuleBundleItem>();
    public DbSet<BrandPlatformSubscription> BrandPlatformSubscriptions => Set<BrandPlatformSubscription>();
    public DbSet<BrandPlatformInvoice> BrandPlatformInvoices => Set<BrandPlatformInvoice>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginHistory> LoginHistories => Set<LoginHistory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();
    // OAuth 2.1 authorization-server facade (RFC 7591 + RFC 7636)
    public DbSet<OAuthClient> OAuthClients => Set<OAuthClient>();
    public DbSet<OAuthAuthorizationCode> OAuthAuthorizationCodes => Set<OAuthAuthorizationCode>();

    // customer_catalog
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<CustomerDevice> CustomerDevices => Set<CustomerDevice>();
    public DbSet<AccountDeletionRequest> AccountDeletionRequests => Set<AccountDeletionRequest>();
    public DbSet<DpdpConsent> DpdpConsents => Set<DpdpConsent>();
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<FabricType> FabricTypes => Set<FabricType>();
    public DbSet<ItemGroup> ItemGroups => Set<ItemGroup>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemVariant> ItemVariants => Set<ItemVariant>();
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<PriceListItem> PriceListItems => Set<PriceListItem>();
    public DbSet<PricingChangeLog> PricingChangeLogs => Set<PricingChangeLog>();
    public DbSet<AddOn> AddOns => Set<AddOn>();

    // order_lifecycle
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderAddon> OrderAddons => Set<OrderAddon>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<OrderNote> OrderNotes => Set<OrderNote>();
    public DbSet<PickupRequest> PickupRequests => Set<PickupRequest>();
    public DbSet<DeliveryAssignment> DeliveryAssignments => Set<DeliveryAssignment>();
    public DbSet<DeliverySlot> DeliverySlots => Set<DeliverySlot>();
    public DbSet<DeliverySlotBooking> DeliverySlotBookings => Set<DeliverySlotBooking>();
    public DbSet<FulfillmentUnit> FulfillmentUnits => Set<FulfillmentUnit>();
    public DbSet<FulfillmentUnitTag> FulfillmentUnitTags => Set<FulfillmentUnitTag>();
    public DbSet<FulfillmentUnitInspection> FulfillmentUnitInspections => Set<FulfillmentUnitInspection>();
    public DbSet<FulfillmentUnitInspectionPhoto> FulfillmentUnitInspectionPhotos => Set<FulfillmentUnitInspectionPhoto>();
    public DbSet<FulfillmentUnitCondition> FulfillmentUnitConditions => Set<FulfillmentUnitCondition>();
    public DbSet<WarehouseBatch> WarehouseBatches => Set<WarehouseBatch>();
    public DbSet<WarehouseProcess> WarehouseProcesses => Set<WarehouseProcess>();
    public DbSet<ProcessLog> ProcessLogs => Set<ProcessLog>();
    public DbSet<QualityCheck> QualityChecks => Set<QualityCheck>();
    public DbSet<StockReconciliation> StockReconciliations => Set<StockReconciliation>();
    public DbSet<StockReconciliationItem> StockReconciliationItems => Set<StockReconciliationItem>();
    public DbSet<Invoice> Invoices => Set<Invoice>();

    // logistics
    public DbSet<Rider> Riders => Set<Rider>();
    public DbSet<RiderAssignment> RiderAssignments => Set<RiderAssignment>();
    public DbSet<RiderCapacityConfig> RiderCapacityConfigs => Set<RiderCapacityConfig>();
    public DbSet<RiderLocationPing> RiderLocationPings => Set<RiderLocationPing>();
    public DbSet<RiderSettlement> RiderSettlements => Set<RiderSettlement>();
    public DbSet<RiderDocument> RiderDocuments => Set<RiderDocument>();
    public DbSet<RiderPayoutRequest> RiderPayoutRequests => Set<RiderPayoutRequest>();
    public DbSet<IncentiveRule> IncentiveRules => Set<IncentiveRule>();
    public DbSet<RiderIncentiveAward> RiderIncentiveAwards => Set<RiderIncentiveAward>();
    public DbSet<RiderRating> RiderRatings => Set<RiderRating>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();

    // logistics — RaaS partner MVP (partner_id-isolated via rls_partner)
    public DbSet<Partner> Partners => Set<Partner>();
    public DbSet<PartnerUser> PartnerUsers => Set<PartnerUser>();
    public DbSet<PartnerBooking> PartnerBookings => Set<PartnerBooking>();

    // commerce
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<CustomerPackage> CustomerPackages => Set<CustomerPackage>();
    public DbSet<PackageUsageLedger> PackageUsageLedger => Set<PackageUsageLedger>();
    public DbSet<LoyaltyProgram> LoyaltyPrograms => Set<LoyaltyProgram>();
    public DbSet<LoyaltyPointsLedger> LoyaltyPointsLedger => Set<LoyaltyPointsLedger>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponRedemption> CouponRedemptions => Set<CouponRedemption>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentRefund> PaymentRefunds => Set<PaymentRefund>();
    public DbSet<WalletAccount> WalletAccounts => Set<WalletAccount>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();

    // commerce — subscriptions (ADR-010 module A)
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<PaymentMandate> PaymentMandates => Set<PaymentMandate>();
    public DbSet<CustomerSubscription> CustomerSubscriptions => Set<CustomerSubscription>();
    public DbSet<SubscriptionInvoice> SubscriptionInvoices => Set<SubscriptionInvoice>();
    public DbSet<SubscriptionBillingAttempt> SubscriptionBillingAttempts => Set<SubscriptionBillingAttempt>();
    public DbSet<SubscriptionUsageLedger> SubscriptionUsageLedger => Set<SubscriptionUsageLedger>();

    // finance_royalty
    public DbSet<CashBook> CashBooks => Set<CashBook>();
    public DbSet<CashBookEntry> CashBookEntries => Set<CashBookEntry>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseAttachment> ExpenseAttachments => Set<ExpenseAttachment>();
    public DbSet<ShiftHandover> ShiftHandovers => Set<ShiftHandover>();
    public DbSet<RoyaltyInvoice> RoyaltyInvoices => Set<RoyaltyInvoice>();
    public DbSet<RoyaltyCalculation> RoyaltyCalculations => Set<RoyaltyCalculation>();

    // finance_royalty — SaaS subscriptions (ADR-010 module B)
    public DbSet<PlatformPlan> PlatformPlans => Set<PlatformPlan>();
    public DbSet<FranchiseSubscription> FranchiseSubscriptions => Set<FranchiseSubscription>();
    public DbSet<FranchiseSubscriptionInvoice> FranchiseSubscriptionInvoices => Set<FranchiseSubscriptionInvoice>();
    public DbSet<FranchiseSubscriptionEvent> FranchiseSubscriptionEvents => Set<FranchiseSubscriptionEvent>();

    // kernel
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();

    // analytics (keyless read-only materialized views — no RLS, must filter by brand_id in every query)
    public DbSet<DailyStoreRevenue> DailyStoreRevenues => Set<DailyStoreRevenue>();
    public DbSet<MonthlyFranchiseRevenue> MonthlyFranchiseRevenues => Set<MonthlyFranchiseRevenue>();
    public DbSet<WarehouseThroughput> WarehouseThroughputs => Set<WarehouseThroughput>();
    public DbSet<CustomerLtv> CustomerLtvs => Set<CustomerLtv>();
    public DbSet<RiderPerformance> RiderPerformances => Set<RiderPerformance>();

    // engagement_cms
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<NotificationOutbox> NotificationOutboxes => Set<NotificationOutbox>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<WhatsAppMessageLog> WhatsAppMessageLogs => Set<WhatsAppMessageLog>();
    public DbSet<OnboardingSlide> OnboardingSlides => Set<OnboardingSlide>();
    public DbSet<AppBanner> AppBanners => Set<AppBanner>();
    public DbSet<MobileAppConfig> MobileAppConfigs => Set<MobileAppConfig>();
    public DbSet<PushToken> PushTokens => Set<PushToken>();
    public DbSet<NotificationEventCursor> NotificationEventCursors => Set<NotificationEventCursor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LaundryGharDbContext).Assembly);
    }
}
