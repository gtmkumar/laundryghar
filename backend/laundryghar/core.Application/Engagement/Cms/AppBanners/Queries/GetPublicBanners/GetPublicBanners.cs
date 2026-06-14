using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.AppBanners.Queries.GetPublicBanners;

// Public (anonymous) list: only banners currently active and within their show window.
public sealed record GetPublicBannersQuery(Guid BrandId, string? Placement) : IQuery<List<AppBannerDto>>;

public class GetPublicBannersQueryHandler : IQueryHandler<GetPublicBannersQuery, List<AppBannerDto>>
{
    private readonly ICoreDbContext _db;

    public GetPublicBannersQueryHandler(ICoreDbContext db) => _db = db;

    public async Task<List<AppBannerDto>> HandleAsync(GetPublicBannersQuery query, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var q = _db.AppBanners.AsNoTracking().Where(x =>
            x.BrandId == query.BrandId
            && x.IsActive
            && x.Status == "active"
            && (x.ShowFrom == null || x.ShowFrom <= now)
            && (x.ShowUntil == null || x.ShowUntil >= now));

        if (!string.IsNullOrEmpty(query.Placement))
            q = q.Where(x => x.Placement == query.Placement);

        var banners = await q.OrderBy(x => x.DisplayOrder).ToListAsync(cancellationToken);
        return banners.Select(AppBannerDto.FromEntity).ToList();
    }
}
