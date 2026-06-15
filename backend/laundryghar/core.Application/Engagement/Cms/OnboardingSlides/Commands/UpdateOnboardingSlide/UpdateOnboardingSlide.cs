using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.OnboardingSlides.Common;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.OnboardingSlides.Commands.UpdateOnboardingSlide;

public sealed record UpdateOnboardingSlideCommand(Guid Id, UpdateOnboardingSlideRequest Request, Guid? ActorId) : ICommand<OnboardingSlideDto?>;

public class UpdateOnboardingSlideCommandHandler : ICommandHandler<UpdateOnboardingSlideCommand, OnboardingSlideDto?>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateOnboardingSlideCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<OnboardingSlideDto?> HandleAsync(UpdateOnboardingSlideCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.OnboardingSlides
            .FirstOrDefaultAsync(x => x.Id == command.Id && x.BrandId == brandId, cancellationToken);
        if (entity is null) return null;

        entity.ApplyFields(command.Request);
        entity.Status = command.Request.Status;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = command.ActorId;

        await _db.SaveChangesAsync(cancellationToken);
        return OnboardingSlideDto.FromEntity(entity);
    }
}
