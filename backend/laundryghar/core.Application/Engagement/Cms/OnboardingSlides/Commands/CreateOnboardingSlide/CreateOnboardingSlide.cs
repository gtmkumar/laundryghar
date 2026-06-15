using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.OnboardingSlides.Common;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.Utilities.Services;

namespace core.Application.Engagement.Cms.OnboardingSlides.Commands.CreateOnboardingSlide;

public sealed record CreateOnboardingSlideCommand(CreateOnboardingSlideRequest Request, Guid? ActorId) : ICommand<OnboardingSlideDto>;

public class CreateOnboardingSlideCommandHandler : ICommandHandler<CreateOnboardingSlideCommand, OnboardingSlideDto>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public CreateOnboardingSlideCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<OnboardingSlideDto> HandleAsync(CreateOnboardingSlideCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();

        var now = DateTimeOffset.UtcNow;
        var entity = new OnboardingSlide
        {
            Id = Guid.NewGuid(),
            BrandId = brandId,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = command.ActorId,
            UpdatedBy = command.ActorId,
        }.ApplyFields(command.Request);

        _db.OnboardingSlides.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return OnboardingSlideDto.FromEntity(entity);
    }
}
