using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.AppBanners.Queries.GetAppBanners;

// Admin list (paged), scoped to the caller's brand.
public sealed record GetAppBannersQuery(int Page, int PageSize, string? Placement) : IQuery<PaginatedList<AppBannerDto>>;

public class GetAppBannersQueryHandler : IQueryHandler<GetAppBannersQuery, PaginatedList<AppBannerDto>>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public GetAppBannersQueryHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<PaginatedList<AppBannerDto>> HandleAsync(GetAppBannersQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();

        var q = _db.AppBanners.AsNoTracking().Where(x => x.BrandId == brandId);
        if (!string.IsNullOrEmpty(query.Placement))
            q = q.Where(x => x.Placement == query.Placement);

        var paged = await PaginatedList<AppBanner>.CreateAsync(
            q.OrderBy(x => x.DisplayOrder), query.Page, query.PageSize, cancellationToken);

        return paged.Map(AppBannerDto.FromEntity);
    }
}
