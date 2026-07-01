using System.Linq.Expressions;
using commerce.Application.Common.Interfaces;
using laundryghar.SharedDataModel.Entities.Analytics;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace commerce.Infrastructure.Persistence;

/// <summary>
/// Adapts the shared <see cref="LaundryGharDbContext"/> to <see cref="ICommerceDbContext"/>, exposing
/// only the entity sets the commerce slices use. Lets Application handlers depend on the context
/// surface they own without taking a dependency on the shared concrete context.
/// Mirrors <c>OperationsDbContext</c>. DbSets are added per-slice as Commerce endpoints are migrated.
/// </summary>
public sealed class CommerceDbContext : ICommerceDbContext
{
    private readonly LaundryGharDbContext _db;

    public CommerceDbContext(LaundryGharDbContext db) => _db = db;

    // ─── Analytics: matview-backed read models ─────────────────────────────────
    public DbSet<DailyStoreRevenue> DailyStoreRevenues => _db.DailyStoreRevenues;
    public DbSet<MonthlyFranchiseRevenue> MonthlyFranchiseRevenues => _db.MonthlyFranchiseRevenues;
    public DbSet<WarehouseThroughput> WarehouseThroughputs => _db.WarehouseThroughputs;
    public DbSet<CustomerLtv> CustomerLtvs => _db.CustomerLtvs;
    public DbSet<RiderPerformance> RiderPerformances => _db.RiderPerformances;

    // ─── Finance: cash books, expenses, royalty, SaaS subscriptions ────────────
    public DbSet<CashBook> CashBooks => _db.CashBooks;
    public DbSet<CashBookEntry> CashBookEntries => _db.CashBookEntries;
    public DbSet<ShiftHandover> ShiftHandovers => _db.ShiftHandovers;
    public DbSet<Expense> Expenses => _db.Expenses;
    public DbSet<ExpenseCategory> ExpenseCategories => _db.ExpenseCategories;
    public DbSet<ExpenseAttachment> ExpenseAttachments => _db.ExpenseAttachments;
    public DbSet<RoyaltyInvoice> RoyaltyInvoices => _db.RoyaltyInvoices;
    public DbSet<RoyaltyCalculation> RoyaltyCalculations => _db.RoyaltyCalculations;
    public DbSet<PlatformPlan> PlatformPlans => _db.PlatformPlans;
    public DbSet<FranchiseSubscription> FranchiseSubscriptions => _db.FranchiseSubscriptions;
    public DbSet<FranchiseSubscriptionEvent> FranchiseSubscriptionEvents => _db.FranchiseSubscriptionEvents;

    // ─── Tenancy/org + commerce read dependencies ──────────────────────────────
    public DbSet<Franchise> Franchises => _db.Franchises;
    public DbSet<Store> Stores => _db.Stores;
    public DbSet<Warehouse> Warehouses => _db.Warehouses;
    public DbSet<Payment> Payments => _db.Payments;

    // ─── Commerce: payments, refunds, payment methods ──────────────────────────
    public DbSet<PaymentRefund> PaymentRefunds => _db.PaymentRefunds;
    public DbSet<PaymentMethod> PaymentMethods => _db.PaymentMethods;

    // ─── Commerce: packages + customer packages + usage ledger ─────────────────
    public DbSet<Package> Packages => _db.Packages;
    public DbSet<CustomerPackage> CustomerPackages => _db.CustomerPackages;
    public DbSet<PackageUsageLedger> PackageUsageLedger => _db.PackageUsageLedger;

    // ─── Commerce: coupons + redemptions, promotions ───────────────────────────
    public DbSet<Coupon> Coupons => _db.Coupons;
    public DbSet<CouponRedemption> CouponRedemptions => _db.CouponRedemptions;
    public DbSet<Promotion> Promotions => _db.Promotions;

    // ─── Commerce: loyalty program + points ledger ─────────────────────────────
    public DbSet<LoyaltyProgram> LoyaltyPrograms => _db.LoyaltyPrograms;
    public DbSet<LoyaltyPointsLedger> LoyaltyPointsLedger => _db.LoyaltyPointsLedger;

    // ─── Commerce: wallet account + transactions ledger ────────────────────────
    public DbSet<WalletAccount> WalletAccounts => _db.WalletAccounts;
    public DbSet<WalletTransaction> WalletTransactions => _db.WalletTransactions;

    // ─── Commerce: RaaS partner prepaid wallet + ledger (rls_partner-isolated) ──
    public DbSet<PartnerWalletAccount> PartnerWalletAccounts => _db.PartnerWalletAccounts;
    public DbSet<PartnerWalletTransaction> PartnerWalletTransactions => _db.PartnerWalletTransactions;

    // ─── Commerce: subscription plans, customer subscriptions, mandates ────────
    public DbSet<SubscriptionPlan> SubscriptionPlans => _db.SubscriptionPlans;
    public DbSet<CustomerSubscription> CustomerSubscriptions => _db.CustomerSubscriptions;
    public DbSet<PaymentMandate> PaymentMandates => _db.PaymentMandates;

    // ─── Order spine + customer + kernel rows ──────────────────────────────────
    public DbSet<Order> Orders => _db.Orders;
    public DbSet<Customer> Customers => _db.Customers;
    public DbSet<OutboxEvent> OutboxEvents => _db.OutboxEvents;
    public DbSet<SystemSetting> SystemSettings => _db.SystemSettings;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<int> ExecuteSqlRawAsync(string sql, CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlRawAsync(sql, cancellationToken);

    /// <inheritdoc/>
    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        // The retrying execution strategy owns the transaction boundary — opening one outside it
        // throws. See ICommerceDbContext.ExecuteInTransactionAsync remarks.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            await action(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        });
    }

    /// <inheritdoc/>
    public Task ReloadAsync<TEntity>(TEntity entity, CancellationToken cancellationToken)
        where TEntity : class =>
        _db.Entry(entity).ReloadAsync(cancellationToken);

    /// <inheritdoc/>
    public Task LoadReferenceAsync<TEntity, TProperty>(
        TEntity entity,
        Expression<Func<TEntity, TProperty?>> navigation,
        CancellationToken cancellationToken)
        where TEntity : class
        where TProperty : class =>
        _db.Entry(entity).Reference(navigation).LoadAsync(cancellationToken);
}
