using laundryghar.SharedDataModel.Entities.Analytics;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Common.Interfaces;

/// <summary>
/// The commerce context's data-access surface, exposed to Application handlers as an interface
/// (no repositories). Backed by the shared <c>LaundryGharDbContext</c> via an adapter in
/// commerce.Infrastructure. Handlers inject this and write EF Core LINQ directly.
/// Modelled on <c>IOperationsDbContext</c>; only the entity sets the commerce slices touch are
/// surfaced here. DbSets are added per-slice as Commerce endpoints are migrated.
/// </summary>
public interface ICommerceDbContext
{
    // ─── Analytics: matview-backed read models (keyless; never tracked for writes) ──
    DbSet<DailyStoreRevenue> DailyStoreRevenues { get; }
    DbSet<MonthlyFranchiseRevenue> MonthlyFranchiseRevenues { get; }
    DbSet<WarehouseThroughput> WarehouseThroughputs { get; }
    DbSet<CustomerLtv> CustomerLtvs { get; }
    DbSet<RiderPerformance> RiderPerformances { get; }

    // ─── Finance: cash books, expenses, royalty, SaaS subscriptions ────────────
    DbSet<CashBook> CashBooks { get; }
    DbSet<CashBookEntry> CashBookEntries { get; }
    DbSet<ShiftHandover> ShiftHandovers { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<ExpenseCategory> ExpenseCategories { get; }
    DbSet<ExpenseAttachment> ExpenseAttachments { get; }
    DbSet<RoyaltyInvoice> RoyaltyInvoices { get; }
    DbSet<RoyaltyCalculation> RoyaltyCalculations { get; }
    DbSet<PlatformPlan> PlatformPlans { get; }
    DbSet<FranchiseSubscription> FranchiseSubscriptions { get; }
    DbSet<FranchiseSubscriptionEvent> FranchiseSubscriptionEvents { get; }

    // ─── Tenancy/org + commerce read dependencies (cross-brand guards, revenue) ─
    DbSet<Franchise> Franchises { get; }
    DbSet<Store> Stores { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<Payment> Payments { get; }

    // ─── Commerce: payments, refunds, payment methods, gateway settings ────────
    DbSet<PaymentRefund> PaymentRefunds { get; }
    DbSet<PaymentMethod> PaymentMethods { get; }

    // ─── Commerce: packages + customer packages + usage ledger ─────────────────
    DbSet<Package> Packages { get; }
    DbSet<CustomerPackage> CustomerPackages { get; }
    DbSet<PackageUsageLedger> PackageUsageLedger { get; }

    // ─── Commerce: coupons + redemptions, promotions ───────────────────────────
    DbSet<Coupon> Coupons { get; }
    DbSet<CouponRedemption> CouponRedemptions { get; }
    DbSet<Promotion> Promotions { get; }

    // ─── Commerce: loyalty program + points ledger ─────────────────────────────
    DbSet<LoyaltyProgram> LoyaltyPrograms { get; }
    DbSet<LoyaltyPointsLedger> LoyaltyPointsLedger { get; }

    // ─── Commerce: wallet account + transactions ledger ────────────────────────
    DbSet<WalletAccount> WalletAccounts { get; }
    DbSet<WalletTransaction> WalletTransactions { get; }

    // ─── Commerce: RaaS partner prepaid wallet + ledger (rls_partner-isolated) ──
    DbSet<PartnerWalletAccount> PartnerWalletAccounts { get; }
    DbSet<PartnerWalletTransaction> PartnerWalletTransactions { get; }

    // ─── Commerce: RaaS partner invoices (rls_partner-isolated) ────────────────
    DbSet<PartnerInvoice> PartnerInvoices { get; }

    // ─── Commerce: subscription plans, customer subscriptions, mandates ────────
    DbSet<SubscriptionPlan> SubscriptionPlans { get; }
    DbSet<CustomerSubscription> CustomerSubscriptions { get; }
    DbSet<PaymentMandate> PaymentMandates { get; }

    // ─── Order spine (offline counter payment + coupon redemption) ─────────────
    DbSet<Order> Orders { get; }

    // ─── Customer (loyalty balance self-filter) ────────────────────────────────
    DbSet<Customer> Customers { get; }

    // ─── Kernel: outbox events (webhook capture/fail) + gateway settings rows ──
    DbSet<OutboxEvent> OutboxEvents { get; }
    DbSet<SystemSetting> SystemSettings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Executes a non-query raw-SQL statement against the underlying connection. Used by the
    /// analytics matview refresh, which calls the <c>analytics.refresh_all_matviews()</c>
    /// SECURITY DEFINER function (the matviews are owned by postgres and app_user cannot REFRESH
    /// them directly). The function refreshes all matviews CONCURRENTLY across every brand.
    /// </summary>
    Task<int> ExecuteSqlRawAsync(string sql, CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="action"/> inside a database transaction, wrapped in the provider's
    /// retrying execution strategy. The strategy owns the transaction boundary — required because
    /// <c>NpgsqlRetryingExecutionStrategy</c> rejects a manually-opened <c>BeginTransactionAsync</c>
    /// unless it is created inside <c>CreateExecutionStrategy().ExecuteAsync(...)</c>. Use only when
    /// the unit of work spans more than a single <see cref="SaveChangesAsync"/> call.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);

    /// <summary>
    /// Reloads a tracked entity from the database, refreshing all property values. Finance handlers
    /// use this after <see cref="SaveChangesAsync"/> to pick up DB-generated columns
    /// (cash-book <c>variance</c>, expense <c>total_amount</c>, royalty <c>amount_due</c>,
    /// shift-handover <c>cash_variance</c>) that the database computes via generated columns.
    /// </summary>
    Task ReloadAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class;

    /// <summary>
    /// Eager-loads a single reference navigation on a tracked entity. Used after an insert+reload
    /// to populate the expense <c>Category</c> navigation for the response DTO without a second
    /// round-trip query.
    /// </summary>
    Task LoadReferenceAsync<TEntity, TProperty>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, TProperty?>> navigation,
        CancellationToken cancellationToken)
        where TEntity : class
        where TProperty : class;
}
