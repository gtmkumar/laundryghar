using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Infrastructure.Persistence;

/// <summary>
/// Adapts the shared <see cref="LaundryGharDbContext"/> to <see cref="IOperationsDbContext"/>, exposing
/// only the entity sets the operations slices use. Lets Application handlers depend on the context
/// surface they own without taking a dependency on the shared concrete context.
/// Mirrors <c>CoreDbContext</c>. DbSets are added per-slice as Operations endpoints are migrated.
/// </summary>
public sealed class OperationsDbContext : IOperationsDbContext
{
    private readonly LaundryGharDbContext _db;

    public OperationsDbContext(LaundryGharDbContext db) => _db = db;

    public DbSet<FulfillmentUnit> FulfillmentUnits => _db.FulfillmentUnits;
    public DbSet<FulfillmentUnitTag> FulfillmentUnitTags => _db.FulfillmentUnitTags;

    public DbSet<FulfillmentUnitInspection> FulfillmentUnitInspections => _db.FulfillmentUnitInspections;
    public DbSet<FulfillmentUnitInspectionPhoto> FulfillmentUnitInspectionPhotos => _db.FulfillmentUnitInspectionPhotos;
    public DbSet<FulfillmentUnitCondition> FulfillmentUnitConditions => _db.FulfillmentUnitConditions;

    public DbSet<WarehouseProcess> WarehouseProcesses => _db.WarehouseProcesses;
    public DbSet<ProcessLog> ProcessLogs => _db.ProcessLogs;

    public DbSet<QualityCheck> QualityChecks => _db.QualityChecks;

    public DbSet<WarehouseBatch> WarehouseBatches => _db.WarehouseBatches;

    public DbSet<StockReconciliation> StockReconciliations => _db.StockReconciliations;
    public DbSet<StockReconciliationItem> StockReconciliationItems => _db.StockReconciliationItems;

    public DbSet<Order> Orders => _db.Orders;
    public DbSet<OrderItem> OrderItems => _db.OrderItems;

    public DbSet<Warehouse> Warehouses => _db.Warehouses;

    public DbSet<OutboxEvent> OutboxEvents => _db.OutboxEvents;

    public DbSet<Rider> Riders => _db.Riders;
    public DbSet<RiderDocument> RiderDocuments => _db.RiderDocuments;
    public DbSet<RiderAssignment> RiderAssignments => _db.RiderAssignments;
    public DbSet<RiderCapacityConfig> RiderCapacityConfigs => _db.RiderCapacityConfigs;
    public DbSet<RiderIncentiveAward> RiderIncentiveAwards => _db.RiderIncentiveAwards;
    public DbSet<RiderLocationPing> RiderLocationPings => _db.RiderLocationPings;
    public DbSet<RiderPayoutRequest> RiderPayoutRequests => _db.RiderPayoutRequests;
    public DbSet<RiderSettlement> RiderSettlements => _db.RiderSettlements;
    public DbSet<IncentiveRule> IncentiveRules => _db.IncentiveRules;
    public DbSet<RiderRating> RiderRatings => _db.RiderRatings;

    // ─── RaaS partner MVP (partner_id-isolated via rls_partner) ────────────────
    public DbSet<Partner> Partners => _db.Partners;
    public DbSet<PartnerUser> PartnerUsers => _db.PartnerUsers;
    public DbSet<PartnerBooking> PartnerBookings => _db.PartnerBookings;
    public DbSet<PartnerWalletAccount> PartnerWalletAccounts => _db.PartnerWalletAccounts;

    public DbSet<DeliveryAssignment> DeliveryAssignments => _db.DeliveryAssignments;
    public DbSet<PickupRequest> PickupRequests => _db.PickupRequests;
    public DbSet<OrderStatusHistory> OrderStatusHistories => _db.OrderStatusHistories;

