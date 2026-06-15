using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.MobileAppConfigs.Queries.GetMobileAppConfigById;

// Admin get by id, scoped to the caller's brand.
public sealed record GetMobileAppConfigByIdQuery(Guid Id) : IQuery<MobileAppConfigDto?>;

public class GetMobileAppConfigByIdQueryHandler : IQueryHandler<GetMobileAppConfigByIdQuery, MobileAppConfigDto?>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public GetMobileAppConfigByIdQueryHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<MobileAppConfigDto?> HandleAsync(GetMobileAppConfigByIdQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.MobileAppConfigs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Id && x.BrandId == brandId, cancellationToken);
        return entity is null ? null : MobileAppConfigDto.FromEntity(entity);
    }
}
