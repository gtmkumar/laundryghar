using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.AppBanners.Common;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.AppBanners.Commands.UpdateAppBanner;

public sealed record UpdateAppBannerCommand(Guid Id, UpdateAppBannerRequest Request, Guid? ActorId) : ICommand<AppBannerDto?>;

public class UpdateAppBannerCommandHandler : ICommandHandler<UpdateAppBannerCommand, AppBannerDto?>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateAppBannerCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<AppBannerDto?> HandleAsync(UpdateAppBannerCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.AppBanners
            .FirstOrDefaultAsync(x => x.Id == command.Id && x.BrandId == brandId, cancellationToken);
        if (entity is null) return null;

        await _db.EnsureReferencesExistAsync(brandId, command.Request, cancellationToken);

        entity.ApplyFields(command.Request);
        entity.Status = command.Request.Status;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = command.ActorId;

        await _db.SaveChangesAsync(cancellationToken);
        return AppBannerDto.FromEntity(entity);
    }
}