    // ─── Orders sub-domain: addons / notes / invoices / delivery slots ─────────
    public DbSet<OrderAddon> OrderAddons => _db.OrderAddons;
    public DbSet<OrderNote> OrderNotes => _db.OrderNotes;
    public DbSet<Invoice> Invoices => _db.Invoices;
    public DbSet<DeliverySlot> DeliverySlots => _db.DeliverySlots;
    public DbSet<DeliverySlotBooking> DeliverySlotBookings => _db.DeliverySlotBookings;

    public DbSet<Payment> Payments => _db.Payments;

    // ─── Commerce: order placement money flow ──────────────────────────────────
    public DbSet<Coupon> Coupons => _db.Coupons;
    public DbSet<CouponRedemption> CouponRedemptions => _db.CouponRedemptions;
    public DbSet<LoyaltyProgram> LoyaltyPrograms => _db.LoyaltyPrograms;
    public DbSet<LoyaltyPointsLedger> LoyaltyPointsLedger => _db.LoyaltyPointsLedger;
    public DbSet<CustomerPackage> CustomerPackages => _db.CustomerPackages;
    public DbSet<PackageUsageLedger> PackageUsageLedger => _db.PackageUsageLedger;
    public DbSet<Promotion> Promotions => _db.Promotions;
    public DbSet<PaymentRefund> PaymentRefunds => _db.PaymentRefunds;

    public DbSet<CashBook> CashBooks => _db.CashBooks;
    public DbSet<CashBookEntry> CashBookEntries => _db.CashBookEntries;

    public DbSet<PushToken> PushTokens => _db.PushTokens;

    // ─── Engagement: support tickets + messages ────────────────────────────────
    public DbSet<SupportTicket> SupportTickets => _db.SupportTickets;
    public DbSet<TicketMessage> TicketMessages => _db.TicketMessages;

    public DbSet<Store> Stores => _db.Stores;
    public DbSet<Franchise> Franchises => _db.Franchises;

    public DbSet<Customer> Customers => _db.Customers;
    public DbSet<CustomerAddress> CustomerAddresses => _db.CustomerAddresses;

    // ─── Catalog: customer-self (devices / DPDP consents / account-deletion) ──
    public DbSet<CustomerDevice> CustomerDevices => _db.CustomerDevices;
    public DbSet<DpdpConsent> DpdpConsents => _db.DpdpConsents;
    public DbSet<AccountDeletionRequest> AccountDeletionRequests => _db.AccountDeletionRequests;

    // ─── Catalog: service catalog + pricing ───────────────────────────────────
    public DbSet<ServiceCategory> ServiceCategories => _db.ServiceCategories;
    public DbSet<Service> Services => _db.Services;
    public DbSet<FabricType> FabricTypes => _db.FabricTypes;
    public DbSet<ItemGroup> ItemGroups => _db.ItemGroups;
    public DbSet<Item> Items => _db.Items;
    public DbSet<ItemVariant> ItemVariants => _db.ItemVariants;
    public DbSet<AddOn> AddOns => _db.AddOns;
    public DbSet<PriceList> PriceLists => _db.PriceLists;
    public DbSet<PriceListItem> PriceListItems => _db.PriceListItems;
    public DbSet<PricingChangeLog> PricingChangeLogs => _db.PricingChangeLogs;

    // ─── Tenancy: territories (serviceability) ────────────────────────────────
    public DbSet<Territory> Territories => _db.Territories;

    public DbSet<User> Users => _db.Users;
    public DbSet<UserProfile> UserProfiles => _db.UserProfiles;
    public DbSet<UserScopeMembership> UserScopeMemberships => _db.UserScopeMemberships;

    public DbSet<SystemSetting> SystemSettings => _db.SystemSettings;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<int> ExecuteSqlInterpolatedAsync(FormattableString sql, CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);

    /// <inheritdoc/>
    public Task<T> SqlQueryScalarAsync<T>(FormattableString sql, CancellationToken cancellationToken) =>
        _db.Database.SqlQuery<T>(sql).SingleAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        // The retrying execution strategy owns the transaction boundary — opening one outside it
        // throws. See IOperationsDbContext.ExecuteInTransactionAsync remarks.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            await action(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        });
    }
}
