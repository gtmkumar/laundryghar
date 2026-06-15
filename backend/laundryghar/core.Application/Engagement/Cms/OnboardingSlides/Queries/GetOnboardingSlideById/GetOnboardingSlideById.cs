using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.OnboardingSlides.Queries.GetOnboardingSlideById;

// Admin get by id, scoped to the caller's brand.
public sealed record GetOnboardingSlideByIdQuery(Guid Id) : IQuery<OnboardingSlideDto?>;

public class GetOnboardingSlideByIdQueryHandler : IQueryHandler<GetOnboardingSlideByIdQuery, OnboardingSlideDto?>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public GetOnboardingSlideByIdQueryHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<OnboardingSlideDto?> HandleAsync(GetOnboardingSlideByIdQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.OnboardingSlides.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Id && x.BrandId == brandId, cancellationToken);
        return entity is null ? null : OnboardingSlideDto.FromEntity(entity);
    }
}
