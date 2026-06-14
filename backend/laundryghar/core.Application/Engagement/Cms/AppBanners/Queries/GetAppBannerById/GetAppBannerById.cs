using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.AppBanners.Queries.GetAppBannerById;

// Admin get by id, scoped to the caller's brand.
public sealed record GetAppBannerByIdQuery(Guid Id) : IQuery<AppBannerDto?>;

public class GetAppBannerByIdQueryHandler : IQueryHandler<GetAppBannerByIdQuery, AppBannerDto?>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public GetAppBannerByIdQueryHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<AppBannerDto?> HandleAsync(GetAppBannerByIdQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.AppBanners.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Id && x.BrandId == brandId, cancellationToken);
        return entity is null ? null : AppBannerDto.FromEntity(entity);
    }
}
