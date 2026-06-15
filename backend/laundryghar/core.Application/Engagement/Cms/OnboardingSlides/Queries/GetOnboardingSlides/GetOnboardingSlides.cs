using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.OnboardingSlides.Queries.GetOnboardingSlides;

// Admin list (paged), scoped to the caller's brand.
public sealed record GetOnboardingSlidesQuery(int Page, int PageSize, string? AppType) : IQuery<PaginatedList<OnboardingSlideDto>>;

public class GetOnboardingSlidesQueryHandler : IQueryHandler<GetOnboardingSlidesQuery, PaginatedList<OnboardingSlideDto>>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public GetOnboardingSlidesQueryHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<PaginatedList<OnboardingSlideDto>> HandleAsync(GetOnboardingSlidesQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();

        var q = _db.OnboardingSlides.AsNoTracking().Where(x => x.BrandId == brandId);
        if (!string.IsNullOrEmpty(query.AppType))
            q = q.Where(x => x.AppType == query.AppType);

        var paged = await PaginatedList<OnboardingSlide>.CreateAsync(
            q.OrderBy(x => x.DisplayOrder), query.Page, query.PageSize, cancellationToken);

        return paged.Map(OnboardingSlideDto.FromEntity);
    }
}
