using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.AppBanners.Commands.DeleteAppBanner;

// Soft delete → status "archived" (engagement_cms banners have no deleted_at column).
public sealed record DeleteAppBannerCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public class DeleteAppBannerCommandHandler : ICommandHandler<DeleteAppBannerCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteAppBannerCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<bool> HandleAsync(DeleteAppBannerCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.AppBanners
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
