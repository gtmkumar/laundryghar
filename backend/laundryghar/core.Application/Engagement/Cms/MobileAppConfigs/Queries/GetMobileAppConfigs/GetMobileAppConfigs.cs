using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.MobileAppConfigs.Queries.GetMobileAppConfigs;

// Admin list (paged), scoped to the caller's brand.
public sealed record GetMobileAppConfigsQuery(int Page, int PageSize, string? Platform) : IQuery<PaginatedList<MobileAppConfigDto>>;

public class GetMobileAppConfigsQueryHandler : IQueryHandler<GetMobileAppConfigsQuery, PaginatedList<MobileAppConfigDto>>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public GetMobileAppConfigsQueryHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<PaginatedList<MobileAppConfigDto>> HandleAsync(GetMobileAppConfigsQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();

        var q = _db.MobileAppConfigs.AsNoTracking().Where(x => x.BrandId == brandId);
        if (!string.IsNullOrEmpty(query.Platform))
            q = q.Where(x => x.Platform == query.Platform);

        var paged = await PaginatedList<MobileAppConfig>.CreateAsync(
            q.OrderBy(x => x.ConfigKey), query.Page, query.PageSize, cancellationToken);

        return paged.Map(MobileAppConfigDto.FromEntity);
    }
}
