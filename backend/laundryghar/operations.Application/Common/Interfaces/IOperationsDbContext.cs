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

namespace operations.Application.Common.Interfaces;

/// <summary>
/// The operations context's data-access surface, exposed to Application handlers as an interface
/// (no repositories). Backed by the shared <c>LaundryGharDbContext</c> via an adapter in
/// operations.Infrastructure. Handlers inject this and write EF Core LINQ directly.
/// Modelled on <c>ICoreDbContext</c>; only the entity sets the operations slices touch are surfaced here.
/// </summary>
public interface IOperationsDbContext
{
    // ─── Warehouse: garments + tags ──────────────────────────────────────────
    DbSet<Garment> Garments { get; }
    DbSet<GarmentTag> GarmentTags { get; }

    // ─── Warehouse: inspections + conditions ─────────────────────────────────
    DbSet<GarmentInspection> GarmentInspections { get; }
    DbSet<GarmentInspectionPhoto> GarmentInspectionPhotos { get; }
    DbSet<GarmentCondition> GarmentConditions { get; }

    // ─── Warehouse: processes + process logs ─────────────────────────────────
    DbSet<WarehouseProcess> WarehouseProcesses { get; }
    DbSet<ProcessLog> ProcessLogs { get; }

    // ─── Warehouse: quality checks ───────────────────────────────────────────
    DbSet<QualityCheck> QualityChecks { get; }

    // ─── Warehouse: batches ──────────────────────────────────────────────────
    DbSet<WarehouseBatch> WarehouseBatches { get; }

    // ─── Warehouse: stock reconciliation ─────────────────────────────────────
    DbSet<StockReconciliation> StockReconciliations { get; }
    DbSet<StockReconciliationItem> StockReconciliationItems { get; }

