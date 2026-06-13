using laundryghar.Engagement.Infrastructure.Services;
using ICurrentUser = laundryghar.Engagement.Infrastructure.Services.ICurrentUser;
using laundryghar.Engagement.Application.Cms.Commands;
using laundryghar.Engagement.Application.Cms.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Engagement.Application.Cms.Queries;

// ── Admin list ─────────────────────────────────────────────────────────────────

public sealed record GetOnboardingSlidesQuery(int Page, int PageSize, string? AppType) : IRequest<PaginatedList<OnboardingSlideDto>>;

public sealed class GetOnboardingSlidesHandler
    : IRequestHandler<GetOnboardingSlidesQuery, PaginatedList<OnboardingSlideDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetOnboardingSlidesHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<PaginatedList<OnboardingSlideDto>> Handle(
        GetOnboardingSlidesQuery query, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var q = _db.OnboardingSlides.Where(x => x.BrandId == brandId);
        if (!string.IsNullOrEmpty(query.AppType))
            q = q.Where(x => x.AppType == query.AppType);

        var projected = q.OrderBy(x => x.DisplayOrder)
            .Select(x => CreateOnboardingSlideHandler.ToDto(x));

        return await PaginatedList<OnboardingSlideDto>.CreateAsync(projected, query.Page, query.PageSize, ct);
    }
}

// ── Admin get by Id ────────────────────────────────────────────────────────────

public sealed record GetOnboardingSlideByIdQuery(Guid Id) : IRequest<OnboardingSlideDto?>;

public sealed class GetOnboardingSlideByIdHandler
    : IRequestHandler<GetOnboardingSlideByIdQuery, OnboardingSlideDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetOnboardingSlideByIdHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<OnboardingSlideDto?> Handle(
        GetOnboardingSlideByIdQuery query, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.OnboardingSlides
            .FirstOrDefaultAsync(x => x.Id == query.Id && x.BrandId == brandId, ct);
        return entity is null ? null : CreateOnboardingSlideHandler.ToDto(entity);
    }
}

// ── Public list (anonymous) ────────────────────────────────────────────────────

public sealed record GetPublicOnboardingSlidesQuery(Guid BrandId, string AppType) : IRequest<List<OnboardingSlideDto>>;

public sealed class GetPublicOnboardingSlidesHandler
    : IRequestHandler<GetPublicOnboardingSlidesQuery, List<OnboardingSlideDto>>
{
    private readonly LaundryGharDbContext _db;

    public GetPublicOnboardingSlidesHandler(LaundryGharDbContext db) => _db = db;

    public async Task<List<OnboardingSlideDto>> Handle(
        GetPublicOnboardingSlidesQuery query, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await _db.OnboardingSlides
            .Where(x => x.BrandId == query.BrandId
                     && x.AppType == query.AppType
                     && x.IsActive
                     && x.Status == "active"
                     && (x.ShowFrom == null || x.ShowFrom <= now)
                     && (x.ShowUntil == null || x.ShowUntil >= now))
            .OrderBy(x => x.DisplayOrder)
            .Select(x => CreateOnboardingSlideHandler.ToDto(x))
            .ToListAsync(ct);
    }
}
