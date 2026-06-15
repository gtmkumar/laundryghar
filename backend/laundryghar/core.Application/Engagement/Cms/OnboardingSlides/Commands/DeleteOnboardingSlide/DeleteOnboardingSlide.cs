using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.OnboardingSlides.Commands.DeleteOnboardingSlide;

// Soft delete → status "archived" (engagement_cms onboarding slides have no deleted_at column).
public sealed record DeleteOnboardingSlideCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public class DeleteOnboardingSlideCommandHandler : ICommandHandler<DeleteOnboardingSlideCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteOnboardingSlideCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<bool> HandleAsync(DeleteOnboardingSlideCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.OnboardingSlides
            .FirstOrDefaultAsync(x => x.Id == command.Id && x.BrandId == brandId, cancellationToken);
        if (entity is null) return false;

        entity.Status = "archived";
        entity.IsActive = false;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = command.ActorId;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
