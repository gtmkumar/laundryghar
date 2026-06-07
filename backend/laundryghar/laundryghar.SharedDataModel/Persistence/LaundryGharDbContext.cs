using laundryghar.SharedDataModel.Entities.Analytics;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
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
///   order_lifecycle: Order, OrderNote, GarmentInspectionPhoto
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
    public DbSet<AppModule> Modules => Set<AppModule>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginHistory> LoginHistories => Set<LoginHistory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();

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
    public DbSet<Garment> Garments => Set<Garment>();
    public DbSet<GarmentTag> GarmentTags => Set<GarmentTag>();
    public DbSet<GarmentInspection> GarmentInspections => Set<GarmentInspection>();
    public DbSet<GarmentInspectionPhoto> GarmentInspectionPhotos => Set<GarmentInspectionPhoto>();
    public DbSet<GarmentCondition> GarmentConditions => Set<GarmentCondition>();
    public DbSet<WarehouseBatch> WarehouseBatches => Set<WarehouseBatch>();
    public DbSet<WarehouseProcess> WarehouseProcesses => Set<WarehouseProcess>();
    public DbSet<ProcessLog> ProcessLogs => Set<ProcessLog>();
    public DbSet<QualityCheck> QualityChecks => Set<QualityCheck>();
    public DbSet<StockReconciliation> StockReconciliations => Set<StockReconciliation>();
    public DbSet<StockReconciliationItem> StockReconciliationItems => Set<StockReconciliationItem>();

    // logistics
    public DbSet<Rider> Riders => Set<Rider>();
    public DbSet<RiderAssignment> RiderAssignments => Set<RiderAssignment>();
    public DbSet<RiderCapacityConfig> RiderCapacityConfigs => Set<RiderCapacityConfig>();
    public DbSet<RiderLocationPing> RiderLocationPings => Set<RiderLocationPing>();

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

    // finance_royalty
    public DbSet<CashBook> CashBooks => Set<CashBook>();
    public DbSet<CashBookEntry> CashBookEntries => Set<CashBookEntry>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseAttachment> ExpenseAttachments => Set<ExpenseAttachment>();
    public DbSet<ShiftHandover> ShiftHandovers => Set<ShiftHandover>();
    public DbSet<RoyaltyInvoice> RoyaltyInvoices => Set<RoyaltyInvoice>();
    public DbSet<RoyaltyCalculation> RoyaltyCalculations => Set<RoyaltyCalculation>();

    // kernel
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();

    // analytics (keyless read-only materialized views — no RLS, must filter by brand_id in every query)
    public DbSet<DailyStoreRevenue>        DailyStoreRevenues        => Set<DailyStoreRevenue>();
    public DbSet<MonthlyFranchiseRevenue>  MonthlyFranchiseRevenues  => Set<MonthlyFranchiseRevenue>();
    public DbSet<WarehouseThroughput>      WarehouseThroughputs      => Set<WarehouseThroughput>();
    public DbSet<CustomerLtv>              CustomerLtvs               => Set<CustomerLtv>();
    public DbSet<RiderPerformance>         RiderPerformances          => Set<RiderPerformance>();

    // engagement_cms
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<NotificationOutbox> NotificationOutboxes => Set<NotificationOutbox>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<WhatsAppMessageLog> WhatsAppMessageLogs => Set<WhatsAppMessageLog>();
    public DbSet<OnboardingSlide> OnboardingSlides => Set<OnboardingSlide>();
    public DbSet<AppBanner> AppBanners => Set<AppBanner>();
    public DbSet<MobileAppConfig> MobileAppConfigs => Set<MobileAppConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LaundryGharDbContext).Assembly);
    }
}
