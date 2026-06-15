using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.MobileAppConfigs.Queries.GetPublicAppConfig;

// Public (anonymous) list: all active config keys for the brand+platform as a flat list.
public sealed record GetPublicAppConfigQuery(Guid BrandId, string Platform) : IQuery<List<MobileAppConfigDto>>;

public class GetPublicAppConfigQueryHandler : IQueryHandler<GetPublicAppConfigQuery, List<MobileAppConfigDto>>
{
    private readonly ICoreDbContext _db;

    public GetPublicAppConfigQueryHandler(ICoreDbContext db) => _db = db;

    public async Task<List<MobileAppConfigDto>> HandleAsync(GetPublicAppConfigQuery query, CancellationToken cancellationToken)
    {
        var configs = await _db.MobileAppConfigs.AsNoTracking()
            .Where(x => x.BrandId == query.BrandId
                     && x.Platform == query.Platform
                     && x.IsActive
                     && x.Status == "active")
            .OrderBy(x => x.ConfigKey)
            .ToListAsync(cancellationToken);

        return configs.Select(MobileAppConfigDto.FromEntity).ToList();
    }
}
