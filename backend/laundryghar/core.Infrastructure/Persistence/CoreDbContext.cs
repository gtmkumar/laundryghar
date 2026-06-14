using core.Application.Common.Interfaces;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Entities.EngagementCms;
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
    public DbSet<Promotion> Promotions => _db.Promotions;
    public DbSet<Coupon> Coupons => _db.Coupons;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
