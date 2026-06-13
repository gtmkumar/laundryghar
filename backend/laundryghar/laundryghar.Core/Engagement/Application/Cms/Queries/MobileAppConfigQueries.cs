using laundryghar.Engagement.Application.Cms.Commands;
using laundryghar.Engagement.Application.Cms.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Engagement.Application.Cms.Queries;

// ── Admin list ─────────────────────────────────────────────────────────────────

public sealed record GetMobileAppConfigsQuery(int Page, int PageSize, string? Platform) : IRequest<PaginatedList<MobileAppConfigDto>>;

public sealed class GetMobileAppConfigsHandler
    : IRequestHandler<GetMobileAppConfigsQuery, PaginatedList<MobileAppConfigDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetMobileAppConfigsHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<PaginatedList<MobileAppConfigDto>> Handle(
        GetMobileAppConfigsQuery query, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var q = _db.MobileAppConfigs.Where(x => x.BrandId == brandId);
        if (!string.IsNullOrEmpty(query.Platform))
            q = q.Where(x => x.Platform == query.Platform);

        var projected = q.OrderBy(x => x.ConfigKey)
            .Select(x => CreateMobileAppConfigHandler.ToDto(x));

        return await PaginatedList<MobileAppConfigDto>.CreateAsync(projected, query.Page, query.PageSize, ct);
    }
}

// ── Admin get by Id ────────────────────────────────────────────────────────────

public sealed record GetMobileAppConfigByIdQuery(Guid Id) : IRequest<MobileAppConfigDto?>;

public sealed class GetMobileAppConfigByIdHandler
    : IRequestHandler<GetMobileAppConfigByIdQuery, MobileAppConfigDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetMobileAppConfigByIdHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<MobileAppConfigDto?> Handle(
        GetMobileAppConfigByIdQuery query, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.MobileAppConfigs
            .FirstOrDefaultAsync(x => x.Id == query.Id && x.BrandId == brandId, ct);
        return entity is null ? null : CreateMobileAppConfigHandler.ToDto(entity);
    }
}

// ── Public get (anonymous) ─────────────────────────────────────────────────────
/// <summary>Returns all active config keys for the brand+platform as a flat list.</summary>
public sealed record GetPublicAppConfigQuery(Guid BrandId, string Platform) : IRequest<List<MobileAppConfigDto>>;

public sealed class GetPublicAppConfigHandler
    : IRequestHandler<GetPublicAppConfigQuery, List<MobileAppConfigDto>>
{
    private readonly LaundryGharDbContext _db;

    public GetPublicAppConfigHandler(LaundryGharDbContext db) => _db = db;

    public async Task<List<MobileAppConfigDto>> Handle(GetPublicAppConfigQuery query, CancellationToken ct)
    {
        return await _db.MobileAppConfigs
            .Where(x => x.BrandId == query.BrandId
                     && x.Platform == query.Platform
                     && x.IsActive
                     && x.Status == "active")
            .OrderBy(x => x.ConfigKey)
            .Select(x => CreateMobileAppConfigHandler.ToDto(x))
            .ToListAsync(ct);
    }
}
