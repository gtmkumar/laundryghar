using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.OnboardingSlides.Queries.GetPublicOnboardingSlides;

// Public (anonymous) list: only slides currently active and within their show window.
public sealed record GetPublicOnboardingSlidesQuery(Guid BrandId, string AppType) : IQuery<List<OnboardingSlideDto>>;

public class GetPublicOnboardingSlidesQueryHandler : IQueryHandler<GetPublicOnboardingSlidesQuery, List<OnboardingSlideDto>>
{
    private readonly ICoreDbContext _db;

    public GetPublicOnboardingSlidesQueryHandler(ICoreDbContext db) => _db = db;

    public async Task<List<OnboardingSlideDto>> HandleAsync(GetPublicOnboardingSlidesQuery query, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var slides = await _db.OnboardingSlides.AsNoTracking()
            .Where(x => x.BrandId == query.BrandId
                     && x.AppType == query.AppType
                     && x.IsActive
                     && x.Status == "active"
                     && (x.ShowFrom == null || x.ShowFrom <= now)
                     && (x.ShowUntil == null || x.ShowUntil >= now))
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync(cancellationToken);

        return slides.Select(OnboardingSlideDto.FromEntity).ToList();
    }
}
