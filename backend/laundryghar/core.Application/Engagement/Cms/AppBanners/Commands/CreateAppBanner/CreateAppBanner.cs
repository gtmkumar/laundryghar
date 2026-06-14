using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.AppBanners.Common;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.Utilities.Services;

namespace core.Application.Engagement.Cms.AppBanners.Commands.CreateAppBanner;

public sealed record CreateAppBannerCommand(CreateAppBannerRequest Request, Guid? ActorId) : ICommand<AppBannerDto>;

public class CreateAppBannerCommandHandler : ICommandHandler<CreateAppBannerCommand, AppBannerDto>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public CreateAppBannerCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<AppBannerDto> HandleAsync(CreateAppBannerCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        await _db.EnsureReferencesExistAsync(brandId, command.Request, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var entity = new AppBanner
        {
            Id = Guid.NewGuid(),
            BrandId = brandId,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = command.ActorId,
            UpdatedBy = command.ActorId,
        }.ApplyFields(command.Request);

        _db.AppBanners.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return AppBannerDto.FromEntity(entity);
    }
}
