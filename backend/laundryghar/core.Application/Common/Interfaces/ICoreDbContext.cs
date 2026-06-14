using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Entities.EngagementCms;
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
    DbSet<NotificationTemplate> NotificationTemplates { get; }
    DbSet<Promotion> Promotions { get; }
    DbSet<Coupon> Coupons { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
