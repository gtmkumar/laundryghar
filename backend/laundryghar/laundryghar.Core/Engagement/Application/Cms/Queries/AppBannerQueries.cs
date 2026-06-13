using laundryghar.Engagement.Infrastructure.Services;
using ICurrentUser = laundryghar.Engagement.Infrastructure.Services.ICurrentUser;
using laundryghar.Engagement.Application.Cms.Commands;
using laundryghar.Engagement.Application.Cms.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Engagement.Application.Cms.Queries;

// ── Admin list ─────────────────────────────────────────────────────────────────

public sealed record GetAppBannersQuery(int Page, int PageSize, string? Placement) : IRequest<PaginatedList<AppBannerDto>>;

public sealed class GetAppBannersHandler
    : IRequestHandler<GetAppBannersQuery, PaginatedList<AppBannerDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetAppBannersHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<PaginatedList<AppBannerDto>> Handle(GetAppBannersQuery query, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var q = _db.AppBanners.Where(x => x.BrandId == brandId);
        if (!string.IsNullOrEmpty(query.Placement))
            q = q.Where(x => x.Placement == query.Placement);

        var projected = q.OrderBy(x => x.DisplayOrder)
            .Select(x => CreateAppBannerHandler.ToDto(x));

        return await PaginatedList<AppBannerDto>.CreateAsync(projected, query.Page, query.PageSize, ct);
    }
}

// ── Admin get by Id ────────────────────────────────────────────────────────────

public sealed record GetAppBannerByIdQuery(Guid Id) : IRequest<AppBannerDto?>;

public sealed class GetAppBannerByIdHandler
    : IRequestHandler<GetAppBannerByIdQuery, AppBannerDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetAppBannerByIdHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<AppBannerDto?> Handle(GetAppBannerByIdQuery query, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.AppBanners
            .FirstOrDefaultAsync(x => x.Id == query.Id && x.BrandId == brandId, ct);
        return entity is null ? null : CreateAppBannerHandler.ToDto(entity);
    }
}

// ── Public list (anonymous) ────────────────────────────────────────────────────

public sealed record GetPublicBannersQuery(Guid BrandId, string? Placement) : IRequest<List<AppBannerDto>>;

public sealed class GetPublicBannersHandler
    : IRequestHandler<GetPublicBannersQuery, List<AppBannerDto>>
{
    private readonly LaundryGharDbContext _db;

    public GetPublicBannersHandler(LaundryGharDbContext db) => _db = db;

    public async Task<List<AppBannerDto>> Handle(GetPublicBannersQuery query, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var q = _db.AppBanners.Where(x =>
            x.BrandId == query.BrandId
            && x.IsActive
            && x.Status == "active"
            && (x.ShowFrom == null || x.ShowFrom <= now)
            && (x.ShowUntil == null || x.ShowUntil >= now));

        if (!string.IsNullOrEmpty(query.Placement))
            q = q.Where(x => x.Placement == query.Placement);

        return await q
            .OrderBy(x => x.DisplayOrder)
            .Select(x => CreateAppBannerHandler.ToDto(x))
            .ToListAsync(ct);
    }
}