    // ─── Order spine (read for FK resolution on garment create) ──────────────
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }

    // ─── Tenancy (warehouse lookup + board metrics) ──────────────────────────
    // Fully-qualified: the simple name 'Warehouse' clashes with the operations.Application.Warehouse namespace.
    DbSet<laundryghar.SharedDataModel.Entities.TenancyOrg.Warehouse> Warehouses { get; }

    // ─── Kernel (outbox events: garment.lost / garment.qc_* / garment.rewash) ─
    DbSet<OutboxEvent> OutboxEvents { get; }

    // ─── Logistics: riders + lifecycle ───────────────────────────────────────
    DbSet<Rider> Riders { get; }
    DbSet<RiderDocument> RiderDocuments { get; }
    DbSet<RiderAssignment> RiderAssignments { get; }
    DbSet<RiderCapacityConfig> RiderCapacityConfigs { get; }
    DbSet<RiderIncentiveAward> RiderIncentiveAwards { get; }
    DbSet<RiderLocationPing> RiderLocationPings { get; }
    DbSet<RiderPayoutRequest> RiderPayoutRequests { get; }
    DbSet<RiderSettlement> RiderSettlements { get; }
    DbSet<IncentiveRule> IncentiveRules { get; }

    // ─── Logistics: rider ratings (customer rates the rider after delivery) ──
    DbSet<RiderRating> RiderRatings { get; }

    // ─── Order spine: delivery/pickup legs + status history ──────────────────
    DbSet<DeliveryAssignment> DeliveryAssignments { get; }
    DbSet<PickupRequest> PickupRequests { get; }
    DbSet<OrderStatusHistory> OrderStatusHistories { get; }

    // ─── Order spine: addons / notes / invoices / delivery slots (Orders sub-domain) ──
    DbSet<OrderAddon> OrderAddons { get; }
    DbSet<OrderNote> OrderNotes { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<DeliverySlot> DeliverySlots { get; }
    DbSet<DeliverySlotBooking> DeliverySlotBookings { get; }

    // ─── Commerce (COD payment rows on delivery completion) ──────────────────
    DbSet<Payment> Payments { get; }

    // ─── Commerce: order placement money flow (coupon / loyalty / package / promotion / refund) ──
    DbSet<Coupon> Coupons { get; }
    DbSet<CouponRedemption> CouponRedemptions { get; }
    DbSet<LoyaltyProgram> LoyaltyPrograms { get; }
    DbSet<LoyaltyPointsLedger> LoyaltyPointsLedger { get; }
    DbSet<CustomerPackage> CustomerPackages { get; }
    DbSet<PackageUsageLedger> PackageUsageLedger { get; }
    DbSet<Promotion> Promotions { get; }
    DbSet<PaymentRefund> PaymentRefunds { get; }

    // ─── Finance (rider-payout cash-book postings) ───────────────────────────
    DbSet<CashBook> CashBooks { get; }
    DbSet<CashBookEntry> CashBookEntries { get; }

    // ─── Engagement (rider Expo push tokens) ─────────────────────────────────
    DbSet<PushToken> PushTokens { get; }

    // ─── Engagement (support tickets + messages — customer/rider help desk) ───
    DbSet<SupportTicket> SupportTickets { get; }
    DbSet<TicketMessage> TicketMessages { get; }

    // ─── Tenancy (franchise + store scoping / geofencing) ────────────────────
    DbSet<Store> Stores { get; }
    DbSet<Franchise> Franchises { get; }

    // ─── Customer (rider-task customer + address hydration) ──────────────────
    DbSet<Customer> Customers { get; }
    DbSet<CustomerAddress> CustomerAddresses { get; }

    // ─── Catalog: customer devices / DPDP consents / account-deletion (customer-self) ─
    DbSet<CustomerDevice> CustomerDevices { get; }
    DbSet<DpdpConsent> DpdpConsents { get; }
    DbSet<AccountDeletionRequest> AccountDeletionRequests { get; }

    // ─── Catalog: service catalog + pricing (admin + customer browse) ────────
    DbSet<ServiceCategory> ServiceCategories { get; }
    DbSet<Service> Services { get; }
    DbSet<FabricType> FabricTypes { get; }
    DbSet<ItemGroup> ItemGroups { get; }
    DbSet<Item> Items { get; }
    DbSet<ItemVariant> ItemVariants { get; }
    DbSet<AddOn> AddOns { get; }
    DbSet<PriceList> PriceLists { get; }
    DbSet<PriceListItem> PriceListItems { get; }

    // ─── Tenancy: territories (serviceability pincode lookup) ────────────────
    DbSet<Territory> Territories { get; }

    // ─── Identity (rider profile enrichment + brand-membership guard) ────────
    DbSet<User> Users { get; }
    DbSet<UserProfile> UserProfiles { get; }
    DbSet<UserScopeMembership> UserScopeMemberships { get; }

    // ─── Kernel settings (rider payout-rate config) ──────────────────────────
    DbSet<SystemSetting> SystemSettings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Executes a parameterized raw-SQL statement against the underlying connection. Used by the
    /// rider-load adjustment (a guarded atomic UPDATE that cannot be expressed as a tracked entity
    /// change). Pass interpolated values — they are bound as parameters, never string-concatenated.
    /// </summary>
    Task<int> ExecuteSqlInterpolatedAsync(FormattableString sql, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a parameterized raw-SQL query that returns a single scalar value and returns the first
    /// row. Used by the order/invoice number allocators, which delegate to the
    /// <c>order_lifecycle.next_order_number(...)</c> / <c>next_invoice_number(...)</c> SQL functions
    /// (atomic per-(brand,store,year/fy) counters that cannot be expressed as tracked entity changes).
    /// Pass interpolated values — they are bound as parameters, never string-concatenated.
    /// </summary>
    Task<T> SqlQueryScalarAsync<T>(FormattableString sql, CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="action"/> inside a database transaction, wrapped in the provider's
    /// retrying execution strategy. The strategy owns the transaction boundary — required because
    /// <c>NpgsqlRetryingExecutionStrategy</c> rejects a manually-opened <c>BeginTransactionAsync</c>
    /// unless it is created inside <c>CreateExecutionStrategy().ExecuteAsync(...)</c>. Use only when
    /// the unit of work spans more than a single <see cref="SaveChangesAsync"/> call.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);
}
